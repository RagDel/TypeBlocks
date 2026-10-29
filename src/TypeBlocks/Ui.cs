namespace TypeBlocks;

internal static class Ui
{
    public static readonly Color Background = Color.FromArgb(247, 248, 250);
    public static readonly Color Ink = Color.FromArgb(28, 39, 53);
    public static readonly Color Muted = Color.FromArgb(88, 102, 121);
    public static readonly Color Accent = Color.FromArgb(42, 92, 143);

    public static void Style(Form form, string title, Size size)
    {
        form.Text = title;
        form.ClientSize = size;
        form.Font = new Font("Segoe UI", 10);
        form.BackColor = Background;
        form.ForeColor = Ink;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Icon = AppIcon.Application;
    }

    public static Label Label(string text, int x, int y, int width, int height, float size = 10, bool bold = false)
        => new() { Text = text, Location = new(x, y), Size = new(width, height), Font = new("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = size < 12 ? Muted : Ink };

    public static Button Button(string text, int x, int y, int width = 130, bool primary = false)
    {
        var button = new Button { Text = text, Location = new(x, y), Size = new(width, 38), FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, BackColor = primary ? Accent : Color.White, ForeColor = primary ? Color.White : Ink };
        button.FlatAppearance.BorderColor = primary ? Accent : Color.FromArgb(211, 219, 227);
        return button;
    }

    public static TextBox Password(int x, int y, int width) => new() { Location = new(x, y), Width = width, UseSystemPasswordChar = true, MaxLength = 1024 };
    public static void BringForward(Form form)
    {
        if (form.IsDisposed) return;
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        if (!form.Visible) form.Show();
        form.BringToFront();
        form.Activate();
    }
    public static void Error(IWin32Window? owner, Exception error) => MessageBox.Show(owner, error.Message, "TypeBlocks", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
