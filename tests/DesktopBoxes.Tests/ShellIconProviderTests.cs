using System;
using System.IO;
using DesktopBoxes.Win32;
using Vanara.Windows.Shell;
using Xunit;

namespace DesktopBoxes.Tests;

public class ShellIconProviderTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    public void GetIconBitmap_ReturnsIcon(int pixels)
    {
        var bmp = ShellIconProvider.GetIconBitmap(@"C:\Windows\System32\notepad.exe", pixels);
        Assert.NotNull(bmp);
        Assert.True(bmp!.Width >= 16);
        bmp.Dispose();
    }

    [Fact]
    public void GetIconBitmap_LnkResolvesTargetIcon()
    {
        string lnk = Path.Combine(Path.GetTempPath(), "db-icon-test-" + Guid.NewGuid().ToString("N") + ".lnk");
        try
        {
            using (ShellLink.Create(lnk, @"C:\Windows\System32\notepad.exe", "", "", ""))
            {
            }

            var bmp = ShellIconProvider.GetIconBitmap(lnk, 48);
            Assert.NotNull(bmp);
            Assert.True(bmp!.Width >= 16);
            bmp.Dispose();
        }
        finally
        {
            if (File.Exists(lnk))
            {
                File.Delete(lnk);
            }
        }
    }
}
