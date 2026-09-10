namespace DesktopBoxes.Core;

/// <summary>Creates a non-destructive, human-readable copy of every box on the desktop.</summary>
public static class BoxExitExporter
{
    private const int MaxFolderNameLength = 120;

    public static ExportResult Export(string desktopDirectory, IReadOnlyList<Box> boxes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopDirectory);
        ArgumentNullException.ThrowIfNull(boxes);

        string desktop = Path.GetFullPath(desktopDirectory);
        Directory.CreateDirectory(desktop);

        var result = new ExportResult();
        var usedFolderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var box in boxes)
        {
            string folderName = MakeUniqueFolderName(SanitizeFolderName(box.Name), usedFolderNames);
            string boxDirectory = Path.Combine(desktop, folderName);
            try
            {
                if (File.Exists(boxDirectory))
                {
                    folderName = MakeUniqueAvailableFolderName(desktop, folderName, usedFolderNames);
                    boxDirectory = Path.Combine(desktop, folderName);
                }

                Directory.CreateDirectory(boxDirectory);
                result.BoxDirectories.Add(boxDirectory);
            }
            catch (Exception ex)
            {
                result.Errors.Add($"盒子“{box.Name}”无法创建导出文件夹：{ex.Message}");
                continue;
            }

            foreach (var item in box.Items)
            {
                try
                {
                    CopyItem(item.Path, boxDirectory, result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"盒子“{box.Name}”中的“{item.DisplayName}”导出失败：{ex.Message}");
                }
            }
        }

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

    private static void CopyItem(string source, string boxDirectory, ExportResult result)
    {
        source = Path.GetFullPath(source);
        if (BoxFileTransfers.IsMissing(source)) throw new FileNotFoundException("源项目不存在。", source);

        string destination = Path.Combine(boxDirectory, Path.GetFileName(source));
        if (BoxFileTransfers.SamePath(source, destination))
        {
            result.UnchangedFiles++;
            return;
        }
        if (IsInside(source, destination) || IsInside(destination, source))
            throw new IOException("源项目与导出目录互相包含，已跳过以避免递归复制。");

        CopyEntry(source, destination, result);
    }

    private static void CopyEntry(string source, string destination, ExportResult result)
    {
        FileAttributes attributes = File.GetAttributes(source);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("暂不复制符号链接、目录联接或云占位文件。");

        if ((attributes & FileAttributes.Directory) != 0)
        {
            destination = FindAvailableDirectory(destination);
            Directory.CreateDirectory(destination);
            foreach (string entry in Directory.EnumerateFileSystemEntries(source))
            {
                try
                {
                    CopyEntry(entry, Path.Combine(destination, Path.GetFileName(entry)), result);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"“{entry}”导出失败：{ex.Message}");
                }
            }
            return;
        }

        string? fileDestination = FindAvailableOrMatchingFile(source, destination);
        if (fileDestination == null)
        {
            result.UnchangedFiles++;
            return;
        }

        File.Copy(source, fileDestination, overwrite: false);
        result.CopiedFiles++;
    }

    private static string? FindAvailableOrMatchingFile(string source, string requestedDestination)
    {
        string directory = Path.GetDirectoryName(requestedDestination)!;
        string name = Path.GetFileName(requestedDestination);
        string extension = Path.GetExtension(name);
        string stem = Path.GetFileNameWithoutExtension(name);
        for (int number = 1; number < 10000; number++)
        {
            string candidate = number == 1
                ? requestedDestination
                : Path.Combine(directory, $"{stem} ({number}){extension}");
            if (File.Exists(candidate))
            {
                if (FilesEqual(source, candidate)) return null;
                continue;
            }
            if (!Directory.Exists(candidate)) return candidate;
        }
        throw new IOException("同名项目过多，无法安全导出。");
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length) return false;

        const int bufferSize = 64 * 1024;
        using var leftStream = new FileStream(left, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize, FileOptions.SequentialScan);
        using var rightStream = new FileStream(right, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize, FileOptions.SequentialScan);
        Span<byte> leftBuffer = stackalloc byte[bufferSize];
        Span<byte> rightBuffer = stackalloc byte[bufferSize];
        while (true)
        {
            int leftRead = leftStream.Read(leftBuffer);
            int rightRead = rightStream.Read(rightBuffer);
            if (leftRead != rightRead) return false;
            if (leftRead == 0) return true;
            if (!leftBuffer[..leftRead].SequenceEqual(rightBuffer[..rightRead])) return false;
        }
    }

    private static string FindAvailableDirectory(string requestedDestination)
    {
        if (Directory.Exists(requestedDestination)) return requestedDestination;
        if (!File.Exists(requestedDestination)) return requestedDestination;

        string parent = Path.GetDirectoryName(requestedDestination)!;
        string name = Path.GetFileName(requestedDestination);
        for (int number = 2; number < 10000; number++)
        {
            string candidate = Path.Combine(parent, $"{name} ({number})");
            if (Directory.Exists(candidate)) return candidate;
            if (!File.Exists(candidate)) return candidate;
        }
        throw new IOException("同名项目过多，无法安全导出。");
    }

    private static string MakeUniqueFolderName(string requested, HashSet<string> used)
    {
        if (used.Add(requested)) return requested;
        for (int number = 2; number < 10000; number++)
        {
            string candidate = $"{requested} ({number})";
            if (used.Add(candidate)) return candidate;
        }
        throw new IOException("同名盒子过多，无法创建独立文件夹。");
    }

    private static string MakeUniqueAvailableFolderName(string desktop, string requested, HashSet<string> used)
    {
        for (int number = 2; number < 10000; number++)
        {
            string candidate = $"{requested} ({number})";
            string path = Path.Combine(desktop, candidate);
            if (!used.Contains(candidate) && !File.Exists(path))
            {
                used.Add(candidate);
                return candidate;
            }
        }
        throw new IOException("同名文件夹过多，无法创建盒子导出目录。");
    }

    private static bool IsInside(string parent, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> ReservedDeviceNames = new(
        new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" },
        StringComparer.OrdinalIgnoreCase);
}

public sealed class ExportResult
{
    public int CopiedFiles { get; internal set; }
    public int UnchangedFiles { get; internal set; }
    public List<string> BoxDirectories { get; } = new();
    public List<string> Errors { get; } = new();
}
