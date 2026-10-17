using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexQuotaTray.Avalonia;

public sealed class QuotaGaugeControl : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<QuotaGaugeControl, double>(nameof(Value));

    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<QuotaGaugeControl, IBrush?>(nameof(Accent));

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, Math.Clamp(value, 0, 100));
    }

    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    static QuotaGaugeControl()
    {
        AffectsRender<QuotaGaugeControl>(ValueProperty, AccentProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Max(1, Math.Min(Bounds.Width, Bounds.Height) / 2 - 6);
        var accentColor = (Accent as SolidColorBrush)?.Color ?? Color.Parse("#74E0B1");
        var accentBrush = new SolidColorBrush(accentColor);
        var glowBrush = new SolidColorBrush(Color.FromArgb(38, accentColor.R, accentColor.G, accentColor.B));
        var trackBrush = new SolidColorBrush(Color.Parse("#35566D73"));

        context.DrawEllipse(null, new Pen(trackBrush, 4), center, radius, radius);

        var progress = Math.Clamp(Value, 0, 100) / 100d;
        if (progress <= 0)
        {
            return;
        }

        if (progress >= 0.999)
        {
            context.DrawEllipse(null, new Pen(glowBrush, 9), center, radius, radius);
            context.DrawEllipse(null, new Pen(accentBrush, 4.5), center, radius, radius);
            return;
        }

        var startAngle = -90d;
        var endAngle = startAngle + progress * 360d;
        var start = PointOnCircle(center, radius, startAngle);
        var end = PointOnCircle(center, radius, endAngle);
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(start, false);
            path.ArcTo(end, new Size(radius, radius), 0, progress > 0.5, SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, new Pen(glowBrush, 9), geometry);
        context.DrawGeometry(null, new Pen(accentBrush, 4.5), geometry);
        context.DrawEllipse(accentBrush, null, end, 3.1, 3.1);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), null, end, 1.15, 1.15);
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var angle = angleDegrees * Math.PI / 180d;
        return new Point(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle));
    }
}
