using System.Diagnostics;

namespace Lertaro.Plugins.DoubleCommander;

internal static class DoubleCommanderLauncher
{
    public static bool Navigate(IntPtr windowHwnd, string path)
    {
        if (windowHwnd == IntPtr.Zero
            || !DoubleCommanderPathHeuristics.LooksLikeWindowsPath(path))
        {
            return false;
        }

        var mainWindow = DoubleCommanderNativeMethods.GetRootWindow(windowHwnd);
        if (mainWindow == IntPtr.Zero)
            mainWindow = windowHwnd;

        if (!DoubleCommanderPathHeuristics.IsMainWindowClass(
                DoubleCommanderNativeMethods.GetClassNameValue(mainWindow))
            || !DoubleCommanderPathHeuristics.IsDoubleCommanderProcess(
                DoubleCommanderNativeMethods.GetProcessName(mainWindow)))
        {
            return false;
        }

        // Prefer the panel that could actually be resolved. When none is known -- for example the click
        // arrives long after the panel was last recorded -- fall back to the handle Lertaro passed (usually
        // the panel that had focus, otherwise the main window) so opening a result still works instead of
        // silently doing nothing.
        var resolvedPanel = DoubleCommanderNativeMethods.ResolveKnownPanel(mainWindow, windowHwnd);
        var targetPanel = resolvedPanel != IntPtr.Zero ? resolvedPanel : windowHwnd;

        var executable = DoubleCommanderNativeMethods.GetProcessImagePath(mainWindow);
        if (string.IsNullOrWhiteSpace(executable))
            return false;

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -C activates the existing instance. -P keeps a result in the pane where the user invoked
        // Lertaro; Double Commander documents that a full filename opens its parent and selects it.
        // -T routes the path through Double Commander's AddTab(), so the result does not take over the
        // tab the user is working in (without it DC reuses the active tab's directory).
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add("-T");
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(IsLeftPanel(mainWindow, targetPanel) ? "L" : "R");
        startInfo.ArgumentList.Add(path);

        try
        {
            using var process = Process.Start(startInfo);
            return process is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLeftPanel(IntPtr mainWindow, IntPtr focusedWindow)
    {
        if (!DoubleCommanderNativeMethods.TryGetWindowRect(mainWindow, out var mainBounds)
            || !DoubleCommanderNativeMethods.TryGetWindowRect(focusedWindow, out var focusedBounds))
        {
            return true;
        }

        return focusedBounds.CenterX < mainBounds.CenterX;
    }
}
