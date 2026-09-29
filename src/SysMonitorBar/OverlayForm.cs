using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SysMonitorBar;

/// <summary>屏幕顶部正上方的 1~2 行硬件监控悬浮条。</summary>
public sealed class OverlayForm : Form
{
    private AppConfig _cfg;
    private Snapshot _snap;
    private Font _font;
    private bool _dragging;
    private Point _dragStart;
    private int _dragOffsetX, _dragOffsetY;
    private ContextMenuStrip _menu;

    // 逐像素 alpha 的离屏画布
    private Bitmap _layer;

    // 鼠标移入淡出
    private System.Windows.Forms.Timer _hoverTimer;
    private System.Windows.Forms.Timer _fadeTimer;
    private double _curOpacity = 1.0;
    private double _targetOpacity = 1.0;
    private bool _hovering;

    public event Action SettingsRequested;
    public event Action HideRequested;
    public event Action ExitRequested;

    public bool DragMode { get; private set; }

    public OverlayForm(AppConfig cfg, Snapshot snap)
    {
        _cfg = cfg;
        _snap = snap;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Text = "SysMonitorBar";
        MinimizeBox = false;
        MaximizeBox = false;
        DoubleBuffered = true;

        // 悬浮条完全由 UpdateLayeredWindow 推像素，WinForms 自己的绘制路径全部关掉，
        // 否则它会用不透明的 BackColor 把透明区域刷掉。
        SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, false);
        BackColor = Color.Black;
        UpdateStyles();

        BuildMenu();
        ApplyConfig(cfg, snap);   // 内部会 EnsureFont()
        StartHoverTracking();
    }

    // ==================================================================
    //  鼠标移入 → 高强度淡化
    // ==================================================================

    /// <summary>
    /// 注意：悬浮条默认是鼠标穿透的（WS_EX_TRANSPARENT），收不到 MouseEnter/Leave，
    /// 所以这里直接轮询鼠标在屏幕上的位置来判断"是否落在条内"。
    /// </summary>
    private void StartHoverTracking()
    {
        _hoverTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _hoverTimer.Tick += (_, __) => CheckHover();
        _hoverTimer.Start();

        _fadeTimer = new System.Windows.Forms.Timer { Interval = 20 };
        _fadeTimer.Tick += (_, __) => StepFade();
        _fadeTimer.Start();

        _curOpacity = _cfg.Opacity;
        _targetOpacity = _cfg.Opacity;
    }

    private void CheckHover()
    {
        if (IsDisposed || !IsHandleCreated) return;

        // 自愈：WinForms 在某些样式刷新后会丢掉穿透位，这里低频兜底校正一次。
        bool wantCt = _cfg.ClickThrough && !DragMode;
        if (wantCt != _clickThrough) SetClickThrough(wantCt);
        else if (wantCt && !Native.IsClickThrough(Handle))
        {
            Log.Warn($"[悬浮条] 检测到鼠标穿透位丢失，已自动恢复 (ExStyle=0x{Native.GetExStyle(Handle):X8})");
            SetClickThrough(true);
        }

        double want;
        if (!_cfg.HoverFade || DragMode || !Visible)
        {
            _hovering = false;
            want = _cfg.Opacity;
        }
        else
        {
            _hovering = Bounds.Contains(Cursor.Position);
            want = _hovering ? Math.Max(0.0, Math.Min(_cfg.HoverOpacity, _cfg.Opacity)) : _cfg.Opacity;
        }

        if (Math.Abs(want - _targetOpacity) > 0.001) _targetOpacity = want;
    }

    private void StepFade()
    {
        if (IsDisposed) return;
        double diff = _targetOpacity - _curOpacity;
        if (Math.Abs(diff) < 0.004)
        {
            if (_curOpacity != _targetOpacity)
            {
                _curOpacity = _targetOpacity;
                Render();
            }
            return;
        }
        // 每 20ms 走 18%，大约 150ms 完成过渡
        _curOpacity += diff * 0.18;
        Render();
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>当前是否要求鼠标穿透（CreateParams 会据此决定样式位）。</summary>
    private bool _clickThrough = true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00000080;   // WS_EX_TOOLWINDOW  不在 Alt+Tab 出现
            cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE  不抢焦点

            // ⚠ 关键：穿透位必须放进 CreateParams，不能只在建好窗口后 SetWindowLong。
            // WinForms 每次 UpdateStyles() / 重建句柄都会用 CreateParams 整体覆盖扩展样式，
            // 只在外面加一次的写法会被静默抹掉 —— 表现就是悬浮条挡住后面的窗口点不动。
            //
            // WS_EX_LAYERED 现在是无条件需要的：悬浮条走 UpdateLayeredWindow 逐像素 alpha，
            // 没有这个位调用就会直接失败（表现为整条不显示）。
            cp.ExStyle |= 0x00080000;   // WS_EX_LAYERED
            if (_clickThrough)
                cp.ExStyle |= 0x00000020;   // WS_EX_TRANSPARENT 鼠标穿透

            return cp;
        }
    }

    /// <summary>设置鼠标穿透。走 UpdateStyles() 让 CreateParams 生效，再直接写一次兜底。</summary>
    public void SetClickThrough(bool on)
    {
        bool changed = _clickThrough != on;
        _clickThrough = on;
        if (!IsHandleCreated || IsDisposed) return;

        try
        {
            if (changed) UpdateStyles();
            Native.SetClickThrough(Handle, on);
            if (changed)
            {
                Render();                    // UpdateStyles 带 SWP_FRAMECHANGED，重推一帧确认内容还在
                Native.BringToTop(Handle);
                Log.Info($"[悬浮条] 鼠标穿透 = {on}  实际 ExStyle=0x{Native.GetExStyle(Handle):X8}");
            }
        }
        catch (Exception ex) { Log.Warn("设置鼠标穿透失败: " + ex.Message); }
    }

    private void BuildMenu()
    {
        _menu = new ContextMenuStrip();
        _menu.Items.Add(MenuItem("设置…", () => SettingsRequested?.Invoke()));
        _menu.Items.Add(MenuItem("重新加载配置", () =>
        {
            var fresh = AppConfig.Load(_cfg.ConfigPath);
            ApplyConfig(fresh, _snap);
        }));
        _menu.Items.Add(MenuItem("隐藏监控条", () => HideRequested?.Invoke()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(MenuItem("退出", () => ExitRequested?.Invoke()));
        ContextMenuStrip = _menu;
    }

    private static ToolStripMenuItem MenuItem(string text, Action act)
    {
        var mi = new ToolStripMenuItem(text);
        mi.Click += (_, __) => { try { act(); } catch (Exception ex) { Log.Error("菜单动作失败: " + ex.Message); } };
        return mi;
    }

    public void ApplyConfig(AppConfig cfg, Snapshot snap)
    {
        _cfg = cfg;
        _snap = snap;

        EnsureFont();
        _curOpacity = Math.Max(0.05, Math.Min(1.0, cfg.Opacity));
        _targetOpacity = _curOpacity;

        if (IsHandleCreated)
            SetClickThrough(cfg.ClickThrough && !DragMode);

        _wantSize = Size.Empty;   // 强制重新量一次尺寸（字号等参数可能变了）
        Relayout();
        Render();
    }

    public void SetDragMode(bool on)
    {
        DragMode = on;
        if (IsHandleCreated) SetClickThrough(_cfg.ClickThrough && !DragMode);
    }

    public void UpdateSnapshot(Snapshot snap)
    {
        _snap = snap;
        Relayout();
        Render();
    }

    /// <summary>
    /// 按当前配置确保字体是最新的。
    ///
    /// 这里刻意记录"当前字体是用哪组参数建出来的"，而不是拿新配置和旧配置做差异比较——
    /// 因为设置界面应用之后 TrayApp/OverlayForm/SettingsForm 持有的可能是同一个 AppConfig
    /// 对象，自己跟自己比永远等于"没变化"，字体就再也不会重建（表现为改字号点了应用没反应）。
    /// </summary>
    private void EnsureFont()
    {
        string key = _cfg.FontFamily + "\u0001" +
                     _cfg.FontSize.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "\u0001" +
                     _cfg.Bold;
        if (_font != null && _fontKey == key) return;

        _font?.Dispose();
        _font = BarPainter.CreateFont(_cfg);
        _fontKey = key;
        Log.Info($"[悬浮条] 字体已更新：{_cfg.FontFamily} {(_cfg.Bold ? "粗体" : "常规")} {_cfg.FontSize:0.#}pt");
    }

    private string _fontKey;

    // 悬浮条"应该"有多大、在哪 —— 这是唯一的事实来源。
    //
    // 不能拿 WinForms 的 Width/Height/Left/Top 当准：UpdateLayeredWindow 会按我们推过去的
    // 位图尺寸直接改窗口矩形，而 WinForms 缓存的 Bounds 不会跟着更新，两边一旦不一致就会
    // 出现"背景板只画了半截、右边文字被裁掉"的情况。所以尺寸和位置全部自己记账。
    private Size _wantSize = Size.Empty;
    private Point _wantPos = Point.Empty;

    private void Relayout()
    {
        if (IsDisposed || !IsHandleCreated) return;

        var lines = BarPainter.BuildLines(_cfg, _snap);
        Size content;
        using (var g = CreateGraphics())
            content = BarPainter.Measure(g, _font, _cfg, lines);

        var screen = AppConfig.ResolveScreen(_cfg.ScreenDevice);
        var bounds = screen.Bounds;

        int w = Math.Min(content.Width, bounds.Width);
        int h = Math.Max(8, content.Height);

        int x = bounds.Left + (bounds.Width - w) / 2 + _cfg.OffsetX;
        int y = bounds.Top + _cfg.OffsetY;
        x = Math.Max(bounds.Left, Math.Min(x, bounds.Right - w));
        y = Math.Max(bounds.Top, Math.Min(y, bounds.Bottom - h));

        _wantSize = new Size(w, h);
        _wantPos = new Point(x, y);

        // 位置和大小完全交给 UpdateLayeredWindow（见 Render()）。
        //
        // 这里刻意【不】调用 SetBounds：ULW 会按我们推过去的位图尺寸设置窗口矩形，
        // 而 WinForms 缓存的 Bounds 不会同步。两边各设一次的结果就是窗口 746px、
        // 底板只画 583px，表现为"背景板缺了左边一大块、文字挤在右边"。
        // 让 ULW 当唯一的尺寸来源，两者就永远一致。
        Render();
    }

    /// <summary>
    /// 离屏画布：Format32bppPArgb（预乘 alpha）是 UpdateLayeredWindow 要求的格式。
    /// 尺寸以 _wantSize 为准，不用 Width/Height（会被 ULW 改得跟 WinForms 缓存不一致）。
    /// </summary>
    private void EnsureLayer()
    {
        int w = Math.Max(1, _wantSize.Width);
        int h = Math.Max(1, _wantSize.Height);
        if (_layer != null && _layer.Width == w && _layer.Height == h) return;
        var old = _layer;
        _layer = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        old?.Dispose();
    }

    /// <summary>
    /// 把当前内容渲染成一张带透明通道的位图并推给合成器。
    /// 整体不透明度(_curOpacity)在这里乘进每一个像素的 alpha —— 以前是交给 Form.Opacity，
    /// 但那条路和 UpdateLayeredWindow 互斥，只能自己算。
    /// </summary>
    /// <returns>位图是否被合成器接受。</returns>
    private bool Render()
    {
        if (IsDisposed || !IsHandleCreated) return false;
        if (_wantSize.Width <= 0 || _wantSize.Height <= 0) return false;

        try
        {
            EnsureLayer();
            var lines = BarPainter.BuildLines(_cfg, _snap);

            using (var g = Graphics.FromImage(_layer))
            {
                g.Clear(Color.Transparent);          // 先整张铺成全透明
                g.CompositingMode = CompositingMode.SourceOver;
                BarPainter.Paint(g, _font, _cfg, lines,
                    new Rectangle(0, 0, _layer.Width, _layer.Height), _curOpacity);
            }

            return Native.PushLayeredBitmap(Handle, _layer, _wantPos.X, _wantPos.Y);
        }
        catch (Exception ex)
        {
            Log.Warn("[悬浮条] 渲染失败: " + ex.Message);
            return false;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        // UpdateLayeredWindow 之后系统不再让我们靠 WM_PAINT 出图，这里只是兜底。
        Render();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 不刷任何背景，否则会把透明区域涂黑。
    }

    // ---------------- 拖动 ----------------
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && (DragMode || !_cfg.ClickThrough))
        {
            _dragging = true;
            _dragStart = Cursor.Position;
            _dragOffsetX = _cfg.OffsetX;
            _dragOffsetY = _cfg.OffsetY;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var cur = Cursor.Position;
        _cfg.OffsetX = _dragOffsetX + (cur.X - _dragStart.X);
        _cfg.OffsetY = _dragOffsetY + (cur.Y - _dragStart.Y);
        Relayout();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_dragging)
        {
            _dragging = false;
            _cfg.Save();
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Native.MakeToolWindow(Handle);
        SetClickThrough(_cfg.ClickThrough && !DragMode);
        Native.BringToTop(Handle);

        // 构造阶段还没有句柄，Relayout() 会直接返回，所以尺寸必须在这里补算一次。
        // 漏掉这一步的表现是：窗口停在 WinForms 默认的 300x300，内容被裁掉一截。
        Relayout();

        // 启动即自检，防止"穿透悄悄失效、用户点不动"这类问题再次蒙混过关
        bool ok = !_clickThrough || Native.IsClickThrough(Handle);
        Log.Info($"[悬浮条] 创建完成 期望穿透={_clickThrough} 实际={Native.IsClickThrough(Handle)} " +
                 $"ExStyle=0x{Native.GetExStyle(Handle):X8} " + (ok ? "OK" : "异常!"));
        if (!ok) Log.Error("[悬浮条] 鼠标穿透设置失败，悬浮条会挡住后面的窗口！");
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Native.BringToTop(Handle);

        // 首帧自检：推一次位图不等于它一定被合成器接受，这里把结果记下来，
        // 免得出现"进程活着但屏幕上什么都没有"这种没有报错的失败。
        bool pushed = Render();
        Log.Info($"[悬浮条] 首帧渲染 {(pushed ? "成功" : "失败")}  尺寸={_wantSize.Width}x{_wantSize.Height}  " +
                 $"位置={_wantPos.X},{_wantPos.Y}  背景=#{_cfg.BackColor}  " +
                 $"整体不透明度={_curOpacity:0.00}  实际背景不透明度={_cfg.Back.A / 255.0:0.00}");
        if (!pushed) Log.Error("[悬浮条] 首帧渲染失败，悬浮条可能不可见！");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hoverTimer?.Stop(); _hoverTimer?.Dispose();
            _fadeTimer?.Stop(); _fadeTimer?.Dispose();
            _font?.Dispose();
            _layer?.Dispose();
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
