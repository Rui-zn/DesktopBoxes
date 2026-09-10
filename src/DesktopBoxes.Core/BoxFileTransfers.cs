using System.Text.Json;

namespace DesktopBoxes.Core;

/// <summary>Move files, then reconcile models. A flushed journal bridges filesystem/config commits.</summary>
public sealed class BoxFileTransfers
{
    private readonly string _dataDir;
    private readonly Func<IReadOnlyList<Box>> _boxes;
    private readonly Action _save;
    private readonly Action<string, string> _move;

    public BoxFileTransfers(string dataDir, Func<IReadOnlyList<Box>> boxes, Action save, Action<string, string> move)
    {
        _dataDir = Path.GetFullPath(dataDir);
        _boxes = boxes;
        _save = save;
        _move = move;
    }

    public bool IsBusy { get; private set; }

    public void ValidateForExport(BoxItem item) => ValidateSource(item.Path);

    public void Import(Box target, string source, Box? sourceBox = null, BoxItem? sourceItem = null)
    {
        source = Path.GetFullPath(source);
        ValidateSource(source);
        if (sourceBox == target) return;
        if (sourceBox != null && (sourceItem == null || !sourceBox.Items.Contains(sourceItem) || !SamePath(sourceItem.Path, source)))
            throw new IOException("拖动的项目已变化，请重新拖动。");
        if (target.Items.Any(i => i.StoragePath != null && SamePath(i.Path, source))) return;

        string relative = Path.Combine("items", target.Id.ToString("N"), Guid.NewGuid().ToString("N"), Path.GetFileName(source));
        var item = new BoxItem
        {
            Path = Path.Combine(_dataDir, relative),
            StoragePath = relative,
            Kind = BoxItem.DetectKind(source),
            DisplayName = sourceItem?.DisplayName ?? (Directory.Exists(source) ? Path.GetFileName(source) : Path.GetFileNameWithoutExtension(source)),
            ResolvedTarget = sourceItem?.ResolvedTarget,
        };
        MoveWithJournal(new Transfer { Source = source, Destination = item.Path, TargetBoxId = target.Id, Item = item });
    }

    public void ReturnToDirectory(Box box, BoxItem item, string directory)
    {
        if (IsBusy) throw new IOException("正在移动文件，请等待当前操作完成。");
        if (!box.Items.Contains(item)) return;
        directory = Path.GetFullPath(directory);
        // A legacy desktop reference already lives on the desktop: only remove the reference.
        if (SamePath(Path.GetDirectoryName(item.Path)!, directory))
        {
            box.Items.Remove(item);
            _save();
            return;
        }
        ValidateSource(item.Path);
        Directory.CreateDirectory(directory);
        string destination = AvailablePath(directory, Path.GetFileName(item.Path));
        MoveWithJournal(new Transfer { Source = item.Path, Destination = destination, Item = item });
    }

    /// <summary>Explorer usually performs the complete move. Never delete a file based on a drop-effect flag.</summary>
    public bool CompleteExternalMove(Box box, BoxItem item)
    {
        if (!box.Items.Contains(item) || !IsMissing(item.Path)) return false;
        RemoveReferences(item.Path);
        _save();
        return true;
    }

    private void MoveWithJournal(Transfer transfer)
    {
        if (IsBusy) throw new IOException("正在移动文件，请等待当前操作完成。");
        IsBusy = true;
        string journal = Path.Combine(_dataDir, "transfers", Guid.NewGuid().ToString("N") + ".json");
        try
        {
            // Verify that current configuration is writable before touching any user file.
            _save();
            Directory.CreateDirectory(Path.GetDirectoryName(journal)!);
            using (var stream = new FileStream(journal, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, transfer);
                stream.Flush(true);
            }
            Exception? failure = null;
            try
            {
                if (!IsMissing(transfer.Destination)) throw new IOException("目标已经存在，请重新操作；没有覆盖任何文件。");
                Directory.CreateDirectory(Path.GetDirectoryName(transfer.Destination)!);
                _move(transfer.Source, transfer.Destination);
            }
            catch (Exception ex) { failure = ex; }

            Reconcile(transfer, _boxes());
            _save(); // If this fails the journal remains for startup recovery.
            bool sourceMissing = IsMissing(transfer.Source);
            bool destinationMissing = IsMissing(transfer.Destination);
            if (sourceMissing || destinationMissing) File.Delete(journal);
            if (failure != null || !sourceMissing || destinationMissing)
            {
                string detail = !sourceMissing && !destinationMissing
                    ? "部分内容可能已移动，源位置和目标位置均已保留，请检查两处文件。"
                    : "未完成的项目保留在原位置；已完成的项目可从盒子或目标目录访问。";
                throw new IOException($"{failure?.Message ?? "移动未完成。"}\n{detail}\n源：{transfer.Source}\n目标：{transfer.Destination}", failure);
            }
        }
        finally { IsBusy = false; }
    }

    private void Reconcile(Transfer transfer, IReadOnlyList<Box> boxes)
    {
        bool sourceMissing = IsMissing(transfer.Source);
        bool destinationMissing = IsMissing(transfer.Destination);
        if (!destinationMissing && transfer.TargetBoxId is Guid targetId)
        {
            var target = boxes.FirstOrDefault(b => b.Id == targetId);
            if (target == null) throw new IOException("目标盒子已不存在。文件仍保留在：" + transfer.Destination);
            // Replace old references in this box, but do not duplicate an already recovered item.
            if (sourceMissing) target.Items.RemoveAll(i => SamePath(i.Path, transfer.Source));
            if (!target.Items.Any(i => SamePath(i.Path, transfer.Destination))) target.Items.Add(transfer.Item);
        }
        if (sourceMissing && !destinationMissing)
        {
            foreach (var box in boxes)
                box.Items.RemoveAll(i => SamePath(i.Path, transfer.Source));
        }
    }

    private void RemoveReferences(string path)
    {
        foreach (var box in _boxes()) box.Items.RemoveAll(i => SamePath(i.Path, path));
    }

    private void ValidateSource(string source)
    {
        if (IsMissing(source)) throw new FileNotFoundException("项目不存在，无法移动。", source);
        string full = Path.GetFullPath(source);
        string root = Path.GetPathRoot(full)!;
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (SamePath(full, root) || (!string.IsNullOrEmpty(desktop) && SamePath(full, desktop)) || SamePath(full, _dataDir) || IsInside(full, _dataDir) ||
            (!string.IsNullOrEmpty(windows) && (SamePath(full, windows) || IsInside(windows, full))) ||
            SamePath(full, AppContext.BaseDirectory) || IsInside(full, AppContext.BaseDirectory))
            throw new IOException("不能移动磁盘根目录、桌面目录、系统目录或包含程序 / 盒子数据的目录。");
        if (IsInside(_dataDir, full) && !_boxes().SelectMany(b => b.Items).Any(i => i.StoragePath != null && SamePath(i.Path, full)))
            throw new IOException("不能直接移动程序配置或托管目录，请从盒子内拖动具体项目。");
        // Do not let a junction/symlink alias bypass the ancestor check.
        for (string? part = full; part != null; part = Path.GetDirectoryName(part))
            if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("暂不移动符号链接、目录联接或未本地化的云文件，请先使用普通本地文件。");
        if (Directory.Exists(full))
        {
            var pending = new Stack<string>();
            pending.Push(full);
            while (pending.Count > 0)
                foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("文件夹含有链接或云占位文件，未执行移动：" + entry);
                    if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
                }
        }
    }

    public static string AvailablePath(string directory, string name)
    {
        string path = Path.Combine(directory, name);
        if (IsMissing(path)) return path;
        string extension = Path.GetExtension(name);
        string stem = Path.GetFileNameWithoutExtension(name);
        for (int n = 2; n < 10000; n++)
        {
            path = Path.Combine(directory, $"{stem} ({n}){extension}");
            if (IsMissing(path)) return path;
        }
        throw new IOException("同名项目过多，请先重命名后再移动。");
    }

    public static bool IsMissing(string path)
    {
        try { _ = File.GetAttributes(path); return false; }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        // Access denied/offline volumes are NOT evidence that a move succeeded.
    }

    public static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)), StringComparison.OrdinalIgnoreCase);

    private static bool IsInside(string parent, string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static string ResolveStoredPath(string dataDir, string relative)
    {
        if (Path.IsPathRooted(relative)) throw new IOException("托管文件路径必须为相对路径。");
        string root = Path.Combine(Path.GetFullPath(dataDir), "items");
        string full = Path.GetFullPath(relative, dataDir);
        if (!IsInside(root, full)) throw new IOException("托管文件路径超出了 items 目录。");
        return full;
    }

    public static void CopyManagedFiles(string sourceDataDir, string targetDataDir)
    {
        string source = Path.Combine(sourceDataDir, "items");
        string target = Path.Combine(targetDataDir, "items");
        if (!Directory.Exists(source)) return;
        if (Directory.Exists(target) || File.Exists(target)) throw new IOException("目标目录已有 items 文件，请选择空的数据目录，避免覆盖托管文件。");
        if (SamePath(source, target) || IsInside(source, target) || IsInside(target, source)) throw new IOException("迁移目录不能互相包含。");
        void CopyTree(string from, string to)
        {
            if ((File.GetAttributes(from) & FileAttributes.ReparsePoint) != 0) throw new IOException("不能迁移目录联接：" + from);
            Directory.CreateDirectory(to);
            foreach (string entry in Directory.EnumerateFileSystemEntries(from))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("不能迁移链接或云占位文件：" + entry);
                string destination = Path.Combine(to, Path.GetFileName(entry));
                if ((attributes & FileAttributes.Directory) != 0) CopyTree(entry, destination);
                else File.Copy(entry, destination, overwrite: false);
            }
        }
        CopyTree(source, target);
    }

    /// <summary>Read-only filesystem reconciliation, followed by a config commit; never moves/deletes user files.</summary>
    public static IReadOnlyList<string> Recover(string dataDir, List<Box> boxes, Action save)
    {
        var messages = new List<string>();
        var completed = new List<string>();
        var helper = new BoxFileTransfers(dataDir, () => boxes, save, (_, _) => throw new InvalidOperationException());
        string directory = Path.Combine(dataDir, "transfers");
        if (Directory.Exists(directory))
            foreach (string file in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    var transfer = JsonSerializer.Deserialize<Transfer>(File.ReadAllText(file)) ?? throw new IOException("空移动记录");
                    if (!Path.IsPathFullyQualified(transfer.Source) || !Path.IsPathFullyQualified(transfer.Destination)) throw new IOException("无效移动记录");
                    if (transfer.TargetBoxId is Guid targetId)
                    {
                        transfer.Destination = ResolveStoredPath(dataDir, transfer.Item.StoragePath ?? throw new IOException("缺少托管路径"));
                        transfer.Item.Path = transfer.Destination;
                        if (!IsMissing(transfer.Destination) && boxes.All(b => b.Id != targetId))
                            boxes.Add(new Box { Id = targetId, Name = "恢复的盒子" });
                    }
                    helper.Reconcile(transfer, boxes);
                    if (IsMissing(transfer.Source) || IsMissing(transfer.Destination)) completed.Add(file);
                    else messages.Add($"未完成的移动，请检查两处内容：\n{transfer.Source}\n{transfer.Destination}");
                }
                catch (Exception ex) { messages.Add($"移动记录已保留：{file}\n{ex.Message}"); }
            }
        // Handles an Explorer move followed by a crash before DoDragDrop returned.
        foreach (var box in boxes)
            foreach (var item in box.Items.Where(i => i.StoragePath != null).ToList())
            {
                try { if (IsMissing(item.Path)) box.Items.Remove(item); }
                catch { /* Unavailable storage must not erase references. */ }
            }
        // A corrupt/stale config must not make already-stored files disappear from the UI.
        string itemsRoot = Path.Combine(dataDir, "items");
        if (Directory.Exists(itemsRoot))
            foreach (string boxDirectory in Directory.EnumerateDirectories(itemsRoot))
            {
                if (!Guid.TryParseExact(Path.GetFileName(boxDirectory), "N", out Guid id) || IsReparse(boxDirectory)) continue;
                foreach (string operationDirectory in Directory.EnumerateDirectories(boxDirectory))
                {
                    if (!Guid.TryParseExact(Path.GetFileName(operationDirectory), "N", out _) || IsReparse(operationDirectory)) continue;
                    foreach (string entry in Directory.EnumerateFileSystemEntries(operationDirectory))
                    {
                        if (IsReparse(entry) || boxes.SelectMany(b => b.Items).Any(i => SamePath(i.Path, entry))) continue;
                        var box = boxes.FirstOrDefault(b => b.Id == id);
                        if (box == null) { box = new Box { Id = id, Name = "恢复的盒子" }; boxes.Add(box); }
                        box.Items.Add(new BoxItem
                        {
                            Path = entry,
                            StoragePath = Path.GetRelativePath(dataDir, entry),
                            Kind = BoxItem.DetectKind(entry),
                            DisplayName = Directory.Exists(entry) ? Path.GetFileName(entry) : Path.GetFileNameWithoutExtension(entry)
                        });
                    }
                }
            }
        save();
        foreach (string file in completed) File.Delete(file);
        return messages;
    }

    private static bool IsReparse(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    public sealed class Transfer
    {
        public string Source { get; set; } = "";
        public string Destination { get; set; } = "";
        public Guid? TargetBoxId { get; set; }
        public BoxItem Item { get; set; } = new();
    }
}
