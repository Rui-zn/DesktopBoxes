using System.Runtime.InteropServices;
using System.Text;
using static Vanara.PInvoke.Shell32;

namespace DesktopBoxes.Win32;

/// <summary>Windows Shell moves include cross-volume folders, native progress/cancel and error reporting.</summary>
public static class ShellFileMover
{
    public static void Move(string source, string destination)
    {
        if (!DesktopBoxes.Core.BoxFileTransfers.IsMissing(destination))
            throw new IOException("目标已存在，不会覆盖。");
        var operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
        IShellItem? sourceItem = null, destinationFolder = null;
        try
        {
            sourceItem = SHCreateItemFromParsingName<IShellItem>(LongPath(source)) ?? throw new IOException("无法读取源项目。");
            destinationFolder = SHCreateItemFromParsingName<IShellItem>(LongPath(Path.GetDirectoryName(destination)!)) ?? throw new IOException("无法读取目标文件夹。");
            // Keep the native progress/cancel UI. A colliding destination is never silently overwritten.
            operation.SetOperationFlags(FILEOP_FLAGS.FOF_NOCONFIRMMKDIR | FILEOP_FLAGS.FOF_NOERRORUI);
            operation.MoveItem(sourceItem, destinationFolder, Path.GetFileName(destination), null);
            operation.PerformOperations();
            if (operation.GetAnyOperationsAborted()) throw new OperationCanceledException("文件移动已取消或被系统中止。");
        }
        finally
        {
            if (sourceItem != null) Marshal.ReleaseComObject(sourceItem);
            if (destinationFolder != null) Marshal.ReleaseComObject(destinationFolder);
            Marshal.ReleaseComObject(operation);
        }
    }

    private static string LongPath(string path)
    {
        var buffer = new StringBuilder(32768);
        uint length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);
}
