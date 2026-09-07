using System;
using System.IO;
using DesktopBoxes.Core;
using Xunit;

namespace DesktopBoxes.Tests;

public class BoxStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        string dir = Path.Combine(Path.GetTempPath(), "desktopboxes-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BoxStore(dir);
            var boxes = new List<Box>
            {
                new Box
                {
                    Id = Guid.NewGuid(),
                    Name = "工具",
                    X = 10,
                    Y = 20,
                    Width = 300,
                    Height = 240,
                    Collapsed = true,
                    Items =
                    {
                        new BoxItem { Path = @"C:\Windows\System32\notepad.exe", Kind = ItemKind.Exe, DisplayName = "记事本" },
                    },
                },
            };

            store.Save(boxes);

            var loaded = store.Load();
            Assert.Single(loaded);
            Assert.Equal("工具", loaded[0].Name);
            Assert.True(loaded[0].Collapsed);
            Assert.Single(loaded[0].Items);
            Assert.Equal("记事本", loaded[0].Items[0].DisplayName);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Theory]
    [InlineData(@"C:\x\a.exe", ItemKind.Exe)]
    [InlineData(@"C:\x\a.lnk", ItemKind.Shortcut)]
    [InlineData(@"C:\x\a.txt", ItemKind.File)]
    public void DetectKind_Works(string path, ItemKind expected)
    {
        Assert.Equal(expected, BoxItem.DetectKind(path));
    }

    [Fact]
    public void Load_CorruptPrimary_RecoversBackupAndPreservesCorruptFile()
    {
        string dir = Path.Combine(Path.GetTempPath(), "desktopboxes-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new BoxStore(dir);
            store.Save(new List<Box> { new() { Name = "第一版" } });
            store.Save(new List<Box> { new() { Name = "第二版" } });
            File.WriteAllText(Path.Combine(dir, "boxes.json"), "{ damaged json");

            var loaded = store.Load();

            Assert.True(store.RecoveredFromBackup);
            Assert.Single(loaded);
            Assert.Equal("第一版", loaded[0].Name);
            Assert.NotNull(store.PreservedCorruptFile);
            Assert.True(File.Exists(store.PreservedCorruptFile));
            Assert.False(File.Exists(Path.Combine(dir, "boxes.json")));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_CorruptWithoutBackup_PreservesOriginal()
    {
        string dir = Path.Combine(Path.GetTempPath(), "desktopboxes-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, "boxes.json");
            File.WriteAllText(file, "not-json");
            var store = new BoxStore(dir);

            var loaded = store.Load();

            Assert.Empty(loaded);
            Assert.False(store.RecoveredFromBackup);
            Assert.NotNull(store.PreservedCorruptFile);
            Assert.True(File.Exists(store.PreservedCorruptFile));
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Load_NormalizesUnsafeValues()
    {
        string dir = Path.Combine(Path.GetTempPath(), "desktopboxes-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(
                Path.Combine(dir, "boxes.json"),
                """
                [{"Name":"","Width":-50,"Height":999999,"IconSize":7,"CornerRadius":-1,"Opacity":500,"Items":null}]
                """);

            var loaded = new BoxStore(dir).Load();

            Assert.Single(loaded);
            Assert.Equal("新建盒子", loaded[0].Name);
            Assert.Equal(120, loaded[0].Width);
            Assert.Equal(10000, loaded[0].Height);
            Assert.Equal(32, loaded[0].IconSize);
            Assert.Equal(0, loaded[0].CornerRadius);
            Assert.Equal(100, loaded[0].Opacity);
            Assert.Empty(loaded[0].Items);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
