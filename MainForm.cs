namespace PCLInjector;

public sealed class MainForm : Form
{
    // 使用 RichTextBox 而非 TextBox：长路径可以自动换行，完整显示而不被截断。
    private readonly RichTextBox _txtSource = new();
    private readonly RichTextBox _log = new();
    private readonly Label _status = new();
    private readonly Button _btnBrowse = new();
    private readonly Button _btnBypass = new();

    public MainForm()
    {
        Text = "PCL 验证补丁工具 · 绕过正版验证";
        ClientSize = new Size(980, 640);
        MinimumSize = new Size(640, 460);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiTheme.Bg;
        Font = UiTheme.Font(9.5f);
        try { Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { /* 图标不可用时使用默认 */ }

        BuildUi();
        AppendLog("就绪。选择 PCL 的 exe 文件后，点击“绕过正版验证”。", Color.FromArgb(90, 90, 90));
        AppendLog("提示：补丁将写回原文件（生成版与原文件同名）；执行前可选择备份 .bak。注入点按方法名动态查找，不依赖固定偏移。", Color.FromArgb(120, 120, 120));
    }

    private void BuildUi()
    {
        // 根布局：标题栏固定高度、内容区占满剩余空间、状态栏固定高度。
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, BackColor = UiTheme.Bg
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));

        // —— 标题栏 ——
        var header = new GradientPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = new Padding(22, 10, 22, 8),
            Title = "PCL 验证补丁工具",
            Subtitle = "动态查找验证判定注入点 · 修改 IL 绕过正版账户前置校验 · 支持单文件 .NET 程序集"
        };
        root.Controls.Add(header, 0, 0);

        // —— 内容区：行高自适应，日志区吃掉所有剩余空间 ——
        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Margin = Padding.Empty, Padding = new Padding(18, 14, 18, 14),
            BackColor = UiTheme.Bg
        };
        main.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 112f)); // 文件卡片
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 10f));  // 间隔
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 44f));  // 按钮行
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 10f));  // 间隔
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));  // 日志卡片

        // 文件卡片
        var fileCard = new CardPanel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            Padding = new Padding(16, 12, 16, 12)
        };
        var fileTitle = new Label
        {
            Text = "目标 exe（注入后将写回原文件）", Dock = DockStyle.Top, Height = 22,
            AutoSize = false, ForeColor = UiTheme.Text,
            Font = UiTheme.Font(10f, FontStyle.Bold), TextAlign = ContentAlignment.MiddleLeft
        };
        var fileRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = new Padding(0, 6, 0, 0), BackColor = Color.Transparent
        };
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        fileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116f));
        fileRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _txtSource.ReadOnly = true;
        _txtSource.Dock = DockStyle.Fill;
        _txtSource.Margin = Padding.Empty;
        _txtSource.BorderStyle = BorderStyle.None;
        _txtSource.BackColor = Color.FromArgb(248, 250, 252);
        _txtSource.ForeColor = UiTheme.Text;
        _txtSource.Font = UiTheme.Font(9.5f);
        _txtSource.WordWrap = true;
        _txtSource.ScrollBars = RichTextBoxScrollBars.Vertical;
        fileRow.Controls.Add(_txtSource, 0, 0);

        _btnBrowse.Text = "选择 exe";
        StyleButton(_btnBrowse, Color.FromArgb(71, 85, 105));
        _btnBrowse.Height = 36;
        _btnBrowse.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _btnBrowse.Margin = new Padding(10, 0, 0, 0);
        _btnBrowse.Click += (_, _) => BrowseSource();
        fileRow.Controls.Add(_btnBrowse, 1, 0);

        fileCard.Controls.Add(fileRow);
        fileCard.Controls.Add(fileTitle);
        main.Controls.Add(fileCard, 0, 0);

        // 按钮行：左主按钮、右次按钮，中间留空，宽度随窗口变化。
        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, BackColor = Color.Transparent
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
        actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _btnBypass.Text = "绕过正版验证";
        StyleButton(_btnBypass, UiTheme.Accent);
        _btnBypass.Height = 40;
        _btnBypass.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _btnBypass.Click += (_, _) => Bypass();
        actions.Controls.Add(_btnBypass, 0, 0);

        var btnClear = new Button { Text = "清空日志" };
        StyleButton(btnClear, Color.FromArgb(100, 116, 139));
        btnClear.Height = 40;
        btnClear.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        btnClear.Click += (_, _) => _log.Clear();
        actions.Controls.Add(btnClear, 2, 0);
        main.Controls.Add(actions, 0, 2);

        // 日志卡片
        var logCard = new CardPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty };
        var logLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = new Padding(14, 10, 14, 12)
        };
        logLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var logLabel = new Label
        {
            Text = "运行日志", Dock = DockStyle.Fill, AutoEllipsis = true,
            Font = UiTheme.Font(10f, FontStyle.Bold), ForeColor = UiTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft
        };
        _log.Dock = DockStyle.Fill;
        // 留出内边距，避免首行第一个字符贴着 RichTextBox 边缘被裁切。
        _log.Margin = new Padding(6, 4, 4, 4);
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(17, 24, 39);
        _log.ForeColor = Color.FromArgb(226, 232, 240);
        _log.Font = new Font("Consolas", 9.5f);
        _log.BorderStyle = BorderStyle.None;
        _log.ScrollBars = RichTextBoxScrollBars.Vertical;
        logLayout.Controls.Add(logLabel, 0, 0);
        logLayout.Controls.Add(_log, 0, 1);
        logCard.Controls.Add(logLayout);
        main.Controls.Add(logCard, 0, 4);

        root.Controls.Add(main, 0, 1);

        // —— 状态栏 ——
        var statusBar = new Panel
        {
            Dock = DockStyle.Fill, Margin = Padding.Empty,
            BackColor = Color.FromArgb(235, 241, 250)
        };
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Padding = new Padding(20, 0, 12, 0);
        _status.AutoEllipsis = true;
        _status.ForeColor = Color.FromArgb(51, 65, 85);
        _status.Font = UiTheme.Font(9f);
        _status.Text = "状态：就绪";
        statusBar.Controls.Add(_status);
        root.Controls.Add(statusBar, 0, 2);

        Controls.Add(root);
    }

    private static void StyleButton(Button b, Color color)
    {
        b.FlatStyle = FlatStyle.Flat;
        b.FlatAppearance.BorderSize = 0;
        b.BackColor = color;
        b.ForeColor = Color.White;
        b.Font = UiTheme.Font(10f, FontStyle.Bold);
        b.Cursor = Cursors.Hand;
    }

    private void BrowseSource()
    {
        using var ofd = new OpenFileDialog
        {
            Title = "选择 PCL 可执行文件",
            Filter = "可执行程序 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            CheckFileExists = true
        };
        if (ofd.ShowDialog() != DialogResult.OK) return;

        _txtSource.Text = ofd.FileName;
        _txtSource.SelectionStart = 0;
        _txtSource.ScrollToCaret();
        AppendLog($"已选择：{ofd.FileName}", Color.FromArgb(200, 210, 230));
    }

    private void Bypass()
    {
        string src = _txtSource.Text.Trim();
        if (string.IsNullOrWhiteSpace(src) || !File.Exists(src))
        {
            MessageBox.Show("请先选择一个有效的 exe 文件。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dlg = new ConfirmDialog(src);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        bool doBackup = dlg.Selection == ConfirmDialog.Choice.Backup;

        SetBusy(true);
        _log.Clear();

        try
        {
            var progress = new Progress<string>(s => AppendLog(s, Color.FromArgb(190, 205, 225)));
            byte[] image = File.ReadAllBytes(src);
            AppendLog($"读取源文件：{image.Length:N0} 字节", Color.FromArgb(120, 200, 140));

            var result = PatcherEngine.Patch(image, progress);

            foreach (var h in result.Hits)
                AppendLog($"  命中 → {h.Type}::{h.Method}", Color.FromArgb(140, 210, 160));

            if (!result.Success || result.Image is null)
            {
                AppendLog("失败：" + result.Message, Color.FromArgb(255, 120, 120));
                _status.Text = "状态：失败";
                MessageBox.Show(result.Message, "注入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            string? backupPath = null;
            if (doBackup)
            {
                backupPath = src + ".bak";
                if (!File.Exists(backupPath))
                {
                    File.Copy(src, backupPath, false);
                    AppendLog($"已备份原文件：{backupPath}", Color.FromArgb(160, 200, 255));
                }
                else
                {
                    AppendLog($"备份已存在，保留不覆盖：{backupPath}", Color.FromArgb(200, 180, 120));
                }
            }

            File.WriteAllBytes(src, result.Image);

            AppendLog($"已写回原文件：{src}", Color.FromArgb(140, 220, 160));
            AppendLog($"输出大小：{new FileInfo(src).Length:N0} 字节", Color.FromArgb(140, 220, 160));
            _status.Text = $"状态：成功 · 应用 {result.Hits.Count} 处补丁" + (doBackup ? " · 已备份" : " · 已替换原文件");

            string backupLine = doBackup
                ? $"\n\n备份文件：{backupPath}"
                : "\n\n注意：原文件已被直接替换，未创建备份。";
            MessageBox.Show(
                $"绕过正版验证完成！\n\n{result.Message}{backupLine}",
                "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("异常：" + ex, Color.FromArgb(255, 120, 120));
            _status.Text = "状态：异常";
            MessageBox.Show(ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _btnBypass.Enabled = !busy;
        _btnBrowse.Enabled = !busy;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
        if (busy) _status.Text = "状态：正在分析并注入...";
    }

    private void AppendLog(string text, Color color)
    {
        if (_log.InvokeRequired)
        {
            _log.BeginInvoke(new Action(() => AppendLog(text, color)));
            return;
        }
        _log.SelectionStart = _log.TextLength;
        _log.SelectionColor = color;
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine);
        _log.ScrollToCaret();
    }
}
