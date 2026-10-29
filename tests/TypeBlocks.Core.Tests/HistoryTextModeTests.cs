using System.Globalization;
using TypeBlocks.Core;

internal static class HistoryTextModeTests
{
    public static void Verify(Action<string, Action> run)
    {
        var start = new DateTimeOffset(new DateTime(2031, 1, 2, 12, 0, 0, DateTimeKind.Local));
        KeystrokeEntry Entry(string key, string? text = null, int seconds = 0, string app = "notepad") =>
            new(start.AddSeconds(seconds), app, key, text, false);
        string Header(int seconds = 0, string app = "notepad") =>
            "[" + start.AddSeconds(seconds).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " · " + app + "]" + Environment.NewLine;

        run("pure text keeps typed characters while intelligent pure text applies edits", () =>
        {
            KeystrokeEntry[] entries = [Entry("A", "abc"), Entry("Backspace"), Entry("Left"), Entry("X", "X")];
            Equal(Header() + "abcX", HistoryTextFormatter.Format(entries, HistoryTextMode.PureText));
            Equal(Header() + "aXb", HistoryTextFormatter.Format(entries, HistoryTextMode.IntelligentPureText));
            Equal(Header() + "abc[Backspace][Left]X", HistoryTextFormatter.Format(entries, HistoryTextMode.Original));
            Equal(HistoryTextFormatter.Format(entries, true), HistoryTextFormatter.Format(entries, HistoryTextMode.IntelligentPureText));
        });
        run("pure text hides shortcut markers and retains literal bracket text Unicode and whitespace", () =>
        {
            KeystrokeEntry[] entries =
            [
                Entry("Left Ctrl"), Entry("A", "[Backspace]éα🙂e\u0301"), Entry("Ctrl+S", "s"),
                Entry("Alt+F", "f"), Entry("Win+R", "r"), Entry("F1"), Entry("Delete"),
                Entry("Ctrl+Alt+E", "€"), Entry("Ctrl+Alt+Q"), Entry("Shift+Space"),
                Entry("Shift+Enter"), Entry("Tab"), Entry("Shift+Tab"), Entry("Num Enter"), Entry("F2", "\u0001")
            ];
            Equal(Header() + "[Backspace]éα🙂e\u0301€ " + Environment.NewLine + "\t\t" + Environment.NewLine,
                HistoryTextFormatter.Format(entries, HistoryTextMode.PureText));
            Equal("", HistoryTextFormatter.Format([Entry("Ctrl+S"), Entry("Backspace"), Entry("Left Shift")], HistoryTextMode.PureText));
        });
        run("pure text preserves application and idle boundaries even through hidden shortcuts", () =>
        {
            KeystrokeEntry[] entries =
            [
                Entry("A", "one"), Entry("Ctrl+S", seconds: 1, app: "browser"),
                Entry("B", "two", seconds: 2), Entry("C", "three", seconds: 302)
            ];
            string separation = Environment.NewLine + Environment.NewLine;
            Equal(Header() + "one" + separation + Header(2) + "two" + separation + Header(302) + "three",
                HistoryTextFormatter.Format(entries, HistoryTextMode.PureText));
        });
        run("all keys preserves modifiers repeated presses and editing keys without replay", () =>
        {
            KeystrokeEntry[] entries =
            [
                Entry("Left Shift"), Entry("A", "A"), Entry("A", "A") with { IsRepeat = true },
                Entry("Backspace"), Entry("Backspace") with { IsRepeat = true }, Entry("Left"),
                Entry("Delete"), Entry("Ctrl+Left Shift"), Entry("Ctrl+S", "s"), Entry("F1")
            ];
            Equal(Header() + "[Left Shift]AA[Backspace][Backspace][Left][Delete][Ctrl+Left Shift][Ctrl+S][F1]",
                HistoryTextFormatter.Format(entries, HistoryTextMode.AllKeys));
            Equal(Header() + "AA[Backspace][Backspace][Left][Delete][Ctrl+S][F1]",
                HistoryTextFormatter.Format(entries));
            Equal(Header() + "AA", HistoryTextFormatter.Format(entries, HistoryTextMode.PureText));
        });
        run("all keys labels control keys and preserves printable Unicode and AltGr text", () =>
        {
            KeystrokeEntry[] entries =
            [
                Entry("Left Ctrl"), Entry("Ctrl+Right Alt"), Entry("Ctrl+Alt+E", "€"),
                Entry("A", "éα🙂e\u0301[Backspace]"), Entry("Space", " "), Entry("Shift+Space"),
                Entry("Tab"), Entry("Shift+Tab"), Entry("Enter"), Entry("Num Enter"),
                Entry("Shift+Enter"), Entry("Shift+Num Enter"), Entry("F2", "\u0001"),
                Entry("Oemtilde"), Entry("Ctrl+Alt+Q"), Entry("Alt+F", "f"), Entry("Win+R", "r")
            ];
            Equal(Header() + "[Left Ctrl][Ctrl+Right Alt]€éα🙂e\u0301[Backspace]  [Tab][Shift+Tab][Enter]" + Environment.NewLine +
                "[Num Enter]" + Environment.NewLine + "[Shift+Enter]" + Environment.NewLine + "[Shift+Num Enter]" + Environment.NewLine +
                "[F2][Oemtilde][Ctrl+Alt+Q][Alt+F][Win+R]", HistoryTextFormatter.Format(entries, HistoryTextMode.AllKeys));
        });
        run("all keys keeps modifier-only blocks and reverses whole blocks without dropping events", () =>
        {
            KeystrokeEntry[] entries =
            [
                Entry("A", "old"), Entry("Backspace", seconds: 1), Entry("Left Shift", seconds: 2, app: "browser"),
                Entry("B", "new", seconds: 302, app: "browser"), Entry("Enter", seconds: 303, app: "browser")
            ];
            string separation = Environment.NewLine + Environment.NewLine;
            Equal(Header(302, "browser") + "new[Enter]" + Environment.NewLine + Environment.NewLine +
                Header(2, "browser") + "[Left Shift]" + separation + Header() + "old[Backspace]",
                HistoryTextFormatter.Format(entries, HistoryTextMode.AllKeys, newestFirst: true));
            Equal("", HistoryTextFormatter.Format([], HistoryTextMode.AllKeys));
        });
        run("newest first reverses complete text blocks while preserving characters and edits", () =>
        {
            KeystrokeEntry[] entries =
            [
                Entry("A", "ab"), Entry("Enter", seconds: 1), Entry("C", "cd", seconds: 2),
                Entry("X", "xy", seconds: 3, app: "browser"), Entry("Backspace", seconds: 4, app: "browser"),
                Entry("Z", "z", seconds: 5, app: "browser"), Entry("N", "new", seconds: 305, app: "browser")
            ];
            string separation = Environment.NewLine + Environment.NewLine;
            string oldest = Header() + "ab" + Environment.NewLine + "cd";
            string newest = Header(305, "browser") + "new";
            Equal(newest + separation + Header(3, "browser") + "xyz" + separation + oldest,
                HistoryTextFormatter.Format(entries, HistoryTextMode.PureText, newestFirst: true));
            Equal(newest + separation + Header(3, "browser") + "xz" + separation + oldest,
                HistoryTextFormatter.Format(entries, HistoryTextMode.IntelligentPureText, newestFirst: true));
            Equal(newest + separation + Header(3, "browser") + "xy[Backspace]z" + separation + oldest,
                HistoryTextFormatter.Format(entries, HistoryTextMode.Original, newestFirst: true));
            Equal(oldest + separation + Header(3, "browser") + "xyz" + separation + newest,
                HistoryTextFormatter.Format(entries, HistoryTextMode.PureText));
        });
        run("newest first uses block encounter order for equal timestamps", () =>
        {
            KeystrokeEntry[] entries = [Entry("A", "first"), Entry("B", "second", app: "browser"), Entry("C", "third")];
            string separation = Environment.NewLine + Environment.NewLine;
            foreach (HistoryTextMode mode in Enum.GetValues<HistoryTextMode>())
                Equal(Header() + "third" + separation + Header(app: "browser") + "second" + separation + Header() + "first",
                    HistoryTextFormatter.Format(entries, mode, newestFirst: true));
        });
        run("newest first omits empty blocks without losing surrounding application boundaries", () =>
        {
            KeystrokeEntry[] entries =
            [
                Entry("A", "old"), Entry("Ctrl+S", seconds: 1, app: "browser"),
                Entry("B", "new", seconds: 2), Entry("Left Shift", seconds: 3, app: "other")
            ];
            string separation = Environment.NewLine + Environment.NewLine;
            foreach (var mode in new[] { HistoryTextMode.PureText, HistoryTextMode.IntelligentPureText })
            {
                Equal(Header(2) + "new" + separation + Header() + "old", HistoryTextFormatter.Format(entries, mode, newestFirst: true));
                Equal("", HistoryTextFormatter.Format([Entry("Ctrl+S"), Entry("Left Shift", app: "other")], mode, newestFirst: true));
            }
            Equal(Header(2, "browser") + "kept", HistoryTextFormatter.Format(
                [Entry("A", "gone"), Entry("Ctrl+A"), Entry("Backspace"), Entry("B", "kept", seconds: 2, app: "browser")],
                HistoryTextMode.IntelligentPureText, newestFirst: true));
        });
        run("newest first preserves literal header-like text inside a block", () =>
        {
            string literal = Header(20, "literal").TrimEnd('\r', '\n');
            KeystrokeEntry[] entries =
            [Entry("A", "old"), Entry("Enter"), Entry("B", literal), Entry("Enter"), Entry("C", "body"), Entry("N", "new", seconds: 30, app: "browser")];
            foreach (HistoryTextMode mode in Enum.GetValues<HistoryTextMode>())
            {
                string lineBreak = (mode == HistoryTextMode.AllKeys ? "[Enter]" : "") + Environment.NewLine;
                Equal(Header(30, "browser") + "new" + Environment.NewLine + Environment.NewLine + Header() + "old" + lineBreak + literal + lineBreak + "body",
                    HistoryTextFormatter.Format(entries, mode, newestFirst: true));
            }
        });
        run("newest first reverses intelligent replay boundaries without reversing edit operations", () =>
        {
            KeystrokeEntry[] entries =
            [Entry("A", "first"), Entry("Ctrl+V", seconds: 1), Entry("B", "seconx", seconds: 2), Entry("Backspace", seconds: 3), Entry("D", "d", seconds: 4)];
            Equal(Header(2) + "second" + Environment.NewLine + Environment.NewLine + Header() + "first",
                HistoryTextFormatter.Format(entries, HistoryTextMode.IntelligentPureText, newestFirst: true));
        });
    }

    private static void Equal(string expected, string actual)
    {
        if (expected != actual) throw new Exception("Text mode output differs from the expected transcript.");
    }
}
