namespace ClaudeVS
{
    using System;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Windows;
    using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

    [ComImport]
    [Guid("00000122-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IOleDropTarget
    {
        [PreserveSig]
        int DragEnter([In, MarshalAs(UnmanagedType.Interface)] ComDataObject dataObject, int keyState, long point, ref int effect);

        [PreserveSig]
        int DragOver(int keyState, long point, ref int effect);

        [PreserveSig]
        int DragLeave();

        [PreserveSig]
        int Drop([In, MarshalAs(UnmanagedType.Interface)] ComDataObject dataObject, int keyState, long point, ref int effect);
    }

    [ComVisible(true)]
    public sealed class TerminalFileDropTarget : IOleDropTarget
    {
        private const int DropEffectNone = 0;
        private const int DropEffectCopy = 1;
        private const int Ok = 0;

        [DllImport("ole32.dll")]
        private static extern int RegisterDragDrop(IntPtr hwnd, IOleDropTarget dropTarget);

        [DllImport("ole32.dll")]
        private static extern int RevokeDragDrop(IntPtr hwnd);

        private readonly Action<string> onPathsDropped;
        private bool canAcceptDrag;

        public TerminalFileDropTarget(Action<string> onPathsDropped)
        {
            this.onPathsDropped = onPathsDropped;
        }

        public static bool Register(IntPtr hwnd, Action<string> onPathsDropped)
        {
            return RegisterDragDrop(hwnd, new TerminalFileDropTarget(onPathsDropped)) >= 0;
        }

        public static void Revoke(IntPtr hwnd)
        {
            RevokeDragDrop(hwnd);
        }

        public int DragEnter(ComDataObject dataObject, int keyState, long point, ref int effect)
        {
            canAcceptDrag = new DataObject(dataObject).GetDataPresent(DataFormats.FileDrop);
            effect = GetEffect(effect);
            return Ok;
        }

        public int DragOver(int keyState, long point, ref int effect)
        {
            effect = GetEffect(effect);
            return Ok;
        }

        public int DragLeave()
        {
            canAcceptDrag = false;
            return Ok;
        }

        public int Drop(ComDataObject dataObject, int keyState, long point, ref int effect)
        {
            effect = GetEffect(effect);
            canAcceptDrag = false;

            if (effect != DropEffectNone && new DataObject(dataObject).GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            {
                onPathsDropped(string.Join(" ", paths.Select(QuoteIfNeeded)));
            }

            return Ok;

            string QuoteIfNeeded(string path)
            {
                return path.Contains(" ") ? $"\"{path}\"" : path;
            }
        }

        private int GetEffect(int allowedEffects)
        {
            return canAcceptDrag && (allowedEffects & DropEffectCopy) != 0 ? DropEffectCopy : DropEffectNone;
        }
    }
}
