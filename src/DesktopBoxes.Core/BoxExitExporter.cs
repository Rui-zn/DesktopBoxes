using System.Security.Cryptography;
using System.Text.Json;

namespace DesktopBoxes.Core;

/// <summary>Creates identifiable desktop copies on exit and safely removes unchanged copies on startup.</summary>
public static class BoxExitExporter
{
    private const int ManifestVersion = 1;
    private const int MaxFolderNameLength = 120;
    private const string ManifestFileName = "desktop-exports.json";
    private const string MarkerFileName = ".desktopboxes-exit-export.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static ExportResult Export(string desktopDirectory, string dataDirectory, IReadOnlyList<Box> boxes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(boxes);

        string desktop = Path.GetFullPath(desktopDirectory);
        string manifestPath = GetManifestPath(dataDirectory);
        Directory.CreateDirectory(desktop);

        ExportManifest manifest = LoadManifestForExport(manifestPath, desktop);
        var result = new ExportResult();
        var usedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in manifest.Boxes)
            usedFolderNames.Add(Path.GetFileName(existing.RelativeDirectory));

        foreach (var box in boxes)
        {
            string folderName = FindAvailableBoxFolder(desktop, SanitizeFolderName(box.Name), usedFolderNames);
            string boxDirectory = Path.Combine(desktop, folderName);
            var record = new ExportedBox
            {
                BoxId = box.Id,
                BoxName = box.Name,
                RelativeDirectory = folderName,
                SessionId = Guid.NewGuid().ToString("N"),
            };

            try
            {
                Directory.CreateDirectory(boxDirectory);
                WriteMarker(boxDirectory, record);
                manifest.Boxes.Add(record);
                SaveManifest(manifestPath, manifest);
                result.BoxDirectories.Add(boxDirectory);

                foreach (var item in box.Items)
                {
                    try
                    {
                        CopyItem(item.Path, boxDirectory, record, manifest, manifestPath, result);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"盒子“{box.Name}”中的“{item.DisplayName}”导出失败：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"盒子“{box.Name}”无法创建可恢复的导出文件夹：{ex.Message}");
            }
        }

        return result;
    }

    public static CleanupResult Cleanup(string desktopDirectory, string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);

        string desktop = Path.GetFullPath(desktopDirectory);
        string manifestPath = GetManifestPath(dataDirectory);
        var result = new CleanupResult();
        if (!File.Exists(manifestPath)) return result;

        ExportManifest manifest = LoadManifest(manifestPath);
        if (manifest.Version != ManifestVersion || !BoxFileTransfers.SamePath(manifest.DesktopDirectory, desktop))
            throw new IOException("桌面导出清单版本或桌面位置不匹配，已保留桌面文件。", new InvalidDataException(manifestPath));

        var retryBoxes = new List<ExportedBox>();
        foreach (var box in manifest.Boxes)
        {
            string boxDirectory;
            try
            {
                boxDirectory = ResolveInside(desktop, box.RelativeDirectory);
                if (!Directory.Exists(boxDirectory)) continue;
                ValidateMarker(boxDirectory, box);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"盒子“{box.BoxName}”的桌面副本未清理：{ex.Message}");
                retryBoxes.Add(box);
                continue;
            }

            var retryFiles = new List<ExportedFile>();
            foreach (var file in box.Files)
            {
                try
                {
                    string path = ResolveInside(boxDirectory, file.RelativePath);
                    if (!File.Exists(path)) continue;
                    EnsureNoReparsePoints(boxDirectory, path);
                    if (!FingerprintMatches(path, file))
                    {
                        result.PreservedFiles++;
                        continue;
                    }

                    File.Delete(path);
                    result.DeletedFiles++;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"“{file.RelativePath}”未能清理：{ex.Message}");
                    retryFiles.Add(file);
                }
            }

            if (retryFiles.Count > 0)
            {
                box.Files = retryFiles;
                retryBoxes.Add(box);
                continue;
            }

            try
            {
                DeleteMarker(boxDirectory);
                result.DeletedFolders += DeleteTrackedEmptyDirectories(boxDirectory, box.Directories, result.Errors);
                if (!Directory.EnumerateFileSystemEntries(boxDirectory).Any())
                {
                    Directory.Delete(boxDirectory);
                    result.DeletedFolders++;
                }
            }
            catch (Exception ex)
            {
                result.Errors.Add($"盒子“{box.BoxName}”的空目录未能清理：{ex.Message}");
            }
        }

        manifest.Boxes = retryBoxes;
        if (manifest.Boxes.Count == 0)
            File.Delete(manifestPath);
        else
            SaveManifest(manifestPath, manifest);
        return result;
    }

    public static string SanitizeFolderName(string? name)
    {
        string value = string.IsNullOrWhiteSpace(name) ? "未命名盒子" : name.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        value = new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray())
            .TrimEnd(' ', '.');
        if (string.IsNullOrWhiteSpace(value)) value = "未命名盒子";
        if (value.Length > MaxFolderNameLength) value = value[..MaxFolderNameLength].TrimEnd(' ', '.');

        string stem = value.Split('.')[0];
        if (ReservedDeviceNames.Contains(stem)) value = "_" + value;
        return value;
    }

    private static void CopyItem(
        string source,
        string boxDirectory,
        ExportedBox record,
        ExportManifest manifest,
        string manifestPath,
        ExportResult result)
    {
        source = Path.GetFullPath(source);
        if (BoxFileTransfers.IsMissing(source)) throw new FileNotFoundException("源项目不存在。", source);
        if (IsInside(source, boxDirectory) || IsInside(boxDirectory, source))
            throw new IOException("源项目与导出目录互相包含，已跳过以避免递归复制。");

        bool isDirectory = Directory.Exists(source);
        string destination = FindAvailableEntryPath(boxDirectory, Path.GetFileName(source), isDirectory);
        CopyEntry(source, destination, boxDirectory, record, manifest, manifestPath, result);
    }

    private static void CopyEntry(
        string source,
        string destination,
        string boxDirectory,
        ExportedBox record,
        ExportManifest manifest,
        string manifestPath,
        ExportResult result)
    {
        FileAttributes attributes = File.GetAttributes(source);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("暂不复制符号链接、目录联接或云占位文件。");

        if ((attributes & FileAttributes.Directory) != 0)
        {
            Directory.CreateDirectory(destination);
            record.Directories.Add(new ExportedDirectory
            {
                RelativePath = Path.GetRelativePath(boxDirectory, destination),
                CreationTimeUtcTicks = Directory.GetCreationTimeUtc(destination).Ticks,
            });
            SaveManifest(manifestPath, manifest);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                try
                {
                    CopyEntry(entry, Path.Combine(destination, Path.GetFileName(entry)), boxDirectory, record, manifest, manifestPath, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"“{entry}”导出失败：{ex.Message}");
                }
            }
            return;
        }

        File.Copy(source, destination, overwrite: false);
        record.Files.Add(CreateFingerprint(boxDirectory, destination));
        SaveManifest(manifestPath, manifest);
        result.CopiedFiles++;
    }

    private static ExportedFile CreateFingerprint(string boxDirectory, string path)
    {
        var info = new FileInfo(path);
        return new ExportedFile
        {
            RelativePath = Path.GetRelativePath(boxDirectory, path),
            Length = info.Length,
            Sha256 = ComputeSha256(path),
        };
    }

    private static bool FingerprintMatches(string path, ExportedFile expected)
    {
        var info = new FileInfo(path);
        return info.Length == expected.Length &&
               string.Equals(ComputeSha256(path), expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string FindAvailableBoxFolder(string desktop, string requested, HashSet<string> used)
    {
        for (int number = 1; number < 10000; number++)
        {
            string candidate = number == 1 ? requested : $"{requested} ({number})";
            string path = Path.Combine(desktop, candidate);
            if (used.Add(candidate) && !File.Exists(path) && !Directory.Exists(path)) return candidate;
        }
        throw new IOException("同名盒子过多，无法创建独立文件夹。");
    }

    private static string FindAvailableEntryPath(string directory, string name, bool isDirectory)
    {
        string extension = isDirectory ? "" : Path.GetExtension(name);
        string stem = isDirectory ? name : Path.GetFileNameWithoutExtension(name);
        for (int number = 1; number < 10000; number++)
        {
            string candidateName = number == 1 ? name : $"{stem} ({number}){extension}";
            string candidate = Path.Combine(directory, candidateName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("同名项目过多，无法安全导出。");
    }

    private static void WriteMarker(string boxDirectory, ExportedBox record)
    {
        string path = Path.Combine(boxDirectory, MarkerFileName);
        var marker = new ExportMarker { Version = ManifestVersion, BoxId = record.BoxId, SessionId = record.SessionId };
        File.WriteAllText(path, JsonSerializer.Serialize(marker, JsonOptions));
        try { File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden); } catch { }
    }

    private static void ValidateMarker(string boxDirectory, ExportedBox record)
    {
        EnsureNoReparsePoints(boxDirectory, boxDirectory);
        string path = Path.Combine(boxDirectory, MarkerFileName);
        if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("缺少程序生成的安全标记。");
        var marker = JsonSerializer.Deserialize<ExportMarker>(File.ReadAllText(path), JsonOptions);
        if (marker == null || marker.Version != ManifestVersion || marker.BoxId != record.BoxId || marker.SessionId != record.SessionId)
            throw new IOException("安全标记与导出清单不匹配。");
    }

    private static void DeleteMarker(string boxDirectory)
    {
        string marker = Path.Combine(boxDirectory, MarkerFileName);
        if (File.Exists(marker)) File.Delete(marker);
    }

    private static int DeleteTrackedEmptyDirectories(string root, IEnumerable<ExportedDirectory> directories, List<string> errors)
    {
        int deleted = 0;
        foreach (var record in directories.OrderByDescending(directory => directory.RelativePath.Count(character => character == Path.DirectorySeparatorChar)))
        {
            try
            {
                string path = ResolveInside(root, record.RelativePath);
                if (!Directory.Exists(path)) continue;
                EnsureNoReparsePoints(root, path);
                if (Directory.GetCreationTimeUtc(path).Ticks != record.CreationTimeUtcTicks) continue;
                if (!Directory.EnumerateFileSystemEntries(path).Any())
                {
                    Directory.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"空目录“{record.RelativePath}”未能清理：{ex.Message}");
            }
        }
        return deleted;
    }

    private static void EnsureNoReparsePoints(string root, string path)
    {
        string fullRoot = Path.GetFullPath(root);
        string? current = Directory.Exists(path) ? Path.GetFullPath(path) : Path.GetDirectoryName(Path.GetFullPath(path));
        while (current != null && IsInsideOrSame(fullRoot, current))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("导出路径包含符号链接或目录联接，已拒绝自动删除。");
            if (BoxFileTransfers.SamePath(current, fullRoot)) break;
            current = Path.GetDirectoryName(current);
        }
    }

    private static string ResolveInside(string root, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("导出清单包含绝对路径。");
        string full = Path.GetFullPath(relative, root);
        if (!IsInside(root, full)) throw new IOException("导出清单路径超出了桌面盒子目录。");
        return full;
    }

    private static ExportManifest LoadManifestForExport(string path, string desktop)
    {
        if (!File.Exists(path)) return new ExportManifest { Version = ManifestVersion, DesktopDirectory = desktop };
        ExportManifest manifest = LoadManifest(path);
        if (manifest.Version != ManifestVersion || !BoxFileTransfers.SamePath(manifest.DesktopDirectory, desktop))
            throw new IOException("已有桌面导出清单无法安全继续，请先处理现有桌面副本。");
        return manifest;
    }

    private static ExportManifest LoadManifest(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<ExportManifest>(File.ReadAllText(path), JsonOptions)
                   ?? throw new IOException("桌面导出清单为空。");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new IOException("桌面导出清单已损坏，已保留桌面文件。", ex);
        }
    }

    private static void SaveManifest(string path, ExportManifest manifest)
    {
        string directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        string temp = Path.Combine(directory, $"desktop-exports.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, manifest, JsonOptions);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(path)) File.Replace(temp, path, null, ignoreMetadataErrors: true);
            else File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static string GetManifestPath(string dataDirectory) => Path.Combine(Path.GetFullPath(dataDirectory), ManifestFileName);

    private static bool IsInside(string parent, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    private static bool IsInsideOrSame(string parent, string path) =>
        BoxFileTransfers.SamePath(parent, path) || IsInside(parent, path);

    private static readonly HashSet<string> ReservedDeviceNames = new(
        new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" },
        StringComparer.OrdinalIgnoreCase);

    private sealed class ExportManifest
    {
        public int Version { get; set; }
        public string DesktopDirectory { get; set; } = "";
        public List<ExportedBox> Boxes { get; set; } = new();
    }

    private sealed class ExportedBox
    {
        public Guid BoxId { get; set; }
        public string BoxName { get; set; } = "";
        public string RelativeDirectory { get; set; } = "";
        public string SessionId { get; set; } = "";
        public List<ExportedFile> Files { get; set; } = new();
        public List<ExportedDirectory> Directories { get; set; } = new();
    }

    private sealed class ExportedFile
    {
        public string RelativePath { get; set; } = "";
        public long Length { get; set; }
        public string Sha256 { get; set; } = "";
    }

    private sealed class ExportedDirectory
    {
        public string RelativePath { get; set; } = "";
        public long CreationTimeUtcTicks { get; set; }
    }

    private sealed class ExportMarker
    {
        public int Version { get; set; }
        public Guid BoxId { get; set; }
        public string SessionId { get; set; } = "";
    }
}

public sealed class ExportResult
{
    public int CopiedFiles { get; internal set; }
    public List<string> BoxDirectories { get; } = new();
    public List<string> Errors { get; } = new();
}

public sealed class CleanupResult
{
    public int DeletedFiles { get; internal set; }
    public int PreservedFiles { get; internal set; }
    public int DeletedFolders { get; internal set; }
    public List<string> Errors { get; } = new();
}
