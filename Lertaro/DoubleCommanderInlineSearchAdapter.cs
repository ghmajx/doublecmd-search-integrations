using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

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

        // The caller usually passes the panel that had the focus when the inline search was summoned:
        // Lertaro's keyboard hook re-broadcasts that focused control as the active window. That is the
        // most precise anchor, and it stays correct when the user switched panels with Tab before typing.
        if (IsPanelControl(mainWindow, hwnd)
            && DoubleCommanderNativeMethods.TryGetWindowRect(hwnd, out var passedBounds))
        {
            rect = ToAdapterRect(passedBounds);
            return true;
        }

        // Otherwise prefer the panel that currently holds the focus.
        var focused = DoubleCommanderNativeMethods.GetFocusedControl(mainWindow);
        if (IsPanelControl(mainWindow, focused)
            && DoubleCommanderNativeMethods.TryGetWindowRect(focused, out var focusedBounds))
        {
            rect = ToAdapterRect(focusedBounds);
            return true;
        }

        // Positioning normally happens after the inline window took the focus, and an inactive thread
        // reports no focused control at all. Fall back to the panel under the mouse cursor: the inline
        // search is summoned by typing over a panel, where the cursor usually still is.
        if (TryGetPanelUnderCursor(mainWindow, out var cursorBounds))
        {
            rect = ToAdapterRect(cursorBounds);
            return true;
        }

        if (DoubleCommanderNativeMethods.TryGetWindowRect(mainWindow, out var mainBounds))
        {
            rect = ToAdapterRect(mainBounds);
            return true;
        }

        return false;
    }

    public bool CanEnterActionsMode(IntPtr hwnd) => hwnd != IntPtr.Zero;

    private static bool TryGetPanelUnderCursor(IntPtr mainWindow, out NativeRect bounds)
    {
        bounds = default;

        if (DoubleCommanderNativeMethods.TryGetCursorPosition(out var cursorX, out var cursorY))
        {
            var found = false;
            var best = default(NativeRect);
            foreach (var child in DoubleCommanderNativeMethods.EnumerateChildWindows(mainWindow))
            {
                if (!IsPanelControl(mainWindow, child)
                    || !DoubleCommanderNativeMethods.TryGetWindowRect(child, out var childBounds)
                    || !childBounds.Contains(cursorX, cursorY))
                {
                    continue;
                }

                // Nested panel controls can overlap at the cursor; keep the largest, which is the file list.
                if (!found || childBounds.Width * childBounds.Height > best.Width * best.Height)
                {
                    best = childBounds;
                    found = true;
                }
            }

            if (found)
            {
                bounds = best;
                return true;
            }
        }

        return false;
    }

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
