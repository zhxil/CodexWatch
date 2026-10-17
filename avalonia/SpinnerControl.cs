using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CodexQuotaTray.Avalonia;

public sealed class SpinnerControl : Control
{
    public static readonly StyledProperty<bool> IsActiveProperty =
        AvaloniaProperty.Register<SpinnerControl, bool>(nameof(IsActive));

    private readonly DispatcherTimer _timer;
    private double _angle;

    public bool IsActive
    {
        get => GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public SpinnerControl()
    {
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(32), DispatcherPriority.Render, (_, _) =>
        {
            _angle = (_angle + 12) % 360;
            InvalidateVisual();
        });
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!IsActive || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var radius = Math.Min(Bounds.Width, Bounds.Height) / 2 - 2.2;
        context.DrawEllipse(
            null,
            new Pen(new SolidColorBrush(Color.FromArgb(105, 195, 231, 215)), 1),
            center,
            radius,
            radius);

        var angle = _angle * Math.PI / 180d;
        var point = new Point(
            center.X + radius * Math.Cos(angle),
            center.Y + radius * Math.Sin(angle));
        context.DrawEllipse(
            new SolidColorBrush(Color.Parse("#D5FFEC")),
            null,
            point,
            1.35,
            1.35);
    }
}
