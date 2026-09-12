using Lertaro.PluginSdk.Abstractions.Plugins.WindowAdapters;

namespace Lertaro.Plugins.DoubleCommander;

/// <summary>
/// Reads the active Double Commander pane from visible window metadata, with a guarded Ctrl+P fallback
/// for Lazarus path labels that do not own an HWND. Double Commander does not expose a Total Commander-
/// compatible WM_COPYDATA query protocol, so this adapter never scans or validates the filesystem.
/// </summary>
public sealed class DoubleCommanderPathCollector : IActivePathCollector
{
    public string Name => "Double Commander";

    public string TargetName => "Double Commander";

    public bool CanHandle(string className)
        => DoubleCommanderPathHeuristics.IsMainWindowClass(className);

    public bool CanHandle(IntPtr windowHwnd, string windowClassName, string processName)
        => CanHandle(windowClassName)
            && DoubleCommanderPathHeuristics.IsDoubleCommanderProcess(processName);

    public string? TryGetPath(
        IntPtr activeHwnd,
        string activeClassName,
        IntPtr windowHwnd,
        string windowClassName,
        string processName)
    {
        var mainWindow = windowHwnd != IntPtr.Zero
            ? windowHwnd
            : DoubleCommanderNativeMethods.GetRootWindow(activeHwnd);

        if (mainWindow == IntPtr.Zero)
        {
            return null;
        }

        var resolvedClassName = string.IsNullOrEmpty(windowClassName)
            ? DoubleCommanderNativeMethods.GetClassNameValue(mainWindow)
            : windowClassName;
        var resolvedProcessName = string.IsNullOrEmpty(processName)
            ? DoubleCommanderNativeMethods.GetProcessName(mainWindow)
            : processName;
        if (!CanHandle(mainWindow, resolvedClassName, resolvedProcessName))
            return null;

        var focused = activeHwnd != IntPtr.Zero
            ? activeHwnd
            : DoubleCommanderNativeMethods.GetFocusedControl(mainWindow);

        return DoubleCommanderPathReader.FindActivePath(mainWindow, focused);
    }

    public IReadOnlyList<OpenedFolder> GetOpenedFolders()
    {
        var result = new List<OpenedFolder>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var window in DoubleCommanderNativeMethods.EnumerateTopLevelWindows())
        {
            if (!DoubleCommanderNativeMethods.IsVisible(window))
                continue;

            var className = DoubleCommanderNativeMethods.GetClassNameValue(window);
            var processName = DoubleCommanderNativeMethods.GetProcessName(window);
            if (!CanHandle(window, className, processName))
                continue;

            var candidates = DoubleCommanderPathReader.FindCandidates(window);
            foreach (var candidate in candidates)
            {
                var key = $"{window.ToInt64():X}:{candidate.Path}";
                if (seen.Add(key))
                    result.Add(new OpenedFolder(candidate.Path, window));
            }

            // The path header is often a graphic control, so only the foreground window can be
            // queried safely without stealing focus from another Double Commander instance.
            if (candidates.Count == 0
                && DoubleCommanderNativeMethods.IsForegroundWindow(window))
            {
                var path = DoubleCommanderPathReader.FindActivePath(
                    window,
                    DoubleCommanderNativeMethods.GetFocusedControl(window));
                var key = path is null ? string.Empty : $"{window.ToInt64():X}:{path}";
                if (!string.IsNullOrEmpty(path) && seen.Add(key))
                    result.Add(new OpenedFolder(path, window));
            }
        }

        return result;
    }
}
