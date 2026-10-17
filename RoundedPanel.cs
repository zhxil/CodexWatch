using System.Drawing.Drawing2D;

namespace CodexQuotaTray;

internal sealed class RoundedPanel : Panel
{
    public int CornerRadius { get; set; } = 14;
    public Color BorderColor { get; set; } = Color.Transparent;
    public bool UseGlassEffect { get; set; }
    public Color GlassTintColor { get; set; } = Color.FromArgb(22, 31, 40);

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // Paint the rounded surface ourselves so the parent background never leaks into the corners.
        using var path = CreateRoundedPath(ClientRectangle, CornerRadius);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (UseGlassEffect)
        {
            using var brush = new LinearGradientBrush(
                ClientRectangle,
                Color.FromArgb(246, GlassTintColor),
                BackColor,
                LinearGradientMode.Vertical);
            e.Graphics.FillPath(brush, path);
        }
        else
        {
            using var brush = new SolidBrush(BackColor);
            e.Graphics.FillPath(brush, path);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (BorderColor == Color.Transparent || ClientSize.Width < 2 || ClientSize.Height < 2)
        {
            return;
        }

        using var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        using var pen = new Pen(BorderColor, 1);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawPath(pen, path);

        if (UseGlassEffect)
        {
            using var highlightPath = CreateRoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), Math.Max(2, CornerRadius - 1));
            using var highlightPen = new Pen(Color.FromArgb(24, 221, 239, 255), 1f);
            e.Graphics.SetClip(new Rectangle(0, 0, Width, Math.Max(1, Height / 2)));
            e.Graphics.DrawPath(highlightPen, highlightPath);
            e.Graphics.ResetClip();
        }
    }

    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(Math.Max(radius, 2) * 2, Math.Min(bounds.Width, bounds.Height));
        var arc = new Rectangle(bounds.Left, bounds.Top, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
