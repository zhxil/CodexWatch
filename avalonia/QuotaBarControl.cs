using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using System.Diagnostics;
using System.Runtime.InteropServices;
namespace CodexQuotaTray.Avalonia;

public sealed class QuotaBarControl : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<QuotaBarControl, double>(nameof(Value));
    public static readonly StyledProperty<IBrush> ForegroundProperty = AvaloniaProperty.Register<QuotaBarControl, IBrush>(nameof(Foreground), new SolidColorBrush(Color.Parse("#CCCCCC")));
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _clock = new();
    private double _sweep = -1;
    public QuotaBarControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => { _sweep = _clock.Elapsed.TotalMilliseconds / 480; if (_sweep >= 1) { _timer.Stop(); _sweep = -1; } InvalidateVisual(); };
        DetachedFromVisualTree += (_, _) => { _timer.Stop(); _sweep = -1; };
    }
    public void Pulse()
    {
        if (OperatingSystem.IsWindows() && SystemParametersInfo(0x1042, 0, out var animate, 0) && !animate) return;
        if (Value <= 0) return;
        _clock.Restart(); _sweep = 0; _timer.Start();
    }
    [DllImport("user32.dll")]
    private static extern bool SystemParametersInfo(uint action, uint param, [MarshalAs(UnmanagedType.Bool)] out bool value, uint flags);
    static QuotaBarControl() => AffectsRender<QuotaBarControl>(ValueProperty, ForegroundProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        const int count = 28;
        const double gap = 2;
        var segment = (Bounds.Width - gap * (count - 1)) / count;
        if (segment <= 0) return;
        var y = (Bounds.Height - 4) / 2;
        var fraction = Math.Clamp(Value, 0, 100) / 100;
        var color = (Foreground as ISolidColorBrush)?.Color ?? Color.Parse("#CCCCCC");
        for (var i = 0; i < count; i++)
        {
            var x = i * (segment + gap);
            var rect = new Rect(x, y, segment, 4);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#363638")), null, rect, .7, .7);
            var fill = Math.Clamp(fraction * count - i, 0, 1);
            if (fill <= 0) continue;
            var t = (double)i / (count - 1);
            var lit = Color.FromRgb((byte)(color.R * (.72 + t * .28)), (byte)(color.G * (.72 + t * .28)), (byte)(color.B * (.72 + t * .28)));
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(25, lit.R, lit.G, lit.B)), null, new Rect(x - .7, y - 2, segment * fill + 1.4, 8), 2, 2);
            context.DrawRectangle(new SolidColorBrush(lit), null, new Rect(x, y, segment * fill, 4), .7, .7);
            if (_sweep >= 0)
            {
                var strength = Math.Max(0, 1 - Math.Abs(t - _sweep * 1.3) / .14);
                if (strength > 0) context.DrawRectangle(new SolidColorBrush(Color.FromArgb((byte)(strength * 190), 245, 235, 255)), null, new Rect(x, y, segment * fill, 4), .7, .7);
            }
        }
        if (fraction > 0)
        {
            var last = Math.Min(count - 1, (int)Math.Ceiling(fraction * count) - 1);
            var end = last * (segment + gap) + segment * Math.Clamp(fraction * count - last, 0, 1);
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#EEEEEE")), null, new Point(Math.Max(1, end - 1), y + 2), 1, 1.6);
        }
    }
}

public sealed class InstrumentFrame : Control
{
    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width; var h = Bounds.Height;
        var edge = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative), GradientStops = new GradientStops { new GradientStop(Color.Parse("#C6ADFF"), 0), new GradientStop(Color.Parse("#005A4080"), 1) } };
        context.DrawRectangle(edge, null, new Rect(12, 0, w * .55, 1), 0, 0);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#8CA58AFF")), null, new Rect(0, 12, 1, 19), 0, 0);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#705F4D8A")), null, new Rect(w - 32, h - 1, 20, 1), 0, 0);
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#9CA58AFF")), null, new Rect(w - 1, h - 30, 1, 18), 0, 0);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#12D5BCFF")), 1);
        context.DrawLine(pen, new Point(w - 65, 1), new Point(w - 120, h - 1));
        context.DrawLine(pen, new Point(w - 61, 1), new Point(w - 116, h - 1));
    }
}
