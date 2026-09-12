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

        var root = DoubleCommanderNativeMethods.GetRootWindow(focusedHwnd);
        if (root == IntPtr.Zero
            || !DoubleCommanderPathHeuristics.IsMainWindowClass(
                DoubleCommanderNativeMethods.GetClassNameValue(root))
            || !DoubleCommanderPathHeuristics.IsDoubleCommanderProcess(
                DoubleCommanderNativeMethods.GetProcessName(root)))
        {
            return false;
        }

        if (DoubleCommanderPathHeuristics.IsFileListClass(className))
            return true;

        if (!DoubleCommanderPathHeuristics.IsGenericLclListHostClass(className))
            return false;

        // The generic LCL window class is shared by many controls, so only accept it when the window
        // belongs to a Double Commander main window and is large enough to be a file panel.
        return DoubleCommanderNativeMethods.TryGetWindowRect(focusedHwnd, out var bounds)
            && bounds.Width >= 120
            && bounds.Height >= 120;
    }

    // Same question inline search asks for its keyboard trigger ("is the user over a file list?"), so the
    // SDK default is the right answer here too. CanRecognizeHost would be too broad: it also accepts the
    // command line and other controls of the main window, which must not open the quick navigation menu.
    public bool CanShowQuickNav(IntPtr hwndUnderCursor, string classNameUnderCursor)
        => CanTrigger(hwndUnderCursor, classNameUnderCursor);

    public string? GetSearchScope(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        var mainWindow = DoubleCommanderNativeMethods.GetRootWindow(hwnd);
        if (mainWindow == IntPtr.Zero)
            mainWindow = hwnd;

        var panel = DoubleCommanderNativeMethods.ResolveKnownPanel(mainWindow, hwnd);
        return panel == IntPtr.Zero
            ? null
            : DoubleCommanderPathReader.FindActivePath(mainWindow, panel);
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
        if (Logger.LogAction is not null)
        {
            var focusedProbe = DoubleCommanderNativeMethods.GetFocusedControl(mainWindow);
            var cursorProbe = DoubleCommanderNativeMethods.TryGetCursorPosition(out var px, out var py)
                ? $"{px},{py}"
                : "n/a";
            DoubleCommanderNativeMethods.TryGetWindowRect(hwnd, out var handleRect);
            Logger.Log(
                $"[DoubleCommander] dock resolve: hwnd=0x{hwnd.ToInt64():X} class='{DoubleCommanderNativeMethods.GetClassNameValue(hwnd)}' rect={handleRect.Left},{handleRect.Top} {handleRect.Width}x{handleRect.Height}; "
                + $"focus=0x{focusedProbe.ToInt64():X} class='{DoubleCommanderNativeMethods.GetClassNameValue(focusedProbe)}' rect={DescribeRect(focusedProbe)}; "
                + $"cursor={cursorProbe}; foreground=0x{DoubleCommanderNativeMethods.GetForegroundWindowValue().ToInt64():X}",
                LogLevel.Debug);
        }

        // Lertaro passes the panel that had focus when the search was summoned. Prefer it because the
        // inline window may have already taken focus by the time this callback runs.
        if (DoubleCommanderNativeMethods.IsPanelControl(mainWindow, hwnd)
            && DoubleCommanderNativeMethods.TryGetWindowRect(hwnd, out bounds))
        {
            DoubleCommanderNativeMethods.PublishActivePanel(mainWindow, hwnd);
            source = "handle";
            return true;
        }

        // The panel that currently holds the focus, while Double Commander still has it.
        var focused = DoubleCommanderNativeMethods.GetFocusedControl(mainWindow);
        if (DoubleCommanderNativeMethods.IsPanelControl(mainWindow, focused)
            && DoubleCommanderNativeMethods.TryGetWindowRect(focused, out bounds))
        {
            DoubleCommanderNativeMethods.PublishActivePanel(mainWindow, focused);
            source = "focus";
            return true;
        }

        // Panel recorded while Double Commander still had the focus (published by the hook process on every
        // focus change, so a panel switched with Tab is followed as well).
        var published = DoubleCommanderNativeMethods.GetPublishedActivePanel(mainWindow);
        if (DoubleCommanderNativeMethods.IsPanelControl(mainWindow, published)
            && DoubleCommanderNativeMethods.TryGetWindowRect(published, out bounds))
        {
            source = "published";
            return true;
        }

        // Positioning normally happens after the inline window took the focus, and an inactive thread
        // reports no focused control at all, so the panel is resolved from the mouse cursor: the inline
        // search is summoned by typing over a panel, where the cursor still is. The helper picks the
        // innermost control containing the cursor, so a container of both panels is never anchored to.
        if (DoubleCommanderNativeMethods.TryGetPanelUnderCursor(mainWindow, out var cursorPanel)
            && DoubleCommanderNativeMethods.TryGetWindowRect(cursorPanel, out bounds))
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

    private static string DescribeRect(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return "(zero)";

        return DoubleCommanderNativeMethods.TryGetWindowRect(hwnd, out var rect)
            ? $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}"
            : "(no rect)";
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
