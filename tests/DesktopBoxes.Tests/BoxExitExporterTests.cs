using DesktopBoxes.Core;

namespace DesktopBoxes.Tests;

public sealed class BoxExitExporterTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "desktopboxes-exit-export-test-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_fixture, "source");
    private string Desktop => Path.Combine(_fixture, "desktop");
    private string Data => Path.Combine(_fixture, "data");

    [Fact]
    public void ExportThenCleanup_RemovesOnlyDesktopCopiesAndNamedFolders()
    {
        string file = FileAt("notes.txt", "hello");
        string folder = Path.Combine(Source, "project");
        Directory.CreateDirectory(Path.Combine(folder, "empty"));
        File.WriteAllText(Path.Combine(folder, "code.cs"), "code");
        var boxes = new List<Box>
        {
            new()
            {
                Name = "工作",
                Items =
                {
                    new BoxItem { Path = file, DisplayName = "notes" },
                    new BoxItem { Path = folder, DisplayName = "project" },
                },
            },
            new() { Name = "空盒子" },
        };

        ExportResult exported = BoxExitExporter.Export(Desktop, Data, boxes);
        Assert.Equal("hello", File.ReadAllText(Path.Combine(Desktop, "工作", "notes.txt")));
        Assert.True(Directory.Exists(Path.Combine(Desktop, "工作", "project", "empty")));
        Assert.True(File.Exists(Path.Combine(Data, "desktop-exports.json")));

        CleanupResult cleaned = BoxExitExporter.Cleanup(Desktop, Data);

        Assert.Equal(2, exported.CopiedFiles);
        Assert.Equal(2, cleaned.DeletedFiles);
        Assert.False(Directory.Exists(Path.Combine(Desktop, "工作")));
        Assert.False(Directory.Exists(Path.Combine(Desktop, "空盒子")));
        Assert.False(File.Exists(Path.Combine(Data, "desktop-exports.json")));
        Assert.True(File.Exists(file));
        Assert.Empty(exported.Errors);
        Assert.Empty(cleaned.Errors);
    }

    [Fact]
    public void Cleanup_PreservesModifiedAndNewUserContent()
    {
        string changedSource = FileAt("changed.txt", "original");
        string unchangedSource = FileAt("unchanged.txt", "same");
        var box = new Box { Name = "工作" };
        box.Items.Add(new BoxItem { Path = changedSource, DisplayName = "changed" });
        box.Items.Add(new BoxItem { Path = unchangedSource, DisplayName = "unchanged" });
        BoxExitExporter.Export(Desktop, Data, new[] { box });
        string boxDirectory = Path.Combine(Desktop, "工作");
        File.WriteAllText(Path.Combine(boxDirectory, "changed.txt"), "edited while stopped");
        Directory.CreateDirectory(Path.Combine(boxDirectory, "new empty folder"));
        File.WriteAllText(Path.Combine(boxDirectory, "new.txt"), "new");

        CleanupResult result = BoxExitExporter.Cleanup(Desktop, Data);

        Assert.Equal("edited while stopped", File.ReadAllText(Path.Combine(boxDirectory, "changed.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(boxDirectory, "new.txt")));
        Assert.True(Directory.Exists(Path.Combine(boxDirectory, "new empty folder")));
        Assert.False(File.Exists(Path.Combine(boxDirectory, "unchanged.txt")));
        Assert.False(File.Exists(Path.Combine(boxDirectory, ".desktopboxes-exit-export.json")));
        Assert.Equal(1, result.PreservedFiles);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Export_DoesNotAdoptAnExistingUnmarkedFolder()
    {
        string existing = Path.Combine(Desktop, "工作");
        Directory.CreateDirectory(existing);
        File.WriteAllText(Path.Combine(existing, "keep.txt"), "keep");
        string source = FileAt("notes.txt", "content");

        BoxExitExporter.Export(Desktop, Data, new[] { BoxWith("工作", source) });
        BoxExitExporter.Cleanup(Desktop, Data);

        Assert.Equal("keep", File.ReadAllText(Path.Combine(existing, "keep.txt")));
        Assert.False(Directory.Exists(Path.Combine(Desktop, "工作 (2)")));
    }

    [Fact]
    public void Cleanup_WithTamperedMarker_PreservesEverythingForReview()
    {
        string source = FileAt("notes.txt", "content");
        BoxExitExporter.Export(Desktop, Data, new[] { BoxWith("工作", source) });
        string marker = Path.Combine(Desktop, "工作", ".desktopboxes-exit-export.json");
        File.SetAttributes(marker, FileAttributes.Normal);
        File.WriteAllText(marker, "{}");

        CleanupResult result = BoxExitExporter.Cleanup(Desktop, Data);

        Assert.True(File.Exists(Path.Combine(Desktop, "工作", "notes.txt")));
        Assert.True(File.Exists(Path.Combine(Data, "desktop-exports.json")));
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Cleanup_ManifestPathCannotEscapeOwnedBoxFolder()
    {
        string source = FileAt("notes.txt", "content");
        BoxExitExporter.Export(Desktop, Data, new[] { BoxWith("工作", source) });
        string outside = Path.Combine(Desktop, "outside.txt");
        File.WriteAllText(outside, "keep");
        string manifest = Path.Combine(Data, "desktop-exports.json");
        string json = File.ReadAllText(manifest).Replace("notes.txt", "../outside.txt", StringComparison.Ordinal);
        File.WriteAllText(manifest, json);

        CleanupResult result = BoxExitExporter.Cleanup(Desktop, Data);

        Assert.Equal("keep", File.ReadAllText(outside));
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Export_DuplicateOrUnsafeBoxNamesGetSeparateSafeFolders()
    {
        string first = FileAt("first.txt", "1");
        string second = FileAt("second.txt", "2");

        BoxExitExporter.Export(Desktop, Data, new[] { BoxWith("A/B", first), BoxWith("A/B", second) });

        Assert.Equal("1", File.ReadAllText(Path.Combine(Desktop, "A_B", "first.txt")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(Desktop, "A_B (2)", "second.txt")));
    }

    private string FileAt(string name, string content)
    {
        Directory.CreateDirectory(Source);
        string path = Path.Combine(Source, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static Box BoxWith(string name, string path)
    {
        var box = new Box { Name = name };
        box.Items.Add(new BoxItem { Path = path, DisplayName = Path.GetFileNameWithoutExtension(path) });
        return box;
    }

    public void Dispose()
    {
        string full = Path.GetFullPath(_fixture);
        string tempRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("desktopboxes-exit-export-test-"))
            throw new InvalidOperationException("Unsafe fixture cleanup path.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
