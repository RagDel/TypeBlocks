using System.Globalization;
using System.Text;

namespace TypeBlocks.Core;

/// <summary>A readable event transcript, with optional best-effort replay of recorded text edits.</summary>
public static class HistoryTextFormatter
{
    // Keep the original bool API's replay behavior for existing callers.
    public static string Format(IEnumerable<KeystrokeEntry> entries, bool pureText = false) =>
        Format(entries, pureText ? HistoryTextMode.IntelligentPureText : HistoryTextMode.Original);

    public static string Format(IEnumerable<KeystrokeEntry> entries, HistoryTextMode mode) => Format(entries, mode, newestFirst: false);

    public static string Format(IEnumerable<KeystrokeEntry> entries, HistoryTextMode mode, bool newestFirst)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (mode == HistoryTextMode.IntelligentPureText) return PureTextReplay.Format(entries, newestFirst);
        var blocks = new List<string>();
        var text = new StringBuilder();
        KeystrokeEntry? previous = null;
        bool needsHeader = true;
        foreach (var entry in entries)
        {
            if (previous is not null &&
                (!string.Equals(previous.Application, entry.Application, StringComparison.OrdinalIgnoreCase) ||
                 entry.Timestamp - previous.Timestamp >= TimeSpan.FromMinutes(5)))
                needsHeader = true;
            previous = entry;

            string content = FormatEntry(entry, mode);
            if (content.Length == 0) continue;
            if (needsHeader)
            {
                if (text.Length > 0)
                {
                    blocks.Add(text.ToString());
                    text.Clear();
                }
                text.Append('[')
                    .Append(entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append(" · ").Append(entry.Application).AppendLine("]");
                needsHeader = false;
            }
            text.Append(content);
        }
        if (text.Length > 0) blocks.Add(text.ToString());
        return JoinBlocks(blocks, newestFirst);
    }

    internal static string JoinBlocks(IReadOnlyList<string> blocks, bool newestFirst)
    {
        var output = new StringBuilder();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (output.Length > 0)
            {
                if (output[^1] != '\n') output.AppendLine();
                output.AppendLine();
            }
            // Only completed display blocks move. Entries, caret motion, and edit replay stay chronological.
            output.Append(blocks[newestFirst ? blocks.Count - 1 - i : i]);
        }
        return output.ToString();
    }

    private static string FormatEntry(KeystrokeEntry entry, HistoryTextMode mode)
    {
        bool pureText = mode == HistoryTextMode.PureText;
        bool allKeys = mode == HistoryTextMode.AllKeys;
        string key = entry.Key;
        // The prose modes omit modifiers; All keys preserves each recorded press.
        if (key.Split('+').All(IsModifier)) return allKeys ? "[" + key + "]" : "";

        bool control = key.StartsWith("Ctrl+", StringComparison.Ordinal);
        bool alt = key.StartsWith("Alt+", StringComparison.Ordinal) || key.Contains("+Alt+", StringComparison.Ordinal);
        bool windows = key.StartsWith("Win+", StringComparison.Ordinal) || key.Contains("+Win+", StringComparison.Ordinal);
        // Match the recorder's shortcut policy, while keeping printable AltGr (Ctrl+Alt) text.
        if (windows || control != alt) return pureText ? "" : "[" + key + "]";

        if (allKeys && key is "Enter" or "Num Enter" or "Shift+Enter" or "Shift+Num Enter")
            return "[" + key + "]" + Environment.NewLine;
        if (allKeys && key is "Tab" or "Shift+Tab") return "[" + key + "]";
        if (key is "Enter" or "Num Enter") return Environment.NewLine;
        if (key == "Tab") return "\t";
        if (pureText && key is "Shift+Enter" or "Shift+Num Enter") return Environment.NewLine;
        if (pureText && key == "Shift+Tab") return "\t";
        if (!string.IsNullOrEmpty(entry.Text) && !entry.Text.Any(char.IsControl)) return entry.Text;
        if (key == "Space" || ((pureText || allKeys) && key == "Shift+Space")) return " ";
        return pureText ? "" : "[" + key + "]";
    }

    private static bool IsModifier(string key) => key is
        "Shift" or "Ctrl" or "Alt" or "Win" or
        "Left Shift" or "Right Shift" or "Left Ctrl" or "Right Ctrl" or
        "Left Alt" or "Right Alt" or "Left Win" or "Right Win";
}
