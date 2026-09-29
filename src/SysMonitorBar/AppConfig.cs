using System.Drawing;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SysMonitorBar;

public sealed class ItemConfig
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Template { get; set; } = "{label} {value}{unit}";
    /// <summary>0 = 第一行，1 = 第二行</summary>
    public int Line { get; set; }
    public bool Enabled { get; set; } = true;

    public ItemConfig Clone() => new()
    {
        Key = Key, Label = Label, Template = Template, Line = Line, Enabled = Enabled
    };
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    // ---------- 采样 ----------
    public int RefreshMs { get; set; } = 1000;

    // ---------- 外观 ----------
    public string FontFamily { get; set; } = "Microsoft YaHei UI";
    public float FontSize { get; set; } = 11f;
    public bool Bold { get; set; } = true;
    public string TextColor { get; set; } = "#FFE9F1FF";
    public string AccentColor { get; set; } = "#FF6FD3FF";
    public string BackColor { get; set; } = "#D2101319";
    public int CornerRadius { get; set; } = 10;
    public int PaddingX { get; set; } = 16;
    public int PaddingY { get; set; } = 5;
    public int LineSpacing { get; set; } = 3;
    public double Opacity { get; set; } = 0.92;
    public string Separator { get; set; } = "   ";

    // ---------- 位置 ----------
    /// <summary>显示器设备名（如 \\.\DISPLAY5）。空字符串表示主显示器。</summary>
    public string ScreenDevice { get; set; } = "";
    public int OffsetX { get; set; } = 0;
    /// <summary>相对屏幕上边缘的偏移。0 = 完全紧贴顶部。</summary>
    public int OffsetY { get; set; } = 0;
    public bool ClickThrough { get; set; } = true;
    public bool Show { get; set; } = true;

    // ---------- 鼠标移入淡出 ----------
    /// <summary>鼠标移到悬浮条范围内时是否强烈淡化。</summary>
    public bool HoverFade { get; set; } = true;
    /// <summary>淡化后的不透明度。</summary>
    public double HoverOpacity { get; set; } = 0.15;

    // ---------- 其它 ----------
    public bool HideUnavailable { get; set; } = true;
    public string NicId { get; set; } = "";

    public List<ItemConfig> Items { get; set; } = new();

    // ------------------------------------------------------------------
    [JsonIgnore] public string ConfigPath { get; set; } = "";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string DefaultPath
    {
        get
        {
            string dir = AppContext.BaseDirectory;
            try
            {
                // 优先放在程序目录（便携）
                string probe = Path.Combine(dir, ".write-test");
                File.WriteAllText(probe, "x");
                File.Delete(probe);
                return Path.Combine(dir, "config.json");
            }
            catch
            {
                string alt = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SysMonitorBar");
                Directory.CreateDirectory(alt);
                return Path.Combine(alt, "config.json");
            }
        }
    }

    public static AppConfig CreateDefault()
    {
        var c = new AppConfig { Items = MetricCatalog.DefaultItems() };
        return c;
    }

    public static AppConfig Load(string path = null)
    {
        path ??= DefaultPath;
        AppConfig cfg = null;
        try
        {
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("读取配置失败，使用默认配置: " + ex.Message);
            try { File.Copy(path, path + ".bad", true); } catch { }
        }

        if (cfg == null) cfg = CreateDefault();
        if (cfg.Items == null || cfg.Items.Count == 0) cfg.Items = MetricCatalog.DefaultItems();

        cfg.ConfigPath = path;
        cfg.Normalize();
        return cfg;
    }

    public void Normalize()
    {
        if (RefreshMs < 250) RefreshMs = 250;
        if (RefreshMs > 10000) RefreshMs = 10000;
        if (FontSize < 6f) FontSize = 6f;
        if (FontSize > 40f) FontSize = 40f;
        if (Opacity < 0.2) Opacity = 0.2;
        if (Opacity > 1.0) Opacity = 1.0;
        if (HoverOpacity < 0.0) HoverOpacity = 0.0;
        if (HoverOpacity > 1.0) HoverOpacity = 1.0;
        if (HoverOpacity >= Opacity) HoverOpacity = Math.Max(0, Opacity - 0.05);
        if (CornerRadius < 0) CornerRadius = 0;
        if (PaddingX < 0) PaddingX = 0;
        if (PaddingY < 0) PaddingY = 0;
        if (LineSpacing < 0) LineSpacing = 0;
        if (string.IsNullOrEmpty(Separator)) Separator = "   ";
        foreach (var it in Items)
        {
            if (string.IsNullOrWhiteSpace(it.Template)) it.Template = "{label} {value}{unit}";
            if (it.Line != 1) it.Line = 0;
        }
    }

    public void Save()
    {
        try
        {
            string path = string.IsNullOrEmpty(ConfigPath) ? DefaultPath : ConfigPath;
            ConfigPath = path;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOpts));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception ex)
        {
            Log.Warn("保存配置失败: " + ex.Message);
        }
    }

    public AppConfig CloneConfig()
    {
        var c = JsonSerializer.Deserialize<AppConfig>(
            JsonSerializer.Serialize(this, JsonOpts), JsonOpts) ?? CreateDefault();
        c.ConfigPath = ConfigPath;
        return c;
    }

    // ---------------- 颜色辅助 ----------------
    public static Color ParseColor(string s, Color fallback)    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        try
        {
            s = s.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            if (s.Length == 6)
            {
                int rgb = Convert.ToInt32(s, 16);
                return Color.FromArgb(255, (rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
            }
            if (s.Length == 8)
            {
                uint v = Convert.ToUInt32(s, 16);
                return Color.FromArgb((int)(v >> 24) & 0xFF, (int)(v >> 16) & 0xFF, (int)(v >> 8) & 0xFF, (int)(v & 0xFF));
            }
        }
        catch { }
        return fallback;
    }

    public static string ColorToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    // 这三个是派生属性，必须排除在序列化之外，否则每次保存都会往 config.json 里
    // 塞三个没用的 Color 对象（R/G/B/A/IsKnownColor/Name…）。
    [JsonIgnore] public Color Text => ParseColor(TextColor, Color.FromArgb(255, 233, 241, 255));
    [JsonIgnore] public Color Accent => ParseColor(AccentColor, Color.FromArgb(255, 111, 211, 255));
    [JsonIgnore] public Color Back => ParseColor(BackColor, Color.FromArgb(210, 16, 19, 25));

    /// <summary>按设备名解析显示器，找不到时退回主显示器。</summary>
    public static System.Windows.Forms.Screen ResolveScreen(string device)
    {
        var all = System.Windows.Forms.Screen.AllScreens;
        if (!string.IsNullOrEmpty(device))
        {
            foreach (var s in all)
                if (string.Equals(s.DeviceName, device, StringComparison.OrdinalIgnoreCase))
                    return s;
        }
        foreach (var s in all)
            if (s.Primary) return s;
        return all.Length > 0 ? all[0] : System.Windows.Forms.Screen.PrimaryScreen;
    }
}

public static class Log
{
    private static readonly object Gate = new();
    private static string _path;

    public static string Path_
    {
        get
        {
            if (_path != null) return _path;
            try { _path = System.IO.Path.Combine(AppContext.BaseDirectory, "SysMonitorBar.log"); }
            catch { _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SysMonitorBar.log"); }
            return _path;
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERR ", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                var fi = new FileInfo(Path_);
                if (fi.Exists && fi.Length > 512 * 1024) fi.Delete();
                File.AppendAllText(Path_, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
