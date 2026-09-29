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
    private Size _lastSize = Size.Empty;
    private ContextMenuStrip _menu;

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

        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
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
                SetOpacity(_curOpacity);
            }
            return;
        }
        // 每 20ms 走 18%，大约 150ms 完成过渡
        _curOpacity += diff * 0.18;
        SetOpacity(_curOpacity);
    }

    private void SetOpacity(double v)
    {
        try
        {
            v = Math.Max(0.0, Math.Min(1.0, v));
            if (Math.Abs(Opacity - v) > 0.002) Opacity = v;
        }
        catch { }
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
            if (Opacity < 1.0 || _clickThrough)
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
                ApplyRegion();               // UpdateStyles 带 SWP_FRAMECHANGED，确认圆角还在
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
        _curOpacity = Math.Max(0.2, Math.Min(1.0, cfg.Opacity));
        _targetOpacity = _curOpacity;
        SetOpacity(_curOpacity);
        // 背景必须是不透明色（整体透明度由 Opacity 控制），否则 WinForms 会抛异常
        var b = cfg.Back;
        BackColor = Color.FromArgb(255, b.R, b.G, b.B);

        if (IsHandleCreated)
            SetClickThrough(cfg.ClickThrough && !DragMode);

        _lastSize = Size.Empty;
        Relayout();
        Invalidate();
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
        Invalidate();
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

        var newSize = new Size(w, h);
        if (newSize != _lastSize)
        {
            _lastSize = newSize;
            SetBounds(x, y, w, h);
            ApplyRegion();
        }
        else if (Left != x || Top != y)
        {
            Location = new Point(x, y);
        }
    }

    private void ApplyRegion()
    {
        int r = Math.Max(0, Math.Min(_cfg.CornerRadius, Math.Min(Width, Height) / 2));
        if (r <= 0) { Region = null; return; }
        using var path = BarPainter.RoundedPath(new Rectangle(0, 0, Width, Height), r);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var lines = BarPainter.BuildLines(_cfg, _snap);
        BarPainter.Paint(e.Graphics, _font, _cfg, lines, ClientRectangle, true);
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
        _lastSize = Size.Empty;
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hoverTimer?.Stop(); _hoverTimer?.Dispose();
            _fadeTimer?.Stop(); _fadeTimer?.Dispose();
            _font?.Dispose();
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}
