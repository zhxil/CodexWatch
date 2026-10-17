using System.Drawing.Drawing2D;

namespace CodexQuotaTray;

internal sealed class QuotaMeterControl : Control
{
    private int? _remainingPercent;
    private int? _markerPercent;
    private Color _progressColor = Color.FromArgb(116, 224, 177);

    public int? RemainingPercent
    {
        get => _remainingPercent;
        set
        {
            _remainingPercent = value;
            Invalidate();
        }
    }

    public Color TrackColor { get; set; } = Color.FromArgb(43, 47, 53);
    public Color UsedColor { get; set; } = Color.FromArgb(76, 82, 91);

    public int? MarkerPercent
    {
        get => _markerPercent;
        set
        {
            _markerPercent = value;
            Invalidate();
        }
    }

    public Color MarkerColor { get; set; } = Color.FromArgb(244, 246, 248);

    public Color ProgressColor
    {
        get => _progressColor;
        set
        {
            _progressColor = value;
            Invalidate();
        }
    }

    public QuotaMeterControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Height = 15;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        const float outerPadding = 2f;
        var barBounds = new RectangleF(outerPadding, outerPadding, ClientSize.Width - outerPadding * 2, ClientSize.Height - outerPadding * 2);
        using var trackBrush = new SolidBrush(TrackColor);
        using var usedBrush = new SolidBrush(UsedColor);
        using var remainingBrush = new SolidBrush(ProgressColor);
        using (var barPath = CreateRoundedPath(barBounds, barBounds.Height / 2))
        {
            e.Graphics.FillPath(_remainingPercent.HasValue ? usedBrush : trackBrush, barPath);
        }

        if (_remainingPercent.HasValue && _remainingPercent.Value > 0)
        {
            var remainingWidth = barBounds.Width * Math.Clamp(_remainingPercent.Value, 0, 100) / 100f;
            var remainingBounds = new RectangleF(barBounds.X, barBounds.Y, remainingWidth, barBounds.Height);
            using var remainingPath = CreateRoundedPath(remainingBounds, barBounds.Height / 2);
            e.Graphics.FillPath(remainingBrush, remainingPath);
        }

        if (_markerPercent.HasValue)
        {
            var dailyAllowance = 100d / 7d;
            var startPercent = Math.Clamp(_markerPercent.Value, 0, 100);
            var endPercent = Math.Clamp(startPercent + dailyAllowance, 0, 100);
            var markerX = barBounds.X + barBounds.Width * (float)(startPercent / 100d);
            var markerRight = barBounds.X + barBounds.Width * (float)(endPercent / 100d);
            var markerBounds = new RectangleF(markerX - 1.5f, 0.5f, markerRight - markerX + 3f, ClientSize.Height - 1f);
            using var markerPath = CreateRoundedPath(markerBounds, markerBounds.Height / 2f);
            using var markerPen = new Pen(MarkerColor, 2f);
            e.Graphics.DrawPath(markerPen, markerPath);
        }
    }

    private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
