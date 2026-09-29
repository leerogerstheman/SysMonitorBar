using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace SysMonitorBar;

/// <summary>
/// 通过 DXGI Desktop Duplication 统计桌面合成器每秒真正呈现的画面数，
/// 即"显示器实际帧率"。独占全屏游戏或被远程串流占用时会失效，此时该指标不可用。
/// </summary>
public sealed class DisplayFpsMonitor : IDisposable
{
    private Thread _thread;
    private volatile bool _stopping;
    private volatile float _fps = -1f;
    private volatile bool _available;
    private string _lastError = "";

    public float Fps => _fps;
    public bool Available => _available;
    public string LastError => _lastError;

    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "DisplayFpsMonitor",
            Priority = ThreadPriority.BelowNormal,
        };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Stop()
    {
        _stopping = true;
        try { _thread?.Join(1500); } catch { }
        _thread = null;
    }

    public void Dispose() => Stop();

    private void Loop()
    {
        while (!_stopping)
        {
            IDXGIOutputDuplication dupl = null;
            ID3D11Device device = null;
            ID3D11DeviceContext context = null;
            IDXGIAdapter1 adapter = null;
            IDXGIOutput1 output1 = null;
            try
            {
                if (!TryCreate(out device, out context, out adapter, out output1, out string diag))
                {
                    _available = false;
                    _lastError = diag;
                    Log.Warn("桌面复制初始化失败: " + diag);
                    Sleep(5000);
                    continue;
                }

                dupl = output1.DuplicateOutput(device);
                _available = true;
                _lastError = "";
                Log.Info("桌面复制帧率监测已启动");

                int frames = 0;
                var window = System.Diagnostics.Stopwatch.StartNew();

                while (!_stopping)
                {
                    bool got = false;
                    Result hr = Result.Ok;
                    try
                    {
                        hr = dupl.AcquireNextFrame(1000, out OutduplFrameInfo _, out IDXGIResource resource);
                        if (hr.Success)
                        {
                            resource?.Dispose();
                            dupl.ReleaseFrame();
                            got = true;
                        }
                    }
                    catch (SharpGenException ex)
                    {
                        hr = ex.ResultCode;
                    }
                    catch (Exception ex)
                    {
                        _lastError = ex.Message;
                        break;
                    }

                    if (got) frames++;
                    else if (hr.Failure &&
                             (hr == Vortice.DXGI.ResultCode.AccessLost ||
                              hr == Vortice.DXGI.ResultCode.InvalidCall))
                    {
                        Log.Warn("桌面复制失效，重建: " + hr.Description);
                        break;
                    }

                    if (window.ElapsedMilliseconds >= 1000)
                    {
                        double sec = window.Elapsed.TotalSeconds;
                        _fps = (float)(frames / sec);
                        frames = 0;
                        window.Restart();
                    }
                }
            }
            catch (Exception ex)
            {
                _available = false;
                _lastError = ex.Message;
                Log.Warn("帧率监测失败: " + ex.Message);
                Sleep(5000);
            }
            finally
            {
                try { dupl?.Dispose(); } catch { }
                try { output1?.Dispose(); } catch { }
                try { context?.Dispose(); } catch { }
                try { device?.Dispose(); } catch { }
                try { adapter?.Dispose(); } catch { }
            }

            Sleep(300);
        }
    }

    private void Sleep(int ms)
    {
        int waited = 0;
        while (!_stopping && waited < ms) { Thread.Sleep(100); waited += 100; }
    }

    private static bool TryCreate(out ID3D11Device device, out ID3D11DeviceContext context,
        out IDXGIAdapter1 adapter, out IDXGIOutput1 output1, out string diag)
    {
        device = null; context = null; adapter = null; output1 = null;
        diag = "";

        // 注意：Vortice 的 Dispose() 会把 NativePointer 清零，而列表里保存的是同一个托管对象，
        // 因此所有 COM 对象必须"用完再释放"，否则传进去的会变成 NULL 指针。
        // （NULL 适配器 + DriverType.Unknown 会让 D3D11CreateDevice 返回 E_INVALIDARG）
        var alive = new List<IDisposable>();
        IDXGIFactory1 factory = null;

        try
        {
            factory = DXGI.CreateDXGIFactory1<IDXGIFactory1>();

            var cands = new List<(IDXGIAdapter1 ad, IDXGIOutput op, long area, int ai, int oi)>();
            uint ai = 0;
            while (true)
            {
                Result r;
                IDXGIAdapter1 ad;
                try { r = factory.EnumAdapters1(ai, out ad); }
                catch (Exception ex) { diag += $"EnumAdapters1({ai}) 异常:{ex.Message}; "; break; }
                if (r.Failure || ad == null) break;
                alive.Add(ad);

                string adName = "";
                try { adName = ad.Description1.Description; } catch { }

                uint oi = 0;
                while (true)
                {
                    IDXGIOutput op;
                    Result ro;
                    try { ro = ad.EnumOutputs(oi, out op); }
                    catch (Exception ex) { diag += $"EnumOutputs({ai},{oi}) 异常:{ex.Message}; "; break; }
                    if (ro.Failure || op == null) break;
                    alive.Add(op);

                    var d = op.Description;
                    var rc = d.DesktopCoordinates;
                    long area = (long)Math.Max(0, rc.Right - rc.Left) * Math.Max(0, rc.Bottom - rc.Top);
                    diag += $"[a{ai} '{adName}' o{oi} {d.DeviceName} {rc.Left},{rc.Top},{rc.Right},{rc.Bottom} area={area}] ";
                    cands.Add((ad, op, area, (int)ai, (int)oi));
                    oi++;
                    if (oi > 16) break;
                }
                ai++;
                if (ai > 16) break;
            }

            diag = $"候选={cands.Count} :: " + diag;

            // 面积大的优先（主显示器）
            cands.Sort((x, y) => y.area.CompareTo(x.area));

            FeatureLevel[] levels =
            {
                FeatureLevel.Level_11_1, FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1, FeatureLevel.Level_10_0,
            };

            var failures = new List<string>();
            foreach (var c in cands)
            {
                ID3D11Device dev = null;
                ID3D11DeviceContext ctx = null;
                try
                {
                    var res = D3D11.D3D11CreateDevice(c.ad, DriverType.Unknown, DeviceCreationFlags.BgraSupport,
                        levels, out dev, out ctx);
                    if (res.Failure || dev == null)
                    {
                        failures.Add($"a{c.ai}o{c.oi} 创建设备失败 0x{res.Code:X8}");
                        try { ctx?.Dispose(); } catch { }
                        try { dev?.Dispose(); } catch { }
                        continue;
                    }

                    output1 = c.op.QueryInterface<IDXGIOutput1>(); // 独立引用，不受下面释放影响
                    device = dev;
                    context = ctx;
                    adapter = c.ad;                                 // 所有权移交调用方
                    diag += $" 选中 a{c.ai}o{c.oi}";
                    if (failures.Count > 0) diag += " (跳过: " + string.Join("; ", failures) + ")";
                    return true;
                }
                catch (Exception ex)
                {
                    failures.Add($"a{c.ai}o{c.oi} 异常:{ex.Message}");
                    try { ctx?.Dispose(); } catch { }
                    try { dev?.Dispose(); } catch { }
                }
            }

            diag += " 全部失败: " + string.Join("; ", failures);
            return false;
        }
        catch (Exception ex)
        {
            diag = "异常: " + ex.Message + " :: " + diag;
            return false;
        }
        finally
        {
            foreach (var d in alive)
            {
                if (ReferenceEquals(d, adapter)) continue; // 已移交给调用方
                try { d.Dispose(); } catch { }
            }
            try { factory?.Dispose(); } catch { }
        }
    }
}
