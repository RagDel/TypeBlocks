using System.Globalization;
using System.Text.Json;
using TypeBlocks.Core;

internal static class PureTextCases
{
    private static readonly DateTimeOffset Timestamp = new(new DateTime(2031, 1, 2, 12, 0, 0, DateTimeKind.Local));
    private sealed record Step(string Key, string? Text = null, int Seconds = 0, string App = "notepad", bool Repeat = false);
    private sealed record Case(string Id, string Group, string Title, string Description, string Expected, Step[] Steps);

    public static void Verify(Action<string, Action> run, string? previewPath)
    {
        var preview = new List<object>();
        foreach (var sample in Examples())
        {
            string actual = HistoryTextFormatter.Format(sample.Steps.Select(s =>
                new KeystrokeEntry(Timestamp.AddSeconds(s.Seconds), s.App, s.Key, s.Text, s.Repeat)), pureText: true);
            bool passed = actual == sample.Expected;
            preview.Add(new { id = sample.Id, group = sample.Group, title = sample.Title,
                description = sample.Description, input = Describe(sample.Steps), expected = sample.Expected,
                actual, passed, status = passed ? "Verified with synthetic events" : "Needs correction" });
            run("pure text: " + sample.Title, () =>
            {
                if (!passed) throw new Exception($"Expected {JsonSerializer.Serialize(sample.Expected)}; got {JsonSerializer.Serialize(actual)}");
            });
        }
        if (previewPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(previewPath))!);
            File.WriteAllText(previewPath, JsonSerializer.Serialize(preview, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static IEnumerable<Case> Examples()
    {
        yield return C("backspace-three", "Delete and correct", "Three Backspaces remove three characters",
            "Each recorded press, including a repeat, removes one character before the reconstructed caret.", "abc",
            [.. T("abcdef"), K("Backspace"), K("Backspace", repeat: true), K("Backspace", repeat: true)]);
        yield return C("correct-typo", "Delete and correct", "Correct a mistyped ending",
            "New typing goes where the removed characters were.", "hello", [.. T("helxx"), K("Backspace"), K("Backspace"), .. T("lo")]);
        yield return C("backspace-limit", "Delete and correct", "Backspace stops at the block start",
            "Extra Backspaces cannot delete timestamps or an earlier block.", "B", [.. T("A"), K("Backspace"), K("Backspace"), .. T("B")]);
        yield return C("delete-forward", "Delete and correct", "Delete removes the next character",
            "The caret stays in place while forward characters are removed.", "acd", [.. T("abcd"), K("Home"), K("Right"), K("Delete")]);
        yield return C("delete-end", "Delete and correct", "Delete at the end changes nothing",
            "A forward deletion beyond the recorded text is a no-op.", "abc", [.. T("abc"), K("Delete")]);
        yield return C("erase-block", "Delete and correct", "Fully erased blocks disappear",
            "When every recorded character is deleted, the empty block and its heading are omitted.", null, [.. T("abc"), K("Backspace"), K("Backspace"), K("Backspace")]);
        yield return C("join-lines-backspace", "Delete and correct", "Backspace can join two lines",
            "A recorded line break is removed as one unit.", "onetwo", [.. T("one\ntwo"), K("Home"), K("Backspace")]);
        yield return C("join-lines-delete", "Delete and correct", "Delete can join two lines",
            "Delete at the end of the first line removes its line break.", "onetwo", [.. T("one\ntwo"), K("Ctrl+Home"), K("End"), K("Delete")]);
        yield return C("insert-left", "Move and insert", "Left arrow supports insertion inside text",
            "Typing inserts at the simulated caret instead of always appending.", "cat", [.. T("ct"), K("Left"), .. T("a")]);
        yield return C("insert-right", "Move and insert", "Right arrow advances the caret",
            "Repeated movement is clamped to the current block.", "abXc", [.. T("abc"), K("Home"), K("Right"), K("Right"), .. T("X")]);
        yield return C("home-end", "Move and insert", "Home and End use the current logical line",
            "These keys use recorded line breaks; screen wrapping is unavailable.", "one\n>two!", [.. T("one\ntwo"), K("Home"), .. T(">"), K("End"), .. T("!")]);
        yield return C("ctrl-home-end", "Move and insert", "Ctrl+Home and Ctrl+End use the block",
            "The whole reconstructed block is the available document.", "[one\ntwo]", [.. T("one\ntwo"), K("Ctrl+Home"), .. T("["), K("Ctrl+End"), .. T("]")]);
        yield return C("up-column", "Move and insert", "Up keeps the character column",
            "Moves between logical lines, with Unicode characters counted as text elements.", "abXcd\nxy", [.. T("abcd\nxy"), K("Up"), .. T("X")]);
        yield return C("preferred-column", "Move and insert", "Vertical movement remembers the column",
            "Passing through a short line does not lose the desired column on the next line.", "abcXdef\nx\n123", [.. T("abcdef\nx\n123"), K("Up"), K("Up"), .. T("X")]);
        yield return C("down-column", "Move and insert", "Down moves to the next logical line",
            "A line shorter than the desired column clamps the caret to its end.", "abcd\nx!", [.. T("abcd\nx"), K("Ctrl+Home"), K("Right"), K("Right"), K("Down"), .. T("!")]);
        yield return C("word-left", "Word editing", "Ctrl+Left moves to the previous word",
            "Skips preceding whitespace, then the preceding word or punctuation group.", "one Xtwo", [.. T("one two"), K("Ctrl+Left"), .. T("X")]);
        yield return C("word-right", "Word editing", "Ctrl+Right moves to the next word",
            "Skips the current character group and following whitespace.", "one Xtwo", [.. T("one two"), K("Home"), K("Ctrl+Right"), .. T("X")]);
        yield return C("word-backspace", "Word editing", "Ctrl+Backspace deletes the previous word",
            "Uses the same word grouping as Ctrl+Left.", "one ", [.. T("one two"), K("Ctrl+Backspace")]);
        yield return C("word-delete", "Word editing", "Ctrl+Delete deletes the next word",
            "Deletes through the next word boundary, including following spaces.", "two", [.. T("one two"), K("Home"), K("Ctrl+Delete")]);
        yield return C("word-punctuation", "Word editing", "Word deletion treats punctuation separately",
            "Letters, digits and underscore form words; punctuation and whitespace form separate groups.", "foo", [.. T("foo, bar"), K("Ctrl+Backspace"), K("Ctrl+Backspace")]);
        yield return C("word-whitespace", "Word editing", "Word deletion skips trailing spaces",
            "Ctrl+Backspace removes trailing whitespace and the preceding word.", "one ", [.. T("one two   "), K("Ctrl+Backspace")]);
        yield return C("replace-selection", "Select and replace", "Shift+Left then typing replaces a selection",
            "Selections are simulated from recorded keyboard movement.", "abXY", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), .. T("XY")]);
        yield return C("selection-backspace", "Select and replace", "Backspace deletes a selection",
            "A selection is removed instead of an extra preceding character.", "ab", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), K("Backspace")]);
        yield return C("selection-delete", "Select and replace", "Delete removes a forward selection",
            "Selection direction does not affect the removed range.", "cd", [.. T("abcd"), K("Home"), K("Shift+Right"), K("Shift+Right"), K("Delete")]);
        yield return C("select-word", "Select and replace", "Ctrl+Shift+Left selects a word",
            "Word selection can be replaced with newly typed text.", "one three", [.. T("one two"), K("Ctrl+Shift+Left"), .. T("three")]);
        yield return C("select-next-word", "Select and replace", "Ctrl+Shift+Right selects forward",
            "The selection includes following whitespace using the replay word rules.", "new two", [.. T("one two"), K("Home"), K("Ctrl+Shift+Right"), .. T("new ")]);
        yield return C("select-line-home", "Select and replace", "Shift+Home selects to the line start",
            "Other logical lines remain intact.", "one\nnew", [.. T("one\ntwo"), K("Shift+Home"), .. T("new")]);
        yield return C("select-line-end", "Select and replace", "Shift+End selects to the line end",
            "Line breaks beyond the selected range are preserved.", "new\ntwo", [.. T("one\ntwo"), K("Ctrl+Home"), K("Shift+End"), .. T("new")]);
        yield return C("select-block-home", "Select and replace", "Ctrl+Shift+Home selects to the block start",
            "The selection never reaches preceding transcript blocks.", "new", [.. T("one\ntwo"), K("Ctrl+Shift+Home"), .. T("new")]);
        yield return C("select-block-end", "Select and replace", "Ctrl+Shift+End selects to the block end",
            "Delete removes the selected remainder of the current block.", "o", [.. T("one\ntwo"), K("Ctrl+Home"), K("Right"), K("Ctrl+Shift+End"), K("Delete")]);
        yield return C("select-up", "Select and replace", "Shift+Up selects across logical lines",
            "Uses the vertical caret movement rules while preserving the selection anchor.", "ab!", [.. T("abc\nxy"), K("Shift+Up"), .. T("!")]);
        yield return C("select-down", "Select and replace", "Shift+Down selects into the next line",
            "Only the recorded span between anchor and caret is replaced.", "a!y", [.. T("abc\nxy"), K("Ctrl+Home"), K("Right"), K("Shift+Down"), .. T("!")]);
        yield return C("select-all", "Select and replace", "Ctrl+A selects the reconstructed block",
            "Typing replaces all known text in this block, not every app or the whole day.", "new", [.. T("old text"), K("Ctrl+A"), .. T("new")]);
        yield return C("selection-collapse-left", "Select and replace", "Plain Left collapses a selection",
            "Collapses to the selection start without an extra step.", "ab!cd", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), K("Left"), .. T("!")]);
        yield return C("selection-collapse-right", "Select and replace", "Plain Right collapses a selection",
            "Collapses to the selection end without an extra step.", "abcd!", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), K("Right"), .. T("!")]);
        yield return C("cut-selection", "Clipboard and boundaries", "Ctrl+X cuts a known selection",
            "Removes the selected text; it does not read or write the real clipboard.", "ab", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), K("Ctrl+X")]);
        yield return C("shift-delete-cut", "Clipboard and boundaries", "Shift+Delete cuts a known selection",
            "Uses the conventional cut shortcut when a selection is known.", "ab", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), K("Shift+Delete")]);
        yield return C("cut-no-selection", "Clipboard and boundaries", "Cut without a known selection is conservative",
            "No recorded text is assumed selected; the prior block is preserved.", "abc", [.. T("abc"), K("Ctrl+X")]);
        yield return F("cut-unknown-boundary", "Clipboard and boundaries", "Unknown cut protects the preceding block",
            "Some editors cut a whole line without a selection. Later deletion cannot safely be applied to the earlier text.", B("known") + Gap + B("!"),
            [.. T("known"), K("Ctrl+X"), K("Backspace"), .. T("!")]);
        yield return F("shift-cut-unknown-boundary", "Clipboard and boundaries", "Shift+Delete without a selection also isolates edits",
            "A cut with unknown scope starts a fresh editable block.", B("known") + Gap + B("!"),
            [.. T("known"), K("Shift+Delete"), K("Ctrl+A"), K("Delete"), .. T("!")]);
        yield return C("copy-selection", "Clipboard and boundaries", "Copy preserves text and selection",
            "Ctrl+C and Ctrl+Insert do not alter text; subsequent typing still replaces the selection.", "abX", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), K("Ctrl+C"), K("Ctrl+Insert"), .. T("X")]);
        yield return F("paste-boundary", "Clipboard and boundaries", "Unknown paste starts a fresh edit block",
            "Clipboard content was never recorded. Later Backspace cannot erase text from before Ctrl+V.", B("known") + Gap + B("!"),
            [.. T("known"), K("Ctrl+V", "v"), K("Backspace"), .. T("!")]);
        yield return F("shift-insert-boundary", "Clipboard and boundaries", "Shift+Insert also protects earlier text",
            "Treats this paste alias as an unknown insertion, without inventing clipboard content.", B("known") + Gap + B("!"),
            [.. T("known"), K("Shift+Insert"), K("Ctrl+A"), K("Delete"), .. T("!")]);
        yield return F("paste-plain-boundary", "Clipboard and boundaries", "Ctrl+Shift+V protects earlier text",
            "Paste-as-plain-text still needs unavailable clipboard contents.", B("known") + Gap + B("!"),
            [.. T("known"), K("Ctrl+Shift+V"), K("Ctrl+Z"), .. T("!")]);
        yield return F("focus-shortcut", "Clipboard and boundaries", "Focus-changing shortcuts start a new block",
            "Ctrl+L may select an address bar; subsequent editing is isolated from the old text.", B("page") + Gap + B("query"),
            [.. T("page"), K("Ctrl+L"), K("Ctrl+A"), .. T("query")]);
        yield return C("safe-shortcuts", "Clipboard and boundaries", "Save and copy do not add shortcut letters",
            "Known non-editing shortcuts stay hidden and do not break safe editing.", "ab", [.. T("abc"), K("Ctrl+S", "s"), K("Ctrl+C", "c"), K("Backspace")]);
        yield return C("undo-delete", "Undo and overwrite", "Ctrl+Z restores a recorded deletion",
            "Undo is one recorded edit at a time within the current block; editor-specific grouping is unavailable.", "cat", [.. T("cat"), K("Backspace"), K("Ctrl+Z")]);
        yield return C("redo-delete", "Undo and overwrite", "Ctrl+Y redoes a recorded deletion",
            "Redo applies the most recently undone local edit.", "ca", [.. T("cat"), K("Backspace"), K("Ctrl+Z"), K("Ctrl+Y")]);
        yield return C("redo-alias", "Undo and overwrite", "Ctrl+Shift+Z is a redo alias",
            "Uses the common alternative redo shortcut.", "ca", [.. T("cat"), K("Backspace"), K("Ctrl+Z"), K("Ctrl+Shift+Z")]);
        yield return C("undo-selection", "Undo and overwrite", "Undo restores replaced selection text",
            "Restores both the text and the simulated selection from before the edit.", "abcd", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), .. T("X"), K("Ctrl+Z")]);
        yield return C("undo-selection-caret", "Undo and overwrite", "Undo restores the selection for the next edit",
            "After undoing a replacement, typing replaces the original selected range again.", "abY", [.. T("abcd"), K("Shift+Left"), K("Shift+Left"), .. T("X"), K("Ctrl+Z"), .. T("Y")]);
        yield return C("redo-invalidated", "Undo and overwrite", "A new edit clears redo",
            "A new typing branch cannot replay an obsolete deletion.", "cat!", [.. T("cat"), K("Backspace"), K("Ctrl+Z"), .. T("!"), K("Ctrl+Y")]);
        yield return C("redo-after-movement", "Undo and overwrite", "Navigation alone preserves redo",
            "A caret movement is not a text edit; redo still restores the original recorded change.", "ca", [.. T("cat"), K("Backspace"), K("Ctrl+Z"), K("Home"), K("Ctrl+Y")]);
        yield return C("overwrite", "Undo and overwrite", "Insert toggles overwrite mode",
            "In overwrite mode, typing replaces the next character at the caret.", "aXc", [.. T("abc"), K("Home"), K("Right"), K("Insert"), .. T("X")]);
        yield return C("overwrite-toggle-off", "Undo and overwrite", "Insert toggles back to insertion",
            "The second press restores normal insertion.", "aXYc", [.. T("abc"), K("Home"), K("Right"), K("Insert"), .. T("X"), K("Insert"), .. T("Y")]);
        yield return C("overwrite-newline", "Undo and overwrite", "Overwrite preserves the next line break",
            "Typing at a logical line end inserts before the break instead of swallowing it.", "abX\ncd", [.. T("ab\ncd"), K("Ctrl+Home"), K("End"), K("Insert"), .. T("X")]);
        yield return C("emoji-delete", "Characters and whitespace", "Emoji delete as a whole character",
            "Backspace does not leave an unmatched UTF-16 surrogate.", "a", [.. T("a🙂"), K("Backspace")]);
        yield return C("combining-delete", "Characters and whitespace", "Combined accents delete together",
            "A base letter and combining accent recorded separately are treated as one text element.", "a", [.. T("ae"), K("Accent", "\u0301"), K("Backspace")]);
        yield return C("joined-emoji-delete", "Characters and whitespace", "Joined emoji stay intact",
            "A multi-code-point emoji cluster is not cut in half.", "a", [.. T("a👩‍💻"), K("Backspace")]);
        yield return C("emoji-join-in-middle", "Characters and whitespace", "Joining existing emoji keeps one deletion boundary",
            "If a recorded joiner merges neighboring emoji, the resulting character can still be removed whole.", "a", [.. T("a👩💻"), K("Left"), K("Joiner", "\u200d"), K("Backspace")]);
        yield return C("flag-pair-delete", "Characters and whitespace", "Flag symbols recorded separately form one character",
            "A regional-indicator pair is one grapheme for Backspace.", "a", [.. T("a"), K("Text", "🇬"), K("Text", "🇷"), K("Backspace")]);
        yield return C("unicode-movement", "Characters and whitespace", "Arrows and Delete respect Unicode characters",
            "Caret movement and forward deletion use the same text-element boundaries.", "aZb", [.. T("a🙂b"), K("Left"), K("Left"), K("Delete"), .. T("Z")]);
        yield return C("literal-brackets", "Characters and whitespace", "Typed bracket text stays literal",
            "Only recorded key events are interpreted. Typed strings such as [Backspace] are preserved.", "[Backspace] [Ctrl+V]", [.. T("[Backspace] [Ctrl+V]")]);
        yield return C("altgr-unicode", "Characters and whitespace", "Shift and AltGr preserve translated text",
            "Printed symbols and non-Latin text come from the recorded translation, not guessed key names.", "A€éα🙂", [K("Left Shift"), K("Shift+A", "A"), K("Ctrl+Alt+E", "€"), .. T("éα🙂")]);
        yield return C("whitespace", "Characters and whitespace", "Enter, keypad Enter, Shift+Enter and Tab",
            "Enter variants insert a line break; plain Tab inserts a tab character.", "a\nb\nc\n\td ", [.. T("a"), K("Enter"), .. T("b"), K("Num Enter"), .. T("c"), K("Shift+Enter"), K("Tab"), .. T("d"), K("Space")]);
        yield return F("app-boundary", "Block safety", "Application changes isolate edits",
            "Returning to an application does not recover its previous caret or document.", B("first") + Gap + B("new", 2),
            [K("Text", "first"), K("Backspace", seconds: 1, app: "browser"), K("Backspace", seconds: 2), K("Text", "new", seconds: 2)]);
        yield return F("idle-boundary", "Block safety", "Five-minute gaps isolate edits",
            "Later deletes and undo cannot change the earlier block.", B("first") + Gap + B("new", 301),
            [K("Text", "first"), K("Backspace", seconds: 300), K("Ctrl+Z", seconds: 301), K("Text", "new", seconds: 301)]);
        yield return C("hidden-only", "Block safety", "Non-text-only input creates no empty headings",
            "Modifiers, shortcuts and editing keys with no reconstructed text stay invisible.", null,
            [K("Left Ctrl"), K("Ctrl+V"), K("Backspace"), K("Left"), K("Ctrl+A"), K("Delete")]);
        yield return F("unknown-keys", "Block safety", "Escape, page keys and unknown commands isolate edits",
            "Their effect depends on the application or screen layout, so no movement or edit is invented.", B("known") + Gap + B("!"),
            [.. T("known"), K("Escape"), K("PageUp"), K("PageDown"), K("F1"), K("Shift+Tab"), K("Backspace"), .. T("!")]);
    }

    private static string Gap => Environment.NewLine + Environment.NewLine;
    private static string B(string text, int seconds = 0, string app = "notepad") =>
        $"[{Timestamp.AddSeconds(seconds).ToLocalTime():HH:mm:ss} · {app}]" + Environment.NewLine + text.Replace("\n", Environment.NewLine);
    private static Case C(string id, string group, string title, string description, string? expected, Step[] steps) =>
        new(id, group, title, description, expected is null ? "" : B(expected), steps);
    private static Case F(string id, string group, string title, string description, string expected, Step[] steps) =>
        new(id, group, title, description, expected, steps);
    private static Step K(string key, string? text = null, int seconds = 0, string app = "notepad", bool repeat = false) => new(key, text, seconds, app, repeat);
    private static IEnumerable<Step> T(string text)
    {
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            yield return element switch { "\n" => K("Enter"), "\t" => K("Tab"), " " => K("Space", " "), _ => K("Text", element) };
        }
    }
    private static string Describe(IEnumerable<Step> steps)
    {
        var tokens = new List<string>();
        var typed = new System.Text.StringBuilder();
        void FlushText()
        {
            if (typed.Length == 0) return;
            tokens.Add("Type " + JsonSerializer.Serialize(typed.ToString(), new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
            typed.Clear();
        }
        foreach (var step in steps)
        {
            if (step.Key is "Text" or "Space" && step.Text is not null && step.Seconds == 0 && step.App == "notepad") typed.Append(step.Text);
            else
            {
                FlushText();
                tokens.Add("[" + step.Key + "]" + (step.Text is not null ? " (text: " + step.Text + ")" : "") +
                    (step.Seconds > 0 || step.App != "notepad" ? $" ({step.App}, +{step.Seconds}s)" : ""));
            }
        }
        FlushText();
        return string.Join(" → ", tokens);
    }
}
