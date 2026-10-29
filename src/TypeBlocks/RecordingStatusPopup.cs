using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TypeBlocks;

/// <summary>A brief recording indicator that never takes keyboard focus.</summary>
internal sealed class RecordingStatusPopup : Form
{
    private readonly System.Windows.Forms.Timer _dismissTimer = new() { Interval = 2000 };
    private bool _recording;

    internal string StatusText => _recording ? "Typeblocks: On" : "Typeblocks: Off";

    public RecordingStatusPopup()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        ClientSize = new Size(270, 64);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(31, 35, 41);
        ForeColor = Color.FromArgb(248, 249, 251);
        DoubleBuffered = true;
        AccessibleRole = AccessibleRole.Alert;
        _dismissTimer.Tick += (_, _) => Dismiss();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000 | 0x00000080; // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            return parameters;
        }
    }

    public void ShowStatus(bool recording)
    {
        if (IsDisposed) return;
        _dismissTimer.Stop();
        SetStatus(recording);
        nint foreground = GetForegroundWindow();
        Screen screen = foreground != 0 ? Screen.FromHandle(foreground) : Screen.FromPoint(Cursor.Position);
        PositionOn(screen);
        if (!Visible) Show();
        // Showing on another monitor can change DPI and size. Center the final size.
        PositionOn(screen);
        _dismissTimer.Start();
    }

    public void Dismiss()
    {
        if (IsDisposed) return;
        _dismissTimer.Stop();
        Hide();
    }

    internal void SetStatusForTest(bool recording) => SetStatus(recording);

    private void SetStatus(bool recording)
    {
        _recording = recording;
        Text = StatusText;
        AccessibleName = StatusText;
        Invalidate();
    }

    private void PositionOn(Screen screen)
    {
        Rectangle area = screen.WorkingArea;
        int bottomGap = Scale(48);
        Location = new Point(area.Left + Math.Max(0, (area.Width - Width) / 2),
            Math.Max(area.Top, area.Bottom - Height - bottomGap));
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
        using var outline = RoundedOutline();
        Region? previous = Region;
        Region = new Region(outline);
        previous?.Dispose();
    }

    private GraphicsPath RoundedOutline()
    {
        float diameter = Math.Min(Scale(20), Math.Min(ClientSize.Width - 1, ClientSize.Height - 1));
        float right = ClientSize.Width - 1, bottom = ClientSize.Height - 1;
        var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(right - diameter, 0, diameter, diameter, 270, 90);
        path.AddArc(right - diameter, bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(0, bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var outline = RoundedOutline();
        using var border = new Pen(Color.FromArgb(64, 70, 79), Math.Max(1, Scale(1)));
        e.Graphics.DrawPath(border, outline);
        int diameter = Scale(10);
        using var dot = new SolidBrush(_recording ? Color.FromArgb(246, 83, 88) : Color.FromArgb(141, 149, 160));
        e.Graphics.FillEllipse(dot, Scale(27), (ClientSize.Height - diameter) / 2f, diameter, diameter);
        // Equal left and right insets keep the text centered independently of the dot.
        int inset = Scale(46);
        var textBounds = new Rectangle(inset, Scale(12), ClientSize.Width - inset * 2, ClientSize.Height - Scale(24));
        using var statusFont = new Font("Segoe UI", Scale(15), FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(e.Graphics, StatusText, statusFont, textBounds, ForeColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE: do not steal focus even when clicked.
        {
            message.Result = (nint)4; // MA_NOACTIVATEANDEAT
            return;
        }
        base.WndProc(ref message);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dismissTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
