using Microsoft.Win32;

namespace SysMonitorBar;

/// <summary>开机自启动。已提权时用计划任务（可免 UAC 以管理员运行），否则退回注册表 Run 项。</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "SysMonitorBar";
    private const string TaskName = "SysMonitorBar";

    public static string ExePath
    {
        get
        {
            string p = Environment.ProcessPath;
            if (string.IsNullOrEmpty(p))
                p = Path.Combine(AppContext.BaseDirectory, "SysMonitorBar.exe");
            return p;
        }
    }

    public static bool IsEnabled()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, false);
            if (k?.GetValue(ValueName) != null) return true;
        }
        catch { }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                $"/Query /TN \"{TaskName}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p.WaitForExit(8000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    public static bool Set(bool enable, bool elevated, out string message)
    {
        message = "";
        bool ok = true;

        // 先把两种方式都清掉，避免重复启动
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            k?.DeleteValue(ValueName, false);
        }
        catch { }

        if (!enable)
        {
            if (elevated) RunTask($"/Delete /TN \"{TaskName}\" /F", out _, out _);
            message = "已关闭开机自启";
            return true;
        }

        if (elevated)
        {
            string tr = $"\\\"{ExePath}\\\" --tray";
            string args = $"/Create /TN \"{TaskName}\" /TR \"{tr}\" /SC ONLOGON /RL HIGHEST /DELAY 0000:10 /F";
            if (RunTask(args, out string so, out string se))
            {
                message = "已创建计划任务（开机以管理员身份自动启动）";
                return true;
            }
            message = "计划任务创建失败，改用注册表方式：" + (so + " " + se).Trim();
            ok = false;
        }

        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey, true);
            k.SetValue(ValueName, "\"" + ExePath + "\" --tray");
            if (ok) message = "已设置开机自启（当前用户）";
            else message += "（已设置注册表自启）";
            return true;
        }
        catch (Exception ex)
        {
            message = "设置开机自启失败: " + ex.Message;
            return false;
        }
    }

    private static bool RunTask(string args, out string stdout, out string stderr)    {
        stdout = stderr = "";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            stdout = p.StandardOutput.ReadToEnd();
            stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(15000);
            Log.Info($"schtasks {args} -> exit={p.ExitCode} {stdout.Trim()} {stderr.Trim()}");
            return p.ExitCode == 0;
        }
        catch (Exception ex)
        {
            stderr = ex.Message;
            Log.Warn("schtasks 调用失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>以管理员身份重新启动本程序（会弹出 UAC 确认框）。</summary>
    public static bool RestartElevated(out string error)
    {
        error = "";
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(ExePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = "--show",
                WorkingDirectory = AppContext.BaseDirectory,
            };
            System.Diagnostics.Process.Start(psi);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            Log.Warn("提权重启失败: " + ex.Message);
            return false;
        }
    }
}
