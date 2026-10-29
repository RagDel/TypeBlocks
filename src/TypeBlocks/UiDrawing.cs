using System.Drawing.Drawing2D;

namespace TypeBlocks;

internal enum UiIcon { None, Search, Settings, Lock, Refresh, Sun, Moon, Key, Shield, Copy, Check, ChevronDown, Close }

internal static class UiDrawing
{
    public static void Backdrop(Graphics graphics, Control control)
    {
        using var brush = new SolidBrush(control.Parent?.BackColor ?? Ui.Background);
        graphics.FillRectangle(brush, control.ClientRectangle);
    }

    public static int Scale(Control control, float value) => (int)Math.Round(value * control.DeviceDpi / 96f);

    public static GraphicsPath Round(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        float diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Surface(Graphics graphics, Rectangle bounds, Color fill, Color? border, float radius, float stroke = 1)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Round(new RectangleF(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2), radius);
        using var brush = new SolidBrush(fill);
        graphics.FillPath(brush, path);
        if (border is Color color)
        {
            using var pen = new Pen(color, stroke);
            graphics.DrawPath(pen, path);
        }
    }

    public static void Icon(Graphics graphics, UiIcon icon, RectangleF bounds, Color color)
    {
        if (icon == UiIcon.None) return;
        var state = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        switch (icon)
        {
            case UiIcon.Search:
                graphics.DrawEllipse(pen, 3, 3, 13, 13); graphics.DrawLine(pen, 14, 14, 21, 21); break;
            case UiIcon.Settings:
                for (int i = 0; i < 3; i++) { float y = 5 + i * 7; graphics.DrawLine(pen, 3, y, 21, y); }
                using (var brush = new SolidBrush(color))
                    foreach (var point in new[] { new PointF(8, 5), new PointF(16, 12), new PointF(10, 19) })
                    { graphics.FillEllipse(brush, point.X - 2, point.Y - 2, 4, 4); }
                break;
            case UiIcon.Lock:
                graphics.DrawArc(pen, 7, 2, 10, 13, 180, 180);
                using (var path = Round(new RectangleF(4, 10, 16, 12), 3)) graphics.DrawPath(pen, path);
                graphics.DrawLine(pen, 12, 15, 12, 18); break;
            case UiIcon.Refresh:
                graphics.DrawArc(pen, 4, 4, 16, 16, 40, 290);
                graphics.DrawLines(pen, [new PointF(17, 3), new PointF(21, 7), new PointF(16, 8)]); break;
            case UiIcon.Sun:
                graphics.DrawEllipse(pen, 7, 7, 10, 10);
                for (int i = 0; i < 8; i++) { double angle = i * Math.PI / 4; graphics.DrawLine(pen, 12 + (float)Math.Cos(angle) * 8, 12 + (float)Math.Sin(angle) * 8, 12 + (float)Math.Cos(angle) * 11, 12 + (float)Math.Sin(angle) * 11); } break;
            case UiIcon.Moon:
                using (var path = new GraphicsPath())
                { path.AddBezier(11, 2, 0, 4, 0, 18, 11, 21); path.AddBezier(11, 21, 17, 23, 22, 18, 22, 13); path.AddBezier(22, 13, 12, 18, 6, 10, 11, 2); graphics.DrawPath(pen, path); } break;
            case UiIcon.Key:
                graphics.DrawEllipse(pen, 2, 3, 10, 10); graphics.DrawLine(pen, 11, 12, 21, 22); graphics.DrawLine(pen, 17, 18, 20, 15); graphics.DrawLine(pen, 14, 15, 17, 12); break;
            case UiIcon.Shield:
                graphics.DrawLines(pen, [new PointF(12, 2), new(21, 6), new(20, 14), new(17, 19), new(12, 22), new(7, 19), new(4, 14), new(3, 6), new(12, 2)]); break;
            case UiIcon.Copy:
                using (var path = Round(new RectangleF(8, 7, 13, 15), 2)) graphics.DrawPath(pen, path);
                graphics.DrawLines(pen, [new PointF(15, 3), new(3, 3), new(3, 16)]); break;
            case UiIcon.Check: graphics.DrawLines(pen, [new PointF(4, 12), new(9, 17), new(20, 6)]); break;
            case UiIcon.ChevronDown: graphics.DrawLines(pen, [new PointF(6, 9), new(12, 15), new(18, 9)]); break;
            case UiIcon.Close: graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 18, 6, 6, 18); break;
        }
        graphics.Restore(state);
    }

    public static void ButtonContent(Graphics graphics, Control control, UiIcon icon, Color ink, bool showKeyboardCues)
    {
        int iconSize = Scale(control, 18), gap = Scale(control, 9), padding = Scale(control, 12);
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        if (!showKeyboardCues) flags |= TextFormatFlags.HidePrefix;
        var bounds = Rectangle.Inflate(control.ClientRectangle, -padding, -2);
        if (icon == UiIcon.None) { TextRenderer.DrawText(graphics, control.Text, control.Font, bounds, ink, flags | TextFormatFlags.HorizontalCenter); return; }
        int textWidth = Math.Min(TextRenderer.MeasureText(graphics, control.Text, control.Font, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | (showKeyboardCues ? 0 : TextFormatFlags.HidePrefix)).Width,
            Math.Max(0, bounds.Width - iconSize - gap));
        int left = bounds.Left + Math.Max(0, (bounds.Width - iconSize - gap - textWidth) / 2);
        Icon(graphics, icon, new Rectangle(left, (control.Height - iconSize) / 2, iconSize, iconSize), ink);
        TextRenderer.DrawText(graphics, control.Text, control.Font, new Rectangle(left + iconSize + gap, bounds.Top, textWidth, bounds.Height), ink, flags);
    }
}
