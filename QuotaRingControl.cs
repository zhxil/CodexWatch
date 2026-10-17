using System.Drawing.Drawing2D;

namespace CodexQuotaTray;

internal sealed class QuotaRingControl : Control
{
    private int? _remainingPercent;
    private int? _progressPercent;
    private int? _markerPercent;
    private string? _valueText;
    private Color? _valueColor;
    private Color _progressColor = Color.FromArgb(97, 205, 145);
    private readonly System.Windows.Forms.Timer _refreshAnimationTimer;
    private float _refreshAngle = -90f;
    private bool _isRefreshing;

    public int? RemainingPercent
    {
        get => _remainingPercent;
        set
        {
            _remainingPercent = value;
            Invalidate();
        }
    }

    public Color TrackColor { get; set; } = Color.FromArgb(42, 47, 57);
    public int? ProgressPercent
    {
        get => _progressPercent;
        set
        {
            _progressPercent = value;
            Invalidate();
        }
    }
    public Color MarkerColor { get; set; } = Color.FromArgb(255, 166, 61);
    public int? MarkerPercent
    {
        get => _markerPercent;
        set
        {
            _markerPercent = value;
            Invalidate();
        }
    }
    public Color ProgressColor
    {
        get => _progressColor;
        set
        {
            _progressColor = value;
            Invalidate();
        }
    }
    public string? ValueText
    {
        get => _valueText;
        set
        {
            _valueText = value;
            Invalidate();
        }
    }
    public Color? ValueColor
    {
        get => _valueColor;
        set
        {
            _valueColor = value;
            Invalidate();
        }
    }
    public bool IsRefreshing
    {
        get => _isRefreshing;
        set
        {
            if (_isRefreshing == value)
            {
                return;
            }

            _isRefreshing = value;
            if (value)
            {
                _refreshAnimationTimer.Start();
            }
            else
            {
                _refreshAnimationTimer.Stop();
                _refreshAngle = -90f;
            }
            Invalidate();
        }
    }

    public QuotaRingControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        _refreshAnimationTimer = new System.Windows.Forms.Timer { Interval = 40 };
        _refreshAnimationTimer.Tick += (_, _) =>
        {
            _refreshAngle = (_refreshAngle + 13f) % 360f;
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var size = Math.Min(ClientSize.Width, ClientSize.Height) - 10;
        var bounds = new Rectangle((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2, size, size);
        const float thickness = 6f;

        using var trackPen = new Pen(TrackColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(trackPen, bounds, -90, 359.8f);

        var progressPercent = _progressPercent ?? _remainingPercent;
        if (progressPercent.HasValue)
        {
            var sweep = Math.Clamp(progressPercent.Value, 0, 100) * 3.6f;
            using var progressPen = new Pen(ProgressColor, thickness) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawArc(progressPen, bounds, -90, sweep);
        }

        if (_markerPercent.HasValue)
        {
            var markerAngle = -90 + Math.Clamp(_markerPercent.Value, 0, 100) * 3.6f;
            using var markerPen = new Pen(MarkerColor, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            e.Graphics.DrawArc(markerPen, bounds, markerAngle - 2.5f, 5f);
        }

        if (_isRefreshing)
        {
            using var refreshPen = new Pen(Color.FromArgb(235, 245, 247, 249), 3f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawArc(refreshPen, bounds, _refreshAngle, 42f);
        }

        var valueText = _valueText ?? (_remainingPercent.HasValue ? $"{_remainingPercent.Value}%" : "—");
        using var valueFont = new Font("Segoe UI", 12f, FontStyle.Bold);
        using var valueBrush = new SolidBrush(_valueColor ?? ForeColor);

        DrawCentered(e.Graphics, valueText, valueFont, valueBrush, ClientRectangle);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshAnimationTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void DrawCentered(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds)
    {
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        graphics.DrawString(text, font, brush, bounds, format);
    }
}
