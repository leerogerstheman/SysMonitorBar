using System.Globalization;
using System.Security.Principal;
using LibreHardwareMonitor.Hardware;

namespace SysMonitorBar;

public sealed class SensorEntry
{
    public string Key { get; set; } = "";
    public string HardwareName { get; set; } = "";
    public string SensorName { get; set; } = "";
    public string SensorType { get; set; } = "";

    public override string ToString() => HardwareName + " · " + SensorName;
}

public sealed class Snapshot
{
    public Dictionary<string, Reading> Values { get; } = new(StringComparer.Ordinal);
    public List<SensorEntry> Sensors { get; } = new();
    public int RefreshRate { get; set; }
    public long Version;

    public Reading Get(string key)
    {
        if (key != null && Values.TryGetValue(key, out var r)) return r;
        return new Reading { Key = key, Label = key ?? "", Available = false };
    }
}

/// <summary>数值格式化工具（尽量让文本宽度稳定，避免悬浮条左右抖动）。</summary>
public static class Fmt
{
    public static string S(double v, int width)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
        string t = v.ToString("0", CultureInfo.InvariantCulture);
        return t.PadLeft(width);
    }

    public static string S1(double v, int width)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) v = 0;
        string t = v.ToString("0.0", CultureInfo.InvariantCulture);
        return t.PadLeft(width);
    }

    /// <summary>速率 → 固定 "999 KB/s" 宽度的 9 字符串。</summary>
    public static string Speed(double bps)
    {
        if (double.IsNaN(bps) || bps < 0) bps = 0;
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bps >= GB) return (bps / GB).ToString("0.00", CultureInfo.InvariantCulture).PadLeft(4) + " GB/s";
        if (bps >= MB) return (bps / MB).ToString("0.00", CultureInfo.InvariantCulture).PadLeft(4) + " MB/s";
        double kb = bps / KB;
        string n = kb >= 100 ? kb.ToString("0", CultureInfo.InvariantCulture)
                             : kb.ToString("0.0", CultureInfo.InvariantCulture);
        return n.PadLeft(4) + " KB/s";
    }

    public static string Uptime(TimeSpan t)
        => t.TotalDays >= 1
            ? $"{(int)t.TotalDays}天{t.Hours:00}:{t.Minutes:00}"
            : $"{t.Hours:00}:{t.Minutes:00}:{t.Seconds:00}";

    public static string Weekday(DateTime d)
        => "周" + "日一二三四五六"[(int)d.DayOfWeek];
}

/// <summary>后台轮询所有硬件数据源，产出最新快照。</summary>
public sealed class SensorService : IDisposable
{
    private Computer _computer;
    private Thread _thread;
    private volatile bool _stop;
    private readonly NetMonitor _net = new();
    private readonly DisplayFpsMonitor _fps = new();
    private readonly object _gate = new();

    private Snapshot _current = new();
    private int _tick;
    private bool _lhmFailed;

    public bool Elevated { get; }
    public NetMonitor Net => _net;
    public DisplayFpsMonitor Fps => _fps;

    public Snapshot Current
    {
        get { lock (_gate) return _current; }
    }

    public SensorService()
    {
        Elevated = CheckElevated();
    }

    private static bool CheckElevated()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public void Start()
    {
        _thread = new Thread(Loop) { IsBackground = true, Name = "SensorService" };
        _thread.Start();
        _fps.Start();
    }

    public void Stop()
    {
        _stop = true;
        try { _thread?.Join(3000); } catch { }
        _fps.Stop();
    }

    public void Dispose() => Stop();

    /// <summary>打开 LibreHardwareMonitor（幂等）。</summary>
    private void EnsureOpen()
    {
        if (_computer != null) return;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsControllerEnabled = true,
            IsStorageEnabled = false,
            IsNetworkEnabled = false,
            IsBatteryEnabled = false,
            IsPsuEnabled = false,
        };
        _computer.Open();
        Log.Info("LibreHardwareMonitor 已打开 (管理员=" + Elevated + ")");
    }

    private void Loop()
    {
        try
        {
            EnsureOpen();
        }
        catch (Exception ex)
        {
            _lhmFailed = true;
            Log.Error("LibreHardwareMonitor 初始化失败: " + ex);
        }

        while (!_stop)
        {
            int interval = Math.Max(250, RefreshMs);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log.Error("采样异常: " + ex.Message);
            }

            int rest = interval - (int)sw.ElapsedMilliseconds;
            if (rest < 60) rest = 60;
            int slept = 0;
            while (!_stop && slept < rest) { Thread.Sleep(25); slept += 25; }
        }

        try { _computer?.Close(); } catch { }
    }

    public int RefreshMs { get; set; } = 1000;

    private void Tick()
    {
        var snap = new Snapshot();
        snap.RefreshRate = Native.GetPrimaryRefreshRate();

        var list = new List<SensorEntry>();
        var values = new Dictionary<string, double?>(StringComparer.Ordinal);
        var units = new Dictionary<string, string>(StringComparer.Ordinal);
        var byType = new Dictionary<SensorType, List<(ISensor sen, string hwName, HardwareType hwType)>>();

        if (!_lhmFailed)
        {
            try
            {
                _computer.Accept(_visitor);
                Collect(_computer.Hardware, null, list, values, units, byType);
            }
            catch (Exception ex)
            {
                Log.Warn("更新硬件失败: " + ex.Message);
            }
        }
        snap.Sensors.AddRange(list);

        // ---------------- 内置指标 ----------------
        ResolvePresets(snap, values, units, byType);

        // ---------------- 内存 ----------------
        if (Native.TryGetMemory(out ulong totalPhys, out ulong availPhys))
        {
            double totalGb = totalPhys / 1073741824.0;
            double usedGb = (totalPhys - availPhys) / 1073741824.0;
            double pct = totalPhys > 0 ? (totalPhys - availPhys) * 100.0 / totalPhys : 0;

            snap.Values[M.MemUsed] = new Reading
            {
                Key = M.MemUsed,
                Label = "内存",
                Value = usedGb.ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4) + "/" + totalGb.ToString("0.0", CultureInfo.InvariantCulture),
                Unit = "GB",
                Available = true,
            };
            snap.Values[M.MemPct] = new Reading
            {
                Key = M.MemPct,
                Label = "内存",
                Value = Fmt.S(pct, 2),
                Unit = "%",
                Available = true,
            };
        }

        // ---------------- 网络 ----------------
        _net.Poll();
        snap.Values[M.NetDown] = new Reading { Key = M.NetDown, Label = "下载", Value = Fmt.Speed(_net.DownBps), Unit = "", Available = true };
        snap.Values[M.NetUp] = new Reading { Key = M.NetUp, Label = "上传", Value = Fmt.Speed(_net.UpBps), Unit = "", Available = true };
        snap.Values[M.NetTotal] = new Reading { Key = M.NetTotal, Label = "网络", Value = Fmt.Speed(_net.DownBps + _net.UpBps), Unit = "", Available = true };

        // ---------------- 显示 / 帧率 ----------------
        snap.Values[M.DispHz] = new Reading
        {
            Key = M.DispHz,
            Label = "刷新率",
            Value = Fmt.S(snap.RefreshRate, 3),
            Unit = "Hz",
            Available = snap.RefreshRate > 0,
        };

        bool fpsOk = _fps.Available && _fps.Fps >= 0;
        double fps = fpsOk ? _fps.Fps : 0;
        // 桌面复制的计数可能高于面板刷新能力；显示器实际呈现的帧率不会超过刷新率，故按刷新率封顶
        if (fpsOk && snap.RefreshRate > 0 && fps > snap.RefreshRate) fps = snap.RefreshRate;
        snap.Values[M.DispFps] = new Reading
        {
            Key = M.DispFps,
            Label = "帧率",
            Value = fpsOk ? Fmt.S(Math.Round(fps), 3) : "--",
            Unit = "FPS",
            Available = fpsOk,
        };

        // ---------------- 时间 ----------------
        var now = DateTime.Now;
        snap.Values[M.TimeNow] = new Reading { Key = M.TimeNow, Label = "时间", Value = now.ToString("HH:mm:ss"), Unit = "", Available = true };
        snap.Values[M.TimeDate] = new Reading { Key = M.TimeDate, Label = "日期", Value = now.ToString("yyyy-MM-dd ") + Fmt.Weekday(now), Unit = "", Available = true };
        TimeSpan up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        snap.Values[M.TimeUptime] = new Reading { Key = M.TimeUptime, Label = "已开机", Value = Fmt.Uptime(up), Unit = "", Available = true };

        // ---------------- 动态传感器 ----------------
        foreach (var se in list)
        {
            if (!values.TryGetValue(se.Key, out var v) || v == null) continue;
            string name = se.SensorName;
            bool isTemp = se.SensorType == "Temperature";
            bool isLoad = se.SensorType == "Load";
            string unit = units.TryGetValue(se.Key, out var u) ? u : "";

            string val;
            if (isTemp) val = Fmt.S(v.Value, 2);
            else if (isLoad) val = Fmt.S(v.Value, 2);
            else if (se.SensorType == "Fan") val = Fmt.S(v.Value, 4);
            else if (se.SensorType == "Control") val = Fmt.S(v.Value, 2);
            else if (se.SensorType == "Clock") val = Fmt.S(v.Value, 4);
            else if (se.SensorType == "Power") val = Fmt.S1(v.Value, 4);
            else if (se.SensorType == "SmallData" || se.SensorType == "Data") val = Fmt.S1(v.Value, 5);
            else val = Fmt.S1(v.Value, 4);

            snap.Values[se.Key] = new Reading
            {
                Key = se.Key,
                Label = name,
                Value = val,
                Unit = unit,
                Available = true,
            };
        }

        snap.Version = ++_tick;
        lock (_gate) _current = snap;
    }

    // =====================================================================
    //  枚举硬件
    // =====================================================================
    private static void Collect(IList<IHardware> hws, string parentName, List<SensorEntry> list,
        Dictionary<string, double?> values, Dictionary<string, string> units,
        Dictionary<SensorType, List<(ISensor, string, HardwareType)>> byType)
    {
        foreach (var hw in hws)
        {
            string hwName = string.IsNullOrEmpty(parentName) ? hw.Name : parentName + " / " + hw.Name;
            try
            {
                foreach (var s in hw.Sensors)
                {
                    string key = M.SensorPrefix + s.Identifier;
                    list.Add(new SensorEntry
                    {
                        Key = key,
                        HardwareName = hw.Name,
                        SensorName = s.Name,
                        SensorType = s.SensorType.ToString(),
                    });
                    values[key] = s.Value;
                    units[key] = UnitOf(s.SensorType);

                    if (!byType.TryGetValue(s.SensorType, out var lst))
                    {
                        lst = new List<(ISensor, string, HardwareType)>();
                        byType[s.SensorType] = lst;
                    }
                    lst.Add((s, hw.Name, hw.HardwareType));
                }
            }
            catch (Exception ex) { Log.Warn("读取传感器失败: " + ex.Message); }

            try
            {
                var subs = hw.SubHardware;
                if (subs != null)
                {
                    var subList = subs.ToList();
                    if (subList.Count > 0)
                        Collect(subList, hwName, list, values, units, byType);
                }
            }
            catch { }
        }
    }

    private static string UnitOf(SensorType t) => t switch
    {
        SensorType.Temperature => "°C",
        SensorType.Load => "%",
        SensorType.Control => "%",
        SensorType.Fan => "RPM",
        SensorType.Clock => "MHz",
        SensorType.Power => "W",
        SensorType.Voltage => "V",
        SensorType.Current => "A",
        SensorType.Frequency => "Hz",
        SensorType.Throughput => "B/s",
        SensorType.Level => "%",
        SensorType.Factor => "",
        SensorType.Data => "GB",
        SensorType.SmallData => "MB",
        SensorType.Energy => "mWh",
        SensorType.TimeSpan => "s",
        SensorType.Humidity => "%",
        _ => "",
    };

    // =====================================================================
    //  内置指标 → 传感器 自动匹配
    // =====================================================================
    private static void ResolvePresets(Snapshot snap,
        Dictionary<string, double?> values, Dictionary<string, string> units,
        Dictionary<SensorType, List<(ISensor sen, string hwName, HardwareType hwType)>> byType)
    {
        List<(ISensor sen, string hwName, HardwareType hwType)> T(SensorType t)
            => byType.TryGetValue(t, out var l) ? l : new List<(ISensor, string, HardwareType)>();

        var cpus = T(SensorType.Load).Where(x => x.hwType == HardwareType.Cpu).ToList();
        var gpuOrder = new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel };

        HardwareType? gpuKind = null;
        foreach (var k in gpuOrder)
        {
            if (T(SensorType.Temperature).Any(x => x.hwType == k) || T(SensorType.Load).Any(x => x.hwType == k))
            {
                gpuKind = k;
                break;
            }
        }

        // ---------- CPU 温度 ----------
        var cpuTemps = T(SensorType.Temperature).Where(x => x.hwType == HardwareType.Cpu).ToList();
        var pick = cpuTemps.FirstOrDefault(x => x.sen.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));
        if (pick.sen == null) pick = cpuTemps.FirstOrDefault(x => x.sen.Name.Contains("Tctl", StringComparison.OrdinalIgnoreCase));
        if (pick.sen == null) pick = cpuTemps.FirstOrDefault(x => x.sen.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
        if (pick.sen == null && cpuTemps.Count > 0) pick = cpuTemps[0];
        if (pick.sen != null)
            snap.Values[M.CpuTemp] = Make(M.CpuTemp, "CPU温度", pick.sen, values, units, "°C", 2);

        // CPU 平均核心温度作为补充（当只有单核温度时）
        if (pick.sen == null && cpuTemps.Count > 0)
        {
            double avg = cpuTemps.Average(x => x.sen.Value ?? 0);
            snap.Values[M.CpuTemp] = new Reading { Key = M.CpuTemp, Label = "CPU温度", Value = Fmt.S(avg, 2), Unit = "°C", Available = true };
        }

        // 兜底：LibreHardwareMonitor 读不到时（未提权 / 驱动加载失败），用 ACPI 热区顶上。
        // 实测这个性能计数器不需要管理员权限。
        if (!snap.Values.TryGetValue(M.CpuTemp, out var curTemp) || !curTemp.Available)
        {
            double tz = ThermalZone.ReadCelsius();
            if (!double.IsNaN(tz))
            {
                snap.Values[M.CpuTemp] = new Reading
                {
                    Key = M.CpuTemp,
                    Label = "CPU温度",
                    Value = Fmt.S(tz, 2),
                    Unit = "°C",
                    Available = true,
                };
            }
        }

        // ---------- CPU 占用 ----------
        var cpuLoad = cpus.FirstOrDefault(x => x.sen.Name.Contains("Total", StringComparison.OrdinalIgnoreCase));
        if (cpuLoad.sen == null && cpus.Count > 0) cpuLoad = cpus[0];
        if (cpuLoad.sen != null)
            snap.Values[M.CpuLoad] = Make(M.CpuLoad, "CPU占用", cpuLoad.sen, values, units, "%", 2);

        // ---------- CPU 频率 ----------
        var clocks = T(SensorType.Clock).Where(x => x.hwType == HardwareType.Cpu && x.sen.Value > 0).ToList();
        if (clocks.Count > 0)
        {
            double mhz = clocks.Max(x => x.sen.Value ?? 0);
            double ghz = mhz / 1000.0;
            snap.Values[M.CpuClock] = new Reading { Key = M.CpuClock, Label = "CPU频率", Value = ghz.ToString("0.00", CultureInfo.InvariantCulture).PadLeft(4), Unit = "GHz", Available = true };
        }

        // ---------- CPU 功耗 ----------
        var cpuPwr = T(SensorType.Power).Where(x => x.hwType == HardwareType.Cpu).ToList();
        var pw = cpuPwr.FirstOrDefault(x => x.sen.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));
        if (pw.sen == null && cpuPwr.Count > 0) pw = cpuPwr[0];
        if (pw.sen != null)
        {
            var rd = Make(M.CpuPower, "CPU功耗", pw.sen, values, units, "W", 4);
            // 未提权时 LHM 读不到 MSR，功耗恒为 0 —— 视为不可用，让界面自动隐藏
            if ((pw.sen.Value ?? 0) <= 0.05) rd.Available = false;
            snap.Values[M.CpuPower] = rd;
        }

        // ---------- 风扇（CPU / 机箱） ----------
        var fans = T(SensorType.Fan).Where(x => x.sen.Value.HasValue).ToList();
        var cpuFan = fans.FirstOrDefault(x => x.sen.Name.Contains("CPU", StringComparison.OrdinalIgnoreCase));
        if (cpuFan.sen == null && fans.Count > 0) cpuFan = fans[0];
        if (cpuFan.sen != null)
            snap.Values[M.CpuFan] = Make(M.CpuFan, "CPU风扇", cpuFan.sen, values, units, "RPM", 4);

        // ---------- GPU ----------
        if (gpuKind.HasValue)
        {
            var hk = gpuKind.Value;
            var gTemps = T(SensorType.Temperature).Where(x => x.hwType == hk).ToList();
            var gLoads = T(SensorType.Load).Where(x => x.hwType == hk).ToList();

            var gt = gTemps.FirstOrDefault(x => x.sen.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase));
            if (gt.sen == null) gt = gTemps.FirstOrDefault(x => x.sen.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
            if (gt.sen == null && gTemps.Count > 0) gt = gTemps[0];
            if (gt.sen != null)
                snap.Values[M.GpuTemp] = Make(M.GpuTemp, "GPU温度", gt.sen, values, units, "°C", 2);

            var ghs = gTemps.FirstOrDefault(x => x.sen.Name.Contains("Hot Spot", StringComparison.OrdinalIgnoreCase)
                                              || x.sen.Name.Contains("Hotspot", StringComparison.OrdinalIgnoreCase));
            if (ghs.sen != null)
                snap.Values[M.GpuHotspot] = Make(M.GpuHotspot, "GPU热点", ghs.sen, values, units, "°C", 2);

            var gl = gLoads.FirstOrDefault(x => x.sen.Name.Equals("GPU Core", StringComparison.OrdinalIgnoreCase));
            if (gl.sen == null) gl = gLoads.FirstOrDefault(x => x.sen.Name.Contains("Core", StringComparison.OrdinalIgnoreCase));
            if (gl.sen == null && gLoads.Count > 0) gl = gLoads[0];
            if (gl.sen != null)
                snap.Values[M.GpuLoad] = Make(M.GpuLoad, "GPU占用", gl.sen, values, units, "%", 2);

            // GPU 风扇（百分比）
            var gctrl = T(SensorType.Control).Where(x => x.hwType == hk).ToList();
            var gf = gctrl.FirstOrDefault(x => x.sen.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase));
            if (gf.sen != null)
                snap.Values[M.GpuFan] = Make(M.GpuFan, "GPU风扇", gf.sen, values, units, "%", 3);

            // GPU 风扇（转/分）
            var gfan = T(SensorType.Fan).Where(x => x.hwType == hk).ToList();
            var gfr = gfan.FirstOrDefault(x => x.sen.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase));
            if (gfr.sen == null && gfan.Count > 0) gfr = gfan[0];
            if (gfr.sen != null)
                snap.Values[M.GpuFanRpm] = Make(M.GpuFanRpm, "GPU风扇", gfr.sen, values, units, "RPM", 4);

            // GPU 功耗
            var gpw = T(SensorType.Power).Where(x => x.hwType == hk).ToList();
            var gp = gpw.FirstOrDefault(x => x.sen.Name.Contains("Package", StringComparison.OrdinalIgnoreCase));
            if (gp.sen == null) gp = gpw.FirstOrDefault(x => x.sen.Name.Contains("Power", StringComparison.OrdinalIgnoreCase));
            if (gp.sen == null && gpw.Count > 0) gp = gpw[0];
            if (gp.sen != null)
            {
                var rd = Make(M.GpuPower, "GPU功耗", gp.sen, values, units, "W", 4);
                if ((gp.sen.Value ?? 0) <= 0.05) rd.Available = false;
                snap.Values[M.GpuPower] = rd;
            }

            // 显存
            var datas = T(SensorType.SmallData).Concat(T(SensorType.Data)).Where(x => x.hwType == hk).ToList();
            var used = datas.FirstOrDefault(x => x.sen.Name.Replace(" ", "").ToLowerInvariant().Contains("memoryused")
                                              || x.sen.Name.Replace(" ", "").ToLowerInvariant().Contains("dedicatedmemoryused"));
            var tot = datas.FirstOrDefault(x => x.sen.Name.Replace(" ", "").ToLowerInvariant().Contains("memorytotal")
                                             || x.sen.Name.Replace(" ", "").ToLowerInvariant().Contains("dedicatedmemorytotal"));
            if (used.sen != null)
            {
                double uMb = used.sen.Value ?? 0;
                double tMb = tot.sen?.Value ?? 0;
                bool usedGb = units.TryGetValue(M.SensorPrefix + used.sen.Identifier, out var uu) && uu == "GB";
                if (usedGb) { uMb *= 1024; tMb *= 1024; }

                string text = tMb > 0
                    ? (uMb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4) + "/" + (tMb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture)
                    : (uMb / 1024.0).ToString("0.0", CultureInfo.InvariantCulture).PadLeft(4);
                snap.Values[M.GpuVram] = new Reading { Key = M.GpuVram, Label = "显存", Value = text, Unit = "GB", Available = true };

                if (tMb > 0)
                    snap.Values[M.GpuVramPct] = new Reading
                    {
                        Key = M.GpuVramPct,
                        Label = "显存",
                        Value = Fmt.S(uMb * 100.0 / tMb, 2),
                        Unit = "%",
                        Available = true,
                    };
            }
        }
    }

    private static Reading Make(string key, string label, ISensor sen,
        Dictionary<string, double?> values, Dictionary<string, string> units,
        string defaultUnit, int width)
    {
        double v = sen.Value ?? 0;
        string unit = units.TryGetValue(M.SensorPrefix + sen.Identifier, out var u) && !string.IsNullOrEmpty(u)
            ? u : defaultUnit;
        return new Reading
        {
            Key = key,
            Label = label,
            Value = Fmt.S(v, width),
            Unit = unit,
            Available = sen.Value.HasValue,
        };
    }

    private readonly UpdateVisitor _visitor = new();

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);

        public void VisitHardware(IHardware hardware)
        {
            try { hardware.Update(); } catch { }
            try
            {
                if (hardware.SubHardware != null)
                    foreach (var sub in hardware.SubHardware) sub.Accept(this);
            }
            catch { }
        }

        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }
}
