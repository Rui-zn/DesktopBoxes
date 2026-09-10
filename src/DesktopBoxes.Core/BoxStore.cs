using System.IO;
using System.Text.Json;

namespace DesktopBoxes.Core;

/// <summary>盒子的 JSON 持久化（boxes.json）。</summary>
public class BoxStore
{
    private readonly string _dir;
    private readonly string _file;
    private readonly string _backupFile;
    private readonly JsonSerializerOptions _options = new() { WriteIndented = true };

    public bool RecoveredFromBackup { get; private set; }
    public string? PreservedCorruptFile { get; private set; }

    public BoxStore(string dataDir)
    {
        _dir = dataDir;
        _file = Path.Combine(dataDir, "boxes.json");
        _backupFile = Path.Combine(dataDir, "boxes.json.bak");
    }

    public List<Box> Load()
    {
        RecoveredFromBackup = false;
        PreservedCorruptFile = null;

        if (!File.Exists(_file))
        {
            return new List<Box>();
        }

        if (TryLoadFile(_file, out var boxes))
        {
            return boxes;
        }

        PreserveCorruptFile();
        if (TryLoadFile(_backupFile, out boxes))
        {
            RecoveredFromBackup = true;
            return boxes;
        }

        return new List<Box>();
    }

    private bool TryLoadFile(string path, out List<Box> boxes)
    {
        boxes = new List<Box>();
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<Box>>(json, _options);
            if (loaded == null || loaded.Any(box => box == null))
            {
                return false;
            }

            foreach (var box in loaded)
            {
                Normalize(box);
                foreach (var item in box.Items.Where(i => i.StoragePath != null))
                    item.Path = BoxFileTransfers.ResolveStoredPath(_dir, item.StoragePath!);
            }
            boxes = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void Normalize(Box box)
    {
        if (string.IsNullOrWhiteSpace(box.Name)) box.Name = "新建盒子";
        if (!double.IsFinite(box.X)) box.X = 120;
        if (!double.IsFinite(box.Y)) box.Y = 120;
        box.Width = double.IsFinite(box.Width) ? Math.Clamp(box.Width, 120, 10000) : 260;
        box.Height = double.IsFinite(box.Height) ? Math.Clamp(box.Height, 60, 10000) : 220;
        box.IconSize = box.IconSize is 32 or 48 or 64 ? box.IconSize : 32;
        box.CornerRadius = Math.Clamp(box.CornerRadius, 0, 100);
        box.Opacity = Math.Clamp(box.Opacity, 0, 100);

        box.Items ??= new List<BoxItem>();
        box.Items = box.Items
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Path))
            .ToList();
        foreach (var item in box.Items)
        {
            if (string.IsNullOrWhiteSpace(item.DisplayName))
            {
                item.DisplayName = Path.GetFileNameWithoutExtension(item.Path);
            }
        }
    }

    private void PreserveCorruptFile()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            string preserved = Path.Combine(
                _dir,
                $"boxes.corrupt-{DateTime.Now:yyyyMMdd-HHmmssfff}.json");
            File.Move(_file, preserved);
            PreservedCorruptFile = preserved;
        }
        catch
        {
            // 无法移动时不允许后续保存覆盖原文件。
            PreservedCorruptFile = _file;
        }
    }

    public void Save(List<Box> boxes)
    {
        Directory.CreateDirectory(_dir);
        if (PreservedCorruptFile == _file && File.Exists(_file))
        {
            throw new IOException("配置文件损坏且无法备份，已拒绝覆盖原文件。");
        }

        string tempFile = Path.Combine(_dir, $"boxes.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       tempFile,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, boxes, _options);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(_file))
            {
                File.Replace(tempFile, _file, _backupFile, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempFile, _file);
            }
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    public string DataDirectory => _dir;
}
