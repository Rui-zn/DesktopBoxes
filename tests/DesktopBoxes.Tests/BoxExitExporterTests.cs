using DesktopBoxes.Core;

namespace DesktopBoxes.Tests;

public sealed class BoxExitExporterTests : IDisposable
{
    private readonly string _fixture = Path.Combine(Path.GetTempPath(), "desktopboxes-exit-export-test-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(_fixture, "source");
    private string Desktop => Path.Combine(_fixture, "desktop");

    [Fact]
    public void Export_CreatesNamedBoxFoldersAndCopiesCompleteContents()
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

        ExportResult result = BoxExitExporter.Export(Desktop, boxes);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(Desktop, "工作", "notes.txt")));
        Assert.Equal("code", File.ReadAllText(Path.Combine(Desktop, "工作", "project", "code.cs")));
        Assert.True(Directory.Exists(Path.Combine(Desktop, "工作", "project", "empty")));
        Assert.True(Directory.Exists(Path.Combine(Desktop, "空盒子")));
        Assert.Equal(2, result.CopiedFiles);
        Assert.Empty(result.Errors);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Export_NeverOverwritesConflictsAndDoesNotDuplicateIdenticalFiles()
    {
        string file = FileAt("notes.txt", "from box");
        string boxDirectory = Path.Combine(Desktop, "工作");
        Directory.CreateDirectory(boxDirectory);
        File.WriteAllText(Path.Combine(boxDirectory, "notes.txt"), "existing");
        var boxes = new[] { BoxWith("工作", file) };

        ExportResult first = BoxExitExporter.Export(Desktop, boxes);
        ExportResult second = BoxExitExporter.Export(Desktop, boxes);

        Assert.Equal("existing", File.ReadAllText(Path.Combine(boxDirectory, "notes.txt")));
        Assert.Equal("from box", File.ReadAllText(Path.Combine(boxDirectory, "notes (2).txt")));
        Assert.Equal(1, first.CopiedFiles);
        Assert.Equal(1, second.UnchangedFiles);
        Assert.False(File.Exists(Path.Combine(boxDirectory, "notes (3).txt")));
    }

    [Fact]
    public void Export_DuplicateOrUnsafeBoxNamesGetSeparateSafeFolders()
    {
        string first = FileAt("first.txt", "1");
        string second = FileAt("second.txt", "2");
        var boxes = new[] { BoxWith("A/B", first), BoxWith("A/B", second) };

        BoxExitExporter.Export(Desktop, boxes);

        Assert.Equal("1", File.ReadAllText(Path.Combine(Desktop, "A_B", "first.txt")));
        Assert.Equal("2", File.ReadAllText(Path.Combine(Desktop, "A_B (2)", "second.txt")));
    }

    [Fact]
    public void Export_WhenFolderNameIsOccupiedByAFile_UsesAvailableFolderName()
    {
        Directory.CreateDirectory(Desktop);
        File.WriteAllText(Path.Combine(Desktop, "工作"), "keep");
        string source = FileAt("notes.txt", "content");

        BoxExitExporter.Export(Desktop, new[] { BoxWith("工作", source) });

        Assert.Equal("keep", File.ReadAllText(Path.Combine(Desktop, "工作")));
        Assert.Equal("content", File.ReadAllText(Path.Combine(Desktop, "工作 (2)", "notes.txt")));
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
