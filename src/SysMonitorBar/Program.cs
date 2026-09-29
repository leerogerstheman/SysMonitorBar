namespace SysMonitorBar;

internal static class Program
{
    private static Mutex _single;

    [STAThread]
    private static void Main(string[] args)
    {
        Native.EnablePerMonitorV2();

        // ---------------- 单实例 ----------------
        // 用 Local\ 而不是 Global\：提权/非提权进程之间不会因 DACL 差异导致访问被拒
        bool created;
        try
        {
            _single = new Mutex(true, @"Local\SysMonitorBar_SingleInstance", out created);
        }
        catch (Exception ex)
        {
            Log.Warn("创建单实例互斥体失败，继续启动: " + ex.Message);
            created = true;
        }

        if (!created)
        {
            MessageBox.Show("顶部硬件监控条已经在运行了。\n请在任务栏右下角的托盘图标上右键进行操作。",
                "顶部硬件监控条", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("未处理异常: " + e.ExceptionObject);
        Application.ThreadException += (_, e) =>
            Log.Error("界面线程异常: " + e.Exception);

        try
        {
            var cfg = AppConfig.Load();

            if (HasArg(args, "--hide")) cfg.Show = false;
            if (HasArg(args, "--show")) cfg.Show = true;

            using var ctx = new TrayApp(cfg);
            if (HasArg(args, "--settings")) ctx.OpenSettingsWhenReady();
            Application.Run(ctx);
        }
        catch (Exception ex)
        {
            Log.Error("启动失败: " + ex);
            MessageBox.Show("启动失败：\n" + ex.Message + "\n\n详细信息见日志：\n" + Log.Path_,
                "顶部硬件监控条", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            try { _single?.ReleaseMutex(); } catch { }
        }
    }

    private static bool HasArg(string[] args, string name)
        => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
}
