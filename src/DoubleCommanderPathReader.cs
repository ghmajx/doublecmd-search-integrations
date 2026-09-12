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
    private static readonly Dictionary<IntPtr, (DateTimeOffset Timestamp, string Path)> CommandLinePathCache = new();
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
        if (!DoubleCommanderPathHeuristics.IsFileListClass(
                DoubleCommanderNativeMethods.GetClassNameValue(focusedControl))
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
                && now - cached.Timestamp < CommandLinePathCacheLifetime)
            {
                return cached.Path;
            }

            // Never overwrite a command the user is currently composing.
            if (!string.IsNullOrWhiteSpace(DoubleCommanderNativeMethods.GetWindowTextValue(commandLine)))
                return null;

            try
            {
                DoubleCommanderNativeMethods.SendControlP();
                Thread.Sleep(35);
                var path = DoubleCommanderPathHeuristics.UnquotePathText(
                    DoubleCommanderNativeMethods.GetWindowTextValue(commandLine));
                if (!DoubleCommanderPathHeuristics.LooksLikeWindowsPath(path))
                    return null;

                CommandLinePathCache[mainWindow] = (DateTimeOffset.UtcNow, path);
                return path;
            }
            finally
            {
                // cm_AddPathToCmdLine appends to edtCommand. We entered only with an empty field,
                // so restoring an empty field removes the temporary observation without deleting
                // user input that existed before the query.
                _ = DoubleCommanderNativeMethods.SetWindowTextValue(commandLine, string.Empty);
            }
        }
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
