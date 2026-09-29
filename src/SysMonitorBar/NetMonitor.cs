using System.Net.NetworkInformation;

namespace SysMonitorBar;

public sealed class NicOption
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public long Total { get; set; }
    public override string ToString() => Name;
}

/// <summary>网卡收发速率统计。</summary>
public sealed class NetMonitor
{
    private readonly Dictionary<string, (long rx, long tx)> _last = new();
    private DateTime _lastTime = DateTime.UtcNow;

    public double DownBps { get; private set; }
    public double UpBps { get; private set; }

    /// <summary>空字符串 = 自动选择流量最大的网卡；"*" = 汇总所有活动网卡；其它 = 指定网卡 Id。</summary>
    public string SelectedNicId { get; set; } = "";

    private string _autoId = "";
    private DateTime _lastNicScan = DateTime.MinValue;

    public List<NicOption> Interfaces { get; } = new();

    public static List<NicOption> ScanInterfaces()
    {
        var list = new List<NicOption>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;

                long total = 0;
                try
                {
                    var st = nic.GetIPStatistics();
                    total = st.BytesReceived + st.BytesSent;
                }
                catch { }

                list.Add(new NicOption
                {
                    Id = nic.Id,
                    Name = string.IsNullOrWhiteSpace(nic.Name) ? nic.Description : nic.Name,
                    Total = total,
                });
            }
        }
        catch (Exception ex) { Log.Warn("枚举网卡失败: " + ex.Message); }
        return list.OrderByDescending(n => n.Total).ToList();
    }

    public void Poll()
    {
        var now = DateTime.UtcNow;
        double dt = (now - _lastTime).TotalSeconds;
        if (dt <= 0.05) return;

        if ((now - _lastNicScan).TotalSeconds > 15)
        {
            _lastNicScan = now;
            Interfaces.Clear();
            Interfaces.AddRange(ScanInterfaces());
            _autoId = Interfaces.FirstOrDefault()?.Id ?? "";
        }

        string want = SelectedNicId;
        if (string.IsNullOrEmpty(want)) want = _autoId;
        bool all = want == "*";

        long dRx = 0, dTx = 0;
        var seen = new HashSet<string>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (!all && nic.Id != want) continue;

                long rx, tx;
                try
                {
                    var st = nic.GetIPStatistics();
                    rx = st.BytesReceived;
                    tx = st.BytesSent;
                }
                catch { continue; }

                seen.Add(nic.Id);
                if (_last.TryGetValue(nic.Id, out var prev))
                {
                    long drx = rx - prev.rx;
                    long dtx = tx - prev.tx;
                    if (drx < 0) drx = 0;   // 计数器回绕/网卡重置
                    if (dtx < 0) dtx = 0;
                    dRx += drx;
                    dTx += dtx;
                }
                _last[nic.Id] = (rx, tx);
            }
        }
        catch (Exception ex) { Log.Warn("读取网速失败: " + ex.Message); }

        // 清理已消失的网卡
        if (_last.Count > 32)
        {
            var stale = _last.Keys.Where(k => !seen.Contains(k)).ToList();
            foreach (var k in stale) _last.Remove(k);
        }

        DownBps = dRx / dt;
        UpBps = dTx / dt;
        _lastTime = now;
    }

    /// <summary>切换网卡后重置基线，避免出现巨大的瞬时值。</summary>
    public void ResetBaseline()
    {
        _last.Clear();
        _lastTime = DateTime.UtcNow;
    }
}
