using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace CodexQuotaTray;

internal sealed class QuotaDashboardControl : Control
{
    private readonly System.Windows.Forms.Timer _refreshAnimationTimer;
    private int? _totalPercent;
    private int? _todayPercent;
    private int? _todayProgressPercent;
    private int? _todayOveragePercent;
    private Color _totalColor = Color.FromArgb(116, 224, 177);
    private Color _todayColor = Color.FromArgb(242, 185, 93);
    private Color _statusColor = Color.FromArgb(116, 224, 177);
    private bool _isRefreshing;
    private float _refreshPhase;

    public Color TrackColor { get; set; } = Color.FromArgb(53, 58, 64);
    public Color MutedTextColor { get; set; } = Color.FromArgb(139, 146, 157);
    public Color SecondaryTextColor { get; set; } = Color.FromArgb(190, 196, 204);
    public string PlanText { get; set; } = "未知计划";
    public string StatusText { get; set; } = "等待同步";
    public string ResetSummaryText { get; set; } = "—";
    public string ForecastText { get; set; } = "等待额度数据";

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

    public Color StatusColor
    {
        get => _statusColor;
        set { _statusColor = value; Invalidate(); }
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
                _refreshPhase = 0f;
            }

            Invalidate();
        }
    }

    public QuotaDashboardControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        AccessibleName = "Codex 额度概览";

        _refreshAnimationTimer = new System.Windows.Forms.Timer { Interval = 45 };
        _refreshAnimationTimer.Tick += (_, _) =>
        {
            _refreshPhase = (_refreshPhase + 0.08f) % 1f;
            Invalidate();
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var width = ClientSize.Width;
        var height = ClientSize.Height;
        if (width < 160 || height < 96)
        {
            return;
        }

        const int left = 17;
        const int right = 17;
        const int headerY = 13;
        const int contentY = 42;
        var splitX = Math.Max(108, (int)(width * 0.34f));
        var rightX = splitX + 14;
        var rightWidth = Math.Max(80, width - rightX - right);

        DrawHeader(e.Graphics, width, headerY, left, right);
        DrawHero(e.Graphics, left, contentY + 12, splitX - left - 8);

        var rowWidth = rightWidth;
        DrawMetricRow(
            e.Graphics,
            rightX,
            contentY + 4,
            rowWidth,
            "周期总额",
            FormatPercent(_totalPercent),
            _totalPercent,
            _totalColor);

        var todayLabel = _todayOveragePercent.HasValue
            ? $"超额 {_todayOveragePercent.Value}%"
            : $"可用 {FormatPercent(_todayPercent)}";
        DrawMetricRow(
            e.Graphics,
            rightX,
            contentY + 43,
            rowWidth,
            "今日建议",
            todayLabel,
            _todayProgressPercent,
            _todayColor);

        using var dividerPen = new Pen(Color.FromArgb(34, 225, 239, 250), 1f);
        e.Graphics.DrawLine(dividerPen, left, height - 29, width - right, height - 29);

        var footerFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
        using (footerFont)
        using (var footerBrush = new SolidBrush(SecondaryTextColor))
        using (var mutedBrush = new SolidBrush(MutedTextColor))
        {
            var forecast = string.IsNullOrWhiteSpace(ForecastText) ? "—" : ForecastText;
            DrawText(e.Graphics, $"预测 · {forecast}", footerFont, footerBrush, new Rectangle(left, height - 25, width / 2 - 4, 18), TextAlign.Left);
            DrawText(e.Graphics, $"重置 · {ResetSummaryText}", footerFont, mutedBrush, new Rectangle(width / 2 + 4, height - 25, width / 2 - right - 4, 18), TextAlign.Right);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshAnimationTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawHeader(Graphics graphics, int width, int y, int left, int right)
    {
        var dotSize = 7;
        var dotBounds = new Rectangle(left, y + 4, dotSize, dotSize);
        var dotColor = IsRefreshing ? Blend(_statusColor, Color.White, 0.42f + 0.18f * MathF.Sin(_refreshPhase * MathF.Tau)) : _statusColor;
        using (var dotBrush = new SolidBrush(dotColor))
        {
            graphics.FillEllipse(dotBrush, dotBounds);
        }

        using var eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var eyebrowBrush = new SolidBrush(MutedTextColor);
        DrawText(graphics, "CODEX WATCH", eyebrowFont, eyebrowBrush, new Rectangle(left + 13, y, 130, 17), TextAlign.Left);

        using var statusFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
        using var statusBrush = new SolidBrush(IsRefreshing ? SecondaryTextColor : MutedTextColor);
        var shownStatus = IsRefreshing ? "同步中" : StatusText;
        var statusTextWidth = Math.Max(IsRefreshing ? 52 : 34, TextRenderer.MeasureText(shownStatus, statusFont).Width + 4);
        var statusWidth = statusTextWidth + (IsRefreshing ? 16 : 0);
        var chipWidth = Math.Max(50, TextRenderer.MeasureText(PlanText, statusFont).Width + 18);
        var chipX = width - right - chipWidth;
        var statusX = chipX - statusWidth - 9;
        if (IsRefreshing)
        {
            var spinnerBounds = new Rectangle(statusX, y + 3, 11, 11);
            using var spinnerPen = new Pen(SecondaryTextColor, 1.8f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(spinnerPen, spinnerBounds, _refreshPhase * 360f, 105f);
            DrawText(graphics, shownStatus, statusFont, statusBrush, new Rectangle(statusX + 16, y, statusTextWidth, 17), TextAlign.Left);
        }
        else
        {
            DrawText(graphics, shownStatus, statusFont, statusBrush, new Rectangle(statusX, y, statusWidth, 17), TextAlign.Right);
        }

        var chipBounds = new Rectangle(chipX, y - 2, chipWidth, 21);
        using (var chipPath = RoundedPanel.CreateRoundedPath(chipBounds, 8))
        using (var chipBrush = new SolidBrush(Color.FromArgb(45, 52, 54)))
        using (var chipPen = new Pen(Color.FromArgb(72, 94, 85), 1f))
        {
            graphics.FillPath(chipBrush, chipPath);
            graphics.DrawPath(chipPen, chipPath);
        }

        using var chipBrushText = new SolidBrush(Color.FromArgb(157, 231, 194));
        DrawText(graphics, PlanText, statusFont, chipBrushText, chipBounds, TextAlign.Center);
    }

    private void DrawHero(Graphics graphics, int left, int y, int width)
    {
        using var valueFont = new Font("Segoe UI", 26f, FontStyle.Bold);
        using var valueBrush = new SolidBrush(_totalPercent.HasValue ? _totalColor : SecondaryTextColor);
        DrawText(graphics, FormatPercent(_totalPercent), valueFont, valueBrush, new Rectangle(left, y - 4, width, 40), TextAlign.Center);

        using var labelFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular);
        using var labelBrush = new SolidBrush(SecondaryTextColor);
        DrawText(graphics, "本周期剩余", labelFont, labelBrush, new Rectangle(left, y + 40, width, 18), TextAlign.Center);
    }

    private void DrawMetricRow(
        Graphics graphics,
        int x,
        int y,
        int width,
        string label,
        string value,
        int? progress,
        Color color)
    {
        using var labelFont = new Font("Microsoft YaHei UI", 8.5f, FontStyle.Regular);
        using var valueFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(SecondaryTextColor);
        using var valueBrush = new SolidBrush(_todayOveragePercent.HasValue && label == "今日建议" ? Color.FromArgb(247, 147, 147) : color);

        DrawText(graphics, label, labelFont, labelBrush, new Rectangle(x, y, width / 2, 16), TextAlign.Left);
        DrawText(graphics, value, valueFont, valueBrush, new Rectangle(x + width / 2 - 4, y, width / 2 + 4, 16), TextAlign.Right);

        var barBounds = new Rectangle(x, y + 21, width, 7);
        using (var trackPath = RoundedPanel.CreateRoundedPath(barBounds, 4))
        using (var trackBrush = new SolidBrush(TrackColor))
        {
            graphics.FillPath(trackBrush, trackPath);
        }

        if (progress.HasValue)
        {
            var fillWidth = Math.Max(3, (int)Math.Round(barBounds.Width * Math.Clamp(progress.Value, 0, 100) / 100d));
            var fillBounds = new Rectangle(barBounds.Left, barBounds.Top, Math.Min(barBounds.Width, fillWidth), barBounds.Height);
            using var fillPath = RoundedPanel.CreateRoundedPath(fillBounds, 4);
            using var fillBrush = new SolidBrush(color);
            graphics.FillPath(fillBrush, fillPath);
        }
    }

    private static string FormatPercent(int? value) => value.HasValue ? $"{value.Value}%" : "—";

    private static Color Blend(Color first, Color second, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)(first.R + (second.R - first.R) * amount),
            (int)(first.G + (second.G - first.G) * amount),
            (int)(first.B + (second.B - first.B) * amount));
    }

    private static void DrawText(Graphics graphics, string text, Font font, Brush brush, Rectangle bounds, TextAlign alignment)
    {
        using var format = new StringFormat
        {
            Alignment = alignment == TextAlign.Right ? StringAlignment.Far : alignment == TextAlign.Center ? StringAlignment.Center : StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    private enum TextAlign
    {
        Left,
        Center,
        Right
    }
}
