using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace TypeBlocks;

internal sealed class RoundedPanel : Panel
{
    public float Radius { get; set; } = 16;
    public RoundedPanel() => DoubleBuffered = true;
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        UiDrawing.Backdrop(e.Graphics, this);
        UiDrawing.Surface(e.Graphics, ClientRectangle, BackColor, null, UiDrawing.Scale(this, Radius));
    }
}

internal sealed class ModernButton : Button
{
    public bool Primary { get; init; }
    public UiIcon Icon { get; init; }
    private bool _hover, _pressed;
    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Opaque, false);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) => UiDrawing.Backdrop(e.Graphics, this);
    protected override void OnPaint(PaintEventArgs e)
    {
        UiDrawing.Backdrop(e.Graphics, this);
        Color fill = Primary && Enabled ? Ui.Accent : Ui.Input;
        if (Enabled && (_hover || _pressed)) fill = Primary ? Ui.Mix(fill, Ui.AccentInk, _pressed ? .18f : .10f) : Ui.Selection;
        Color ink = !Enabled ? Ui.Muted : Primary ? Ui.AccentInk : Ui.Ink;
        UiDrawing.Surface(e.Graphics, ClientRectangle, fill, Focused && ShowFocusCues ? Ui.Accent : null, UiDrawing.Scale(this, 12), 1.5f);
        UiDrawing.ButtonContent(e.Graphics, this, Icon, ink, ShowKeyboardCues);
    }
}

internal class ModernCheckBox : CheckBox
{
    protected bool Hovered { get; private set; }
    public ModernCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Opaque, false);
        Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
    }
    protected override void OnMouseEnter(EventArgs e) { Hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { Hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) => UiDrawing.Backdrop(e.Graphics, this);
    protected override void OnPaint(PaintEventArgs e)
    {
        UiDrawing.Backdrop(e.Graphics, this);
        Color ink = Enabled ? Ui.Ink : Ui.Muted;
        if (Appearance == Appearance.Button)
        {
            ChoicePainting.Draw(e.Graphics, this, Checked, Hovered, Focused && ShowFocusCues, ShowKeyboardCues);
            return;
        }
        int size = UiDrawing.Scale(this, 20), inset = UiDrawing.Scale(this, 2);
        var box = new Rectangle(inset, (Height - size) / 2, size, size);
        UiDrawing.Surface(e.Graphics, box, Checked ? Ui.Accent : Ui.Input, Focused && ShowFocusCues ? Ui.Accent : Ui.Border, UiDrawing.Scale(this, 5));
        if (Checked) UiDrawing.Icon(e.Graphics, UiIcon.Check, Rectangle.Inflate(box, -3, -3), Ui.AccentInk);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(size + UiDrawing.Scale(this, 6), 0, Math.Max(0, Width - size - UiDrawing.Scale(this, 6)), Height), ink,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | (ShowKeyboardCues ? 0 : TextFormatFlags.HidePrefix));
    }
}

internal sealed class ModernRadioButton : RadioButton
{
    private bool _hover;
    public ModernRadioButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Opaque, false);
        Cursor = Cursors.Hand; UseVisualStyleBackColor = false;
    }
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnPaintBackground(PaintEventArgs e) => UiDrawing.Backdrop(e.Graphics, this);
    protected override void OnPaint(PaintEventArgs e)
    {
        UiDrawing.Backdrop(e.Graphics, this);
        ChoicePainting.Draw(e.Graphics, this, Checked, _hover, Focused && ShowFocusCues, ShowKeyboardCues);
    }
}

internal static class ChoicePainting
{
    public static void Draw(Graphics graphics, Control control, bool selected, bool hover, bool focused, bool cues)
    {
        Color background = control.Parent?.BackColor ?? Ui.Input;
        Color fill = selected ? Ui.Surface : hover ? Ui.Selection : background;
        UiDrawing.Surface(graphics, control.ClientRectangle, fill, focused ? Ui.Accent : null, UiDrawing.Scale(control, 11), 1.5f);
        UiDrawing.ButtonContent(graphics, control, UiIcon.None, control.Enabled ? selected ? Ui.Ink : Ui.Muted : Ui.Muted, cues);
    }
}

internal sealed class ThemeToggle : ModernCheckBox
{
    public ThemeToggle() { AccessibleName = "Dark theme"; Size = new(128, 48); }
    protected override void OnPaint(PaintEventArgs e)
    {
        UiDrawing.Backdrop(e.Graphics, this);
        // Derive the switch geometry from its actual bounds; offscreen forms can report
        // a monitor DPI before WinForms has scaled sibling controls to that monitor.
        int Scale(float value) => (int)Math.Round(value * Height / 48f);
        UiDrawing.Surface(e.Graphics, ClientRectangle, Ui.Surface, Focused && ShowFocusCues ? Ui.Accent : Ui.Border, Scale(14), Focused ? 1.5f : 1);
        int iconSize = Scale(22);
        UiDrawing.Icon(e.Graphics, Checked ? UiIcon.Sun : UiIcon.Moon, new Rectangle(Scale(16), (Height - iconSize) / 2, iconSize, iconSize), Ui.Accent);
        var track = new Rectangle(Width - Scale(66), (Height - Scale(28)) / 2, Scale(52), Scale(28));
        UiDrawing.Surface(e.Graphics, track, Ui.Selection, Ui.Border, Scale(14));
        int diameter = Scale(20);
        using var thumb = new SolidBrush(Enabled ? Ui.Accent : Ui.Muted);
        e.Graphics.FillEllipse(thumb, Checked ? track.Right - diameter - Scale(4) : track.Left + Scale(4), track.Top + Scale(4), diameter, diameter);
    }
}

internal sealed class ModernTextBox : UserControl
{
    private readonly NativeTextEditor _editor = new() { BorderStyle = BorderStyle.None, AutoSize = false };
    private readonly EditorScrollBar _scrollRail;
    private ScrollBars _scrollBars;
    private UiIcon _icon;
    private Color _surface;
    public ModernTextBox()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoScaleMode = AutoScaleMode.None;
        Size = new(200, 44); Font = Ui.Font(10.5f); TabStop = false;
        _scrollRail = new EditorScrollBar(_editor) { Visible = false };
        Controls.Add(_editor);
        Controls.Add(_scrollRail);
        _editor.TextChanged += (_, _) => OnTextChanged(EventArgs.Empty);
        _editor.KeyDown += (_, e) => OnKeyDown(e);
        _editor.KeyUp += (_, e) => OnKeyUp(e);
        _editor.KeyPress += (_, e) => OnKeyPress(e);
        _editor.GotFocus += (_, _) => Invalidate();
        _editor.LostFocus += (_, _) => Invalidate();
        ApplyColors();
    }
    [AllowNull]
    public override string Text { get => _editor.Text; set => _editor.Text = value ?? ""; }
    public string[] Lines { get => _editor.Lines; set => _editor.Lines = value; }
    public bool ReadOnly { get => _editor.ReadOnly; set => _editor.ReadOnly = value; }
    public bool Multiline { get => _editor.Multiline; set { _editor.Multiline = value; PerformLayout(); } }
    public int MaxLength { get => _editor.MaxLength; set => _editor.MaxLength = value; }
    public bool UseSystemPasswordChar { get => _editor.UseSystemPasswordChar; set => _editor.UseSystemPasswordChar = value; }
    public string PlaceholderText { get => _editor.PlaceholderText; set => _editor.PlaceholderText = value; }
    public ScrollBars ScrollBars
    {
        get => _scrollBars;
        set
        {
            _scrollBars = value;
            _editor.ScrollBars = value is ScrollBars.Horizontal or ScrollBars.Both ? ScrollBars.Horizontal : ScrollBars.None;
            PerformLayout();
        }
    }
    public bool WordWrap { get => _editor.WordWrap; set => _editor.WordWrap = value; }
    public UiIcon Icon { get => _icon; set { _icon = value; PerformLayout(); Invalidate(); } }
    public void Clear() => _editor.Clear();
    public void SelectAll() => _editor.SelectAll();
    public new bool Focus() => _editor.Focus();
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); _editor.Focus(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); _editor.Focus(); }
    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); if (_editor is not null) { _editor.Font = Font; PerformLayout(); } }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); PerformLayout(); }
    protected override void OnDpiChangedAfterParent(EventArgs e) { base.OnDpiChangedAfterParent(e); PerformLayout(); }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (_editor is null) return;
        int pad = UiDrawing.Scale(this, 14), left = pad + (_icon == UiIcon.None ? 0 : UiDrawing.Scale(this, 26));
        // Geologica's native Windows line metrics are taller than Font.Height.
        // Borderless TextBox.PreferredHeight only uses Font.Height and clips its glyphs.
        int height = Multiline ? Math.Max(1, Height - pad * 2) : Ui.NativeLineHeight(_editor) + 2;
        bool scrolling = Multiline && _scrollBars is ScrollBars.Vertical or ScrollBars.Both;
        int railWidth = scrolling ? UiDrawing.Scale(this, 14) : 0;
        _editor.SetBounds(left, Multiline ? pad : Math.Max(2, (Height - height) / 2), Math.Max(1, Width - left - pad - railWidth), height);
        if (_scrollRail is not null)
        {
            _scrollRail.Visible = scrolling;
            _scrollRail.SetBounds(Width - pad - railWidth, pad, Math.Max(1, railWidth), Math.Max(1, Height - pad * 2));
            _scrollRail.AccessibleName = "Scroll " + (AccessibleName ?? "text");
        }
        _editor.AccessibleName = AccessibleName;
    }
    public void ApplyColors()
    {
        _surface = Ui.Input;
        BackColor = Ui.Input; ForeColor = Ui.Ink;
        _editor.BackColor = _surface; _editor.ForeColor = Ui.Ink;
        Invalidate();
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        UiDrawing.Backdrop(e.Graphics, this);
        UiDrawing.Surface(e.Graphics, ClientRectangle, _surface, _editor.Focused ? Ui.Accent : null, UiDrawing.Scale(this, Multiline ? 13 : 20), 1.5f);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_icon == UiIcon.None) return;
        int size = UiDrawing.Scale(this, 19);
        UiDrawing.Icon(e.Graphics, _icon, new Rectangle(UiDrawing.Scale(this, 14), (Height - size) / 2, size, size), Ui.Muted);
    }
}
