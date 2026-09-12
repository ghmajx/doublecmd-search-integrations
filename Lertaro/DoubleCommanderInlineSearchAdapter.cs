using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;
using Lertaro.PluginSdk;

namespace Lertaro.Plugins.DoubleCommander;

/// <summary>
/// Lertaro inline-search and Quick Navigation integration for Double Commander.
/// </summary>
public sealed class DoubleCommanderInlineSearchAdapter : IInlineSearchAdapter
{
    public string Name => "Double Commander";

    public bool IsFileExplorer => true;

    public bool CanHandle(IntPtr hwnd, string className, string processName)
        => CanRecognizeHost(hwnd, className, processName);

    public bool CanRecognizeHost(IntPtr hwnd, string className, string processName)
    {
        if (!DoubleCommanderPathHeuristics.IsDoubleCommanderProcess(processName))
            return false;

        if (DoubleCommanderPathHeuristics.IsMainWindowClass(className))
            return true;

        // Lertaro resolves the host from the focused control as well (its keyboard hook matches the
        // adapter against the focused control's class), so a file panel whose root window is a Double
        // Commander main window must count as the host. Without this the hotkey falls back to the plain
        // floating search window instead of the docked inline search window.
        var root = DoubleCommanderNativeMethods.GetRootWindow(hwnd);
        return root != IntPtr.Zero
            && DoubleCommanderPathHeuristics.IsMainWindowClass(
                DoubleCommanderNativeMethods.GetClassNameValue(root))
            && DoubleCommanderPathHeuristics.IsDoubleCommanderProcess(
                DoubleCommanderNativeMethods.GetProcessName(root));
    }

    public bool CanTrigger(IntPtr focusedHwnd, string className)
    {
        if (focusedHwnd == IntPtr.Zero
            || DoubleCommanderPathHeuristics.IsEditorClass(className))
        {
            return false;
        }

        if (DoubleCommanderPathHeuristics.IsFileListClass(className))
            return true;

        if (!DoubleCommanderPathHeuristics.IsGenericLclListHostClass(className))
            return false;

        // The generic LCL window class is shared by many controls, so only accept it when the window
        // belongs to a Double Commander main window and is large enough to be a file panel.
        var root = DoubleCommanderNativeMethods.GetRootWindow(focusedHwnd);
        return root != IntPtr.Zero
            && DoubleCommanderPathHeuristics.IsMainWindowClass(
                DoubleCommanderNativeMethods.GetClassNameValue(root))
            && DoubleCommanderPathHeuristics.IsDoubleCommanderProcess(
                DoubleCommanderNativeMethods.GetProcessName(root))
            && DoubleCommanderNativeMethods.TryGetWindowRect(focusedHwnd, out var bounds)
            && bounds.Width >= 120
            && bounds.Height >= 120;
    }

    public string? GetSearchScope(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        var mainWindow = DoubleCommanderNativeMethods.GetRootWindow(hwnd);
        if (mainWindow == IntPtr.Zero)
            mainWindow = hwnd;

        return DoubleCommanderPathReader.FindActivePath(
            mainWindow,
            DoubleCommanderNativeMethods.GetFocusedControl(mainWindow));
    }

    public bool ExecuteItem(IntPtr hwnd, string path, string searchInput)
    {
        try
        {
            return DoubleCommanderLauncher.Navigate(hwnd, path);
        }
        catch
        {
            return false;
        }
    }

    public bool GetDockBounds(IntPtr hwnd, out AdapterRect rect)
    {
        rect = default;
        if (hwnd == IntPtr.Zero)
            return false;

        var mainWindow = DoubleCommanderNativeMethods.GetRootWindow(hwnd);
        if (mainWindow == IntPtr.Zero)
            mainWindow = hwnd;

        if (!TryResolveDockBounds(mainWindow, hwnd, out var bounds, out var source))
            return false;

        rect = ToAdapterRect(bounds);

        Logger.Log(
            $"[DoubleCommander] dock bounds from {source}: {rect.Left},{rect.Top} {rect.Right - rect.Left}x{rect.Bottom - rect.Top} (window 0x{mainWindow.ToInt64():X}, handle 0x{hwnd.ToInt64():X})",
            LogLevel.Debug);
        return true;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => hwnd != IntPtr.Zero;

    private static bool TryResolveDockBounds(IntPtr mainWindow, IntPtr hwnd, out NativeRect bounds, out string source)
    {
        // The panel that currently holds the focus, while Double Commander still has it.
        var focused = DoubleCommanderNativeMethods.GetFocusedControl(mainWindow);
        if (IsPanelControl(mainWindow, focused)
            && DoubleCommanderNativeMethods.TryGetWindowRect(focused, out bounds))
        {
            source = "focus";
            return true;
        }

        // Drill down from the handle Lertaro passed. Its keyboard hook re-broadcasts the focused control
        // as the active window, but that handle can also be a pane container or the window itself, so
        // resolve it to the innermost file list instead of trusting its rectangle directly.
        if (TryResolvePanel(mainWindow, hwnd, out bounds))
        {
            source = "handle";
            return true;
        }

        // Positioning normally happens after the inline window took the focus, and an inactive thread
        // reports no focused control at all. Fall back to the panel under the mouse cursor: the inline
        // search is summoned by typing over a panel, where the cursor usually still is.
        if (TryResolvePanelUnderCursor(mainWindow, out bounds))
        {
            source = "cursor";
            return true;
        }

        if (DoubleCommanderNativeMethods.TryGetWindowRect(mainWindow, out bounds))
        {
            source = "window";
            return true;
        }

        source = string.Empty;
        bounds = default;
        return false;
    }

    /// <summary>
    /// Resolves <paramref name="start"/> to the innermost file list below it. Every step only compares a
    /// child with its own parent ("nearly as large"), so it works for any panel split ratio; the branch
    /// to follow is picked with the mouse cursor when one of them contains it.
    /// </summary>
    private static bool TryResolvePanel(IntPtr mainWindow, IntPtr start, out NativeRect bounds)
    {
        bounds = default;
        if (start == IntPtr.Zero || !DoubleCommanderNativeMethods.TryGetWindowRect(start, out var currentRect))
            return false;

        var current = start;
        for (var depth = 0; depth < 8; depth++)
        {
            var next = IntPtr.Zero;
            var nextRect = default(NativeRect);
            var nextContainsCursor = false;
            foreach (var child in DoubleCommanderNativeMethods.EnumerateDirectChildren(current))
            {
                if (!IsPanelControl(mainWindow, child)
                    || !DoubleCommanderNativeMethods.TryGetWindowRect(child, out var childRect))
                {
                    continue;
                }

                // A real nested panel is nearly as large as its parent; smaller children are chrome.
                if (childRect.Width * 2 < currentRect.Width || childRect.Height * 2 < currentRect.Height)
                    continue;

                var containsCursor = TryGetCursorInside(childRect);
                var better = next == IntPtr.Zero
                    || (containsCursor && !nextContainsCursor)
                    || (containsCursor == nextContainsCursor
                        && childRect.Width * childRect.Height > nextRect.Width * nextRect.Height);
                if (better)
                {
                    next = child;
                    nextRect = childRect;
                    nextContainsCursor = containsCursor;
                }
            }

            if (next == IntPtr.Zero)
                break;

            current = next;
            currentRect = nextRect;
        }

        if (!IsPanelControl(mainWindow, current))
            return false;

        bounds = currentRect;
        return true;
    }

    private static bool TryResolvePanelUnderCursor(IntPtr mainWindow, out NativeRect bounds)
    {
        bounds = default;
        if (!DoubleCommanderNativeMethods.TryGetCursorPosition(out var cursorX, out var cursorY))
            return false;

        var found = false;
        var best = default(NativeRect);
        foreach (var child in DoubleCommanderNativeMethods.EnumerateDirectChildren(mainWindow))
        {
            if (!TryResolvePanel(mainWindow, child, out var candidate)
                || !candidate.Contains(cursorX, cursorY))
            {
                continue;
            }

            if (!found || candidate.Width * candidate.Height > best.Width * best.Height)
            {
                best = candidate;
                found = true;
            }
        }

        bounds = best;
        return found;
    }

    private static bool TryGetCursorInside(NativeRect rect)
        => DoubleCommanderNativeMethods.TryGetCursorPosition(out var x, out var y) && rect.Contains(x, y);

    private static bool IsPanelControl(IntPtr mainWindow, IntPtr control)
    {
        if (control == IntPtr.Zero)
            return false;

        var className = DoubleCommanderNativeMethods.GetClassNameValue(control);
        if (DoubleCommanderPathHeuristics.IsEditorClass(className))
            return false;

        if (!DoubleCommanderPathHeuristics.IsListHostClass(className))
            return false;

        return DoubleCommanderNativeMethods.GetRootWindow(control) == mainWindow
            && DoubleCommanderNativeMethods.TryGetWindowRect(control, out var bounds)
            && bounds.Width >= 120
            && bounds.Height >= 120;
    }

    private static AdapterRect ToAdapterRect(NativeRect rect)
        => new()
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom
        };
}
