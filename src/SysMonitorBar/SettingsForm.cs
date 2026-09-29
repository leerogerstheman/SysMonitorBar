using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SysMonitorBar;

/// <summary>设置界面里的一条可选指标。</summary>
internal sealed class MetricRow
{
    public string Key = "";
    public string Name = "";
    public string Label = "";
    public string Template = "{label} {value}{unit}";
    public string Group = "";
    public string Hint = "";
    public override string ToString() => Name;
}

/// <summary>列表里显示的一行（带文本缓存，因为配置行会频繁重建）。</summary>
internal sealed class LineEntry
{
    public ItemConfig Cfg;
    public string Text = "";
    public override string ToString() => Text;
}

internal sealed class PreviewPanel : Control
{
    public AppConfig Cfg;
    public Snapshot Snap;
    public Font BarFont;

    public PreviewPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Color.FromArgb(60, 64, 72);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var b = new LinearGradientBrush(ClientRectangle,
                   Color.FromArgb(74, 82, 96), Color.FromArgb(38, 42, 50), 90f))
            g.FillRectangle(b, ClientRectangle);

        // 顶部示意线
        using (var p = new Pen(Color.FromArgb(70, 255, 255, 255)))
            g.DrawLine(p, 0, 1, Width, 1);

        if (Cfg == null || BarFont == null) return;

        var lines = BarPainter.BuildLines(Cfg, Snap);
        var size = BarPainter.Measure(g, BarFont, Cfg, lines);
        int w = Math.Min(size.Width, Width - 8);
        int h = Math.Min(size.Height, Height - 8);
        int x = (Width - w) / 2 + Cfg.OffsetX;
        int y = Math.Max(2, Math.Min(4 + Cfg.OffsetY, Height - h - 2));
        x = Math.Max(2, Math.Min(x, Width - w - 2));

        var rect = new Rectangle(x, y, w, h);
        var st = g.Save();
        g.SetClip(rect);
        BarPainter.Paint(g, BarFont, Cfg, lines, rect, true);
        g.Restore(st);
    }
}

public sealed class SettingsForm : Form
{
    private readonly AppConfig _draft;
    private readonly SensorService _sensors;
    private readonly Action<AppConfig> _onApply;

    private Snapshot _snap;
    private List<MetricRow> _rows = new();
    private Font _barFont;
    private bool _loading = true;
    private System.Windows.Forms.Timer _timer;

    // 顶部预览
    private PreviewPanel _preview;

    // 内容页
    private TextBox _search;
    private ListView _avail;
    private ListBox _line1, _line2;
    private TextBox _labelBox, _tplBox;
    private Label _editTitle;

    // 外观页
    private ComboBox _fontBox;
    private NumericUpDown _fontSize, _radius, _padX, _padY, _lineSpacing;
    private CheckBox _bold;
    private Control _textColorBtn, _accentColorBtn, _backColorBtn;
    private TrackBar _opacity;
    private Label _opacityLabel;
    private TextBox _separator;
    private CheckBox _hoverFade;
    private NumericUpDown _hoverOpacity;

    // 行为页
    private NumericUpDown _refreshMs, _offX, _offY;
    private CheckBox _clickThrough, _hideUnavailable, _autoStart;
    private ComboBox _screenBox, _nicBox;
    private Label _elevLabel;

    public SettingsForm(AppConfig cfg, SensorService sensors, Action<AppConfig> onApply)
    {
        _draft = cfg.CloneConfig();
        _sensors = sensors;
        _onApply = onApply;
        _snap = sensors.Current;
        _barFont = BarPainter.CreateFont(_draft);

        Text = "顶部硬件监控条 - 设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(1010, 720);
        MaximizeBox = true;
        ShowInTaskbar = true;
        Font = new Font("Microsoft YaHei UI", 9f);
        Icon = SystemIcons.Application;

        BuildUi();
        LoadFromConfig();
        RefreshAvailableList();
        RefreshCurrentValues();
        _loading = false;

        _timer = new System.Windows.Forms.Timer { Interval = 1000 };
        _timer.Tick += (_, __) =>
        {
            _snap = _sensors.Current;
            RefreshCurrentValues();
            RefreshLineTexts();
            _preview.Snap = _snap;
            _preview.Invalidate();
        };
        _timer.Start();

        FormClosing += (_, __) =>
        {
            _timer?.Stop();
            _timer?.Dispose();
            _barFont?.Dispose();
        };
    }

    /// <summary>定时刷新行列表里的预览文本（不改动编辑框，避免打断输入）。</summary>
    private void RefreshLineTexts()
    {
        _loading = true;
        try { RebuildLineLists(); }
        finally { _loading = false; }
    }

    // ==================================================================
    private void BuildUi()
    {
        // ---------- 预览 ----------
        _preview = new PreviewPanel
        {
            Location = new Point(12, 10),
            Size = new Size(ClientSize.Width - 24, 62),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Cfg = _draft,
            Snap = _snap,
            BarFont = _barFont,
        };

        var tabs = new TabControl
        {
            Location = new Point(12, 80),
            Size = new Size(ClientSize.Width - 24, ClientSize.Height - 80 - 52),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
        };

        tabs.TabPages.Add(BuildContentTab());
        tabs.TabPages.Add(BuildAppearanceTab());
        tabs.TabPages.Add(BuildBehaviorTab());
        Controls.Add(_preview);
        Controls.Add(tabs);

        // ---------- 底部按钮 ----------
        var btnDefault = new Button { Text = "恢复默认", Size = new Size(90, 32) };
        btnDefault.Click += (_, __) =>
        {
            var def = AppConfig.CreateDefault();
            CopyInto(def, _draft);
            _draft.Items.Clear();
            foreach (var it in def.Items) _draft.Items.Add(it);
            LoadFromConfig();
            RebuildLineLists();
            RefreshPreviewFont();
            UpdatePreview();
        };

        var btnCancel = new Button { Text = "取消", Size = new Size(90, 32) };
        btnCancel.Click += (_, __) => Close();

        var btnApply = new Button { Text = "保存并应用", Size = new Size(110, 32) };
        btnApply.Click += (_, __) =>
        {
            try
            {
                _onApply(_draft);
                MessageBox.Show(this, "已保存并应用。", "顶部硬件监控条",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "应用失败：" + ex.Message, "顶部硬件监控条",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };

        int by = ClientSize.Height - 44;
        btnApply.Location = new Point(ClientSize.Width - 12 - btnApply.Width, by);
        btnCancel.Location = new Point(btnApply.Left - 8 - btnCancel.Width, by);
        btnDefault.Location = new Point(btnCancel.Left - 8 - btnDefault.Width, by);
        foreach (var b in new[] { btnDefault, btnCancel, btnApply })
        {
            b.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            Controls.Add(b);
        }
        AcceptButton = btnApply;
    }

    // ==================================================================
    private TabPage _pageContent;
    private GroupBox _g1, _g2;
    private Label _lblAvail, _lblOrder, _lblName, _lblTpl, _hint;
    private Button _btnAdd1, _btnAdd2;
    private readonly List<Button> _lineBtns1 = new();
    private readonly List<Button> _lineBtns2 = new();

    private TabPage BuildContentTab()
    {
        var page = new TabPage("显示内容") { Padding = new Padding(8) };
        _pageContent = page;

        _lblAvail = new Label
        {
            Text = "① 在下面选中要显示的数据　② 再点右下按钮决定放哪一行",
            Location = new Point(6, 6),
            AutoSize = true,
        };
        _search = new TextBox { Location = new Point(6, 28), Width = 300, PlaceholderText = "搜索：cpu / 温度 / 风扇 / 网速 …" };
        _search.TextChanged += (_, __) => RefreshAvailableList();

        _avail = new ListView
        {
            Location = new Point(6, 56),
            Size = new Size(430, 400),
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            UseCompatibleStateImageBehavior = false,
        };
        _avail.Columns.Add("数据", 230);
        _avail.Columns.Add("当前值", 110);
        _avail.Columns.Add("来源", 84);
        _avail.DoubleClick += (_, __) => AddSelectedToLine(CurrentTargetLine());
        _avail.SelectedIndexChanged += (_, __) => UpdateAddButtons();

        _lblOrder = new Label { Text = "显示顺序", AutoSize = true };

        _g1 = new GroupBox { Text = "第一行" };
        _line1 = new ListBox { IntegralHeight = false };
        _g1.Controls.Add(_line1);
        AddLineButtons(_g1, _line1, 0, _lineBtns1);

        _g2 = new GroupBox { Text = "第二行" };
        _line2 = new ListBox { IntegralHeight = false };
        _g2.Controls.Add(_line2);
        AddLineButtons(_g2, _line2, 1, _lineBtns2);

        _btnAdd1 = new Button { Text = "→ 加入第一行", Size = new Size(140, 28), Enabled = false };
        _btnAdd1.Click += (_, __) => AddSelectedToLine(0);
        _btnAdd2 = new Button { Text = "→ 加入第二行", Size = new Size(140, 28), Enabled = false };
        _btnAdd2.Click += (_, __) => AddSelectedToLine(1);

        _editTitle = new Label
        {
            Text = "选中左侧数据 → 点按钮决定放哪一行",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };
        _lblName = new Label { Text = "显示名", AutoSize = true };
        _labelBox = new TextBox { Width = 130 };
        _lblTpl = new Label { Text = "模板", AutoSize = true };
        _tplBox = new TextBox { Width = 250 };
        _hint = new Label
        {
            Text = "模板占位符：{label} 名称  {value} 数值  {unit} 单位",
            AutoSize = true,
            ForeColor = Color.DimGray,
        };

        _line1.SelectedIndexChanged += (_, __) => { if (_line1.Focused || _line1.SelectedIndex >= 0) SyncEditFrom(_line1, _line2); };
        _line2.SelectedIndexChanged += (_, __) => { if (_line2.Focused || _line2.SelectedIndex >= 0) SyncEditFrom(_line2, _line1); };
        _labelBox.TextChanged += (_, __) => ApplyEditToSelection();
        _tplBox.TextChanged += (_, __) => ApplyEditToSelection();

        page.Controls.AddRange(new Control[]
        {
            _lblAvail, _search, _avail, _lblOrder, _g1, _g2,
            _btnAdd1, _btnAdd2, _editTitle, _lblName, _labelBox, _lblTpl, _tplBox, _hint,
        });

        // TabPage 在加入 TabControl 之前 ClientSize 是无效值，必须在尺寸确定后再排版
        page.SizeChanged += (_, __) => LayoutContentTab();
        LayoutContentTab();
        return page;
    }

    private void LayoutContentTab()
    {
        if (_pageContent == null) return;
        int W = _pageContent.ClientSize.Width;
        int H = _pageContent.ClientSize.Height;
        if (W < 200 || H < 200) return;   // 还没拿到真实尺寸

        int leftW = Math.Max(280, Math.Min(460, W * 42 / 100));
        _avail.SetBounds(6, 56, leftW, H - 66);
        _search.Width = Math.Max(150, leftW - 10);

        int rightX = leftW + 18;
        int rightW = Math.Max(320, W - rightX - 8);
        _lblOrder.Location = new Point(rightX, 6);

        int gH = Math.Max(110, (H - 160) / 2);
        _g1.SetBounds(rightX, 26, rightW, gH);
        _g2.SetBounds(rightX, 26 + gH + 10, rightW, gH);
        LayoutLineBox(_g1, _line1, _lineBtns1);
        LayoutLineBox(_g2, _line2, _lineBtns2);

        int rowY = H - 96;
        _btnAdd1.Location = new Point(rightX, rowY);
        _btnAdd2.Location = new Point(rightX + 148, rowY);
        _editTitle.Location = new Point(rightX + 300, rowY + 6);

        int editY = H - 58;
        _lblName.Location = new Point(rightX, editY + 4);
        _labelBox.Location = new Point(rightX + 56, editY);
        _lblTpl.Location = new Point(rightX + 198, editY + 4);
        _tplBox.Location = new Point(rightX + 238, editY);
        _hint.Location = new Point(rightX + 496, editY + 4);
    }

    private static void LayoutLineBox(GroupBox box, ListBox list, List<Button> btns)
    {
        int bw = 120;
        int bx = Math.Max(10, box.ClientSize.Width - bw - 12);
        list.SetBounds(8, 22, Math.Max(80, bx - 12), Math.Max(40, box.ClientSize.Height - 32));
        for (int i = 0; i < btns.Count; i++)
            btns[i].Location = new Point(bx, 24 + i * 28);
    }

    private void AddLineButtons(GroupBox box, ListBox list, int line, List<Button> store)
    {
        var other = new ContextMenuStrip();

        string[] texts =
        {
            "上移", "下移", "移除",
            line == 0 ? "▼ 移到第二行" : "▲ 移到第一行",
        };

        for (int i = 0; i < texts.Length; i++)
        {
            var b = new Button { Text = texts[i], Size = new Size(120, 26), Location = new Point(200, 24 + i * 28) };
            int k = i;
            b.Click += (_, __) =>
            {
                if (k == 0) MoveInLine(list, line, -1);
                else if (k == 1) MoveInLine(list, line, +1);
                else if (k == 2) RemoveFromLine(list, line);
                else MoveToOtherLine(list, line);
            };
            box.Controls.Add(b);
            store.Add(b);
        }

        // 右键菜单，同样的操作再来一份（更符合直觉）
        void Add(string text, Action act)
        {
            var mi = new ToolStripMenuItem(text);
            mi.Click += (_, __) => { try { act(); } catch (Exception ex) { Log.Warn("菜单动作失败: " + ex.Message); } };
            other.Items.Add(mi);
        }
        Add("上移", () => MoveInLine(list, line, -1));
        Add("下移", () => MoveInLine(list, line, +1));
        other.Items.Add(new ToolStripSeparator());
        Add(line == 0 ? "移到第二行" : "移到第一行", () => MoveToOtherLine(list, line));
        other.Items.Add(new ToolStripSeparator());
        Add("从显示中移除", () => RemoveFromLine(list, line));
        list.ContextMenuStrip = other;
    }

    /// <summary>把选中的一项从当前行搬到另一行。</summary>
    private void MoveToOtherLine(ListBox list, int line)
    {
        if (list.SelectedIndex < 0) return;
        if (list.SelectedItem is not LineEntry entry) return;

        int newLine = line == 0 ? 1 : 0;
        entry.Cfg.Line = newLine;

        RebuildLineLists();

        // 在目标行里把刚搬过去的那一项选中，让用户看到它去哪了
        var target = newLine == 1 ? _line2 : _line1;
        for (int i = 0; i < target.Items.Count; i++)
        {
            if (target.Items[i] is LineEntry e && ReferenceEquals(e.Cfg, entry.Cfg))
            {
                target.Focus();
                target.SelectedIndex = i;
                break;
            }
        }
        UpdatePreview();
    }

    // ==================================================================
    private TabPage BuildAppearanceTab()
    {
        var page = new TabPage("外观") { Padding = new Padding(8) };

        int y = 16;
        Label L(string t, int x, int yy) => new() { Text = t, Location = new Point(x, yy + 3), AutoSize = true };

        _fontBox = new ComboBox { Location = new Point(90, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        try
        {
            using var col = new InstalledFontCollection();
            foreach (var f in col.Families.OrderBy(f => f.Name))
                _fontBox.Items.Add(f.Name);
        }
        catch { }
        if (_fontBox.Items.Count == 0) _fontBox.Items.Add("Microsoft YaHei UI");
        _fontBox.SelectedIndexChanged += (_, __) => { _draft.FontFamily = _fontBox.Text; RefreshPreviewFont(); };

        _fontSize = new NumericUpDown { Location = new Point(430, y), Width = 70, Minimum = 6, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m };
        _fontSize.ValueChanged += (_, __) => { _draft.FontSize = (float)_fontSize.Value; RefreshPreviewFont(); };

        _bold = new CheckBox { Text = "粗体", Location = new Point(520, y), AutoSize = true };
        _bold.CheckedChanged += (_, __) => { _draft.Bold = _bold.Checked; RefreshPreviewFont(); };

        page.Controls.AddRange(new Control[] { L("字体", 20, y), _fontBox, L("字号", 366, y), _fontSize, _bold });
        y += 42;

        _textColorBtn = ColorButton("文字颜色", 20, y, () => _draft.TextColor, c => _draft.TextColor = c);
        _accentColorBtn = ColorButton("名称颜色", 250, y, () => _draft.AccentColor, c => _draft.AccentColor = c);
        _backColorBtn = ColorButton("背景颜色", 480, y, () => _draft.BackColor, c => _draft.BackColor = c);
        page.Controls.AddRange(new Control[] { _textColorBtn, _accentColorBtn, _backColorBtn });
        y += 46;

        page.Controls.Add(L("不透明度", 20, y));
        _opacity = new TrackBar
        {
            Location = new Point(90, y - 6),
            Width = 300,
            Minimum = 20,
            Maximum = 100,
            TickFrequency = 10,
        };
        _opacityLabel = new Label { Location = new Point(400, y + 3), AutoSize = true };
        _opacity.ValueChanged += (_, __) =>
        {
            _draft.Opacity = _opacity.Value / 100.0;
            _opacityLabel.Text = _opacity.Value + "%";
            UpdatePreview();
        };
        page.Controls.AddRange(new Control[] { _opacity, _opacityLabel });
        y += 52;

        _radius = Num("圆角", 20, y, 0, 30, _draft.CornerRadius, v => { _draft.CornerRadius = (int)v; UpdatePreview(); }, page, out _);
        _padX = Num("左右内边距", 250, y, 0, 80, _draft.PaddingX, v => { _draft.PaddingX = (int)v; UpdatePreview(); }, page, out _);
        _padY = Num("上下内边距", 480, y, 0, 60, _draft.PaddingY, v => { _draft.PaddingY = (int)v; UpdatePreview(); }, page, out _);
        y += 42;

        _lineSpacing = Num("行间距", 20, y, 0, 40, _draft.LineSpacing, v => { _draft.LineSpacing = (int)v; UpdatePreview(); }, page, out _);
        page.Controls.Add(L("分隔符", 250, y));
        _separator = new TextBox { Location = new Point(320, y), Width = 140, Text = _draft.Separator };
        _separator.TextChanged += (_, __) => { _draft.Separator = _separator.Text; UpdatePreview(); };
        page.Controls.Add(_separator);
        y += 42;

        // ---------- 鼠标移入淡出 ----------
        _hoverFade = new CheckBox { Text = "鼠标移入时强烈淡化（鼠标穿透下也能生效）", Location = new Point(20, y), AutoSize = true };
        _hoverFade.CheckedChanged += (_, __) =>
        {
            _draft.HoverFade = _hoverFade.Checked;
            _hoverOpacity.Enabled = _hoverFade.Checked;
        };
        page.Controls.Add(_hoverFade);
        y += 32;

        _hoverOpacity = new NumericUpDown
        {
            Location = new Point(190, y),
            Width = 80,
            Minimum = 0,
            Maximum = 100,
            Increment = 5,
            Enabled = _draft.HoverFade,
        };
        _hoverOpacity.ValueChanged += (_, __) => _draft.HoverOpacity = (double)_hoverOpacity.Value / 100.0;
        page.Controls.AddRange(new Control[] { L("淡化后不透明度(%)", 20, y), _hoverOpacity });
        y += 46;

        var note = new Label
        {
            Text = "提示：背景留出的空隙由「分隔符」控制，默认三个空格；改成  │  这类竖线更好看。",
            Location = new Point(20, y),
            AutoSize = true,
            ForeColor = Color.DimGray,
        };
        page.Controls.Add(note);
        return page;
    }

    private Control ColorButton(string text, int x, int y, Func<string> get, Action<string> set)
    {
        var host = new Panel { Location = new Point(x, y), Size = new Size(214, 32) };
        var b = new Button { Text = text + "…", Location = new Point(0, 0), Size = new Size(110, 30) };
        var swatch = new Panel { Location = new Point(118, 4), Size = new Size(90, 24), BorderStyle = BorderStyle.FixedSingle };

        void Sync()
        {
            var c = AppConfig.ParseColor(get(), Color.White);
            swatch.BackColor = Color.FromArgb(255, c.R, c.G, c.B);
        }

        b.Click += (_, __) =>
        {
            using var dlg = new ColorDialog { FullOpen = true, AnyColor = true, Color = AppConfig.ParseColor(get(), Color.White) };
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                set(AppConfig.ColorToHex(Color.FromArgb(255, dlg.Color.R, dlg.Color.G, dlg.Color.B)));
                Sync();
                UpdatePreview();
            }
        };

        Sync();
        host.Controls.Add(b);
        host.Controls.Add(swatch);
        _swatches.Add((swatch, Sync));
        return host;
    }

    private readonly List<(Panel panel, Action sync)> _swatches = new();

    private NumericUpDown Num(string label, int x, int y, int min, int max, int value,
        Action<decimal> onChange, Control parent, out Label lbl)
    {
        lbl = new Label { Text = label, Location = new Point(x, y + 3), AutoSize = true };
        var n = new NumericUpDown
        {
            Location = new Point(x + 100, y),
            Width = 80,
            Minimum = min,
            Maximum = max,
            Value = Math.Max(min, Math.Min(max, value)),
        };
        n.ValueChanged += (_, __) => onChange(n.Value);
        parent.Controls.Add(lbl);
        parent.Controls.Add(n);
        return n;
    }

    // ==================================================================
    private TabPage BuildBehaviorTab()
    {
        var page = new TabPage("位置与行为") { Padding = new Padding(8) };
        int y = 20;
        Label L(string t, int x, int yy) => new() { Text = t, Location = new Point(x, yy + 3), AutoSize = true };

        _refreshMs = new NumericUpDown { Location = new Point(150, y), Width = 90, Minimum = 250, Maximum = 10000, Increment = 250 };
        _refreshMs.ValueChanged += (_, __) => _draft.RefreshMs = (int)_refreshMs.Value;
        page.Controls.AddRange(new Control[] { L("刷新间隔(毫秒)", 20, y), _refreshMs });
        y += 40;

        _screenBox = new ComboBox { Location = new Point(120, y), Width = 300, DropDownStyle = ComboBoxStyle.DropDownList };
        for (int i = 0; i < Screen.AllScreens.Length; i++)
        {
            var s = Screen.AllScreens[i];
            _screenBox.Items.Add($"显示器 {i + 1}：{s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? "（主）" : "")}   {s.DeviceName}");
        }
        _screenBox.SelectedIndexChanged += (_, __) =>
        {
            int i = _screenBox.SelectedIndex;
            if (i >= 0 && i < Screen.AllScreens.Length)
                _draft.ScreenDevice = Screen.AllScreens[i].DeviceName;
            UpdatePreview();
        };
        page.Controls.AddRange(new Control[] { L("所在显示器", 20, y), _screenBox });
        y += 40;

        _offX = new NumericUpDown { Location = new Point(120, y), Width = 90, Minimum = -4000, Maximum = 4000 };
        _offX.ValueChanged += (_, __) => { _draft.OffsetX = (int)_offX.Value; UpdatePreview(); };
        _offY = new NumericUpDown { Location = new Point(320, y), Width = 90, Minimum = -4000, Maximum = 4000 };
        _offY.ValueChanged += (_, __) => { _draft.OffsetY = (int)_offY.Value; UpdatePreview(); };
        page.Controls.AddRange(new Control[] { L("水平偏移", 20, y), _offX, L("垂直偏移", 220, y), _offY });
        y += 40;

        _clickThrough = new CheckBox { Text = "鼠标穿透（不挡点击，位置由托盘菜单「拖动调整位置」调整）", Location = new Point(120, y), AutoSize = true };
        _clickThrough.CheckedChanged += (_, __) => _draft.ClickThrough = _clickThrough.Checked;
        page.Controls.Add(_clickThrough);
        y += 32;

        _hideUnavailable = new CheckBox { Text = "自动隐藏读不到的数据（如未提权时的 CPU 温度）", Location = new Point(120, y), AutoSize = true };
        _hideUnavailable.CheckedChanged += (_, __) => { _draft.HideUnavailable = _hideUnavailable.Checked; UpdatePreview(); };
        page.Controls.Add(_hideUnavailable);
        y += 32;

        _autoStart = new CheckBox { Text = "开机自动启动", Location = new Point(120, y), AutoSize = true };
        _autoStart.CheckedChanged += (_, __) =>
        {
            if (_loading) return;
            if (AutoStart.Set(_autoStart.Checked, _sensors.Elevated, out string msg))
                Log.Info("开机自启: " + msg);
            else
                MessageBox.Show(this, msg, "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        page.Controls.Add(_autoStart);
        y += 40;

        _nicBox = new ComboBox { Location = new Point(120, y), Width = 380, DropDownStyle = ComboBoxStyle.DropDownList };
        _nicBox.Items.Add("自动（选流量最大的网卡）");
        _nicBox.Items.Add("全部网卡合计");
        foreach (var n in NetMonitor.ScanInterfaces())
            _nicBox.Items.Add(n.Name);
        _nicBox.SelectedIndexChanged += (_, __) =>
        {
            int i = _nicBox.SelectedIndex;
            if (i == 0) _draft.NicId = "";
            else if (i == 1) _draft.NicId = "*";
            else
            {
                var list = NetMonitor.ScanInterfaces();
                _draft.NicId = (i - 2 >= 0 && i - 2 < list.Count) ? list[i - 2].Id : "";
            }
        };
        page.Controls.AddRange(new Control[] { L("统计网卡", 20, y), _nicBox });
        y += 48;

        _elevLabel = new Label { Location = new Point(20, y), AutoSize = true, MaximumSize = new Size(900, 0) };
        if (_sensors.Elevated)
        {
            _elevLabel.ForeColor = Color.SeaGreen;
            _elevLabel.Text = "✓ 当前以管理员身份运行：CPU 温度、风扇转速等需要内核驱动的数据可正常读取。";
        }
        else
        {
            _elevLabel.ForeColor = Color.FromArgb(200, 100, 0);
            _elevLabel.Text = "⚠ 当前不是管理员：CPU 温度与风扇转速读不到（GPU 数据不受影响）。\n" +
                              "请用程序目录里的「启动(管理员).cmd」启动，或点下面的按钮立即提权重启。";
            var btnElev = new Button { Text = "以管理员身份重新启动", Location = new Point(20, y + 56), Size = new Size(190, 32) };
            btnElev.Click += (_, __) =>
            {
                if (AutoStart.RestartElevated(out string err))
                {
                    Log.Info("已请求提权重启，本进程退出");
                    if (Application.OpenForms.Count > 0) Application.Exit();
                }
                else
                {
                    MessageBox.Show(this, "提权失败：" + err + "\n\n请手动右键程序 → 以管理员身份运行。",
                        "顶部硬件监控条", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };
            page.Controls.Add(btnElev);
        }
        page.Controls.Add(_elevLabel);
        return page;
    }

    // ==================================================================
    private void LoadFromConfig()
    {
        _loading = true;
        try
        {
            _fontBox.Text = _draft.FontFamily;
            if (_fontBox.SelectedIndex < 0 && _fontBox.Items.Contains(_draft.FontFamily))
                _fontBox.SelectedItem = _draft.FontFamily;
            _fontSize.Value = (decimal)Math.Max(6, Math.Min(40, _draft.FontSize));
            _bold.Checked = _draft.Bold;

            _opacity.Value = (int)Math.Round(Math.Max(0.2, Math.Min(1.0, _draft.Opacity)) * 100);
            _opacityLabel.Text = _opacity.Value + "%";
            _radius.Value = Math.Max(0, Math.Min(30, _draft.CornerRadius));
            _padX.Value = Math.Max(0, Math.Min(80, _draft.PaddingX));
            _padY.Value = Math.Max(0, Math.Min(60, _draft.PaddingY));
            _lineSpacing.Value = Math.Max(0, Math.Min(40, _draft.LineSpacing));
            _separator.Text = _draft.Separator;
            _hoverFade.Checked = _draft.HoverFade;
            _hoverOpacity.Value = (decimal)Math.Max(0, Math.Min(100, Math.Round(_draft.HoverOpacity * 100)));
            _hoverOpacity.Enabled = _draft.HoverFade;
            foreach (var (p, sync) in _swatches) sync();

            _refreshMs.Value = Math.Max(250, Math.Min(10000, _draft.RefreshMs));
            int si = 0;
            var all = Screen.AllScreens;
            for (int i = 0; i < all.Length; i++)
                if (string.Equals(all[i].DeviceName, _draft.ScreenDevice, StringComparison.OrdinalIgnoreCase)) { si = i; break; }
            if (string.IsNullOrEmpty(_draft.ScreenDevice))
                for (int i = 0; i < all.Length; i++) if (all[i].Primary) { si = i; break; }
            _screenBox.SelectedIndex = Math.Max(0, Math.Min(all.Length - 1, si));
            _offX.Value = Math.Max(-4000, Math.Min(4000, _draft.OffsetX));
            _offY.Value = Math.Max(-4000, Math.Min(4000, _draft.OffsetY));
            _clickThrough.Checked = _draft.ClickThrough;
            _hideUnavailable.Checked = _draft.HideUnavailable;
            _autoStart.Checked = AutoStart.IsEnabled();
            _nicBox.SelectedIndex = 0;

            RebuildLineLists();
            UpdatePreview();
        }
        finally { _loading = false; }
    }

    private static void CopyInto(AppConfig src, AppConfig dst)
    {
        var keepPath = dst.ConfigPath;
        var json = System.Text.Json.JsonSerializer.Serialize(src);
        var copy = System.Text.Json.JsonSerializer.Deserialize<AppConfig>(json);
        dst.FontFamily = copy.FontFamily;
        dst.FontSize = copy.FontSize;
        dst.Bold = copy.Bold;
        dst.TextColor = copy.TextColor;
        dst.AccentColor = copy.AccentColor;
        dst.BackColor = copy.BackColor;
        dst.CornerRadius = copy.CornerRadius;
        dst.PaddingX = copy.PaddingX;
        dst.PaddingY = copy.PaddingY;
        dst.LineSpacing = copy.LineSpacing;
        dst.Opacity = copy.Opacity;
        dst.Separator = copy.Separator;
        dst.RefreshMs = copy.RefreshMs;
        dst.ScreenDevice = copy.ScreenDevice;
        dst.OffsetX = copy.OffsetX;
        dst.OffsetY = copy.OffsetY;
        dst.ClickThrough = copy.ClickThrough;
        dst.HideUnavailable = copy.HideUnavailable;
        dst.HoverFade = copy.HoverFade;
        dst.HoverOpacity = copy.HoverOpacity;
        dst.ConfigPath = keepPath;
    }

    private void RefreshPreviewFont()
    {
        _barFont?.Dispose();
        _barFont = BarPainter.CreateFont(_draft);
        _preview.BarFont = _barFont;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        _preview.Cfg = _draft;
        _preview.Snap = _snap;
        _preview.Invalidate();
    }

    // ==================================================================
    private void RefreshAvailableList()
    {
        string filter = (_search?.Text ?? "").Trim();
        _avail.BeginUpdate();
        try
        {
            _avail.Items.Clear();
            _avail.Groups.Clear();

            _rows = BuildRows();
            var groups = new Dictionary<string, ListViewGroup>();

            foreach (var row in _rows)
            {
                if (filter.Length > 0)
                {
                    bool hit = row.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
                            || row.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)
                            || row.Key.Contains(filter, StringComparison.OrdinalIgnoreCase);
                    if (!hit) continue;
                }

                if (!groups.TryGetValue(row.Group, out var grp))
                {
                    grp = new ListViewGroup(row.Group) { HeaderAlignment = HorizontalAlignment.Left };
                    groups[row.Group] = grp;
                    _avail.Groups.Add(grp);
                }

                var lvi = new ListViewItem(row.Name) { Tag = row, Group = grp };
                lvi.SubItems.Add("");
                lvi.SubItems.Add(row.Hint);
                if (!row.Hint.StartsWith("内置")) lvi.ForeColor = Color.FromArgb(60, 60, 60);
                _avail.Items.Add(lvi);
            }
        }
        finally { _avail.EndUpdate(); }
        RefreshCurrentValues();
        UpdateAddButtons();
    }

    private List<MetricRow> BuildRows()
    {
        var rows = new List<MetricRow>();
        foreach (var d in MetricCatalog.Builtins)
        {
            rows.Add(new MetricRow
            {
                Key = d.Key,
                Name = d.Name,
                Label = d.Label,
                Template = d.Template,
                Group = d.Group,
                Hint = "内置",
            });
        }

        var seen = new HashSet<string>();
        foreach (var s in _snap?.Sensors ?? new List<SensorEntry>())
        {
            if (!seen.Add(s.Key)) continue;
            rows.Add(new MetricRow
            {
                Key = s.Key,
                Name = s.SensorName,
                Label = s.SensorName,
                Template = "{label} {value}{unit}",
                Group = s.HardwareName,
                Hint = "传感器",
            });
        }
        return rows;
    }

    private void RefreshCurrentValues()
    {
        if (_avail == null) return;
        _avail.BeginUpdate();
        try
        {
            foreach (ListViewItem lvi in _avail.Items)
            {
                if (lvi.Tag is not MetricRow row) continue;
                var r = _snap?.Get(row.Key);
                bool ok = r != null && r.Available;
                lvi.SubItems[1].Text = ok ? MetricCatalog.Render("{value}{unit}", r) : "—";
                lvi.SubItems[1].ForeColor = ok ? Color.SeaGreen : Color.Gray;
                lvi.SubItems[2].Text = row.Hint;
            }
        }
        finally { _avail.EndUpdate(); }
        UpdatePreview();
    }

    // ==================================================================
    private void AddSelectedToLine(int line)
    {
        if (_avail.SelectedItems.Count == 0) return;
        if (_avail.SelectedItems[0].Tag is not MetricRow row) return;

        if (_draft.Items.Any(i => i.Key == row.Key))
        {
            // 已存在则移动行并选中
            var exist = _draft.Items.First(i => i.Key == row.Key);
            exist.Line = line;
            exist.Enabled = true;
        }
        else
        {
            _draft.Items.Add(new ItemConfig
            {
                Key = row.Key,
                Label = row.Label,
                Template = row.Template,
                Line = line,
                Enabled = true,
            });
        }
        RebuildLineLists();
        UpdatePreview();
    }

    private int CurrentTargetLine()
    {
        if (_line2 != null && _line2.Focused) return 1;
        if (_line1 != null && _line1.Focused) return 0;
        return 0;
    }

    private void RebuildLineLists()
    {
        int s1 = _line1.SelectedIndex, s2 = _line2.SelectedIndex;
        _line1.BeginUpdate();
        _line2.BeginUpdate();
        try
        {
            _line1.Items.Clear();
            _line2.Items.Clear();
            foreach (var it in _draft.Items)
            {
                if (!it.Enabled) continue;
                var box = it.Line == 1 ? _line2 : _line1;
                box.Items.Add(MakeEntry(it));
            }
            if (s1 >= 0 && s1 < _line1.Items.Count) _line1.SelectedIndex = s1;
            if (s2 >= 0 && s2 < _line2.Items.Count) _line2.SelectedIndex = s2;
        }
        finally
        {
            _line1.EndUpdate();
            _line2.EndUpdate();
        }
    }

    private LineEntry MakeEntry(ItemConfig it)
    {
        var r = _snap?.Get(it.Key);
        string text;
        if (r != null && r.Available)
            text = MetricCatalog.Render(it.Template, new Reading
            {
                Label = string.IsNullOrWhiteSpace(it.Label) ? r.Label : it.Label,
                Value = r.Value,
                Unit = r.Unit,
            });
        else
            text = (string.IsNullOrWhiteSpace(it.Label) ? MetricCatalog.Find(it.Key)?.Name ?? it.Key : it.Label) + "  (当前不可用)";
        return new LineEntry { Cfg = it, Text = text };
    }

    private ListBox FocusedLineList()
    {
        if (_line2.Focused && _line2.SelectedIndex >= 0) return _line2;
        if (_line1.Focused && _line1.SelectedIndex >= 0) return _line1;
        return _line1.SelectedIndex >= 0 ? _line1 : (_line2.SelectedIndex >= 0 ? _line2 : null);
    }

    private void MoveInLine(ListBox list, int line, int delta)
    {
        int idx = list.SelectedIndex;
        if (idx < 0) return;

        var flat = new List<int>();
        for (int i = 0; i < _draft.Items.Count; i++)
            if (_draft.Items[i].Enabled && _draft.Items[i].Line == line) flat.Add(i);

        int target = idx + delta;
        if (target < 0 || target >= flat.Count) return;

        int a = flat[idx], b = flat[target];
        (_draft.Items[a], _draft.Items[b]) = (_draft.Items[b], _draft.Items[a]);

        RebuildLineLists();
        list.SelectedIndex = target;
        UpdatePreview();
    }

    private void RemoveFromLine(ListBox list, int line)
    {
        if (list.SelectedIndex < 0) return;
        var entry = list.Items[list.SelectedIndex] as LineEntry;
        if (entry == null) return;
        _draft.Items.Remove(entry.Cfg);
        RebuildLineLists();
        UpdatePreview();
    }

    private void SyncEditFrom(ListBox current, ListBox other)
    {
        if (_loading) return;
        if (other.SelectedIndex >= 0)
        {
            _loading = true;
            other.SelectedIndex = -1;
            _loading = false;
        }
        var entry = current.SelectedItem as LineEntry;
        if (entry == null) { _editTitle.Text = "选中行可编辑显示名与模板"; return; }

        _loading = true;
        try
        {
            _labelBox.Text = entry.Cfg.Label;
            _tplBox.Text = entry.Cfg.Template;
            var r = _snap?.Get(entry.Cfg.Key);
            _editTitle.Text = "正在编辑：" + (MetricCatalog.Find(entry.Cfg.Key)?.Name ?? r?.Label ?? entry.Cfg.Key);
        }
        finally { _loading = false; }
    }

    private void UpdateAddButtons()
    {
        bool has = _avail != null && _avail.SelectedItems.Count > 0;
        if (_btnAdd1 != null) _btnAdd1.Enabled = has;
        if (_btnAdd2 != null) _btnAdd2.Enabled = has;

        if (_editTitle == null) return;
        if (!has)
        {
            _editTitle.Text = "选中左侧数据 → 点按钮决定放哪一行";
            return;
        }
        string name = _avail.SelectedItems[0].Tag is MetricRow row ? row.Name : _avail.SelectedItems[0].Text;
        bool inLine1 = _draft.Items.Any(i => i.Key == (_avail.SelectedItems[0].Tag as MetricRow)?.Key && i.Line == 0);
        _editTitle.Text = $"已选中 {name}（当前在第{(inLine1 ? "一" : "二")}行）";
    }

    private void ApplyEditToSelection()
    {
        if (_loading) return;
        var list = FocusedLineList();
        if (list == null) return;
        if (list.SelectedItem is not LineEntry entry) return;

        entry.Cfg.Label = _labelBox.Text;
        entry.Cfg.Template = _tplBox.Text;
        int idx = list.SelectedIndex;
        list.Items[idx] = MakeEntry(entry.Cfg);
        list.SelectedIndex = idx;
        UpdatePreview();
    }
}
