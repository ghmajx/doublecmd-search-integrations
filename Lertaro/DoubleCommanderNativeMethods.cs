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

    public bool Contains(int x, int y)
        => x >= Left && x < Right && y >= Top && y < Bottom;

    public int HorizontalOverlap(NativeRect other)
        => Math.Max(0, Math.Min(Right, other.Right) - Math.Max(Left, other.Left));
}

internal static class DoubleCommanderNativeMethods
{
    private const uint GaRoot = 2;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint WmSetRedraw = 0x000B;
    private const uint WmSetText = 0x000C;
    private const uint WmGetText = 0x000D;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hwnd,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AttachThreadInput(uint attachThread, uint attachToThread, bool attach);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetKeyboardState(byte[] keyState);

    [DllImport("user32.dll")]
    private static extern bool GetKeyboardState(byte[] keyState);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool InvalidateRect(IntPtr hwnd, IntPtr rect, bool erase);

    [DllImport("user32.dll")]
    private static extern bool UpdateWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetProp(IntPtr hwnd, string name, IntPtr value);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetProp(IntPtr hwnd, string name);

    // Window property used to hand the focused panel from the hook process (which sees the focus) to the
    // app process (which positions the inline window but can no longer see Double Commander's focus).
    private const string ActivePanelPropertyName = "Lertaro.DoubleCommander.ActivePanel";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

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

    public static bool TryGetCursorPosition(out int x, out int y)
    {
        if (GetCursorPos(out var point))
        {
            x = point.X;
            y = point.Y;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Records which panel currently has the focus on the Double Commander window. Called from the process
    /// that can see the focus, read back by the one that positions the inline search window.
    /// </summary>
    public static void PublishActivePanel(IntPtr mainWindow, IntPtr panel)
    {
        if (mainWindow == IntPtr.Zero || panel == IntPtr.Zero)
            return;

        _ = SetProp(mainWindow, ActivePanelPropertyName, panel);
    }

    public static IntPtr GetPublishedActivePanel(IntPtr mainWindow)
        => mainWindow == IntPtr.Zero ? IntPtr.Zero : GetProp(mainWindow, ActivePanelPropertyName);

    public static IntPtr ResolveKnownPanel(IntPtr mainWindow, IntPtr requestedHwnd)
    {
        if (mainWindow == IntPtr.Zero)
            return IntPtr.Zero;

        if (IsPanelControl(mainWindow, requestedHwnd))
            return requestedHwnd;

        // While Double Commander still has the focus this is the exact panel; afterwards (the inline window
        // has taken it) the thread reports no focus at all and the recorded panel takes over. Note that a
        // panel is resolved even when the focus currently sits in an editor such as the command line: the
        // command line round trip only ever runs with that field empty and restores the empty field, so the
        // user's own input is never at risk.
        var focused = GetFocusedControl(mainWindow);
        if (IsPanelControl(mainWindow, focused))
            return focused;

        var published = GetPublishedActivePanel(mainWindow);
        if (IsPanelControl(mainWindow, published))
            return published;

        // Last resort: the panel under the mouse cursor, preferring the innermost control that contains it
        // so a container spanning both panels is never picked. Covers the case where a result is opened
        // long after the panel was last recorded.
        return TryGetPanelUnderCursor(mainWindow, out var panel) ? panel : IntPtr.Zero;
    }

    public static bool TryGetPanelUnderCursor(IntPtr mainWindow, out IntPtr panel)
    {
        panel = IntPtr.Zero;
        if (mainWindow == IntPtr.Zero || !TryGetCursorPosition(out var cursorX, out var cursorY))
            return false;

        var found = false;
        var bestArea = long.MaxValue;
        foreach (var child in EnumerateChildWindows(mainWindow))
        {
            if (!IsPanelControl(mainWindow, child)
                || !TryGetWindowRect(child, out var bounds)
                || !bounds.Contains(cursorX, cursorY))
            {
                continue;
            }

            var area = (long)bounds.Width * bounds.Height;
            if (!found || area < bestArea)
            {
                found = true;
                bestArea = area;
                panel = child;
            }
        }

        return found;
    }

    public static bool IsPanelControl(IntPtr mainWindow, IntPtr control)
    {
        if (mainWindow == IntPtr.Zero || control == IntPtr.Zero)
            return false;

        var className = GetClassNameValue(control);
        if (DoubleCommanderPathHeuristics.IsEditorClass(className)
            || !DoubleCommanderPathHeuristics.IsListHostClass(className))
        {
            return false;
        }

        return GetRootWindow(control) == mainWindow
            && TryGetWindowRect(control, out var bounds)
            && bounds.Width >= 120
            && bounds.Height >= 120;
    }

    public static bool IsVisible(IntPtr hwnd)
        => hwnd != IntPtr.Zero && IsWindowVisible(hwnd);

    public static bool IsForegroundWindow(IntPtr hwnd)
        => hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;

    public static IntPtr GetForegroundWindowValue() => GetForegroundWindow();

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

    /// <summary>
    /// Suspends or resumes painting of a window. Used to keep the temporary command line text of the
    /// Ctrl+P query invisible; USER32 handles WM_SETREDRAW for any window, including LCL controls.
    /// </summary>
    public static bool SetWindowRedraw(IntPtr hwnd, bool enable)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var delivered = SendMessageTimeout(
            hwnd,
            WmSetRedraw,
            enable ? new IntPtr(1) : IntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung | SmtoBlock,
            TextMessageTimeoutMs,
            out _);
        if (delivered == IntPtr.Zero)
            return false;

        if (enable)
        {
            _ = InvalidateRect(hwnd, IntPtr.Zero, true);
            _ = UpdateWindow(hwnd);
        }

        return true;
    }

    /// <summary>
    /// Triggers Double Commander's cm_AddPathToCmdLine (Ctrl+P) by posting the key straight to its
    /// window, with Ctrl marked as pressed in the target thread's own input state.
    /// </summary>
    /// <remarks>
    /// Deliberately not keybd_event/SendInput: those inject into whatever window currently has the
    /// foreground, so a focus change between the check and the keystroke leaks "Ctrl+P" into an unrelated
    /// application (observed as the browser's print dialog) or leaves a stray "p" in a text box. A window
    /// message can only be received by the target window, and attaching to its thread makes GetKeyState
    /// report Ctrl as held for the duration of the message -- which is what the app's shortcut handling
    /// looks at.
    /// </remarks>
    public static bool SendControlPTo(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
            return false;

        var targetThread = GetWindowThreadProcessId(targetWindow, out _);
        if (targetThread == 0)
            return false;

        var currentThread = GetCurrentThreadId();
        var attached = targetThread == currentThread
            || AttachThreadInput(currentThread, targetThread, true);
        if (!attached)
            return false;

        try
        {
            var state = new byte[256];
            if (!GetKeyboardState(state))
                return false;

            var previousControlState = state[VirtualKeyControl];
            state[VirtualKeyControl] = 0x80;
            if (!SetKeyboardState(state))
                return false;

            try
            {
                var keyDownDelivered = SendMessageTimeout(
                    targetWindow,
                    WmKeyDown,
                    new IntPtr(VirtualKeyP),
                    CreateKeyMessageParam(VirtualKeyP, keyUp: false),
                    SmtoAbortIfHung | SmtoBlock,
                    TextMessageTimeoutMs,
                    out _);
                var keyUpDelivered = SendMessageTimeout(
                    targetWindow,
                    WmKeyUp,
                    new IntPtr(VirtualKeyP),
                    CreateKeyMessageParam(VirtualKeyP, keyUp: true),
                    SmtoAbortIfHung | SmtoBlock,
                    TextMessageTimeoutMs,
                    out _);
                if (keyDownDelivered == IntPtr.Zero || keyUpDelivered == IntPtr.Zero)
                    return false;
            }
            finally
            {
                state[VirtualKeyControl] = previousControlState;
                _ = SetKeyboardState(state);
            }

            return true;
        }
        finally
        {
            if (targetThread != currentThread)
                _ = AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static IntPtr CreateKeyMessageParam(byte virtualKey, bool keyUp)
    {
        var scanCode = MapVirtualKey(virtualKey, 0);
        var param = 1L | ((long)scanCode << 16);
        if (keyUp)
            param |= 0xC0000000L;
        return new IntPtr(param);
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
