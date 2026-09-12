using System.IO;

namespace Lertaro.Plugins.DoubleCommander;

/// <summary>
/// Pure recognition rules kept separate from Win32 calls so they can be checked without a running
/// Double Commander instance.
/// </summary>
public static class DoubleCommanderPathHeuristics
{
    public static bool IsDoubleCommanderProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
            return false;

        var name = Path.GetFileNameWithoutExtension(processName);
        return name.StartsWith("doublecmd", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMainWindowClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        return className.StartsWith("TfrmMain", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFileListClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        return className.StartsWith("LCLListBox", StringComparison.OrdinalIgnoreCase)
            || className.StartsWith("TMyListBox", StringComparison.OrdinalIgnoreCase)
            || className.Contains("FileViewGrid", StringComparison.OrdinalIgnoreCase)
            || className.Contains("TCustomGrid", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsEditorClass(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return false;

        return className.Contains("Edit", StringComparison.OrdinalIgnoreCase)
            || className.Contains("Combo", StringComparison.OrdinalIgnoreCase)
            || className.Contains("TextBox", StringComparison.OrdinalIgnoreCase);
    }

    public static bool LooksLikeWindowsPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = UnquotePathText(value);
        if (text.Length > 32767 || text.Contains('\r') || text.Contains('\n'))
            return false;

        // A minimized TPathLabel contains an ellipsis. It cannot be reconstructed safely from the
        // window text, so let the caller fall back instead of searching the wrong directory.
        if (text.Contains("...", StringComparison.Ordinal) || text.Contains('…'))
            return false;

        if (text.StartsWith(@"\\", StringComparison.Ordinal)
            || text.StartsWith(@"\\?\", StringComparison.Ordinal)
            || text.StartsWith(@"\\.\", StringComparison.Ordinal))
        {
            return true;
        }

        return text.Length >= 3
            && char.IsLetter(text[0])
            && text[1] == ':'
            && (text[2] == '\\' || text[2] == '/');
    }

    public static string UnquotePathText(string value)
    {
        var text = value.Trim();
        return text.Length >= 2 && text[0] == '"' && text[^1] == '"'
            ? text[1..^1]
            : text;
    }

    public static int PathControlScore(string? className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return 0;

        var score = 0;
        if (className.Contains("Path", StringComparison.OrdinalIgnoreCase))
            score += 30;
        if (className.Contains("Label", StringComparison.OrdinalIgnoreCase)
            || className.Equals("Static", StringComparison.OrdinalIgnoreCase))
            score += 20;
        if (IsEditorClass(className))
            score -= 30;
        return score;
    }
}
