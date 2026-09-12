using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Lertaro.Plugins.DoubleCommander;

internal readonly record struct NativeRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;

    public bool IsUsable => Width > 0 && Height > 0;

    public int HorizontalOverlap(NativeRect other)
        => Math.Max(0, Math.Min(Right, other.Right) - Math.Max(Left, other.Left));
}

internal static class DoubleCommanderNativeMethods
{
    private const uint GaRoot = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint WmSetText = 0x000C;
    private const uint WmGetText = 0x000D;
    private const uint SmtoBlock = 0x0001;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint TextMessageTimeoutMs = 150;
    private const byte VirtualKeyControl = 0x11;
    private const byte VirtualKeyP = 0x50;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRectValue
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public int Size;
        public uint Flags;
        public IntPtr Active;
        public IntPtr Focus;
        public IntPtr Capture;
        public IntPtr MenuOwner;
        public IntPtr MoveSize;
        public IntPtr Caret;
        public NativeRectValue CaretRect;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int maxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRectValue rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetWindowText(IntPtr hwnd, string text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        StringBuilder lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr processHandle,
        int flags,
        StringBuilder imageName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    public static string GetWindowTextValue(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return string.Empty;

        // GetWindowText only returns the text USER32 caches for windows in other processes, and
        // Lazarus/LCL controls (Double Commander's command line) never populate that cache. Send the
        // real WM_GETTEXT message instead, with a timeout so a hung target cannot block the caller.
        var text = new StringBuilder(32768);
        var delivered = SendMessageTimeout(
            hwnd,
            WmGetText,
            (IntPtr)text.Capacity,
            text,
            SmtoAbortIfHung | SmtoBlock,
            TextMessageTimeoutMs,
            out _);
        if (delivered != IntPtr.Zero)
            return text.ToString();

        _ = text.Clear();
        _ = GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    public static string GetClassNameValue(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return string.Empty;

        var className = new StringBuilder(512);
        _ = GetClassName(hwnd, className, className.Capacity);
        return className.ToString();
    }

    public static bool TryGetWindowRect(IntPtr hwnd, out NativeRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var value))
            return false;

        rect = new NativeRect(value.Left, value.Top, value.Right, value.Bottom);
        return rect.IsUsable;
    }

    public static bool IsVisible(IntPtr hwnd)
        => hwnd != IntPtr.Zero && IsWindowVisible(hwnd);

    public static bool IsForegroundWindow(IntPtr hwnd)
        => hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;

    public static bool SetWindowTextValue(IntPtr hwnd, string text)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        // SetWindowText does not reach a control that belongs to another process (it only updates the
        // USER32 cache), so LCL controls keep their old content. WM_SETTEXT is delivered properly.
        var delivered = SendMessageTimeout(
            hwnd,
            WmSetText,
            IntPtr.Zero,
            text,
            SmtoAbortIfHung | SmtoBlock,
            TextMessageTimeoutMs,
            out var result);
        if (delivered != IntPtr.Zero)
            return result != IntPtr.Zero;

        return SetWindowText(hwnd, text);
    }

    public static void SendControlP()
    {
        keybd_event(VirtualKeyControl, 0, 0, UIntPtr.Zero);
        keybd_event(VirtualKeyP, 0, 0, UIntPtr.Zero);
        keybd_event(VirtualKeyP, 0, KeyEventKeyUp, UIntPtr.Zero);
        keybd_event(VirtualKeyControl, 0, KeyEventKeyUp, UIntPtr.Zero);
    }

    public static IntPtr GetRootWindow(IntPtr hwnd)
        => hwnd == IntPtr.Zero ? IntPtr.Zero : GetAncestor(hwnd, GaRoot);

    public static IntPtr GetFocusedControl(IntPtr hwnd)
    {
        var threadId = GetWindowThreadProcessId(hwnd, out _);
        if (threadId == 0)
            return IntPtr.Zero;

        var info = new GuiThreadInfo { Size = Marshal.SizeOf<GuiThreadInfo>() };
        return GetGUIThreadInfo(threadId, ref info) ? info.Focus : IntPtr.Zero;
    }

    public static string GetProcessName(IntPtr hwnd)
    {
        var threadId = GetWindowThreadProcessId(hwnd, out var processId);
        if (threadId == 0 || processId == 0)
            return string.Empty;

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    public static string? GetProcessImagePath(IntPtr hwnd)
    {
        _ = GetWindowThreadProcessId(hwnd, out var processId);
        if (processId == 0)
            return null;

        var handle = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (handle == IntPtr.Zero)
            return null;

        try
        {
            var size = 32768;
            var imageName = new StringBuilder(size);
            return QueryFullProcessImageName(handle, 0, imageName, ref size)
                ? imageName.ToString()
                : null;
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    public static IReadOnlyList<IntPtr> EnumerateChildWindows(IntPtr parent)
    {
        var result = new List<IntPtr>();
        EnumChildWindows(parent, (hwnd, _) =>
        {
            result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static IReadOnlyList<IntPtr> EnumerateTopLevelWindows()
    {
        var result = new List<IntPtr>();
        EnumWindows((hwnd, _) =>
        {
            result.Add(hwnd);
            return true;
        }, IntPtr.Zero);
        return result;
    }
}
