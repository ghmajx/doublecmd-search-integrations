namespace Lertaro.Plugins.DoubleCommander;

internal readonly record struct DoubleCommanderPathCandidate(
    IntPtr Hwnd,
    string ClassName,
    string Path,
    NativeRect Bounds,
    int BaseScore);

internal static class DoubleCommanderPathReader
{
    private static readonly object CommandLineQueryLock = new();
    private static readonly Dictionary<IntPtr, (DateTimeOffset Timestamp, IntPtr FocusedControl, string Path)> CommandLinePathCache = new();
    private static readonly TimeSpan CommandLinePathCacheLifetime = TimeSpan.FromMilliseconds(500);

    public static string? FindActivePath(IntPtr mainWindow, IntPtr focusedControl)
    {
        var candidates = FindCandidates(mainWindow);

        if (focusedControl != IntPtr.Zero
            && DoubleCommanderNativeMethods.TryGetWindowRect(focusedControl, out var focusedBounds))
        {
            if (candidates.Count > 0)
            {
                var candidate = candidates
                    .OrderByDescending(item => ScoreForFocusedControl(item, focusedBounds))
                    .First();
                if (candidate.BaseScore >= 0)
                    return candidate.Path;
            }

            // TPathLabel is a Lazarus graphic control and may not have an HWND. In that case use
            // Double Commander's documented Ctrl+P command only when the command line is empty.
            return TryQueryActivePathFromCommandLine(mainWindow, focusedControl);
        }

        return candidates.Count > 0
            ? candidates.OrderByDescending(candidate => candidate.BaseScore).First().Path
            : null;
    }

    public static IReadOnlyList<DoubleCommanderPathCandidate> FindCandidates(IntPtr mainWindow)
    {
        var candidates = new List<DoubleCommanderPathCandidate>();
        if (mainWindow == IntPtr.Zero)
            return candidates;

        foreach (var child in DoubleCommanderNativeMethods.EnumerateChildWindows(mainWindow))
        {
            if (!DoubleCommanderNativeMethods.IsVisible(child))
                continue;

            var text = DoubleCommanderNativeMethods.GetWindowTextValue(child).Trim();
            if (!DoubleCommanderPathHeuristics.LooksLikeWindowsPath(text))
                continue;

            if (!DoubleCommanderNativeMethods.TryGetWindowRect(child, out var bounds))
                continue;

            var className = DoubleCommanderNativeMethods.GetClassNameValue(child);
            var baseScore = DoubleCommanderPathHeuristics.PathControlScore(className);
            if (baseScore < 0)
                continue;

            candidates.Add(new DoubleCommanderPathCandidate(
                child,
                className,
                text,
                bounds,
                baseScore));
        }

        return candidates;
    }

    private static int ScoreForFocusedControl(
        DoubleCommanderPathCandidate candidate,
        NativeRect focusedBounds)
    {
        var score = candidate.BaseScore;
        var overlap = candidate.Bounds.HorizontalOverlap(focusedBounds);
        if (overlap > 0)
            score += 100 + Math.Min(50, overlap / 10);
        else
            score -= 50;

        // The path header is directly above the file list. A command-line edit containing a
        // path is normally below it and therefore loses this comparison.
        var distanceAbove = focusedBounds.Top - candidate.Bounds.Bottom;
        if (distanceAbove >= -20 && distanceAbove <= 220)
            score += 100 - Math.Min(100, distanceAbove / 2);
        else if (distanceAbove < -20)
            score -= 80;
        else
            score -= Math.Min(80, distanceAbove / 4);

        return score;
    }

    private static string? TryQueryActivePathFromCommandLine(IntPtr mainWindow, IntPtr focusedControl)
    {
        if (!IsFilePanelFocus(mainWindow, focusedControl)
            || !DoubleCommanderNativeMethods.IsForegroundWindow(mainWindow))
        {
            return null;
        }

        var commandLine = FindCommandLine(mainWindow);
        if (commandLine == IntPtr.Zero
            || !DoubleCommanderNativeMethods.IsVisible(commandLine))
        {
            return null;
        }

        lock (CommandLineQueryLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (CommandLinePathCache.TryGetValue(mainWindow, out var cached)
                && cached.FocusedControl == focusedControl
                && now - cached.Timestamp < CommandLinePathCacheLifetime)
            {
                return cached.Path;
            }

            // Never overwrite a command the user is currently composing.
            if (!string.IsNullOrWhiteSpace(DoubleCommanderNativeMethods.GetWindowTextValue(commandLine)))
                return null;

            // The round trip temporarily writes the path into the command line. Painting of that
            // control is suspended for the duration so the text is never visible on screen, which
            // matters because callers may query the scope on every activation or pane switch.
            var redrawSuspended = DoubleCommanderNativeMethods.SetWindowRedraw(commandLine, false);
            try
            {
                DoubleCommanderNativeMethods.SendControlP();

                // Double Commander applies the command asynchronously, so poll briefly instead of
                // reading once: a single short read used to come back empty and made the caller drop
                // the scope it already had.
                var path = string.Empty;
                var deadline = Environment.TickCount64 + 150;
                while (Environment.TickCount64 <= deadline)
                {
                    Thread.Sleep(15);
                    path = DoubleCommanderPathHeuristics.UnquotePathText(
                        DoubleCommanderNativeMethods.GetWindowTextValue(commandLine));
                    if (DoubleCommanderPathHeuristics.LooksLikeWindowsPath(path))
                        break;
                }

                if (!DoubleCommanderPathHeuristics.LooksLikeWindowsPath(path))
                    return null;

                CommandLinePathCache[mainWindow] = (DateTimeOffset.UtcNow, focusedControl, path);
                return path;
            }
            finally
            {
                // cm_AddPathToCmdLine appends to edtCommand. We entered only with an empty field,
                // so restoring an empty field removes the temporary observation without deleting
                // user input that existed before the query.
                ClearCommandLine(commandLine);
                if (redrawSuspended)
                    _ = DoubleCommanderNativeMethods.SetWindowRedraw(commandLine, true);
            }
        }
    }

    /// <summary>
    /// Double Commander can apply the path to the command line slightly after the read, so clear the
    /// field, verify it, and retry once to make sure no temporary text is left behind.
    /// </summary>
    private static void ClearCommandLine(IntPtr commandLine)
    {
        _ = DoubleCommanderNativeMethods.SetWindowTextValue(commandLine, string.Empty);
        Thread.Sleep(25);
        if (!string.IsNullOrEmpty(DoubleCommanderNativeMethods.GetWindowTextValue(commandLine)))
            _ = DoubleCommanderNativeMethods.SetWindowTextValue(commandLine, string.Empty);
    }

    /// <summary>
    /// True when the focused control can host a file panel. Lazarus builds expose the panels as
    /// generic "Window" controls, so class names alone are not enough: for that class the window must
    /// belong to the Double Commander main window and be large enough to be a panel rather than chrome.
    /// </summary>
    private static bool IsFilePanelFocus(IntPtr mainWindow, IntPtr focusedControl)
    {
        if (focusedControl == IntPtr.Zero)
            return false;

        var className = DoubleCommanderNativeMethods.GetClassNameValue(focusedControl);
        if (DoubleCommanderPathHeuristics.IsEditorClass(className))
            return false;

        // The caller falls back to the main window when it cannot resolve the focused control.
        if (DoubleCommanderPathHeuristics.IsMainWindowClass(className))
            return true;

        if (DoubleCommanderPathHeuristics.IsFileListClass(className))
            return true;

        if (!DoubleCommanderPathHeuristics.IsGenericLclListHostClass(className))
            return false;

        return DoubleCommanderNativeMethods.GetRootWindow(focusedControl) == mainWindow
            && DoubleCommanderNativeMethods.TryGetWindowRect(focusedControl, out var bounds)
            && bounds.Width >= 120
            && bounds.Height >= 120;
    }

    private static IntPtr FindCommandLine(IntPtr mainWindow)
    {
        var best = IntPtr.Zero;
        var bestScore = int.MinValue;
        var hasMainBounds = DoubleCommanderNativeMethods.TryGetWindowRect(mainWindow, out var mainBounds);

        foreach (var child in DoubleCommanderNativeMethods.EnumerateChildWindows(mainWindow))
        {
            var className = DoubleCommanderNativeMethods.GetClassNameValue(child);
            if (!className.Contains("ComboBox", StringComparison.OrdinalIgnoreCase)
                || !DoubleCommanderNativeMethods.TryGetWindowRect(child, out var bounds)
                || !DoubleCommanderNativeMethods.IsVisible(child))
            {
                continue;
            }

            var score = bounds.Width;
            if (hasMainBounds && bounds.Bottom >= mainBounds.Bottom - 140)
                score += 1000;
            if (bounds.Width >= 160)
                score += 200;

            if (score > bestScore)
            {
                bestScore = score;
                best = child;
            }
        }

        return best;
    }
}
