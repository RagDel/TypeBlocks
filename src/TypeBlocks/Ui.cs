using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TypeBlocks;

internal static class Ui
{
    private static readonly ConditionalWeakTable<Control, object> ThemedControls = new();

    public static AppTheme Theme { get; private set; } = AppTheme.Light;
    public static event Action? ThemeChanged;

    public static Color Background => SystemInformation.HighContrast ? SystemColors.Control : Pick(250, 245, 228, 36, 37, 38);
    public static Color Surface => SystemInformation.HighContrast ? SystemColors.Window : Pick(255, 252, 242, 43, 44, 45);
    public static Color Input => SystemInformation.HighContrast ? SystemColors.Window : Pick(237, 233, 221, 50, 51, 52);
    public static Color Ink => SystemInformation.HighContrast ? SystemColors.WindowText : Pick(42, 43, 42, 237, 236, 231);
    public static Color Muted => SystemInformation.HighContrast ? SystemColors.WindowText : Pick(97, 98, 94, 183, 183, 177);
    public static Color Accent => SystemInformation.HighContrast ? SystemColors.Highlight : Pick(82, 83, 80, 214, 213, 207);
    public static Color AccentInk => SystemInformation.HighContrast ? SystemColors.HighlightText : Pick(255, 253, 245, 36, 37, 36);
    public static Color Border => SystemInformation.HighContrast ? SystemColors.WindowText : Pick(190, 187, 176, 115, 116, 113);
    public static Color Selection => SystemInformation.HighContrast ? SystemColors.Highlight : Pick(226, 223, 213, 65, 67, 66);
    public static Color SelectionInk => SystemInformation.HighContrast ? SystemColors.HighlightText : Ink;
    public static Color Danger => SystemInformation.HighContrast ? SystemColors.WindowText : Pick(163, 49, 50, 245, 154, 151);

    private static Color Pick(int lr, int lg, int lb, int dr, int dg, int db) => Theme == AppTheme.Dark
        ? Color.FromArgb(dr, dg, db) : Color.FromArgb(lr, lg, lb);

    public static void SetTheme(AppTheme theme)
    {
        if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
        if (Theme == theme) return;
        Theme = theme;
        ThemeChanged?.Invoke();
    }

    // Geologica Light is the supplied face at every hierarchy level; size/color establish emphasis.
    public static Font Font(float size, bool bold = false) => AppFonts.Get(size);

    internal static int NativeLineHeight(Control control)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        if (!control.IsHandleCreated)
            return TextRenderer.MeasureText("Ag", control.Font, new Size(int.MaxValue, int.MaxValue), flags).Height;
        using var graphics = control.CreateGraphics();
        return TextRenderer.MeasureText(graphics, "Ag", control.Font, new Size(int.MaxValue, int.MaxValue), flags).Height;
    }

    public static void Style(Form form, string title, Size size)
    {
        form.Text = title;
        form.Font = Font(10.5f);
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.ClientSize = size;
        form.StartPosition = FormStartPosition.CenterScreen;
        form.Icon = AppIcon.Application;
        void RefreshTheme() { if (!form.IsDisposed) ApplyTheme(form); }
        ThemeChanged += RefreshTheme;
        form.HandleCreated += (_, _) => RefreshTheme();
        form.Disposed += (_, _) => ThemeChanged -= RefreshTheme;
        ApplyTheme(form);
    }

    /// <summary>Uses native controls and shared colors without changing focus, text, or selection.</summary>
    public static void ApplyTheme(Control control)
    {
        if (control.IsDisposed) return;
        if (!ThemedControls.TryGetValue(control, out _))
        {
            ThemedControls.Add(control, new object());
            control.ControlAdded += (_, e) => { if (e.Control is Control child) ApplyTheme(child); };
            if (control is CheckBox check) check.CheckedChanged += (_, _) => ApplyTheme(check);
            if (control is RadioButton radio) radio.CheckedChanged += (_, _) => ApplyTheme(radio);
            if (control is ComboBox combo)
            {
                combo.DrawItem += DrawComboItem;
                combo.DpiChangedAfterParent += (_, _) => ApplyTheme(combo);
            }
        }

        control.BackColor = control.Parent is TabPage or RoundedPanel ? Surface : Background;
        control.ForeColor = Ink;
        switch (control)
        {
            case Form form:
                if (form.IsHandleCreated)
                {
                    int dark = Theme == AppTheme.Dark && !SystemInformation.HighContrast ? 1 : 0;
                    // Unsupported Windows versions simply retain their native title-bar style.
                    _ = DwmSetWindowAttribute(form.Handle, 20, ref dark, sizeof(int));
                    int corners = 2;
                    _ = DwmSetWindowAttribute(form.Handle, 33, ref corners, sizeof(int));
                }
                break;
            case LinkLabel link:
                link.LinkColor = Accent;
                link.ActiveLinkColor = Ink;
                link.VisitedLinkColor = Accent;
                link.DisabledLinkColor = Muted;
                link.LinkBehavior = LinkBehavior.HoverUnderline;
                break;
            case ThemeLabel label:
                label.ForeColor = label.Muted ? Muted : Ink;
                break;
            case ModernTextBox input:
                input.ApplyColors();
                break;
            case TextBoxBase input:
                input.BackColor = control.Parent is ModernTextBox ? Input : Surface;
                input.ForeColor = Ink;
                break;
            case ModernComboBox combo:
                combo.BackColor = Input;
                combo.ForeColor = Ink;
                combo.UpdateMetrics();
                break;
            case ComboBox combo:
                combo.BackColor = Surface;
                combo.ForeColor = Ink;
                combo.FlatStyle = FlatStyle.Flat;
                combo.DrawMode = DrawMode.OwnerDrawFixed;
                combo.ItemHeight = combo.Font.Height + (int)Math.Round(4 * combo.DeviceDpi / 96f);
                break;
            case Button button:
                StyleButton(button, button is ModernButton { Primary: true });
                break;
            case CheckBox check when check.Appearance == Appearance.Button:
                StyleButton(check, check.Checked);
                check.FlatAppearance.CheckedBackColor = Accent;
                break;
            case RadioButton radio when radio.Appearance == Appearance.Button:
                StyleButton(radio, radio.Checked);
                radio.FlatAppearance.CheckedBackColor = Accent;
                break;
            case CheckBox check:
                check.UseVisualStyleBackColor = false;
                break;
            case RadioButton radio:
                radio.UseVisualStyleBackColor = false;
                break;
            case DataGridView grid:
                grid.BackgroundColor = Surface;
                grid.GridColor = Border;
                grid.EnableHeadersVisualStyles = false;
                grid.DefaultCellStyle.BackColor = Surface;
                grid.DefaultCellStyle.ForeColor = Ink;
                grid.DefaultCellStyle.SelectionBackColor = Selection;
                grid.DefaultCellStyle.SelectionForeColor = SelectionInk;
                grid.RowsDefaultCellStyle.BackColor = Surface;
                grid.RowsDefaultCellStyle.ForeColor = Ink;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Background;
                grid.AlternatingRowsDefaultCellStyle.ForeColor = Ink;
                grid.ColumnHeadersDefaultCellStyle.BackColor = Background;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Ink;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Background;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Ink;
                grid.RowHeadersDefaultCellStyle.BackColor = Background;
                grid.RowHeadersDefaultCellStyle.ForeColor = Ink;
                break;
            case TabPage page:
                page.UseVisualStyleBackColor = false;
                page.BackColor = Surface;
                break;
        }

        foreach (Control child in control.Controls) ApplyTheme(child);
        control.Invalidate();
    }

    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo || combo.IsDisposed) return;
        bool selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        Color background = selected ? Selection : Surface;
        Color foreground = combo.Enabled ? (selected ? SelectionInk : Ink) : Muted;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        string text = (e.Index >= 0 && e.Index < combo.Items.Count ? combo.GetItemText(combo.Items[e.Index]) : combo.Text) ?? "";
        int inset = Math.Max(2, (int)Math.Round(4 * combo.DeviceDpi / 96f));
        Rectangle bounds = Rectangle.Inflate(e.Bounds, -inset, 0);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        flags |= combo.RightToLeft == RightToLeft.Yes ? TextFormatFlags.Right | TextFormatFlags.RightToLeft : TextFormatFlags.Left;
        TextRenderer.DrawText(e.Graphics, text, combo.Font, bounds, foreground, flags);
        if ((e.State & DrawItemState.Focus) != 0 && (e.State & DrawItemState.NoFocusRect) == 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds, foreground, background);
    }

    private static void StyleButton(ButtonBase button, bool selected)
    {
        button.FlatStyle = FlatStyle.Flat;
        button.UseVisualStyleBackColor = false;
        button.BackColor = selected ? Accent : Surface;
        button.ForeColor = selected ? AccentInk : Ink;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = selected ? Accent : Border;
        button.FlatAppearance.MouseOverBackColor = selected ? Mix(Accent, AccentInk, 0.10f) : Selection;
        button.FlatAppearance.MouseDownBackColor = selected ? Mix(Accent, AccentInk, 0.18f) : Mix(Selection, Border, 0.25f);
    }

    internal static Color Mix(Color first, Color second, float amount) => Color.FromArgb(
        (int)(first.R + (second.R - first.R) * amount),
        (int)(first.G + (second.G - first.G) * amount),
        (int)(first.B + (second.B - first.B) * amount));

    public static Label Label(string text, int x, int y, int width, int height, float size = 10, bool bold = false)
    {
        var label = new ThemeLabel(!bold && size < 12)
        {
            Text = text, Location = new(x, y), Size = new(width, Math.Max(height, Font(size).Height + 8)), Font = Font(size, bold)
        };
        ApplyTheme(label);
        return label;
    }

    public static Button Button(string text, int x, int y, int width = 130, bool primary = false, UiIcon icon = UiIcon.None)
    {
        var button = new ModernButton { Primary = primary, Icon = icon,
            Text = text, Location = new(x, y), Size = new(width, 42), Cursor = Cursors.Hand,
            Font = Font(10.5f)
        };
        ApplyTheme(button);
        return button;
    }

    public static ModernTextBox Password(int x, int y, int width)
    {
        var input = new ModernTextBox
        {
            Location = new(x, y), Width = width, UseSystemPasswordChar = true, MaxLength = 1024,
            Font = Font(11)
        };
        ApplyTheme(input);
        return input;
    }
    public static void BringForward(Form form)
    {
        if (form.IsDisposed) return;
        if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
        if (!form.Visible) form.Show();
        form.BringToFront();
        form.Activate();
    }
    public static void Error(IWin32Window? owner, Exception error) => MessageBox.Show(owner, error.Message, "TypeBlocks", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private sealed class ThemeLabel(bool muted) : Label
    {
        public bool Muted { get; } = muted;
        protected override void OnPaint(PaintEventArgs e)
        {
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.WordBreak;
            if (!UseMnemonic) flags |= TextFormatFlags.NoPrefix;
            else if (!ShowKeyboardCues) flags |= TextFormatFlags.HidePrefix;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); FitLine(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); FitLine(); }
        protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); FitLine(); }
        private void FitLine() => Height = Math.Max(Height, NativeLineHeight(this) + 2);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
