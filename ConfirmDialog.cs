using System.Drawing;

namespace PCLInjector;

/// <summary>
/// 让用户选择"替换原文件"还是"备份原文件"的确认对话框。
/// 所有文本按当前宽度自动换行，窗口可自由缩放，按钮始终可见。
/// </summary>
public sealed class ConfirmDialog : Form
{
    public enum Choice { Backup, Replace, Cancel }

    public Choice Selection { get; private set; } = Choice.Cancel;

    private readonly Panel _body;
    private readonly FlowLayoutPanel _content;
    private readonly Panel _pathCard;
    private readonly Label _pathLabel;
    private readonly Label _intro;
    private readonly Panel _warnCard;
    private readonly Label _warnLabel;
    private readonly Panel _backupCard;
    private readonly Label _backupLabel;

    public ConfirmDialog(string targetPath)
    {
        Text = "绕过正版验证 · 选择处理方式";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 480);
        MinimumSize = new Size(520, 400);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Font = UiTheme.Font(9.5f);
        BackColor = UiTheme.Bg;
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { /* 图标不可用时使用默认 */ }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62f));

        var header = new GradientPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = new Padding(20, 0, 20, 0),
            Title = "选择如何处理目标 exe",
            TitleSize = 14f
        };
        root.Controls.Add(header, 0, 0);

        // 主体：可滚动，内容按宽度换行
        _body = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty,
            Padding = new Padding(18, 14, 18, 14), BackColor = UiTheme.Bg
        };
        _content = new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Margin = Padding.Empty, BackColor = Color.Transparent
        };
        _body.Controls.Add(_content);

        _pathLabel = new Label
        {
            Text = "目标文件：" + targetPath, AutoSize = true,
            ForeColor = UiTheme.Text, Font = UiTheme.Font(9.5f, FontStyle.Bold),
            Location = new Point(12, 10)
        };
        _pathCard = new CardPanel { Padding = new Padding(12, 10, 12, 10) };
        _pathCard.Controls.Add(_pathLabel);
        _content.Controls.Add(_pathCard);

        _intro = new Label
        {
            Text = "即将把补丁写入上面的原 exe（生成版与原文件同名）。请选择写入方式：",
            AutoSize = true, ForeColor = UiTheme.Muted, Font = UiTheme.Font(9.5f),
            Margin = new Padding(0, 10, 0, 4)
        };
        _content.Controls.Add(_intro);

        _warnLabel = new Label
        {
            Text = "替换风险：选择“替换原文件”将直接覆盖原 exe，操作不可撤销。若补丁与版本不匹配或写入异常，程序可能无法启动，且无法恢复原文件。",
            AutoSize = true, Location = new Point(12, 10),
            ForeColor = Color.FromArgb(146, 64, 14), Font = UiTheme.Font(9f)
        };
        _warnCard = new CardPanel
        {
            FillColor = Color.FromArgb(255, 251, 235),
            BorderColor = Color.FromArgb(253, 230, 138), Radius = 10,
            Padding = new Padding(12, 10, 12, 10)
        };
        _warnCard.Controls.Add(_warnLabel);
        _content.Controls.Add(_warnCard);

        _backupLabel = new Label
        {
            Text = "备份原文件：选择“备份原文件”会先把原 exe 复制为同目录下带 .bak 后缀的备份文件（例如 PCL2_CE.exe.bak），再把补丁写入原文件。出问题时可用 .bak 还原。",
            AutoSize = true, Location = new Point(12, 10),
            ForeColor = Color.FromArgb(30, 80, 150), Font = UiTheme.Font(9f)
        };
        _backupCard = new CardPanel
        {
            FillColor = Color.FromArgb(239, 246, 255),
            BorderColor = Color.FromArgb(191, 219, 254), Radius = 10,
            Padding = new Padding(12, 10, 12, 10)
        };
        _backupCard.Controls.Add(_backupLabel);
        _content.Controls.Add(_backupCard);

        _content.Controls.Add(new Panel { Height = 6, Width = 10, BackColor = Color.Transparent });
        root.Controls.Add(_body, 0, 1);

        // 底部按钮行：固定在底部，不随内容滚动
        var bar = new Panel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            BackColor = Color.White, Padding = new Padding(16, 0, 16, 0)
        };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Margin = new Padding(0, 12, 0, 0), BackColor = Color.Transparent
        };
        buttons.Controls.Add(MakeButton("备份原文件（推荐）", 205, 38, UiTheme.Accent, Choice.Backup));
        buttons.Controls.Add(MakeButton("替换原文件", 140, 38, Color.FromArgb(220, 38, 38), Choice.Replace));
        buttons.Controls.Add(MakeButton("取消", 100, 38, Color.FromArgb(100, 116, 139), Choice.Cancel));
        bar.Controls.Add(buttons);
        root.Controls.Add(bar, 0, 2);

        Controls.Add(root);

        Load += (_, _) => Relayout();
        Resize += (_, _) => Relayout();
    }

    /// <summary>按当前可用宽度重算换行宽度与卡片高度。</summary>
    private void Relayout()
    {
        int cardWidth = Math.Max(260, _content.Width - 2);
        int textWidth = Math.Max(220, cardWidth - 26);

        _pathCard.Width = cardWidth;
        _pathLabel.MaximumSize = new Size(textWidth, 0);
        _pathCard.Height = _pathLabel.Height + 22;

        _intro.MaximumSize = new Size(textWidth, 0);

        _warnCard.Width = cardWidth;
        _warnLabel.MaximumSize = new Size(textWidth, 0);
        _warnCard.Height = _warnLabel.Height + 22;

        _backupCard.Width = cardWidth;
        _backupLabel.MaximumSize = new Size(textWidth, 0);
        _backupCard.Height = _backupLabel.Height + 22;
    }

    private Button MakeButton(string text, int width, int height, Color color, Choice choice)
    {
        var b = new Button
        {
            Text = text,
            Size = new Size(width, height),
            Margin = new Padding(0, 0, 8, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = UiTheme.Font(9f, FontStyle.Bold),
            Cursor = Cursors.Hand
        };
        b.FlatAppearance.BorderSize = 0;
        b.Click += (_, _) =>
        {
            Selection = choice;
            DialogResult = choice == Choice.Cancel ? DialogResult.Cancel : DialogResult.OK;
            Close();
        };
        return b;
    }
}
