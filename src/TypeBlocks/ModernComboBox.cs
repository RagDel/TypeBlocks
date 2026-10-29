using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;

namespace TypeBlocks;

/// <summary>A keyboard-accessible, non-editable choice with a themed native WinForms popup.</summary>
internal sealed class ModernComboBox : Control
{
    private int _selectedIndex = -1;
    private ChoicePopup? _popup;
    private long _deactivatedAt;
    private bool _hover;
    public event EventHandler? SelectedIndexChanged;
    public ItemCollection Items { get; }

    public ModernComboBox()
    {
        Items = new ItemCollection(this);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.ComboBox;
        TabStop = true; Cursor = Cursors.Hand; Size = new(180, 44); Font = Ui.Font(10.5f);
    }

    public ComboBoxStyle DropDownStyle
    {
        get => ComboBoxStyle.DropDownList;
        set { if (value != ComboBoxStyle.DropDownList) throw new ArgumentException("Only non-editable choices are supported."); }
    }
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (value < -1 || value >= Items.Count) throw new ArgumentOutOfRangeException(nameof(value));
            if (_selectedIndex == value) return;
            _selectedIndex = value; Invalidate();
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public object? SelectedItem { get => _selectedIndex >= 0 && _selectedIndex < Items.Count ? Items[_selectedIndex] : null; set => SelectedIndex = value is null ? -1 : Items.IndexOf(value); }
    [AllowNull]
    public override string Text
    {
        get => SelectedItem?.ToString() ?? "";
        set => SelectedIndex = Items.ToList().FindIndex(item => string.Equals(item.ToString(), value, StringComparison.CurrentCulture));
    }
    public bool DroppedDown { get => _popup is { IsDisposed: false }; set { if (value) OpenPopup(); else ClosePopup(false); } }
    public void UpdateMetrics() { Invalidate(); _popup?.RefreshLayout(); }

    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e)
    {
        UiDrawing.Backdrop(e.Graphics, this);
        UiDrawing.Surface(e.Graphics, ClientRectangle, _hover ? Ui.Selection : Ui.Input,
            Focused || DroppedDown ? Ui.Accent : null, Scale(13), 1.5f);
        int inset = Scale(14), icon = Scale(18);
        TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(inset, 0, Math.Max(1, Width - inset * 2 - icon - Scale(6)), Height),
            Enabled ? Ui.Ink : Ui.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        UiDrawing.Icon(e.Graphics, UiIcon.ChevronDown, new Rectangle(Width - inset - icon, (Height - icon) / 2, icon, icon), Enabled ? Ui.Muted : Ui.Border);
    }
    private int Scale(int value) => UiDrawing.Scale(this, value);
    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) ClosePopup(false); base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || !Enabled) return;
        Focus();
        if (Environment.TickCount64 - _deactivatedAt < 180) return;
        if (DroppedDown) ClosePopup(true); else OpenPopup();
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown or Keys.Enter or Keys.Space or Keys.F4 || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((!e.Alt && e.KeyCode is Keys.Space or Keys.Enter or Keys.F4) || e.Alt && e.KeyCode == Keys.Down)
        { OpenPopup(); e.SuppressKeyPress = true; }
        else if (e.KeyCode is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
        {
            if (Items.Count > 0) SelectedIndex = MoveIndex(_selectedIndex, e.KeyCode, Items.Count, 8);
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (Focused && Items.Count > 0 && e.Delta != 0) SelectedIndex = Math.Clamp(_selectedIndex + (e.Delta < 0 ? 1 : -1), 0, Items.Count - 1);
        base.OnMouseWheel(e);
    }
    private static int MoveIndex(int index, Keys key, int count, int page) => Math.Clamp(key switch
    { Keys.Home => 0, Keys.End => count - 1, Keys.Up => index - 1, Keys.Down => index + 1, Keys.PageUp => index - page, Keys.PageDown => index + page, _ => index }, 0, count - 1);

    private void OpenPopup()
    {
        if (!Enabled || IsDisposed || DroppedDown || Items.Count == 0) return;
        var popup = new ChoicePopup(this);
        _popup = popup;
        try
        {
            popup.RefreshLayout();
            if (FindForm() is Form owner) popup.Show(owner); else popup.Show();
            if (ReferenceEquals(_popup, popup) && !popup.IsDisposed) popup.Focus();
        }
        catch { if (ReferenceEquals(_popup, popup)) ClosePopup(false); popup.Dispose(); throw; }
        Invalidate();
        AccessibilityNotifyClients(AccessibleEvents.StateChange, -1);
    }
    private void ClosePopup(bool restoreFocus)
    {
        var popup = _popup; _popup = null;
        if (popup is not null && !popup.IsDisposed) popup.Close();
        if (!IsDisposed) { if (restoreFocus && CanFocus) Focus(); Invalidate(); AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); }
    }
    private void Commit(int index) { ClosePopup(true); if (!IsDisposed && Enabled && index >= 0 && index < Items.Count) SelectedIndex = index; }
    private void ItemsChanged(object? selected)
    {
        int index = selected is null ? -1 : Items.IndexOf(selected);
        bool changed = index != _selectedIndex || (selected is not null && index < 0);
        _selectedIndex = index;
        ClosePopup(false);
        Invalidate();
        if (changed) { AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); }
    }
    protected override void Dispose(bool disposing) { if (disposing) ClosePopup(false); base.Dispose(disposing); }
    protected override AccessibleObject CreateAccessibilityInstance() => new ChoiceAccessible(this);

    internal sealed class ItemCollection(ModernComboBox owner) : Collection<object>
    {
        protected override void InsertItem(int index, object item) { ArgumentNullException.ThrowIfNull(item); var selected = owner.SelectedItem; base.InsertItem(index, item); owner.ItemsChanged(selected); }
        protected override void SetItem(int index, object item) { ArgumentNullException.ThrowIfNull(item); var selected = owner.SelectedItem; base.SetItem(index, item); owner.ItemsChanged(selected); }
        protected override void RemoveItem(int index) { var selected = owner.SelectedItem; base.RemoveItem(index); owner.ItemsChanged(selected); }
        protected override void ClearItems() { var selected = owner.SelectedItem; base.ClearItems(); owner.ItemsChanged(selected); }
    }

    private sealed class ChoicePopup : Form
    {
        private readonly ModernComboBox _owner;
        private int _rowHeight, _visibleRows, _padding, _first, _highlight;
        private bool _dragging;
        public ChoicePopup(ModernComboBox owner)
        {
            _owner = owner; _highlight = Math.Max(0, owner.SelectedIndex);
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None; Font = owner.Font; KeyPreview = true; DoubleBuffered = true;
            AccessibleRole = AccessibleRole.List; AccessibleName = owner.AccessibleName;
            Deactivate += (_, _) =>
            {
                if (!ReferenceEquals(_owner._popup, this)) return;
                // Suppress only the same click that closed the popup by returning to its opener.
                if ((MouseButtons & MouseButtons.Left) != 0 && _owner.RectangleToScreen(_owner.ClientRectangle).Contains(Cursor.Position))
                    _owner._deactivatedAt = Environment.TickCount64;
                _owner.ClosePopup(false);
            };
        }
        public void RefreshLayout()
        {
            if (IsDisposed || _owner.IsDisposed) return;
            Font = _owner.Font; _padding = _owner.Scale(6); _rowHeight = Math.Max(_owner.Scale(38), Ui.NativeLineHeight(_owner) + _owner.Scale(12));
            Rectangle anchor = _owner.RectangleToScreen(_owner.ClientRectangle), area = Screen.FromControl(_owner).WorkingArea;
            int gap = _owner.Scale(5), below = area.Bottom - anchor.Bottom - gap, above = anchor.Top - area.Top - gap;
            bool useBelow = below >= Math.Min(8, _owner.Items.Count) * _rowHeight + _padding * 2 || below >= above;
            int room = Math.Max(_rowHeight + _padding * 2, useBelow ? below : above);
            _visibleRows = Math.Max(1, Math.Min(Math.Min(8, _owner.Items.Count), (room - _padding * 2) / _rowHeight));
            int width = Math.Min(area.Width, Math.Max(anchor.Width, _owner.Scale(140))), height = Math.Min(area.Height, _visibleRows * _rowHeight + _padding * 2);
            Bounds = new Rectangle(Math.Clamp(anchor.Left, area.Left, area.Right - width),
                Math.Clamp(useBelow ? anchor.Bottom + gap : anchor.Top - gap - height, area.Top, area.Bottom - height), width, height);
            using var outline = UiDrawing.Round(new RectangleF(0, 0, Width, Height), _owner.Scale(13));
            var old = Region; Region = new Region(outline); old?.Dispose();
            EnsureVisible(); Invalidate();
        }
        private void EnsureVisible() => _first = Math.Clamp(_first > _highlight ? _highlight : _highlight >= _first + _visibleRows ? _highlight - _visibleRows + 1 : _first,
            0, Math.Max(0, _owner.Items.Count - _visibleRows));
        private Rectangle RowBounds(int index) => new(_padding, _padding + (index - _first) * _rowHeight, Width - _padding * 2 - (_owner.Items.Count > _visibleRows ? _owner.Scale(10) : 0), _rowHeight);
        public Rectangle AccessibleRowBounds(int index) => index >= _first && index < _first + _visibleRows ? RectangleToScreen(RowBounds(index)) : Rectangle.Empty;
        protected override AccessibleObject CreateAccessibilityInstance() => new PopupAccessible(this);
        private sealed class PopupAccessible(ChoicePopup popup) : ControlAccessibleObject(popup)
        {
            public override AccessibleRole Role => AccessibleRole.List;
            public override int GetChildCount() => popup._owner.Items.Count;
            public override AccessibleObject? GetChild(int index) => index >= 0 && index < popup._owner.Items.Count ? new ItemAccessible(popup._owner, this, index) : null;
            public override AccessibleObject? GetFocused() => GetChild(popup._highlight);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            using var background = new SolidBrush(Ui.Surface); e.Graphics.FillRectangle(background, ClientRectangle);
            UiDrawing.Surface(e.Graphics, ClientRectangle, Ui.Surface, Ui.Border, _owner.Scale(13));
            for (int index = _first; index < Math.Min(_owner.Items.Count, _first + _visibleRows); index++)
            {
                var bounds = RowBounds(index);
                if (index == _highlight) UiDrawing.Surface(e.Graphics, bounds, Ui.Selection, null, _owner.Scale(8));
                int inset = _owner.Scale(12), check = _owner.Scale(18);
                TextRenderer.DrawText(e.Graphics, _owner.Items[index].ToString(), Font, new Rectangle(bounds.Left + inset, bounds.Top, Math.Max(1, bounds.Width - inset * 2 - check), bounds.Height), Ui.Ink,
                    TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                if (index == _owner.SelectedIndex) UiDrawing.Icon(e.Graphics, UiIcon.Check, new Rectangle(bounds.Right - inset - check, bounds.Top + (bounds.Height - check) / 2, check, check), Ui.Accent);
            }
            if (_owner.Items.Count > _visibleRows)
            {
                var thumb = ThumbBounds(); UiDrawing.Surface(e.Graphics, thumb, Ui.Border, null, thumb.Width / 2f);
            }
        }
        private Rectangle ThumbBounds()
        {
            int trackHeight = Height - _padding * 2, height = Math.Max(_owner.Scale(22), trackHeight * _visibleRows / _owner.Items.Count);
            int top = _padding + (trackHeight - height) * _first / Math.Max(1, _owner.Items.Count - _visibleRows);
            return new Rectangle(Width - _padding - _owner.Scale(5), top, _owner.Scale(5), height);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) { ScrollTo(e.Y); return; }
            int index = _first + (e.Y - _padding) / _rowHeight;
            if (e.Y >= _padding && index >= _first && index < Math.Min(_owner.Items.Count, _first + _visibleRows) && _highlight != index)
            { _highlight = index; Invalidate(); }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            if (_owner.Items.Count > _visibleRows && e.X >= Width - _padding - _owner.Scale(10)) { _dragging = true; Capture = true; ScrollTo(e.Y); return; }
            int index = _first + (e.Y - _padding) / _rowHeight;
            if (e.Y >= _padding && index >= _first && index < Math.Min(_owner.Items.Count, _first + _visibleRows)) _owner.Commit(index);
        }
        protected override void OnMouseUp(MouseEventArgs e) { _dragging = false; Capture = false; base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) _dragging = false; base.OnMouseCaptureChanged(e); }
        private void ScrollTo(int y) { _first = Math.Clamp((y - _padding) * _owner.Items.Count / Math.Max(1, Height - _padding * 2) - _visibleRows / 2, 0, _owner.Items.Count - _visibleRows); KeepHighlightVisible(); Invalidate(); }
        private void KeepHighlightVisible() => _highlight = Math.Clamp(_highlight, _first, Math.Min(_owner.Items.Count - 1, _first + _visibleRows - 1));
        protected override void OnMouseWheel(MouseEventArgs e)
        { _first = Math.Clamp(_first - Math.Sign(e.Delta) * 3, 0, Math.Max(0, _owner.Items.Count - _visibleRows)); KeepHighlightVisible(); Invalidate(); base.OnMouseWheel(e); }
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape || key == Keys.F4 && (keyData & Keys.Alt) == 0 || key == Keys.Up && (keyData & Keys.Alt) != 0) { _owner.ClosePopup(true); return true; }
            if (key is Keys.Enter or Keys.Space) { _owner.Commit(_highlight); return true; }
            if (key is Keys.Up or Keys.Down or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown)
            { _highlight = MoveIndex(_highlight, key, _owner.Items.Count, _visibleRows); EnsureVisible(); Invalidate(); AccessibilityNotifyClients(AccessibleEvents.Focus, _highlight + 1); return true; }
            if (key == Keys.Tab) { _owner.ClosePopup(true); _owner.FindForm()?.SelectNextControl(_owner, (keyData & Keys.Shift) == 0, true, true, true); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }

    private sealed class ChoiceAccessible(ModernComboBox owner) : ControlAccessibleObject(owner)
    {
        public override AccessibleRole Role => AccessibleRole.ComboBox;
        public override string? Value { get => owner.Text; set { } }
        public override string? DefaultAction => owner.DroppedDown ? "Collapse" : "Expand";
        public override AccessibleStates State => base.State | (owner.DroppedDown ? AccessibleStates.Expanded : AccessibleStates.Collapsed);
        public override void DoDefaultAction() => owner.DroppedDown = !owner.DroppedDown;
        public override int GetChildCount() => owner.Items.Count;
        public override AccessibleObject? GetChild(int index) => index >= 0 && index < owner.Items.Count ? new ItemAccessible(owner, this, index) : null;
    }
    private sealed class ItemAccessible(ModernComboBox owner, AccessibleObject parent, int index) : AccessibleObject
    {
        public override AccessibleObject? Parent => parent;
        public override AccessibleRole Role => AccessibleRole.ListItem;
        public override string? Name { get => index < owner.Items.Count ? owner.Items[index].ToString() : ""; set { } }
        public override string? DefaultAction => "Select";
        public override Rectangle Bounds => owner._popup?.AccessibleRowBounds(index) ?? Rectangle.Empty;
        public override AccessibleStates State => AccessibleStates.Selectable | (index == owner.SelectedIndex ? AccessibleStates.Selected : 0) | (Bounds.IsEmpty ? AccessibleStates.Offscreen : 0) | (!owner.Enabled ? AccessibleStates.Unavailable : 0);
        public override void DoDefaultAction() { if (index < owner.Items.Count) owner.Commit(index); }
    }
}
