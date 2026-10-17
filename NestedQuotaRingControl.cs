using System.Drawing.Drawing2D;

namespace CodexQuotaTray;

internal sealed class NestedQuotaRingControl : Control
{
    private readonly System.Windows.Forms.Timer _refreshAnimationTimer;
    private int? _totalPercent;
    private int? _todayPercent;
    private int? _todayProgressPercent;
    private int? _todayOveragePercent;
    private Color _totalColor = Color.FromArgb(116, 224, 177);
    private Color _todayColor = Color.FromArgb(242, 185, 93);
    private bool _isRefreshing;
    private bool _isRevealAnimating;
    private float _outerRotation = -90f;
    private float _innerRotation = 90f;
    private float _revealProgress = 1f;

    public Color TrackColor { get; set; } = Color.FromArgb(43, 47, 53);
    public Color MutedTextColor { get; set; } = Color.FromArgb(139, 146, 157);

    public int? TotalPercent
    {
        get => _totalPercent;
        set { _totalPercent = value; Invalidate(); }
    }

    public int? TodayPercent
    {
        get => _todayPercent;
        set { _todayPercent = value; Invalidate(); }
    }

    public int? TodayProgressPercent
    {
        get => _todayProgressPercent;
        set { _todayProgressPercent = value; Invalidate(); }
    }

    public int? TodayOveragePercent
    {
        get => _todayOveragePercent;
        set { _todayOveragePercent = value; Invalidate(); }
    }

    public Color TotalColor
    {
        get => _totalColor;
        set { _totalColor = value; Invalidate(); }
    }

    public Color TodayColor
    {
        get => _todayColor;
        set { _todayColor = value; Invalidate(); }
    }

    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (_isRefreshing == value) return;
            _isRefreshing = value;
            if (value)
            {
                _isRevealAnimating = false;
                _outerRotation = -90f;
                _innerRotation = 90f;
                _refreshAnimationTimer.Start();
            }
            else
            {
                _isRevealAnimating = true;
                _revealProgress = 0f;
                _refreshAnimationTimer.Start();
            }
            Invalidate();
        }
    }

    public NestedQuotaRingControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleName = "今日可用额度和总可用额度";

        _refreshAnimationTimer = new System.Windows.Forms.Timer { Interval = 25 };
        _refreshAnimationTimer.Tick += (_, _) =>
        {
            if (_isRefreshing)
            {
                _outerRotation = (_outerRotation + 8f) % 360f;
                _innerRotation = (_innerRotation - 11f) % 360f;
            }
            else if (_isRevealAnimating)
            {
                _revealProgress = Math.Min(1f, _revealProgress + 0.065f);
                if (_revealProgress >= 1f)
                {
                    _isRevealAnimating = false;
                    _refreshAnimationTimer.Stop();
                }
            }
            else
            {
                _refreshAnimationTimer.Stop();
            }
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var diameter = Math.Min(ClientSize.Width, ClientSize.Height) - 10;
        var outerBounds = CenteredSquare(diameter);
        var innerBounds = CenteredSquare(diameter - 26);
        const float outerThickness = 8f;
        const float innerThickness = 6.5f;

        DrawGlassLens(e.Graphics, innerBounds);

        if (_isRefreshing)
        {
            DrawTrack(e.Graphics, outerBounds, outerThickness);
            DrawTrack(e.Graphics, innerBounds, innerThickness);
            DrawSpinnerRing(e.Graphics, outerBounds, outerThickness, _outerRotation, 86f, TotalColor);
            DrawSpinnerRing(e.Graphics, innerBounds, innerThickness, _innerRotation, 68f, TodayColor);
            using var syncFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
            using var syncBrush = new SolidBrush(Color.FromArgb(214, ForeColor));
            DrawCentered(e.Graphics, "同步中", syncFont, syncBrush, ClientRectangle);
            return;
        }

        var reveal = _isRevealAnimating ? EaseOutCubic(_revealProgress) : 1f;
        var totalProgress = _totalPercent.HasValue ? _totalPercent.Value * reveal : (float?)null;
        var todayTarget = _todayProgressPercent ?? _todayPercent;
        var todayProgress = todayTarget.HasValue ? todayTarget.Value * reveal : (float?)null;
        DrawRing(e.Graphics, outerBounds, outerThickness, totalProgress, TotalColor);
        DrawRing(e.Graphics, innerBounds, innerThickness, todayProgress, TodayColor);

        var todayText = _todayOveragePercent.HasValue ? $"超 {_todayOveragePercent.Value}%" : $"今 {FormatValue(_todayPercent)}%";
        var totalText = $"总 {FormatValue(_totalPercent)}%";
        using var todayFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        using var totalFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Bold);
        using var todayBrush = new SolidBrush(_todayOveragePercent.HasValue ? TodayColor : ForeColor);
        using var totalBrush = new SolidBrush(TotalColor);
        using var dividerPen = new Pen(Color.FromArgb(42, 225, 239, 250), 1f);

        var centerY = ClientSize.Height / 2;
        DrawCentered(e.Graphics, todayText, todayFont, todayBrush, new Rectangle(0, centerY - 19, Width, 17));
        e.Graphics.DrawLine(dividerPen, centerY - 15, centerY, centerY + 15, centerY);
        DrawCentered(e.Graphics, totalText, totalFont, totalBrush, new Rectangle(0, centerY + 2, Width, 17));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _refreshAnimationTimer.Dispose();
        base.Dispose(disposing);
    }

    private void DrawRing(Graphics graphics, Rectangle bounds, float thickness, float? percent, Color color)
    {
        DrawTrack(graphics, bounds, thickness);
        if (!percent.HasValue) return;

        var sweep = Math.Clamp(percent.Value, 0f, 100f) * 3.6f;
        using var wideGlowPen = new Pen(Color.FromArgb(16, color), thickness + 4f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(wideGlowPen, bounds, -90f, sweep);
        using var progressPen = new Pen(Lighten(color, 10), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(progressPen, bounds, -90f, sweep);
    }

    private void DrawTrack(Graphics graphics, Rectangle bounds, float thickness)
    {
        using var trackPen = new Pen(Color.FromArgb(225, TrackColor), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(trackPen, bounds, -90f, 359.8f);
    }

    private static void DrawSpinnerRing(Graphics graphics, Rectangle bounds, float thickness, float startAngle, float sweep, Color color)
    {
        using var glowPen = new Pen(Color.FromArgb(28, color), thickness + 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(glowPen, bounds, startAngle, sweep);
        using var spinnerPen = new Pen(Lighten(color, 24), thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(spinnerPen, bounds, startAngle, sweep);
    }

    private static void DrawGlassLens(Graphics graphics, Rectangle innerBounds)
    {
        var lensBounds = Rectangle.Inflate(innerBounds, -6, -6);
        using var lensPath = new GraphicsPath();
        lensPath.AddEllipse(lensBounds);
        using var lensBrush = new LinearGradientBrush(
            lensBounds,
            Color.FromArgb(96, 31, 38, 45),
            Color.FromArgb(178, 13, 17, 21),
            LinearGradientMode.Vertical);
        graphics.FillPath(lensBrush, lensPath);
        using var rimPen = new Pen(Color.FromArgb(28, 225, 241, 255), 1f);
        graphics.DrawPath(rimPen, lensPath);
    }

    private static Color Lighten(Color color, int amount) => Color.FromArgb(
        color.A,
        Math.Min(255, color.R + amount),
        Math.Min(255, color.G + amount),
        Math.Min(255, color.B + amount));

    private static float EaseOutCubic(float value) => 1f - MathF.Pow(1f - Math.Clamp(value, 0f, 1f), 3f);

    private Rectangle CenteredSquare(int size) => new((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2, size, size);

    private static string FormatValue(int? value) => value.HasValue ? value.Value.ToString() : "—";

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }
}
