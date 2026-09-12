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

        var focused = DoubleCommanderNativeMethods.GetFocusedControl(mainWindow);
        if (CanTrigger(
                focused,
                DoubleCommanderNativeMethods.GetClassNameValue(focused))
            && DoubleCommanderNativeMethods.TryGetWindowRect(focused, out var focusedBounds))
        {
            rect = ToAdapterRect(focusedBounds);
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

    private static AdapterRect ToAdapterRect(NativeRect rect)
        => new()
        {
            Left = rect.Left,
            Top = rect.Top,
            Right = rect.Right,
            Bottom = rect.Bottom
        };
}
