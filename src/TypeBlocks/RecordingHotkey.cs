namespace TypeBlocks;

/// <summary>A global recording shortcut, stored with the owner's other local settings.</summary>
internal sealed record RecordingHotkey
{
    public Keys Key { get; init; } = Keys.F11;
    public bool Control { get; init; } = true;
    public bool Alt { get; init; } = true;
    public bool Shift { get; init; } = true;

    public static RecordingHotkey Default { get; } = new();

    public static IReadOnlyList<Keys> AvailableKeys { get; } = Array.AsReadOnly(
        Enumerable.Range((int)Keys.F1, 24).Select(value => (Keys)value).Where(key => key != Keys.F12)
            .Concat(Enumerable.Range((int)Keys.A, 26).Select(value => (Keys)value))
            .Concat(Enumerable.Range((int)Keys.D0, 10).Select(value => (Keys)value))
            .Concat(new[] { Keys.Space, Keys.Pause, Keys.Home, Keys.End, Keys.PageUp, Keys.PageDown,
                Keys.Insert, Keys.Delete, Keys.Left, Keys.Right, Keys.Up, Keys.Down, Keys.Tab,
                Keys.Back, Keys.Enter, Keys.Escape }).ToArray());

    internal uint Modifiers => (Alt ? 0x0001u : 0) | (Control ? 0x0002u : 0) | (Shift ? 0x0004u : 0);

    public bool IsValid(out string? error)
    {
        if (!Control && !Alt)
        {
            error = "Include Ctrl or Alt so ordinary typing cannot toggle recording.";
            return false;
        }
        if (Control && Alt && Key == Keys.Delete)
        {
            error = "Ctrl+Alt+Delete is reserved for Windows security. Choose another shortcut.";
            return false;
        }
        if (!AvailableKeys.Contains(Key))
        {
            error = Key == Keys.F12 ? "F12 is reserved by Windows for debugging. Choose another key."
                : "Choose a supported shortcut key.";
            return false;
        }
        error = null;
        return true;
    }

    public static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(),
        Keys.Back => "Backspace",
        Keys.Enter => "Enter",
        Keys.PageUp => "Page Up",
        Keys.PageDown => "Page Down",
        _ => key.ToString()
    };

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Control) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }
}
