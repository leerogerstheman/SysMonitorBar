using System.Text;

namespace SysMonitorBar;

/// <summary>一次采样得到的单个指标读数。</summary>
public sealed class Reading
{
    public string Key = "";
    /// <summary>短标签，用于 {label} 占位符。</summary>
    public string Label = "";
    /// <summary>已格式化的数值文本，例如 "62"、"12.3/31.9"、"1.2 MB/s"。</summary>
    public string Value = "--";
    /// <summary>单位，例如 "°C"、"%"。可能为空。</summary>
    public string Unit = "";
    /// <summary>数据是否有效（传感器不存在 / 无权限读取时为 false）。</summary>
    public bool Available;
}

/// <summary>内置指标的静态描述。</summary>
public sealed class MetricDescriptor
{
    public string Key;
    /// <summary>设置界面里显示的完整名称。</summary>
    public string Name;
    /// <summary>默认短标签。</summary>
    public string Label;
    /// <summary>分组名（设置界面里用）。</summary>
    public string Group;
    /// <summary>默认模板。</summary>
    public string Template;
    /// <summary>默认是否放在第二行。</summary>
    public bool DefaultLine2;

    public override string ToString() => Name;
}

public static class M
{
    public const string CpuLoad = "cpu.load";
    public const string CpuTemp = "cpu.temp";
    public const string CpuFan = "cpu.fan";
    public const string CpuClock = "cpu.clock";
    public const string CpuPower = "cpu.power";

    public const string GpuLoad = "gpu.load";
    public const string GpuTemp = "gpu.temp";
    public const string GpuFan = "gpu.fan";
    public const string GpuFanRpm = "gpu.fanrpm";
    public const string GpuVram = "gpu.vram";
    public const string GpuVramPct = "gpu.vrampct";
    public const string GpuHotspot = "gpu.hotspot";
    public const string GpuPower = "gpu.power";

    public const string MemUsed = "mem.used";
    public const string MemPct = "mem.pct";

    public const string NetDown = "net.down";
    public const string NetUp = "net.up";
    public const string NetTotal = "net.total";

    public const string DispHz = "disp.hz";
    public const string DispFps = "disp.fps";

    public const string TimeNow = "time.now";
    public const string TimeDate = "time.date";
    public const string TimeUptime = "time.uptime";

    /// <summary>动态传感器指标的前缀，完整键形如 sensor:/nvidiagpu/0/temperature/3</summary>
    public const string SensorPrefix = "sensor:";
}

public static class MetricCatalog
{
    public const string GpCpu = "处理器 (CPU)";
    public const string GpGpu = "显卡 (GPU)";
    public const string GpMem = "内存 / 显存";
    public const string GpNet = "网络";
    public const string GpDisp = "显示 / 帧率";
    public const string GpOther = "其他";

    public static readonly MetricDescriptor[] Builtins =
    {
        new() { Key = M.CpuTemp,  Name = "CPU 温度（LHM 读不到时用 ACPI 热区兜底）", Label = "CPU温度", Group = GpCpu, Template = "{label} {value}{unit}" },
        new() { Key = M.CpuLoad,  Name = "CPU 占用率",    Label = "CPU占用", Group = GpCpu, Template = "{label} {value}{unit}" },
        new() { Key = M.CpuFan,   Name = "CPU 风扇转速",  Label = "CPU风扇", Group = GpCpu, Template = "{label} {value}{unit}" },
        new() { Key = M.CpuClock, Name = "CPU 频率",      Label = "CPU频率", Group = GpCpu, Template = "{label} {value}{unit}" },
        new() { Key = M.CpuPower, Name = "CPU 功耗",      Label = "CPU功耗", Group = GpCpu, Template = "{label} {value}{unit}" },

        new() { Key = M.GpuTemp,    Name = "GPU 温度",     Label = "GPU温度", Group = GpGpu, Template = "{label} {value}{unit}" },
        new() { Key = M.GpuLoad,    Name = "GPU 占用率",   Label = "GPU占用", Group = GpGpu, Template = "{label} {value}{unit}" },
        new() { Key = M.GpuHotspot, Name = "GPU 热点温度", Label = "GPU热点", Group = GpGpu, Template = "{label} {value}{unit}" },
        new() { Key = M.GpuFan,     Name = "GPU 风扇转速", Label = "GPU风扇", Group = GpGpu, Template = "{label} {value}{unit}" },
        new() { Key = M.GpuFanRpm,  Name = "GPU 风扇(转/分)", Label = "GPU风扇", Group = GpGpu, Template = "{label} {value}{unit}" },
        new() { Key = M.GpuPower,   Name = "GPU 功耗",     Label = "GPU功耗", Group = GpGpu, Template = "{label} {value}{unit}" },

        new() { Key = M.MemUsed,     Name = "内存占用",        Label = "内存",  Group = GpMem, Template = "{label} {value}{unit}", DefaultLine2 = true },
        new() { Key = M.MemPct,      Name = "内存占用率",      Label = "内存",  Group = GpMem, Template = "{label} {value}{unit}", DefaultLine2 = true },
        new() { Key = M.GpuVram,     Name = "显存占用",        Label = "显存",  Group = GpMem, Template = "{label} {value}{unit}", DefaultLine2 = true },
        new() { Key = M.GpuVramPct,  Name = "显存占用率",      Label = "显存",  Group = GpMem, Template = "{label} {value}{unit}", DefaultLine2 = true },

        new() { Key = M.NetDown,  Name = "网络下载速度", Label = "下载", Group = GpNet, Template = "↓{value}{unit}", DefaultLine2 = true },
        new() { Key = M.NetUp,    Name = "网络上传速度", Label = "上传", Group = GpNet, Template = "↑{value}{unit}", DefaultLine2 = true },
        new() { Key = M.NetTotal, Name = "网络总速度",   Label = "网络", Group = GpNet, Template = "⇅{value}{unit}", DefaultLine2 = true },

        new() { Key = M.DispFps, Name = "实时帧率(实测)", Label = "帧率",   Group = GpDisp, Template = "{label} {value}{unit}", DefaultLine2 = true },
        new() { Key = M.DispHz,  Name = "显示器刷新率",   Label = "刷新率", Group = GpDisp, Template = "{label} {value}{unit}", DefaultLine2 = true },

        new() { Key = M.TimeNow,    Name = "当前时间", Label = "时间", Group = GpOther, Template = "{value}", DefaultLine2 = true },
        new() { Key = M.TimeDate,   Name = "当前日期", Label = "日期", Group = GpOther, Template = "{value}", DefaultLine2 = true },
        new() { Key = M.TimeUptime, Name = "开机时长", Label = "已开机", Group = GpOther, Template = "{label} {value}", DefaultLine2 = true },
    };

    private static readonly Dictionary<string, MetricDescriptor> Index =
        Builtins.ToDictionary(d => d.Key, d => d);

    public static MetricDescriptor Find(string key)
        => key != null && Index.TryGetValue(key, out var d) ? d : null;

    /// <summary>首次运行时写入配置的默认指标组合。</summary>
    public static List<ItemConfig> DefaultItems()
    {
        // (键, 行)  第一行：CPU/GPU 温度与占用；第二行：内存/显存/帧率/网速
        var plan = new (string key, int line)[]
        {
            (M.CpuTemp, 0), (M.CpuLoad, 0), (M.CpuFan, 0), (M.CpuPower, 0),
            (M.GpuTemp, 0), (M.GpuLoad, 0), (M.GpuFan, 0),

            (M.MemUsed, 1), (M.GpuVram, 1), (M.DispFps, 1),
            (M.NetDown, 1), (M.NetUp, 1),
        };

        var list = new List<ItemConfig>();
        foreach (var (key, line) in plan)
        {
            var d = Find(key);
            if (d == null) continue;
            list.Add(new ItemConfig
            {
                Key = d.Key,
                Label = d.Label,
                Template = d.Template,
                Line = line,
                Enabled = true,
            });
        }
        return list;
    }

    /// <summary>按模板渲染一条读数。</summary>
    public static string Render(string template, Reading r)
    {
        var sb = new StringBuilder();
        foreach (var run in RenderRuns(template, r)) sb.Append(run.Text);
        return sb.ToString();
    }

    public enum RunKind { Text, Label, Sep }

    public sealed class Run
    {
        public string Text;
        public RunKind Kind;
        public override string ToString() => Text;
    }

    /// <summary>
    /// 按模板拆成若干"文本段"，其中 {label} 对应的部分单独成段，
    /// 便于在悬浮条上用不同颜色渲染名称与数值。
    /// </summary>
    public static List<Run> RenderRuns(string template, Reading r)
    {
        if (string.IsNullOrWhiteSpace(template)) template = "{label} {value}{unit}";
        var runs = new List<Run>();
        var cur = new StringBuilder();
        RunKind curKind = RunKind.Text;

        void Flush()
        {
            if (cur.Length > 0)
            {
                runs.Add(new Run { Text = cur.ToString(), Kind = curKind });
                cur.Clear();
            }
        }

        void Append(string s, RunKind kind)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (kind != curKind) { Flush(); curKind = kind; }
            cur.Append(s);
        }

        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    string token = template.Substring(i + 1, end - i - 1).Trim();
                    switch (token)
                    {
                        case "label":
                        case "name":
                            Append(r.Label, RunKind.Label); break;
                        case "value":
                            Append(r.Value, RunKind.Text); break;
                        case "unit":
                            Append(r.Unit, RunKind.Text); break;
                        default:
                            Append("{" + token + "}", RunKind.Text); break;
                    }
                    i = end;
                    continue;
                }
            }
            Append(c.ToString(), RunKind.Text);
        }
        Flush();
        return runs;
    }
}
