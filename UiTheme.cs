using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace PCLInjector;

internal static class UiTheme
{
    public static readonly Color Bg = Color.FromArgb(246, 248, 252);
    public static readonly Color Accent = Color.FromArgb(37, 99, 235);
    public static readonly Color AccentDark = Color.FromArgb(29, 78, 216);
    public static readonly Color Card = Color.White;
    public static readonly Color Border = Color.FromArgb(226, 232, 240);
    public static readonly Color Text = Color.FromArgb(30, 41, 59);
    public static readonly Color Muted = Color.FromArgb(100, 116, 139);

    public static Font Font(float size, FontStyle style = FontStyle.Regular)
        => new("Microsoft YaHei UI", size, style);
}

/// <summary>
/// 渐变标题条。文字由本控件直接绘制，避免子 Label 的透明背景覆盖渐变。
/// </summary>
internal sealed class GradientPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ColorFrom { get; set; } = UiTheme.Accent;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color ColorTo { get; set; } = UiTheme.AccentDark;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float TitleSize { get; set; } = 18f;

    public GradientPanel()
    {
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width <= 0 || Height <= 0) return;
        using var brush = new LinearGradientBrush(ClientRectangle, ColorFrom, ColorTo, LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(brush, ClientRectangle);

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine |
                                      TextFormatFlags.NoPadding;
        int left = Padding.Left;
        int width = Math.Max(10, Width - Padding.Horizontal);
        int titleHeight = Math.Min(34, Math.Max(24, Height - Padding.Vertical));

        using var titleFont = UiTheme.Font(TitleSize, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, Title, titleFont,
            new Rectangle(left, Padding.Top, width, titleHeight), Color.White, flags);

        if (!string.IsNullOrEmpty(Subtitle) && Height - Padding.Top - titleHeight > 12)
        {
            using var subFont = UiTheme.Font(9.5f);
            TextRenderer.DrawText(e.Graphics, Subtitle, subFont,
                new Rectangle(left, Padding.Top + titleHeight, width, Height - Padding.Top - titleHeight - Padding.Bottom),
                Color.FromArgb(226, 236, 255), flags);
        }
    }
}

/// <summary>圆角卡片，圆角区域会裁剪子控件，内部请留出 Padding。</summary>
internal sealed class CardPanel : Panel
{
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Radius { get; set; } = 12;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color FillColor { get; set; } = UiTheme.Card;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor { get; set; } = UiTheme.Border;

    public CardPanel()
    {
        BackColor = Color.Transparent;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        UpdateRegion();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private GraphicsPath BuildPath(int w, int h, int r)
    {
        r = Math.Max(1, Math.Min(r, Math.Min(w, h) / 2));
        var path = new GraphicsPath();
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(w - r, 0, r, r, 270, 90);
        path.AddArc(w - r, h - r, r, r, 0, 90);
        path.AddArc(0, h - r, r, r, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void UpdateRegion()
    {
        if (Width <= 0 || Height <= 0) return;
        using var path = BuildPath(Width, Height, Radius);
        Region = new Region(path);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        if (Width <= 0 || Height <= 0) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = BuildPath(Width, Height, Radius);
        using var fill = new SolidBrush(FillColor);
        e.Graphics.FillPath(fill, path);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }
}
