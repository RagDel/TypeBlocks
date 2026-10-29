using System.Globalization;
using System.Text;

namespace TypeBlocks.Core;

/// <summary>A readable event transcript, with optional best-effort replay of recorded text edits.</summary>
public static class HistoryTextFormatter
{
    public static string Format(IEnumerable<KeystrokeEntry> entries, bool pureText = false)
    {
        ArgumentNullException.ThrowIfNull(entries);
        if (pureText) return PureTextReplay.Format(entries);
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

            string content = FormatEntry(entry);
            if (content.Length == 0) continue;
            if (needsHeader)
            {
                if (text.Length > 0)
                {
                    if (text[^1] != '\n') text.AppendLine();
                    text.AppendLine();
                }
                text.Append('[')
                    .Append(entry.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append(" · ").Append(entry.Application).AppendLine("]");
                needsHeader = false;
            }
            text.Append(content);
        }
        return text.ToString();
    }

    private static string FormatEntry(KeystrokeEntry entry)
    {
        string key = entry.Key;
        // Modifier presses clutter prose; the detailed table retains every original event.
        if (key.Split('+').All(IsModifier)) return "";

        bool control = key.StartsWith("Ctrl+", StringComparison.Ordinal);
        bool alt = key.StartsWith("Alt+", StringComparison.Ordinal) || key.Contains("+Alt+", StringComparison.Ordinal);
        bool windows = key.StartsWith("Win+", StringComparison.Ordinal) || key.Contains("+Win+", StringComparison.Ordinal);
        // Match the recorder's shortcut policy, while keeping printable AltGr (Ctrl+Alt) text.
        if (windows || control != alt) return "[" + key + "]";

        if (key is "Enter" or "Num Enter") return Environment.NewLine;
        if (key == "Tab") return "\t";
        if (!string.IsNullOrEmpty(entry.Text) && !entry.Text.Any(char.IsControl)) return entry.Text;
        if (key == "Space") return " ";
        return "[" + key + "]";
    }

    private static bool IsModifier(string key) => key is
        "Shift" or "Ctrl" or "Alt" or "Win" or
        "Left Shift" or "Right Shift" or "Left Ctrl" or "Right Ctrl" or
        "Left Alt" or "Right Alt" or "Left Win" or "Right Win";
}
