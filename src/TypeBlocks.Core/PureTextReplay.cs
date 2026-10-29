using System.Globalization;
using System.Text;

namespace TypeBlocks.Core;

/// <summary>Replays only the local text implied by recorded events, never the application's document or clipboard.</summary>
internal static class PureTextReplay
{
    public static string Format(IEnumerable<KeystrokeEntry> entries, bool newestFirst = false)
    {
        var blocks = new List<string>();
        var buffer = new EditBuffer();
        KeystrokeEntry? previous = null;
        KeystrokeEntry? firstText = null;

        void Flush()
        {
            if (buffer.Length > 0 && firstText is not null)
            {
                var block = new StringBuilder();
                block.Append('[').Append(firstText.Timestamp.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                    .Append(" · ").Append(firstText.Application).AppendLine("]");
                block.Append(buffer.Text.Replace("\n", Environment.NewLine, StringComparison.Ordinal));
                blocks.Add(block.ToString());
            }
            buffer.Reset();
            firstText = null;
        }

        foreach (var entry in entries)
        {
            if (previous is not null &&
                (!string.Equals(previous.Application, entry.Application, StringComparison.OrdinalIgnoreCase) ||
                 entry.Timestamp - previous.Timestamp >= TimeSpan.FromMinutes(5)))
                Flush();
            previous = entry;
            // Unobservable edits/focus changes stop replay, so later deletion cannot erase earlier known text.
            if (!buffer.Apply(entry)) Flush();
            if (buffer.Length > 0 && firstText is null) firstText = entry;
        }
        Flush();
        return HistoryTextFormatter.JoinBlocks(blocks, newestFirst);
    }

    private sealed class EditBuffer
    {
        private const int MaxUndoEdits = 512;
        private const int MaxUndoCharacters = 1_048_576;
        private readonly StringBuilder _text = new();
        // Includes the final Length boundary. Edits reparse the affected suffix, not the whole growing transcript.
        private readonly List<int> _boundaries = [0];
        private readonly LinkedList<Edit> _undo = new();
        private readonly LinkedList<Edit> _redo = new();
        private int _historyCharacters;
        private int _caret;
        private int _anchor;
        private int? _preferredColumn;
        private bool _overwrite;

        private sealed record Edit(int Start, string Removed, string Inserted, int BeforeCaret, int BeforeAnchor,
            int AfterCaret, int AfterAnchor)
        {
            public int Cost => Removed.Length + Inserted.Length;
        }

        public int Length => _text.Length;
        public string Text => _text.ToString();
        private int SelectionStart => Math.Min(_caret, _anchor);
        private int SelectionLength => Math.Abs(_caret - _anchor);

        public void Reset()
        {
            _text.Clear();
            _boundaries.Clear();
            _boundaries.Add(0);
            _undo.Clear();
            _redo.Clear();
            _historyCharacters = _caret = _anchor = 0;
            _preferredColumn = null;
            _overwrite = false;
        }

        public bool Apply(KeystrokeEntry entry)
        {
            string[] parts = entry.Key.Split('+');
            if (parts.All(IsModifier)) return true;
            string key = parts[^1];
            bool control = parts.Contains("Ctrl"), alt = parts.Contains("Alt"), shift = parts.Contains("Shift"), windows = parts.Contains("Win");
            bool printable = !string.IsNullOrEmpty(entry.Text) && !entry.Text.Any(char.IsControl);
            if (windows) return false;
            if (control && alt)
            {
                // The recorder retains printable Ctrl+Alt characters for AltGr layouts.
                if (!printable) return false;
                Insert(entry.Text!);
                return true;
            }
            if (alt) return false;
            if (control)
            {
                switch (key)
                {
                    case "Left": MoveHorizontal(left: true, word: true, select: shift); return true;
                    case "Right": MoveHorizontal(left: false, word: true, select: shift); return true;
                    case "Home": Move(0, shift); return true;
                    case "End": Move(_text.Length, shift); return true;
                    case "A" when !shift: _anchor = 0; _caret = _text.Length; _preferredColumn = null; return true;
                    case "Backspace" when !shift: Delete(backward: true, word: true); return true;
                    case "Delete" when !shift: Delete(backward: false, word: true); return true;
                    // Some editors cut a whole line with no selection; that behavior is not observable here.
                    case "X" when !shift: if (SelectionLength == 0) return false; Cut(); return true;
                    case "C" or "Insert" or "S" when !shift: return true;
                    case "Z": Undo(redo: shift); return true;
                    case "Y" when !shift: Undo(redo: true); return true;
                    default: return false;
                }
            }
            switch (key)
            {
                case "Left": MoveHorizontal(left: true, word: false, select: shift); return true;
                case "Right": MoveHorizontal(left: false, word: false, select: shift); return true;
                case "Up": MoveVertical(-1, shift); return true;
                case "Down": MoveVertical(1, shift); return true;
                case "Home": Move(LineStart(_caret), shift); return true;
                case "End": Move(LineEnd(_caret), shift); return true;
                case "Backspace": Delete(backward: true, word: false); return true;
                case "Delete" when shift: if (SelectionLength == 0) return false; Cut(); return true;
                case "Delete": Delete(backward: false, word: false); return true;
                case "Insert" when shift: return false; // Clipboard contents were never recorded.
                case "Insert": _overwrite = !_overwrite; return true;
                case "Enter" or "Num Enter": Insert("\n"); return true;
                case "Tab" when shift: return false; // Unindent and backward focus navigation depend on the application.
                case "Tab": Insert("\t"); return true;
                case "Space": Insert(printable ? entry.Text! : " "); return true;
                case "Caps Lock" or "NumLock" or "Scroll" or "Snapshot" or "PrintScreen": return true;
            }
            if (!printable) return false;
            Insert(entry.Text!);
            return true;
        }

        private static bool IsModifier(string key) => key is "Shift" or "Ctrl" or "Alt" or "Win" or
            "Left Shift" or "Right Shift" or "Left Ctrl" or "Right Ctrl" or "Left Alt" or "Right Alt" or "Left Win" or "Right Win";

        private int BoundaryIndex(int offset)
        {
            int index = _boundaries.BinarySearch(offset);
            return index >= 0 ? index : ~index - 1;
        }

        private int CeilingBoundary(int offset)
        {
            int index = _boundaries.BinarySearch(offset);
            return _boundaries[index >= 0 ? index : ~index];
        }

        private void Replace(int start, int length, string value)
        {
            // Include the preceding grapheme: separately captured accents, emoji joins and flag pairs can merge.
            int repairIndex = Math.Max(0, BoundaryIndex(start) - 1);
            int repairStart = _boundaries[repairIndex];
            _text.Remove(start, length).Insert(start, value);
            _boundaries.RemoveRange(repairIndex, _boundaries.Count - repairIndex);
            string suffix = _text.ToString(repairStart, _text.Length - repairStart);
            foreach (int boundary in StringInfo.ParseCombiningCharacters(suffix)) _boundaries.Add(repairStart + boundary);
            _boundaries.Add(_text.Length);
        }

        private void Change(int start, int length, string value)
        {
            int beforeCaret = _caret, beforeAnchor = _anchor;
            string removed = _text.ToString(start, length);
            Replace(start, length, value);
            _caret = _anchor = CeilingBoundary(start + value.Length);
            _preferredColumn = null;
            if (removed == value) return;
            foreach (var edit in _redo) _historyCharacters -= edit.Cost;
            _redo.Clear();
            var change = new Edit(start, removed, value, beforeCaret, beforeAnchor, _caret, _anchor);
            _undo.AddLast(change);
            _historyCharacters += change.Cost;
            while (_undo.Count > MaxUndoEdits || _historyCharacters > MaxUndoCharacters)
            {
                _historyCharacters -= _undo.First!.Value.Cost;
                _undo.RemoveFirst();
            }
        }

        private void Insert(string value)
        {
            int start = SelectionStart, length = SelectionLength;
            if (length == 0 && _overwrite && value != "\n")
            {
                int index = BoundaryIndex(start);
                int count = StringInfo.ParseCombiningCharacters(value).Length;
                while (count-- > 0 && index < _boundaries.Count - 1 && _text[_boundaries[index]] != '\n') index++;
                length = _boundaries[index] - start;
            }
            Change(start, length, value);
        }

        private void Cut()
        {
            if (SelectionLength > 0) Change(SelectionStart, SelectionLength, "");
        }

        private void Delete(bool backward, bool word)
        {
            if (SelectionLength > 0) { Cut(); return; }
            int index = BoundaryIndex(_caret);
            int end = word ? (backward ? WordLeft(_caret) : WordRight(_caret)) :
                _boundaries[Math.Clamp(index + (backward ? -1 : 1), 0, _boundaries.Count - 1)];
            if (end != _caret) Change(Math.Min(end, _caret), Math.Abs(end - _caret), "");
        }

        private void Move(int offset, bool select, bool vertical = false)
        {
            _caret = offset;
            if (!select) _anchor = _caret;
            if (!vertical) _preferredColumn = null;
        }

        private void MoveHorizontal(bool left, bool word, bool select)
        {
            if (!select && SelectionLength > 0) { Move(left ? SelectionStart : SelectionStart + SelectionLength, false); return; }
            int index = BoundaryIndex(_caret);
            Move(word ? (left ? WordLeft(_caret) : WordRight(_caret)) :
                _boundaries[Math.Clamp(index + (left ? -1 : 1), 0, _boundaries.Count - 1)], select);
        }

        private int LineStart(int position)
        {
            while (position > 0 && _text[position - 1] != '\n') position--;
            return position;
        }

        private int LineEnd(int position)
        {
            while (position < _text.Length && _text[position] != '\n') position++;
            return position;
        }

        private void MoveVertical(int direction, bool select)
        {
            int currentStart = LineStart(_caret);
            _preferredColumn ??= BoundaryIndex(_caret) - BoundaryIndex(currentStart);
            int targetStart, targetEnd;
            if (direction < 0)
            {
                if (currentStart == 0) return;
                targetEnd = currentStart - 1;
                targetStart = LineStart(targetEnd);
            }
            else
            {
                int currentEnd = LineEnd(_caret);
                if (currentEnd == _text.Length) return;
                targetStart = currentEnd + 1;
                targetEnd = LineEnd(targetStart);
            }
            int target = Math.Min(BoundaryIndex(targetStart) + _preferredColumn.Value, BoundaryIndex(targetEnd));
            Move(_boundaries[target], select, vertical: true);
        }

        private int WordClass(int boundaryIndex)
        {
            int offset = _boundaries[boundaryIndex];
            char first = _text[offset];
            if (char.IsWhiteSpace(first)) return 0;
            Rune rune;
            if (char.IsHighSurrogate(first) && offset + 1 < _text.Length && Rune.TryCreate(first, _text[offset + 1], out var pair)) rune = pair;
            else if (!Rune.TryCreate(first, out rune)) rune = Rune.ReplacementChar;
            return Rune.IsLetterOrDigit(rune) || first == '_' || Rune.GetUnicodeCategory(rune) is
                UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark ? 1 : 2;
        }

        private int WordLeft(int position)
        {
            int index = BoundaryIndex(position);
            while (index > 0 && WordClass(index - 1) == 0) index--;
            if (index > 0)
            {
                int category = WordClass(index - 1);
                while (index > 0 && WordClass(index - 1) == category) index--;
            }
            return _boundaries[index];
        }

        private int WordRight(int position)
        {
            int index = BoundaryIndex(position), last = _boundaries.Count - 1;
            if (index == last) return position;
            int category = WordClass(index);
            while (index < last && WordClass(index) == category) index++;
            while (index < last && WordClass(index) == 0) index++;
            return _boundaries[index];
        }

        private void Undo(bool redo)
        {
            var from = redo ? _redo : _undo;
            if (from.Count == 0) return;
            var edit = from.Last!.Value;
            from.RemoveLast();
            Replace(edit.Start, redo ? edit.Removed.Length : edit.Inserted.Length, redo ? edit.Inserted : edit.Removed);
            _caret = CeilingBoundary(redo ? edit.AfterCaret : edit.BeforeCaret);
            _anchor = CeilingBoundary(redo ? edit.AfterAnchor : edit.BeforeAnchor);
            _preferredColumn = null;
            (redo ? _undo : _redo).AddLast(edit);
        }
    }
}
