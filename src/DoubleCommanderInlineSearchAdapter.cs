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
        => DoubleCommanderPathHeuristics.IsDoubleCommanderProcess(processName)
            && DoubleCommanderPathHeuristics.IsMainWindowClass(className);

    public bool CanTrigger(IntPtr focusedHwnd, string className)
        => focusedHwnd != IntPtr.Zero
            && DoubleCommanderPathHeuristics.IsFileListClass(className)
            && !DoubleCommanderPathHeuristics.IsEditorClass(className);

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
