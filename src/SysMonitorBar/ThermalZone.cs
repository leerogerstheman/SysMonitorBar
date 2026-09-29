using System.Management;

namespace SysMonitorBar;

/// <summary>
/// ACPI 热区温度兜底源。
/// 读性能计数器 Win32_PerfFormattedData_Counters_ThermalZoneInformation，
/// 实测在 Windows 11 上**不需要管理员权限**即可读取，
/// 用于在 LibreHardwareMonitor 拿不到 CPU 温度时（驱动加载失败 / 未提权）顶上。
/// </summary>
public static class ThermalZone
{
    private const string Scope = @"root\cimv2";
    private const string Query = "SELECT Name, HighPrecisionTemperature, Temperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation";

    private static DateTime _lastTry = DateTime.MinValue;
    private static double _cached = double.NaN;
    private static string _lastError = "";

    public static string LastError => _lastError;

    /// <summary>返回最高的热区温度(摄氏度)，读不到返回 double.NaN。结果缓存 1.5 秒。</summary>
    public static double ReadCelsius()
    {
        if ((DateTime.UtcNow - _lastTry).TotalSeconds < 1.5 && !double.IsNaN(_cached))
            return _cached;
        _lastTry = DateTime.UtcNow;

        double best = double.NaN;
        try
        {
            using var searcher = new ManagementObjectSearcher(Scope, Query);
            using var results = searcher.Get();
            foreach (ManagementBaseObject o in results)
            {
                // 注意：WMI 枚举出来的每个对象都持有 COM 引用，必须显式释放，
                // 否则只能等终结器回收，长时间运行会看到工作集缓慢上涨。
                try
                {
                    double c = double.NaN;

                    // HighPrecisionTemperature 单位是 0.1 开尔文
                    if (o["HighPrecisionTemperature"] != null &&
                        double.TryParse(o["HighPrecisionTemperature"].ToString(), out double hp) && hp > 0)
                    {
                        c = hp / 10.0 - 273.15;
                    }
                    else if (o["Temperature"] != null &&
                             double.TryParse(o["Temperature"].ToString(), out double k) && k > 0)
                    {
                        c = k - 273.15;
                    }

                    // 过滤明显不合理的热区（未接传感器时常返回 0K 或 273.2K 之类的占位值）
                    if (double.IsNaN(c) || c < 5 || c > 125) continue;
                    if (double.IsNaN(best) || c > best) best = c;
                }
                finally
                {
                    try { o.Dispose(); } catch { }
                }
            }
            _lastError = "";
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            Log.Warn("读取 ACPI 热区失败: " + ex.Message);
        }

        _cached = best;
        return best;
    }
}
