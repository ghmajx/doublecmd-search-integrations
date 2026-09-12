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

        var executable = DoubleCommanderNativeMethods.GetProcessImagePath(mainWindow)
            ?? "doublecmd.exe";

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // -C activates the existing instance. -P keeps a result in the pane where the user invoked
        // Lertaro; Double Commander documents that a full filename opens its parent and selects it.
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add("-P");
        startInfo.ArgumentList.Add(IsLeftPanel(mainWindow, windowHwnd) ? "L" : "R");
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
