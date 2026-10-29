using System.Runtime.InteropServices;

namespace TypeBlocks;

internal sealed class NativeTextEditor : TextBox
{
    public event Action? ViewChanged;
    public int FirstLine => IsHandleCreated ? (int)SendMessage(Handle, 0x00CE, 0, 0) : 0;
    public int LineCount => IsHandleCreated ? Math.Max(1, (int)SendMessage(Handle, 0x00BA, 0, 0)) : 1;
    public int VisibleLines => Math.Max(1, ClientSize.Height / Math.Max(1, Ui.NativeLineHeight(this)));
    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        ViewChanged?.Invoke();
    }
    public void ScrollToLine(int line)
    {
        if (!IsHandleCreated) return;
        int target = Math.Clamp(line, 0, Math.Max(0, LineCount - VisibleLines));
        SendMessage(Handle, 0x00B6, 0, target - FirstLine); // EM_LINESCROLL
        ViewChanged?.Invoke();
    }
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is 0x020A or 0x0115 or 0x0100 or 0x0102 or 0x000C or 0x00B6 or 0x00B7 or 0x0005 or 0x0200 or 0x0113)
            ViewChanged?.Invoke();
    }
    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);
}

/// <summary>A small persistent scroll track for multiline inputs, without native arrow buttons.</summary>
internal sealed class EditorScrollBar : Control
{
    private readonly NativeTextEditor _editor;
    private int _dragOffset;
    public EditorScrollBar(NativeTextEditor editor)
    {
        _editor = editor;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false; Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ScrollBar;
        AccessibleName = "Scroll text";
        editor.ViewChanged += RefreshView;
    }
    private void RefreshView() { if (!IsDisposed) Invalidate(); }
    private Rectangle Thumb
    {
        get
        {
            int height = Math.Min(Height, Math.Max(UiDrawing.Scale(this, 20), Height * _editor.VisibleLines / _editor.LineCount));
            int range = Math.Max(1, _editor.LineCount - _editor.VisibleLines);
            int top = Math.Clamp(_editor.FirstLine * Math.Max(0, Height - height) / range, 0, Math.Max(0, Height - height));
            return new Rectangle(Math.Max(1, (Width - 5) / 2), top, Math.Min(5, Width - 2), height);
        }
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        using var background = new SolidBrush(Ui.Input);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var track = new Rectangle(Math.Max(1, (Width - 3) / 2), 0, 3, Height);
        UiDrawing.Surface(e.Graphics, track, Ui.Selection, null, 2);
        if (_editor.LineCount > _editor.VisibleLines)
            UiDrawing.Surface(e.Graphics, Thumb, Ui.Muted, null, 3);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _editor.Focus();
        Rectangle thumb = Thumb;
        _dragOffset = thumb.Contains(e.Location) ? e.Y - thumb.Top : thumb.Height / 2;
        Capture = true; ScrollFrom(e.Y);
    }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (Capture) ScrollFrom(e.Y); }
    protected override void OnMouseUp(MouseEventArgs e) { Capture = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        _editor.ScrollToLine(_editor.FirstLine + (e.Delta > 0 ? -3 : 3));
        base.OnMouseWheel(e);
    }
    private void ScrollFrom(int y)
    {
        int travel = Math.Max(1, Height - Thumb.Height);
        int maximum = Math.Max(0, _editor.LineCount - _editor.VisibleLines);
        _editor.ScrollToLine((int)Math.Round(Math.Clamp(y - _dragOffset, 0, travel) * maximum / (double)travel));
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) _editor.ViewChanged -= RefreshView;
        base.Dispose(disposing);
    }
}
