using System.Drawing.Drawing2D;

namespace CodexQuotaTray;

internal sealed class TrayMenuColorTable : ProfessionalColorTable
{
    private static readonly Color Canvas = Color.FromArgb(22, 24, 28);
    private static readonly Color Hover = Color.FromArgb(34, 38, 44);
    private static readonly Color Edge = Color.FromArgb(52, 57, 65);

    public override Color ToolStripDropDownBackground => Canvas;
    public override Color ImageMarginGradientBegin => Canvas;
    public override Color ImageMarginGradientMiddle => Canvas;
    public override Color ImageMarginGradientEnd => Canvas;
    public override Color MenuBorder => Edge;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Hover;
    public override Color MenuItemSelectedGradientBegin => Hover;
    public override Color MenuItemSelectedGradientEnd => Hover;
    public override Color SeparatorDark => Edge;
    public override Color SeparatorLight => Edge;
}

internal sealed class TrayMenuRenderer : ToolStripProfessionalRenderer
{
    private static readonly Color Accent = Color.FromArgb(116, 224, 177);

    public TrayMenuRenderer() : base(new TrayMenuColorTable())
    {
        RoundedEdges = true;
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var size = 14;
        var bounds = new Rectangle(
            e.ImageRectangle.Left + (e.ImageRectangle.Width - size) / 2,
            e.ImageRectangle.Top + (e.ImageRectangle.Height - size) / 2,
            size,
            size);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPanel.CreateRoundedPath(bounds, 4);
        using var brush = new SolidBrush(Accent);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Color.FromArgb(15, 42, 31), 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLines(pen, new[]
        {
            new Point(bounds.Left + 3, bounds.Top + 7),
            new Point(bounds.Left + 6, bounds.Top + 10),
            new Point(bounds.Left + 11, bounds.Top + 4)
        });
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextRectangle = new Rectangle(16, 0, Math.Max(1, e.Item.Width - 48), e.Item.Height);
        base.OnRenderItemText(e);

        if (e.Item is not ToolStripMenuItem { Checked: true })
        {
            return;
        }

        var bounds = new Rectangle(e.Item.Width - 30, (e.Item.Height - 14) / 2, 14, 14);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPanel.CreateRoundedPath(bounds, 4);
        using var brush = new SolidBrush(Accent);
        e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Color.FromArgb(15, 42, 31), 1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        e.Graphics.DrawLines(pen, new[]
        {
            new Point(bounds.Left + 3, bounds.Top + 7),
            new Point(bounds.Left + 6, bounds.Top + 10),
            new Point(bounds.Left + 11, bounds.Top + 4)
        });
    }
}
