using System.Text.Json;
using DesktopBoxes.Core;

namespace DesktopBoxes.Tests;

public sealed class BoxFileTransfersTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "desktopboxes-move-test-" + Guid.NewGuid().ToString("N"));
    private readonly List<Box> _boxes = new() { new Box { Name = "A" }, new Box { Name = "B" } };
    private string Data => Path.Combine(_fixture, "data");
    private string Desktop => Path.Combine(_fixture, "desktop");
    private BoxFileTransfers Service(Action? save = null, Action<string, string>? move = null) =>
        new(Data, () => _boxes, save ?? (() => new BoxStore(Data).Save(_boxes)), move ?? LocalMove);
    private static void LocalMove(string source, string destination)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination, false);
    }
    private string FileAt(string name = "notes.txt", string content = "original")
    {
        Directory.CreateDirectory(Desktop);
        string file = Path.Combine(Desktop, name);
        File.WriteAllText(file, content);
        return file;
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("app.lnk")]
    [InlineData("portable.exe")]
    public void ImportAndReturn_MovesOriginalBytesAndPersists(string name)
    {
        string source = FileAt(name);
        var service = Service();
        service.Import(_boxes[0], source);
        var item = Assert.Single(_boxes[0].Items);
        Assert.False(File.Exists(source));
        Assert.Equal("original", File.ReadAllText(item.Path));
        Assert.NotNull(item.StoragePath);
        Assert.Equal(item.Path, new BoxStore(Data).Load()[0].Items[0].Path);
        string storedPath = item.Path;
        service.ReturnToDirectory(_boxes[0], item, Desktop);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(storedPath));
        Assert.Empty(_boxes[0].Items);
    }

    [Fact]
    public void FolderRoundTrip_PreservesTreeAndEmptyFolders()
    {
        string folder = Path.Combine(Desktop, "work");
        Directory.CreateDirectory(Path.Combine(folder, "empty"));
        File.WriteAllText(Path.Combine(folder, "a.txt"), "nested");
        var service = Service();
        service.Import(_boxes[0], folder);
        var item = Assert.Single(_boxes[0].Items);
        Assert.False(Directory.Exists(folder));
        Assert.True(Directory.Exists(Path.Combine(item.Path, "empty")));
        service.ReturnToDirectory(_boxes[0], item, Desktop);
        Assert.Equal("nested", File.ReadAllText(Path.Combine(folder, "a.txt")));
        Assert.True(Directory.Exists(Path.Combine(folder, "empty")));
    }

    [Fact]
    public void DuplicateNames_NeverOverwriteInEitherDirection()
    {
        var service = Service();
        service.Import(_boxes[0], FileAt(content: "first"));
        service.Import(_boxes[0], FileAt(content: "second"));
        Assert.Equal(2, _boxes[0].Items.Count);
        Assert.Equal(2, _boxes[0].Items.Select(i => i.Path).Distinct().Count());
        FileAt(content: "desktop");
        foreach (var item in _boxes[0].Items.ToList()) service.ReturnToDirectory(_boxes[0], item, Desktop);
        Assert.Equal("desktop", File.ReadAllText(Path.Combine(Desktop, "notes.txt")));
        Assert.Equal("first", File.ReadAllText(Path.Combine(Desktop, "notes (2).txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(Desktop, "notes (3).txt")));
    }

    [Fact]
    public void InternalMove_PreservesCustomNameAndRemovesOnlySourceReference()
    {
        var service = Service();
        service.Import(_boxes[0], FileAt());
        var item = _boxes[0].Items[0];
        item.DisplayName = "我的自定义名称";
        string original = item.Path;
        service.Import(_boxes[1], item.Path, _boxes[0], item);
        Assert.Empty(_boxes[0].Items);
        var moved = Assert.Single(_boxes[1].Items);
        Assert.Equal("我的自定义名称", moved.DisplayName);
        Assert.Equal("original", File.ReadAllText(moved.Path));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public void SelfDrop_IsNoOp()
    {
        var service = Service();
        service.Import(_boxes[0], FileAt());
        var item = _boxes[0].Items[0];
        service.Import(_boxes[0], item.Path, _boxes[0], item);
        Assert.Same(item, Assert.Single(_boxes[0].Items));
        Assert.True(File.Exists(item.Path));
    }

    [Fact]
    public void CancelledOrRejectedMove_KeepsSourceAndModel()
    {
        string source = FileAt();
        var service = Service(move: (_, _) => throw new OperationCanceledException());
        Assert.Throws<IOException>(() => service.Import(_boxes[0], source));
        Assert.True(File.Exists(source));
        Assert.Empty(_boxes[0].Items);
        Assert.Empty(Directory.GetFiles(Path.Combine(Data, "transfers")));
        Assert.False(service.IsBusy);
    }

    [Fact]
    public void SaveFailureBeforeMove_DoesNotTouchSource()
    {
        string source = FileAt();
        Assert.Throws<IOException>(() => Service(save: () => throw new IOException("readonly config")).Import(_boxes[0], source));
        Assert.True(File.Exists(source));
        Assert.Empty(_boxes[0].Items);
        Assert.False(Directory.Exists(Path.Combine(Data, "items")));
    }

    [Fact]
    public void SaveFailureAfterMove_JournalRecoversExactlyOnce()
    {
        string source = FileAt();
        int saves = 0;
        var store = new BoxStore(Data);
        var service = Service(save: () => { if (++saves > 1) throw new IOException("disk unavailable"); store.Save(_boxes); });
        Assert.Throws<IOException>(() => service.Import(_boxes[0], source));
        string destination = Assert.Single(_boxes[0].Items).Path;
        Assert.True(File.Exists(destination));
        Assert.Single(Directory.GetFiles(Path.Combine(Data, "transfers")));
        var reloaded = store.Load();
        Assert.Empty(reloaded[0].Items);
        BoxFileTransfers.Recover(Data, reloaded, () => store.Save(reloaded));
        BoxFileTransfers.Recover(Data, reloaded, () => store.Save(reloaded));
        Assert.Equal(destination, Assert.Single(reloaded[0].Items).Path);
        Assert.Empty(Directory.GetFiles(Path.Combine(Data, "transfers")));
    }

    [Fact]
    public void PartialFolderMove_KeepsBothAccessibleAndKeepsJournal()
    {
        string source = Path.Combine(Desktop, "folder");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "a.txt"), "a");
        File.WriteAllText(Path.Combine(source, "b.txt"), "b");
        var service = Service(move: (from, to) =>
        {
            Directory.CreateDirectory(to);
            File.Move(Path.Combine(from, "a.txt"), Path.Combine(to, "a.txt"));
            throw new OperationCanceledException();
        });
        Assert.Throws<IOException>(() => service.Import(_boxes[0], source));
        var item = Assert.Single(_boxes[0].Items);
        Assert.Equal("a", File.ReadAllText(Path.Combine(item.Path, "a.txt")));
        Assert.Equal("b", File.ReadAllText(Path.Combine(source, "b.txt")));
        Assert.Single(Directory.GetFiles(Path.Combine(Data, "transfers")));
    }

    [Fact]
    public void Export_CancelOrCopyKeepsItem_ConfirmedMoveRemovesIt()
    {
        var service = Service();
        service.Import(_boxes[0], FileAt());
        var item = _boxes[0].Items[0];
        Assert.False(service.CompleteExternalMove(_boxes[0], item));
        string target = Path.Combine(Desktop, "copied.txt");
        File.Copy(item.Path, target);
        Assert.False(service.CompleteExternalMove(_boxes[0], item));
        File.Move(item.Path, Path.Combine(Desktop, "moved.txt"));
        Assert.True(service.CompleteExternalMove(_boxes[0], item));
        Assert.Empty(_boxes[0].Items);
        Assert.True(File.Exists(target));
    }

    [Fact]
    public void StaleConfigRecovery_FindsOrphansAndRemovesOnlyMissingManagedItems()
    {
        var service = Service();
        service.Import(_boxes[0], FileAt());
        string stored = _boxes[0].Items[0].Path;
        _boxes.Clear(); // Simulate missing/corrupt config, not missing files.
        BoxFileTransfers.Recover(Data, _boxes, () => { });
        Assert.Equal(stored, Assert.Single(Assert.Single(_boxes).Items).Path);
        _boxes[0].Items.Add(new BoxItem { Path = Path.Combine(Desktop, "missing-legacy.txt") });
        File.Move(stored, Path.Combine(Desktop, "moved.txt"));
        BoxFileTransfers.Recover(Data, _boxes, () => { });
        Assert.Null(Assert.Single(_boxes[0].Items).StoragePath);
    }

    [Fact]
    public void Migration_CopiesManagedBytes_RebasesPaths_LeavesOriginalIntact()
    {
        Service().Import(_boxes[0], FileAt());
        var item = _boxes[0].Items[0];
        string oldPath = item.Path;
        string newData = Path.Combine(_fixture, "new-data");
        BoxFileTransfers.CopyManagedFiles(Data, newData);
        new BoxStore(newData).Save(_boxes);
        var moved = new BoxStore(newData).Load()[0].Items[0];
        Assert.NotEqual(oldPath, moved.Path);
        Assert.Equal("original", File.ReadAllText(moved.Path));
        Assert.True(File.Exists(oldPath));
        Assert.Throws<IOException>(() => BoxFileTransfers.CopyManagedFiles(Data, newData));
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("items/../../secret.txt")]
    [InlineData("C:/Windows/file.txt")]
    public void ManagedPaths_CannotEscapeStorage(string relative)
    {
        Assert.Throws<IOException>(() => BoxFileTransfers.ResolveStoredPath(Data, relative));
    }

    [Fact]
    public void ProtectedStorageAndAncestor_AreRejectedBeforeMove()
    {
        Directory.CreateDirectory(Data);
        Assert.Throws<IOException>(() => Service().Import(_boxes[0], Data));
        Assert.Throws<IOException>(() => Service().Import(_boxes[0], _fixture));
        Assert.True(Directory.Exists(Data));
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(_fixture);
        string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("desktopboxes-move-test-"))
            throw new InvalidOperationException("Unsafe fixture cleanup path.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
