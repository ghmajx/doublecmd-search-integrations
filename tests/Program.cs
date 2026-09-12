using Lertaro.Plugins.DoubleCommander;

var cases = new (string Name, bool Actual, bool Expected)[]
{
    ("process name", DoubleCommanderPathHeuristics.IsDoubleCommanderProcess("doublecmd.exe"), true),
    ("64-bit process name", DoubleCommanderPathHeuristics.IsDoubleCommanderProcess("doublecmd64.exe"), true),
    ("main window class", DoubleCommanderPathHeuristics.IsMainWindowClass("TfrmMain"), true),
    ("Double Commander main window class", DoubleCommanderPathHeuristics.IsMainWindowClass("TTOTAL_CMD"), true),
    ("file list class", DoubleCommanderPathHeuristics.IsFileListClass("LCLListBox1"), true),
    ("drive path", DoubleCommanderPathHeuristics.LooksLikeWindowsPath(@"C:\Work"), true),
    ("quoted drive path", DoubleCommanderPathHeuristics.LooksLikeWindowsPath(@"""C:\Program Files\Work"""), true),
    ("UNC path", DoubleCommanderPathHeuristics.LooksLikeWindowsPath(@"\\server\share"), true),
    ("minimized path", DoubleCommanderPathHeuristics.LooksLikeWindowsPath(@"C:\...\project"), false),
    ("relative text", DoubleCommanderPathHeuristics.LooksLikeWindowsPath("project"), false)
};

foreach (var test in cases)
{
    if (test.Actual != test.Expected)
        throw new InvalidOperationException($"{test.Name}: expected {test.Expected}, got {test.Actual}");
}

Console.WriteLine($"{cases.Length} heuristic checks passed.");
