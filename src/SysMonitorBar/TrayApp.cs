using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SysMonitorBar;

/// <summary>托盘驻留 + 悬浮条 + 采集服务的总协调器。</summary>
public sealed class TrayApp : ApplicationContext
{
    private readonly SensorService _sensors = new();
    private AppConfig _cfg;
    private OverlayForm _overlay;
    private NotifyIcon _tray;
    private System.Windows.Forms.Timer _uiTimer;
    private long _lastVersion = -1;
    private SettingsForm _settings;

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    public SensorService Sensors => _sensors;

    public TrayApp(AppConfig cfg)
    {
        _cfg = cfg;
        _cfg.Save();

        _sensors.RefreshMs = _cfg.RefreshMs;
        _sensors.Net.SelectedNicId = _cfg.NicId ?? "";
        _sensors.Start();

        _overlay = new OverlayForm(_cfg, _sensors.Current);
        _overlay.SettingsRequested += ShowSettings;
        _overlay.HideRequested += () => SetVisible(false);
        _overlay.ExitRequested += ExitApp;

        BuildTray();

        _uiTimer = new System.Windows.Forms.Timer { Interval = 200 };
        _uiTimer.Tick += (_, __) => PumpUi();
        _uiTimer.Start();

        if (_cfg.Show) _overlay.Show();
        else _overlay.Hide();

        Log.Info("程序已启动");
        if (!_sensors.Elevated)
            Log.Warn("未以管理员身份运行：CPU 温度与风扇转速可能不可用");
    }

    // ------------------------------------------------------------------
    private void PumpUi()
    {
        _topmostTick++;
        if (_cfg.Show && _overlay != null && !_overlay.IsDisposed && _overlay.Visible && _topmostTick % 15 == 0)
            Native.BringToTop(_overlay.Handle);

        var snap = _sensors.Current;
        if (snap.Version == _lastVersion) return;
        _lastVersion = snap.Version;

        if (_overlay != null && !_overlay.IsDisposed)
        {
            _overlay.UpdateSnapshot(snap);
            if (_cfg.Show && !_overlay.Visible) _overlay.Show();
        }
        UpdateTrayText(snap);
    }

    private int _topmostTick;

    /// <summary>等消息循环跑起来之后再打开设置窗口（避免窗口被系统当成最小化状态）。</summary>
    public void OpenSettingsWhenReady()
    {
        var t = new System.Windows.Forms.Timer { Interval = 250 };
        t.Tick += (_, __) =>
        {
            t.Stop();
            t.Dispose();
            ShowSettings();
        };
        t.Start();
    }

    private void UpdateTrayText(Snapshot snap)
    {
        if (_tray == null) return;
        try
        {
            var parts = new List<string>();
            foreach (var key in new[] { M.CpuTemp, M.CpuLoad, M.GpuTemp, M.GpuLoad })
            {
                var r = snap.Get(key);
                if (r.Available) parts.Add(MetricCatalog.Render("{label}{value}{unit}", r).Replace("CPU", "CPU ").Replace("GPU", "GPU "));
                if (parts.Count >= 4) break;
            }
            _tray.Text = parts.Count > 0
                ? ("顶部监控条\n" + string.Join("  ", parts)).Substring(0, Math.Min(63, ("顶部监控条\n" + string.Join("  ", parts)).Length))
                : "顶部监控条";
        }
        catch { }
    }

    // ------------------------------------------------------------------
    private void BuildTray()
    {
        _tray = new NotifyIcon
        {
            Icon = MakeIcon(),
            Text = "顶部硬件监控条",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };

        var menu = _tray.ContextMenuStrip;
        menu.Items.Add(Item("显示 / 隐藏监控条", () => SetVisible(!_cfg.Show)));
        menu.Items.Add(Item("设置…", ShowSettings));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("字号 增大", () => StepFontSize(+1f)));
        menu.Items.Add(Item("字号 减小", () => StepFontSize(-1f)));
        menu.Items.Add(Item("字号 重置", () => SetFontSize(11f)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("拖动调整位置", ToggleDragMode));
        menu.Items.Add(Item("立即刷新", () => { _lastVersion = -1; PumpUi(); }));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("打开配置文件夹", OpenConfigFolder));
        menu.Items.Add(Item("重新加载配置", ReloadConfig));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Item("退出", ExitApp));

        _tray.DoubleClick += (_, __) => ShowSettings();
    }

    /// <summary>托盘里快速调字号（设置界面里也能精确调）。</summary>
    private void StepFontSize(float delta) => SetFontSize(_cfg.FontSize + delta);

    private void SetFontSize(float size)
    {
        size = Math.Max(6f, Math.Min(40f, (float)Math.Round(size * 2) / 2f));
        if (Math.Abs(size - _cfg.FontSize) < 0.01f) return;
        _cfg.FontSize = size;
        _cfg.Save();
        try
        {
            Log.Info($"字号调整为 {size:0.#}");
            _overlay.ApplyConfig(_cfg, _sensors.Current);
            if (_cfg.Show)
            {
                _overlay.Show();
                Native.BringToTop(_overlay.Handle);
            }
        }
        catch (Exception ex) { Log.Warn("调整字号失败: " + ex.Message); }
    }

    private static ToolStripMenuItem Item(string text, Action act)
    {
        var mi = new ToolStripMenuItem(text);
        mi.Click += (_, __) => { try { act(); } catch (Exception ex) { Log.Error("菜单动作失败: " + ex); } };
        return mi;
    }

    private void ToggleDragMode()
    {
        if (_overlay == null || _overlay.IsDisposed) return;
        bool on = !_overlay.DragMode;
        _overlay.SetDragMode(on);
        if (on)
        {
            if (!_overlay.Visible) SetVisible(true);
            Native.BringToTop(_overlay.Handle);
            _tray.ShowBalloonTip(3000, "拖动调整位置", "现在可以直接用鼠标拖动监控条，拖好后再次点击本菜单项锁定。", ToolTipIcon.Info);
        }
        else
        {
            _cfg.Save();
            _tray.ShowBalloonTip(2000, "位置已锁定", "鼠标穿透已恢复。", ToolTipIcon.Info);
        }
    }

    public void SetVisible(bool visible)
    {
        _cfg.Show = visible;
        if (_overlay == null || _overlay.IsDisposed) return;
        if (visible)
        {
            _overlay.Show();
            Native.BringToTop(_overlay.Handle);
        }
        else _overlay.Hide();
        _cfg.Save();
    }

    private void OpenConfigFolder()
    {
        try
        {
            string dir = Path.GetDirectoryName(_cfg.ConfigPath);
            if (string.IsNullOrEmpty(dir)) dir = AppContext.BaseDirectory;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + dir + "\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) { Log.Warn("打开目录失败: " + ex.Message); }
    }

    private void ReloadConfig()
    {
        var fresh = AppConfig.Load(_cfg.ConfigPath);
        ApplyNewConfig(fresh);
    }

    public void ApplyNewConfig(AppConfig fresh)
    {
        // 克隆一份再接管：设置界面之后还会继续改它自己的工作副本，
        // 如果这里直接持有同一个对象，各种"新旧对比"逻辑会全部失效。
        _cfg = fresh.CloneConfig();
        _cfg.Save();
        _sensors.RefreshMs = _cfg.RefreshMs;
        _sensors.Net.SelectedNicId = _cfg.NicId ?? "";
        _sensors.Net.ResetBaseline();
        _overlay.ApplyConfig(_cfg, _sensors.Current);
        if (_cfg.Show) { _overlay.Show(); Native.BringToTop(_overlay.Handle); }
        else _overlay.Hide();
        _lastVersion = -1;
    }

    public void ShowSettings()
    {
        try
        {
            if (_settings != null && !_settings.IsDisposed)
            {
                _settings.Activate();
                _settings.BringToFront();
                return;
            }

            _settings = new SettingsForm(_cfg, _sensors, ApplyNewConfig);
            _settings.FormClosed += (_, __) => _settings = null;
            _settings.Show();
            _settings.Activate();
            Log.Info($"[设置] Show 后 state={_settings.WindowState} vis={_settings.Visible} bounds={_settings.Bounds}");
        }
        catch (Exception ex)
        {
            Log.Error("打开设置失败: " + ex);
            MessageBox.Show("打开设置窗口失败：\n" + ex.Message, "顶部硬件监控条",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApp()
    {
        try { _cfg.Save(); } catch { }
        try { _uiTimer?.Stop(); } catch { }
        try { _tray.Visible = false; } catch { }
        try { _overlay?.Close(); } catch { }
        try { _sensors.Stop(); } catch { }
        try { _tray?.Dispose(); } catch { }
        ExitThread();
    }

    // ------------------------------------------------------------------
    /// <summary>运行时绘制托盘图标：深色圆角底 + 青色仪表弧。</summary>
    private static Icon MakeIcon()
    {
        try
        {
            int s = 32;
            using var bmp = new Bitmap(s, s);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var bg = new SolidBrush(Color.FromArgb(255, 20, 24, 32)))
                {
                    using var path = new GraphicsPath();
                    path.AddArc(1, 1, 10, 10, 180, 90);
                    path.AddArc(s - 11, 1, 10, 10, 270, 90);
                    path.AddArc(s - 11, s - 11, 10, 10, 0, 90);
                    path.AddArc(1, s - 11, 10, 10, 90, 90);
                    path.CloseFigure();
                    g.FillPath(bg, path);
                }
                using var pen = new Pen(Color.FromArgb(255, 111, 211, 255), 3.2f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                g.DrawArc(pen, 7, 8, 18, 18, 200, 140);
                using var pen2 = new Pen(Color.FromArgb(255, 120, 235, 170), 3.0f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };
                g.DrawLine(pen2, 16, 17, 22, 11);
            }

            IntPtr h = bmp.GetHicon();
            try
            {
                using var tmp = Icon.FromHandle(h);
                return (Icon)tmp.Clone();
            }
            finally { DestroyIcon(h); }
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _uiTimer?.Dispose(); } catch { }
            try { _tray?.Dispose(); } catch { }
            try { _overlay?.Dispose(); } catch { }
            try { _sensors?.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}
