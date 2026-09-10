using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using DesktopBoxes.Core;
using DesktopBoxes.UI;
using DesktopBoxes.Win32;
using static Vanara.PInvoke.Shell32;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace DesktopBoxes.UiChecks;

internal static class FileMoveChecks
{
    internal static void Run(IntPtr host, string output, Action<bool, string> check)
    {
        string fixture = Path.Combine(output, "move-check-" + Guid.NewGuid().ToString("N"));
        string external = Path.Combine(Path.GetTempPath(), "desktopboxes-ui-move-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        Directory.CreateDirectory(external);
        var boxes = new List<Box> { new() { Name = "文件移动测试", X = -20000, Y = -20000 } };
        string dataDir = Path.Combine(fixture, "data");
        var store = new BoxStore(dataDir);
        var transfers = new BoxFileTransfers(dataDir, () => boxes, () => store.Save(boxes), ShellFileMover.Move);
        using var window = new BoxWindow(boxes[0], dataDir, host, transfers);
        IShellItem? dragSourceItem = null;
        object? explorerData = null;
        try
        {
            var root = (FrameworkElement)HwndSource.FromHwnd(window.Handle).RootVisual;
            string file = Path.Combine(external, "sample.txt");
            File.WriteAllText(file, "native-shell-transfer");
            dragSourceItem = SHCreateItemFromParsingName<IShellItem>(file) ?? throw new IOException("Cannot bind source shell item");
            Guid dataHandler = new("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4"); // BHID_DataObject
            Guid dataInterface = typeof(ComDataObject).GUID;
            dragSourceItem.BindToHandler(null, dataHandler, dataInterface, out explorerData);
            var data = new RecordedDataObject(new DataObject(explorerData ?? throw new IOException("No Explorer data object")));
            check(data.GetDataPresent(DataFormats.FileDrop), "real Explorer data object exposes file drop paths");
            var effectMethod = typeof(BoxWindow).GetMethod("DropEffect", BindingFlags.Instance | BindingFlags.NonPublic)!;
            check((DragDropEffects)effectMethod.Invoke(window, new object[] { data, DragDropEffects.Move, DragDropKeyStates.LeftMouseButton })! == DragDropEffects.Move, "drop feedback advertises Move");
            check((DragDropEffects)effectMethod.Invoke(window, new object[] { data, DragDropEffects.Copy, DragDropKeyStates.LeftMouseButton })! == DragDropEffects.None, "copy-only source is rejected without moving files");
            check((DragDropEffects)effectMethod.Invoke(window, new object[] { data, DragDropEffects.Move, DragDropKeyStates.ControlKey })! == DragDropEffects.None, "Ctrl-copy is not silently converted to a move");

            // Exercise the actual WPF routed handler against fixture data, not the user's desktop.
            var constructor = typeof(DragEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
                .Single(c => c.GetParameters().Length == 5);
            var args = (DragEventArgs)constructor.Invoke(new object[] { data, DragDropKeyStates.LeftMouseButton, DragDropEffects.Move, root, new Point(20, 20) });
            args.RoutedEvent = DragDrop.PreviewDropEvent;
            root.RaiseEvent(args);
            check(!File.Exists(file) && boxes[0].Items.Count == 1, "WPF drop moves the actual source file into storage");
            check(args.Handled && args.Effects == DragDropEffects.None, "optimized move does not request source deletion");
            check(data.Effects.First() == ("Performed DropEffect", 0), "performed effect explicitly prevents double deletion before logical feedback");

            // Invoke Explorer's real folder IDropTarget with the same WPF file data object
            // used by the drag source. This tests OLE/Shell integration without UI input.
            var item = boxes[0].Items.Single();
            string storedPath = item.Path;
            var outgoing = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            outgoing.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2)), false);
            ShellDrop(outgoing, external);
            check(File.Exists(file) && !File.Exists(storedPath), "Explorer OLE drop target really moves file out of storage");
            check(transfers.CompleteExternalMove(boxes[0], item) && boxes[0].Items.Count == 0, "completed Explorer move removes box entry");
            check(File.ReadAllText(file) == "native-shell-transfer", "Shell round trip preserves file bytes");
            Pump();

            string folder = Path.Combine(external, "folder");
            Directory.CreateDirectory(Path.Combine(folder, "empty"));
            File.WriteAllText(Path.Combine(folder, "nested.txt"), "folder-bytes");
            transfers.Import(boxes[0], folder);
            item = boxes[0].Items.Single();
            check(!Directory.Exists(folder) && Directory.Exists(Path.Combine(item.Path, "empty")), "native folder import preserves empty directories");
            var folderData = new DataObject(DataFormats.FileDrop, new[] { item.Path });
            folderData.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(2)), false);
            ShellDrop(folderData, external);
            check(transfers.CompleteExternalMove(boxes[0], item) && File.ReadAllText(Path.Combine(folder, "nested.txt")) == "folder-bytes", "native folder OLE round trip preserves nested files");
            string shortcut = Path.Combine(external, "shortcut.lnk");
            using (var link = new Vanara.Windows.Shell.ShellLink(file, "", external, "Fixture shortcut")) link.SaveAs(shortcut);
            transfers.Import(boxes[0], shortcut);
            item = boxes[0].Items.Single();
            check(!File.Exists(shortcut) && File.Exists(file) && File.Exists(item.Path), "shortcut import moves only the lnk, not its target");
            string? resolved = ShellLinkResolver.ResolveTarget(item.Path);
            check(resolved != null && File.ReadAllText(resolved) == "native-shell-transfer", "stored shortcut still resolves to its original target");
            transfers.ReturnToDirectory(boxes[0], item, external);
            check(File.Exists(shortcut) && File.Exists(file) && boxes[0].Items.Count == 0, "shortcut return leaves original target untouched");
            Console.WriteLine($"Native move fixture volumes: {Path.GetPathRoot(external)} -> {Path.GetPathRoot(dataDir)} -> {Path.GetPathRoot(external)}");
        }
        finally
        {
            if (explorerData != null) Marshal.ReleaseComObject(explorerData);
            if (dragSourceItem != null) Marshal.ReleaseComObject(dragSourceItem);
            Cleanup(fixture, output, "move-check-");
            Cleanup(external, Path.GetTempPath(), "desktopboxes-ui-move-");
        }
    }

    private static void ShellDrop(DataObject data, string targetDirectory)
    {
        var shellItem = SHCreateItemFromParsingName<IShellItem>(targetDirectory) ?? throw new IOException("Cannot bind shell folder");
        object? target = null;
        try
        {
            Guid handler = new("3981E225-F559-11D3-8E3A-00C04F6837D5"); // BHID_SFUIObject
            Guid iid = typeof(IFolderDropTarget).GUID;
            shellItem.BindToHandler(null, handler, iid, out target);
            var dropTarget = (IFolderDropTarget)(target ?? throw new IOException("No folder drop target"));
            uint effect = 2;
            dropTarget.DragEnter((ComDataObject)data, 1, default, ref effect);
            dropTarget.Drop((ComDataObject)data, 0, default, ref effect);
        }
        finally
        {
            if (target != null) Marshal.ReleaseComObject(target);
            Marshal.ReleaseComObject(shellItem);
        }
    }

    private static void Pump()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointL { public int X, Y; }
    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFolderDropTarget
    {
        void DragEnter([MarshalAs(UnmanagedType.Interface)] ComDataObject data, uint keyState, PointL point, ref uint effect);
        void DragOver(uint keyState, PointL point, ref uint effect);
        void DragLeave();
        void Drop([MarshalAs(UnmanagedType.Interface)] ComDataObject data, uint keyState, PointL point, ref uint effect);
    }

    private static void Cleanup(string directory, string parent, string prefix)
    {
        string full = Path.GetFullPath(directory);
        string parentFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(parentFull, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(prefix))
            throw new InvalidOperationException("Unsafe fixture cleanup path.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }

    // Explorer consumes SetData feedback rather than necessarily offering it through
    // GetData. Record the writes while forwarding EVERY call to the real Shell object.
    private sealed class RecordedDataObject(DataObject inner) : System.Windows.IDataObject
    {
        internal readonly List<(string Format, int Effect)> Effects = new();
        public object GetData(string format, bool autoConvert) => inner.GetData(format, autoConvert);
        public object GetData(string format) => inner.GetData(format);
        public object GetData(Type format) => inner.GetData(format);
        public bool GetDataPresent(string format, bool autoConvert) => inner.GetDataPresent(format, autoConvert);
        public bool GetDataPresent(string format) => inner.GetDataPresent(format);
        public bool GetDataPresent(Type format) => inner.GetDataPresent(format);
        public string[] GetFormats(bool autoConvert) => inner.GetFormats(autoConvert);
        public string[] GetFormats() => inner.GetFormats();
        public void SetData(string format, object data, bool autoConvert)
        {
            if (data is MemoryStream stream) Effects.Add((format, BitConverter.ToInt32(stream.ToArray())));
            inner.SetData(format, data, autoConvert);
        }
        public void SetData(string format, object data) => SetData(format, data, true);
        public void SetData(Type format, object data) => inner.SetData(format, data);
        public void SetData(object data) => inner.SetData(data);
    }
}
