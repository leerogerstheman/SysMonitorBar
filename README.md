# 顶部硬件监控条 (SysMonitorBar)

在屏幕**顶部正中**用 1~2 行显示实时硬件状态，数据全部自选，鼠标穿透、不挡操作。

Windows 11 · .NET 8 · WinForms · 单文件小工具 · [MIT 协议](LICENSE)

![效果图](docs/效果图.png)

鼠标移到悬浮条上时会强烈淡化（下图，不透明度 15%）：

![鼠标移入淡化](docs/效果图-鼠标移入淡化.png)

```
        CPU占用 16%   GPU温度 44°C   GPU占用 18%
内存 14.1/31.8GB   显存 0.9/6.0GB   帧率 59FPS   ↓ 4.4 KB/s  ↑ 9.5 KB/s
```

---

## 快速开始

```bat
git clone <本仓库>
cd SysMonitorBar
build.cmd             :: 需要 .NET 8 SDK，脚本会自动找
启动-管理员.cmd        :: 或 启动.cmd（不提权）
```

`app\` 目录是编译产物，**不在仓库里**，所以第一次使用必须先跑一次 `build.cmd`。

---

## 一、怎么用

| 文件 | 作用 |
|---|---|
| **启动-管理员.cmd** | **推荐**。弹 UAC 提权后启动，能读到 CPU 风扇等需要内核驱动的数据 |
| 启动.cmd | 不提权启动。**实测 CPU 温度 / GPU / 内存 / 显存 / 网速 / 刷新率 / 帧率 全部正常** |
| 设置.cmd | 打开设置界面（已提权） |
| 退出.cmd | 结束程序 |
| build.cmd | 改过源码后重新编译到 `app\` |

启动后程序常驻**系统托盘**（右下角）。托盘图标上：

- **左键双击** → 打开设置
- **右键菜单** → 显示/隐藏、设置、拖动调整位置、立即刷新、字号增减、打开配置文件夹、重新加载配置、退出

### 调整位置与淡化

悬浮条默认**完全紧贴屏幕上边缘**（`OffsetY = 0`），水平居中。

默认鼠标穿透（不挡任何点击），所以直接拖是拖不动的。
托盘右键 → **「拖动调整位置」** → 用鼠标把条拖到想要的地方 → 再点一次该菜单项锁定。
也可以在设置 → 「位置与行为」里用水平/垂直偏移精确调（垂直偏移填 0 就是紧贴顶部）。

**鼠标移到悬浮条范围内时，它会强烈淡化**（默认淡到 15%，几乎看不见），移开立刻恢复，
过渡约 150 ms。设置 →「外观」里可以关掉，或把「淡化后不透明度」改成别的值
（填 0 就是完全隐形）。

注意：鼠标穿透状态下窗口收不到 `MouseEnter/Leave`，所以程序是**轮询鼠标屏幕坐标**判断的，
穿透与否都能生效。

### 调整字号

两种方式：

- 托盘图标右键 → **「字号 增大 / 减小 / 重置」**，一键快速调
- 设置 →「外观」→ 字号，精确调（6～40，步进 0.5）

改完立刻生效并写入配置。

---

## 二、能显示哪些数据

![设置界面](docs/设置-显示内容.png)

设置 → 「显示内容」，操作就两步：

**① 加数据**：在左边「可选数据」里选中一项（内置指标 + 本机所有硬件传感器都在这里），
再点下面的 **「→ 加入第一行」** 或 **「→ 加入第二行」**。没选中时按钮是灰的。

**② 调整**：右边每行各有一列按钮——

| 按钮 | 作用 |
|---|---|
| 上移 / 下移 | 在这一行里调整先后顺序 |
| 移除 | 从显示里去掉（数据仍在左边可选列表里，随时能加回来） |
| **▼ 移到第二行** / **▲ 移到第一行** | **在两行之间搬动** |

在行列表上**右键**也有同样的菜单（上移 / 下移 / 移到另一行 / 从显示中移除）。

选中任意一行里的项目后，下面的「显示名」「模板」可以直接改它的名称和显示格式。

### 内置指标

| 分组 | 指标 |
|---|---|
| 处理器 | CPU 温度、CPU 占用率、CPU 风扇转速、CPU 频率、CPU 功耗 |
| 显卡 | GPU 温度、GPU 占用率、GPU 热点温度、GPU 风扇转速(%)、GPU 风扇(转/分)、GPU 功耗 |
| 内存/显存 | 内存占用、内存占用率、显存占用、显存占用率 |
| 网络 | 下载速度、上传速度、上下行合计 |
| 显示 | **实时帧率(实测)**、**显示器刷新率** |
| 其他 | 当前时间、当前日期、开机时长 |

### 硬件传感器（高级）

左边列表下半部分会列出 LibreHardwareMonitor 枚举到的**每一个传感器**
（电压、电流、每核心频率、主板温度、硬盘温度……按硬件分组），
都能单独加进悬浮条。列表里的「当前值」列实时显示该传感器的读数，方便挑。

### 显示模板

每条数据都可以自定义模板，占位符：

| 占位符 | 含义 |
|---|---|
| `{label}` | 名称（可在设置里改成任意文字，会用高亮色显示） |
| `{value}` | 数值 |
| `{unit}` | 单位 |

例：模板 `CPU {value}{unit}` + 显示名 `CPU` → `CPU 62°C`

---

## 三、本机的数据可用性（重要）

程序会自动把**读不到的数据隐藏掉**（设置里可关掉这个行为）。本机实测：

| 数据 | 状态 | 说明 |
|---|---|---|
| CPU 温度 | ✅ | 双数据源，见下 |
| CPU 占用、内存、显存、网速 | ✅ | 无限制 |
| GPU 温度 / 占用 / 热点 / 功耗 | ✅ | 走 NVAPI，**不需要管理员** |
| 显示器刷新率 | ✅ | 直接读当前显示模式 |
| 实时帧率 | ✅ | DXGI 桌面复制实测，按刷新率封顶 |
| **CPU 风扇 / GPU 风扇** | ❌ **本机没有该传感器** | 见下 |
| CPU 频率 / CPU 功耗 | ⚠️ 本机读不到 | 依赖 LHM 内核驱动，本机驱动未加载（GPU 功耗正常） |

### 关于 CPU 温度：双数据源

程序按顺序尝试两个来源，哪个有值用哪个：

1. **LibreHardwareMonitor（MSR 直读）** —— 最准，但需要管理员 + 内核驱动能加载。
2. **ACPI 热区兜底** —— 读性能计数器
   `Win32_PerfFormattedData_Counters_ThermalZoneInformation`，
   取所有热区里最高的那个（`HighPrecisionTemperature`，单位 0.1K，换算成摄氏度）。
   **实测在 Windows 11 上不需要管理员权限。**

本机 LibreHardwareMonitor 即使在管理员下也拿不到 CPU 温度
（它的 WinRing0 类内核驱动没有加载成功，HVCI 内存完整性并没有开），
所以实际生效的是 ACPI 兜底：**提权与不提权都能看到 `CPU温度 48°C`**（已实测）。

注意 ACPI 热区是主板/CPU 附近的热敏电阻，不等于核心温度（DIE 温度），
数值通常比 `CPU Package` 略低、变化也更迟钝。这也是为什么设置里它的全名写作
「CPU 温度（LHM 读不到时用 ACPI 热区兜底）」。

### 关于 CPU 风扇 / GPU 风扇

这台机器（i9-11900H + RTX 3060 Laptop，主板 RAV4_TLS）**根本没有对外暴露风扇传感器**：

- LibreHardwareMonitor 枚举不到任何 `Fan` 类型传感器，也没有 SuperIO / 嵌入式控制器
- `nvidia-smi --query-gpu=fan.speed` 返回 `[N/A]`（笔记本 GPU 通常不提供）

所以「风扇转速」这两个指标在**本机**会一直是空的（设置界面里显示 `—`）。
功能本身是完整的：在台式机或支持 SuperIO/EC 的机器上，只要传感器存在就会自动出现。

### 关于「实时帧率」和「刷新率」

- **刷新率**：显示模式的真实值（如 `60Hz`）。注意 `59` 通常就是 59.94Hz。
- **实时帧率**：用 DXGI Desktop Duplication 统计桌面合成器每秒真正呈现的画面数，
  等于屏幕上内容**实际更新**的速度。按刷新率封顶（面板显示不了更多帧）。
  独占全屏游戏 / 远程串流会话下桌面复制会失效，此时该项自动隐藏。
- 桌面完全静止时帧率会显示 `0`，这是正常的；想一直看屏幕能力就用「刷新率」。

---

## 四、配置文件

`app\config.json`（程序同目录，删掉就恢复默认）。

主要项：

```jsonc
{
  "RefreshMs": 1000,          // 采样间隔(毫秒) 250~10000
  "FontFamily": "Microsoft YaHei UI",
  "FontSize": 11,
  "Bold": true,
  "TextColor": "#FFE9F1FF",   // 数值颜色
  "AccentColor": "#FF6FD3FF", // 名称高亮色
  "BackColor": "#D2101319",   // 背景色
  "CornerRadius": 10,
  "PaddingX": 16,
  "PaddingY": 5,
  "LineSpacing": 3,
  "Opacity": 0.92,            // 整体不透明度
  "HoverFade": true,          // 鼠标移入时强烈淡化
  "HoverOpacity": 0.15,       // 淡化后的不透明度（0 = 完全隐形）
  "Separator": "   ",         // 项目之间的空隙
  "ScreenDevice": "",         // 空 = 主显示器；否则如 "\\\\.\\DISPLAY1"
  "OffsetX": 0,               // 相对屏幕顶部正中的偏移
  "OffsetY": 0,               // 0 = 完全紧贴屏幕上边缘
  "ClickThrough": true,       // 鼠标穿透
  "Show": true,
  "HideUnavailable": true,    // 自动隐藏读不到的数据
  "NicId": "",                // "" = 自动选流量最大的网卡；"*" = 全部合计
  "Items": [                  // 按顺序渲染，Line 0=第一行 1=第二行
    { "Key": "cpu.load", "Label": "CPU占用", "Template": "{label} {value}{unit}",
      "Line": 0, "Enabled": true }
  ]
}
```

### 开机自启

设置 → 「位置与行为」→ 勾选**开机自动启动**。

- 提权运行时：创建**计划任务**（`ONLOGON` + `最高权限`），开机自动以管理员启动，**不再弹 UAC**
- 非提权运行时：退回到注册表 `HKCU\...\Run`
- 取消勾选会同时清掉这两种

---

## 五、技术实现

- **.NET 8 + WinForms**，框架依赖部署（本机已装 .NET 8 桌面运行时）
- **LibreHardwareMonitorLib 0.9.6** —— CPU/GPU/主板传感器
- **Vortice.Direct3D11 / Vortice.DXGI** —— DXGI 桌面复制测帧率
- **System.Management** —— ACPI 热区温度兜底
- 内存：`GlobalMemoryStatusEx`
- 网速：`NetworkInterface.GetIPStatistics()` 差分
- 刷新率：`EnumDisplaySettings` + `GetDeviceCaps(VREFRESH)`
- 悬浮条：无边框 `TopMost` + `WS_EX_TOOLWINDOW|WS_EX_NOACTIVATE`，
  圆角 Region + `Form.Opacity`，`WS_EX_TRANSPARENT` 实现鼠标穿透；
  鼠标移入淡化靠轮询 `Cursor.Position`（穿透时收不到鼠标消息）
- 采样在后台线程（默认 1Hz），UI 用 200ms 定时器读取最新快照并按需重绘

### 踩过的坑（改代码时注意）

1. **Vortice 的 `Dispose()` 会把 `NativePointer` 清零**，而同一 COM 对象可能被多处引用。
   过早 Dispose 会让 `D3D11CreateDevice` 收到 NULL 适配器并返回 `E_INVALIDARG`。
   正确做法是「枚举期间不释放，用完再统一释放」。
2. **主显示器可能挂在虚拟显示适配器上**（本机有 GameViewer 虚拟显示器），
   该适配器创建设备会失败，必须遍历所有 (适配器, 输出) 逐个回退。
3. **桌面复制的计数会超过面板刷新率**（实测 79 > 60），所以按刷新率封顶。
4. **WinForms 里 `TabPage.ClientSize` 在加入 `TabControl` 之前是无效值**，
   按它算布局会得到负高度。要等 `SizeChanged` 之后再排版。
5. **`.cmd` 必须用 CRLF**；**`.ps1` 含中文必须存成 UTF-8 带 BOM**
   （PowerShell 5.1 会把无 BOM 的脚本按 GBK 解析而报语法错）。
6. **文件名里别放括号**：`cmd /c 启动(管理员).cmd` 会在 `(` 处截断。
7. **`WS_EX_TRANSPARENT`（鼠标穿透）必须写进 `CreateParams`，不能只在建好窗口后
   `SetWindowLong` 加一次。** WinForms 每次 `UpdateStyles()` / 重建句柄都会用
   `CreateParams.ExStyle` **整体覆盖**扩展样式，外部加的那一位会被静默抹掉——
   症状就是悬浮条挡住后面的窗口点不动（本项目真实踩过）。同时要注意：
   `GetWindowLong` 读到的位才是真相，别只看代码里"我设过了"。
8. **别用"新配置对象 vs 旧配置对象"做差异比较来决定要不要重建资源。**
   设置界面应用之后，`TrayApp` / `OverlayForm` / `SettingsForm` 可能持有
   **同一个 `AppConfig` 对象**，自己跟自己比永远等于"没变化"，资源就再也不会重建。
   本项目真实踩过：改字号点「保存并应用」，配置写进去了，悬浮条纹丝不动；
   托盘菜单的「字号 增大/减小」更是 100% 失效（它先改 `_cfg`，再把 `_cfg` 自己传进去）。
   正确做法是**记录"当前资源是用哪组参数建出来的"**（这里用 `_fontKey` 字符串），
   再和目标参数比——跟对象身份无关。
   另外 `TrayApp.ApplyNewConfig` 现在会 `CloneConfig()` 再接管，从根上避免这种别名。

### 源码结构

```
SysMonitorBar\
  build.cmd / build.ps1    编译（自动探测 .NET SDK）
  launch.ps1               启动/退出逻辑（.cmd 只是它的外壳）
  启动.cmd                 普通权限启动
  启动-管理员.cmd           提权启动（读 CPU 风扇等需要内核驱动的数据）
  设置.cmd                 直接打开设置
  退出.cmd                 结束程序
  docs\                    截图
  src\SysMonitorBar\
    Program.cs             入口 / 单实例 / 启动失败处理
    AppConfig.cs           配置模型 + JSON 读写 + 日志
    Metrics.cs             指标定义 / 指标目录 / 模板渲染
    SensorService.cs       后台采样：LHM 枚举 + 内置指标自动匹配
    DisplayFpsMonitor.cs   DXGI 桌面复制帧率监测
    ThermalZone.cs         ACPI 热区温度兜底（免管理员）
    NetMonitor.cs          网卡枚举与速率统计
    Native.cs              P/Invoke：内存 / 刷新率 / 窗口样式 / DPI
    BarPainter.cs          悬浮条排版与绘制（设置界面预览共用）
    OverlayForm.cs         顶部悬浮条窗口
    SettingsForm.cs        设置界面
    TrayApp.cs             托盘 + 协调
    AutoStart.cs           开机自启 / 提权重启
```

命令行参数（一般用不到，都是给启动器脚本用的）：

```bat
SysMonitorBar.exe --settings     启动后直接打开设置窗口
SysMonitorBar.exe --show         启动时强制显示悬浮条
SysMonitorBar.exe --hide         启动时强制隐藏悬浮条
```

日志：`app\SysMonitorBar.log`（有问题先看它，每次启动都会写关键状态）

---

## 六、重新编译

源码改了之后双击 `build.cmd` 即可。它需要 **.NET 8 SDK**（只装运行时不够），
会按这个顺序自动找：

1. 项目根目录下的 `dotnet-path.txt`（第一行写 `dotnet.exe` 完整路径）
2. 环境变量 `SYSMONITORBAR_DOTNET`
3. `PATH` 里的 `dotnet`
4. `%ProgramFiles%\dotnet\dotnet.exe`、`%ProgramFiles(x86)%\dotnet\dotnet.exe`
5. `%USERPROFILE%\.dotnet\dotnet.exe`、`%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe`
6. `%DOTNET_ROOT%\dotnet.exe`

都没有就按提示去 https://dotnet.microsoft.com/download/dotnet/8.0 装一个。

**SDK 装在非标准位置的机器**：在项目根目录建一个 `dotnet-path.txt`，第一行写路径：

```
D:\somewhere\dotnet8\dotnet.exe
```

也可以只写一行注释，脚本会跳过 `#` 开头的行：

```
# 我的 SDK 装在这儿
D:\somewhere\dotnet8\dotnet.exe
```

（这个文件已经写进 `.gitignore`，不会跟着仓库走。）

另外 `build.cmd` 会先检查程序是否在运行——**运行中的程序会锁住 `app\` 里的文件**，
所以编译前得先退出（托盘右键 → 退出，或双击 `退出.cmd`）。

---

## 七、常见问题

**Q：悬浮条不见了？**
托盘图标右键 → 「显示 / 隐藏监控条」。

**Q：点不动 / 挡住我了？**
默认就是鼠标穿透，不会挡。若真的挡住，按顺序排查：

1. 是不是点过托盘菜单的「拖动调整位置」？该模式下穿透会被临时关闭（否则拖不动），
   再点一次即可恢复。
2. 设置 →「位置与行为」→ 确认「鼠标穿透」是勾上的。
3. 还不行就查日志 `app\SysMonitorBar.log`，程序每次启动都会打印一行自检：

   ```
   [悬浮条] 创建完成 期望穿透=True 实际=True ExStyle=0x080901A8 OK
   ```

   看 `实际` 是不是 `True`、`ExStyle` 里有没有 `0x20` 那一位。

程序每 120 ms 还会自查一次，一旦发现穿透位丢失会自动恢复并记一条 WARN 到日志。

**Q：CPU 温度准吗？**
优先用 LibreHardwareMonitor 的 MSR 直读；读不到时自动退回 ACPI 热区，
后者是主板热敏电阻，通常比核心温度略低。设置界面左侧列表能同时看到两个来源。

**Q：风扇转速一直是空的？**
本机硬件就没有这个传感器（见上文）。换台式机或支持的笔记本会自动出现。

**Q：鼠标移到上面就看不见了？**
这是「鼠标移入淡化」功能。设置 →「外观」里关掉，或把淡化后不透明度调高。

**Q：托盘菜单在哪里？**
任务栏右下角，可能收在 `^` 折叠区里。图标是一个深色圆角方块加青色仪表弧。
双击图标直接打开设置。

**Q：帧率显示 0？**
桌面完全静止时就是 0（没有新画面被呈现）。玩游戏 / 看视频时会动起来。
独占全屏游戏和远程桌面下测不了，该项会自动隐藏。

**Q：两个显示器，它跑到副屏去了？**
旧版本按序号存，可能选错。设置 → 「位置与行为」→ 选「（主）」那块即可，
现在按设备名保存，换显示器顺序也不会跑偏。
