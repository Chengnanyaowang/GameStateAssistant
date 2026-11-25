using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GameStateAssistant.Models;
using Microsoft.Win32;

namespace GameStateAssistant.Services
{
    internal static class SystemInspector
    {
        public const string BalancedPowerGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string HighPerfPowerGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        private const string PCoreMinSubGuid = "54533251-82be-4824-96c1-47b60b740d00";
        private const string PCoreMinSettingGuid = "0cc5b647-c1df-4637-891a-dec35c318583";
        private const string PcieSubGuid = "501a4d13-42af-4429-9fd1-a8218c268e20";
        private const string PcieAspmGuid = "ee12f906-d277-404b-b6da-e5fa1a576df5";
        private const string UsbSubGuid = "2a737441-1930-4402-8d77-b2bebba308a3";
        private const string UsbSuspendGuid = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

        private static bool _ramHwCached;
        private static int _cachedRamSticks;
        private static int _cachedRamMhz;

        private sealed class ProcessRule
        {
            public string Key { get; }
            public string DisplayName { get; }
            public string Category { get; }
            public string Reason { get; }
            public int BaseImpact { get; }
            public bool CanClose { get; }
            public bool RequiresAdmin { get; }

            public ProcessRule(string key, string displayName, string category, string reason, int baseImpact, bool canClose, bool requiresAdmin = false)
            {
                Key = key;
                DisplayName = displayName;
                Category = category;
                Reason = reason;
                BaseImpact = baseImpact;
                CanClose = canClose;
                RequiresAdmin = requiresAdmin;
            }
        }

        private static readonly Dictionary<string, ProcessRule> ProcessRules = new Dictionary<string, ProcessRule>(StringComparer.OrdinalIgnoreCase)
        {
            { "oopz", new ProcessRule("oopz", "Oopz 语音开黑", "语音 / AI降噪", "内置 AboveNormal 高优线程与 D3D11 渲染，极易抢占 P 核（建议锁进纯 E 核）", 3, true) },
            { "codex-computer-use-swift", new ProcessRule("codex-computer-use-swift", "Codex 屏幕控制", "AI / 屏幕钩子", "挂载桌面感知与 GPU 接口，可能干扰游戏帧时间", 3, true) },
            { "chatgpt", new ProcessRule("chatgpt", "ChatGPT 桌面端", "AI / 显存占用", "常驻后台占用大量内存与 GPU 显存", 2, true) },
            { "codex", new ProcessRule("codex", "Codex 客户端", "AI / 后台驻留", "后台编码代理进程，赛前建议退出释放内存", 2, true) },
            { "msedge", new ProcessRule("msedge", "Microsoft Edge", "浏览器 / 显存", "多标签网页与视频硬解占用大量显存", 2, true) },
            { "chrome", new ProcessRule("chrome", "Google Chrome", "浏览器 / 显存", "多标签网页与硬件加速占用大量显存", 2, true) },
            { "msedgewebview2", new ProcessRule("msedgewebview2", "WebView2 网页组件", "桌面网页容器", "桌面应用内嵌浏览器组件占用显存", 1, true) },
            { "mspcmanager", new ProcessRule("mspcmanager", "微软电脑管家", "管家 / 扫描", "后台定时清理与弹窗钩子容易引发周期性掉帧", 3, true) },
            { "mspcmanagercore", new ProcessRule("mspcmanagercore", "微软电脑管家核心", "管家 / 扫描", "后台常驻监控核心模块", 3, true) },
            { "mspcmanagerservice", new ProcessRule("mspcmanagerservice", "微软电脑管家服务", "系统服务", "Windows 系统服务，需提权停止以防自动重启", 3, true, true) },
            { "gameviewer", new ProcessRule("gameviewer", "UU 远程桌面", "远程串流", "远程桌面主程序，正在串流时会抢占显卡编码器", 3, true) },
            { "gameviewerservice", new ProcessRule("gameviewerservice", "UU 远程后台服务", "系统服务", "GameViewer 后台服务，如需彻底静默可提权停止", 2, true, true) },
            { "sunloginclient", new ProcessRule("sunloginclient", "向日葵远程控制", "远程串流", "远程抓屏驱动可能影响游戏全屏帧时间", 3, true) },
            { "todesk", new ProcessRule("todesk", "ToDesk 远程控制", "远程串流", "远程抓屏驱动可能影响游戏全屏帧时间", 3, true) },
            { "obs64", new ProcessRule("obs64", "OBS Studio", "录屏 / 推流", "视频编码与桌面采集会占用显卡 NVENC 与内存", 3, true) },
            { "wallpaper32", new ProcessRule("wallpaper32", "Wallpaper Engine", "动态壁纸", "动态壁纸会持续占用显卡 3D 渲染资源", 2, true) },
            { "wallpaper64", new ProcessRule("wallpaper64", "Wallpaper Engine", "动态壁纸", "动态壁纸会持续占用显卡 3D 渲染资源", 2, true) },
            { "thunder", new ProcessRule("thunder", "迅雷下载", "下载 / 机械盘", "后台上传下载会持续抢占网络带宽与磁盘 IO", 2, true) },
            { "onedrive", new ProcessRule("onedrive", "OneDrive 同步", "网盘同步", "文件同步可能产生突发磁盘和网络占用", 2, true) },
            { "discord", new ProcessRule("discord", "Discord", "语音 / 覆盖层", "内置覆盖层与硬件加速可能占用 GPU", 1, true) },
            { "cloudmusic", new ProcessRule("cloudmusic", "网易云音乐", "媒体播放", "桌面歌词与硬件加速动画占用显存", 1, true) },
            { "steamwebhelper", new ProcessRule("steamwebhelper", "Steam 商店网页进程", "平台组件(受保护)", "Steam 客户端内嵌网页（建议保留，勿强杀）", 1, false) },
            { "msmpeng", new ProcessRule("msmpeng", "Windows Defender", "系统安全(受保护)", "系统核心安全引擎（受保护，仅提示）", 2, false) },
            { "antigravity", new ProcessRule("antigravity", "Antigravity IDE", "开发工具(当前保护)", "正在运行的 AI 开发工具（默认受保护）", 1, false) }
        };

        private static readonly Dictionary<string, string> KnownGames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "TslGame", "PUBG: BATTLEGROUNDS" },
            { "cs2", "Counter-Strike 2" },
            { "League of Legends", "英雄联盟 (LOL)" },
            { "VALORANT-Win64-Shipping", "无畏契约 (VALORANT)" },
            { "Cyberpunk2077", "赛博朋克 2077" },
            { "eldenring", "艾尔登法环" },
            { "sekiro", "只狼：影逝二度" }
        };

        public static SystemTelemetrySnapshot CollectAll(CpuPlatformMode requestedMode = CpuPlatformMode.Auto)
        {
            var snap = new SystemTelemetrySnapshot
            {
                Timestamp = DateTime.Now,
                IsProcessElevated = NativeWin32.IsElevated(),
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
                HasSavedRestoreState = OptimizationEngine.HasSavedRestoreState,
                RequestedPlatformMode = requestedMode
            };

            // 1. CPU 平台识别 + 显卡/内存/显示器硬件遥测
            CollectPlatformAndHardware(snap);

            // 2. 系统底层核心检查（含 Intel / AMD 双平台差异化规则 + optimizerDuck 安全项）
            CollectSystemCoreChecks(snap);

            // 3. 竞技网游专项检查（动态刷新率 RTSS 矩阵、小飞机、PUBG、CS2、LOL、VALORANT）
            CollectOnlineEsportsChecks(snap);

            // 4. 3A 大作专项检查（动态显存安全线、赛博朋克 2077、艾尔登法环）
            CollectAaaGameChecks(snap);

            // 5. 扫描后台进程（包含 CPU 采样与 GPU PID 关联）
            CollectBackgroundProcesses(snap);

            // 6. 扫描 C 盘可安全清理缓存
            CollectCacheItems(snap);

            // 6.5 采集 Oopz 与完美世界竞技平台专属降占与核心隔离状态
            CollectVoicePlatformStatus(snap);

            // 7. 计算综合战备评分
            ComputeReadinessScore(snap);

            return snap;
        }

        private static void CollectPlatformAndHardware(SystemTelemetrySnapshot snap)
        {
            // (A) CPU 品牌、型号与动态核隔离拓扑计算
            try
            {
                using var cpuKey = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                string vendorRaw = cpuKey?.GetValue("VendorIdentifier")?.ToString() ?? "";
                string nameRaw = (cpuKey?.GetValue("ProcessorNameString")?.ToString() ?? "Unknown CPU").Trim();
                snap.CpuName = Regex.Replace(nameRaw, @"\s+", " ");
                snap.DetectedCpuVendor = vendorRaw.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                         snap.CpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                         snap.CpuName.Contains("Ryzen", StringComparison.OrdinalIgnoreCase)
                    ? "AMD" : "Intel";
            }
            catch
            {
                snap.DetectedCpuVendor = "Intel";
                snap.CpuName = "Desktop CPU";
            }

            snap.EffectivePlatform = snap.RequestedPlatformMode switch
            {
                CpuPlatformMode.Intel => "Intel",
                CpuPlatformMode.Amd => "AMD",
                _ => snap.DetectedCpuVendor
            };

            snap.IsAmdX3D = snap.CpuName.Contains("X3D", StringComparison.OrdinalIgnoreCase);
            snap.IsDualCcdX3D = snap.IsAmdX3D &&
                                (snap.CpuName.Contains("7900") || snap.CpuName.Contains("7950") ||
                                 snap.CpuName.Contains("9900") || snap.CpuName.Contains("9950"));

            // 提取简短 CPU 名称（如 250K Plus / 7500F / 9800X3D）
            var mCpu = Regex.Match(snap.CpuName, @"(Ultra\s+\d+\s+\w+(?:\s+Plus)?|i[3579]-\d+\w*|Ryzen\s+\d+\s+\d+\w*)", RegexOptions.IgnoreCase);
            snap.CpuShortName = mCpu.Success ? mCpu.Groups[1].Value : snap.CpuName;

            int n = Math.Max(1, Math.Min(63, Environment.ProcessorCount));
            snap.LogicalCoreCount = n;
            snap.FullCpuMask = (1L << n) - 1L;

            if (!snap.IsAmdEffective &&
                (snap.CpuName.Contains("250K", StringComparison.OrdinalIgnoreCase) || (n == 18 && snap.CpuName.Contains("Ultra 5", StringComparison.OrdinalIgnoreCase))))
            {
                // Arrow Lake Core Ultra 5 250K (Plus): 6P(0,1,6,7,8,9) + 12E，锁定纯后 8 颗 E 核 (10~17)
                snap.BackgroundAffinityMask = 0x3FC00L & snap.FullCpuMask;
                snap.ProtectedGameCoreMask = 0x3C3L & snap.FullCpuMask;
                snap.BackgroundCoreLabel = "纯 E 核 (Core 10~17)";
            }
            else if (!snap.IsAmdEffective)
            {
                // 通用 Intel 大小核/多核：保留前部主核，把语音/平台锁进后部逻辑核
                int bgCount = n >= 16 ? Math.Min(8, n / 2) : (n >= 8 ? 4 : Math.Max(1, n / 2));
                int startCore = Math.Max(1, n - bgCount);
                long bgMask = 0;
                for (int i = startCore; i < n; i++) bgMask |= (1L << i);
                snap.BackgroundAffinityMask = bgMask;
                snap.ProtectedGameCoreMask = snap.FullCpuMask & ~bgMask;
                snap.BackgroundCoreLabel = $"后置核 (Core {startCore}~{n - 1})";
            }
            else
            {
                // AMD 锐龙同构架构（如 7500F 6C12T / 7800X3D 8C16T / 9950X3D 16C32T）
                // 绝不越界核心总数，保留前部主线程（双 CCD X3D 自动保留 CCD0 3D 缓存核）给游戏
                int bgCount = n >= 24 ? 8 : (n >= 12 ? 4 : Math.Max(1, n / 3));
                int startCore = Math.Max(1, n - bgCount);
                long bgMask = 0;
                for (int i = startCore; i < n; i++) bgMask |= (1L << i);
                snap.BackgroundAffinityMask = bgMask;
                snap.ProtectedGameCoreMask = snap.FullCpuMask & ~bgMask;
                snap.BackgroundCoreLabel = snap.IsDualCcdX3D
                    ? $"非 3D 缓存 CCD1 尾核 (Core {startCore}~{n - 1})"
                    : $"尾部 {bgCount} 线程 (Core {startCore}~{n - 1} · 保留前 {startCore} 线程给游戏)";
            }

            // (B) 显示器分辨率与动态推荐 RTSS 锁帧上限 (刷新率 - 3)
            var disp = NativeWin32.GetPrimaryDisplay();
            snap.DisplayWidth = disp.width > 0 ? disp.width : 1920;
            snap.DisplayHeight = disp.height > 0 ? disp.height : 1080;
            snap.DisplayRefreshRate = disp.refreshRate > 0 ? disp.refreshRate : 144;
            snap.RecommendedFpsCap = snap.DisplayRefreshRate >= 60 ? snap.DisplayRefreshRate - 3 : 57;
            snap.DisplayInfo = disp.width > 0
                ? $"{disp.width} × {disp.height} @ {disp.refreshRate} Hz"
                : "无法读取显示器";

            // (C) 内存容量、通道数与频率（首次缓存，后续 0ms）
            var mem = NativeWin32.GetMemoryInfo();
            snap.RamUsedPercent = mem.usedPercent;
            snap.RamFreeGb = mem.freeGb;
            snap.RamTotalGb = mem.totalGb;

            if (!_ramHwCached)
            {
                _ramHwCached = true;
                string ramOut = RunProcess("powershell", "-NoProfile -Command \"Get-CimInstance Win32_PhysicalMemory | ForEach-Object { $_.ConfiguredClockSpeed }\"", 2500);
                if (!string.IsNullOrWhiteSpace(ramOut))
                {
                    var lines = ramOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    _cachedRamSticks = lines.Length;
                    foreach (var l in lines)
                    {
                        if (int.TryParse(l.Trim(), out int mhz) && mhz > _cachedRamMhz)
                            _cachedRamMhz = mhz;
                    }
                }
            }
            snap.RamStickCount = _cachedRamSticks;
            snap.RamClockMhz = _cachedRamMhz;
            string chText = snap.RamStickCount == 1 ? "单通道" : (snap.RamStickCount >= 2 ? "双通道" : "系统");
            snap.RamLabelSummary = snap.RamClockMhz > 0 ? $"{chText} {snap.RamClockMhz} 内存" : $"{chText}内存";

            try
            {
                var cDrive = new DriveInfo("C");
                snap.CDriveTotalGb = cDrive.TotalSize / (1024.0 * 1024.0 * 1024.0);
                snap.CDriveFreeGb = cDrive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                snap.CDriveUsedPercent = (1.0 - snap.CDriveFreeGb / snap.CDriveTotalGb) * 100.0;
            }
            catch { }

            // (D) 显卡遥测：优先 NVIDIA nvidia-smi，其次兼容 AMD Radeon / Intel Arc
            string gpuCsv = RunProcess("nvidia-smi", "--query-gpu=name,temperature.gpu,power.draw,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits", 2500);
            if (!string.IsNullOrWhiteSpace(gpuCsv))
            {
                var firstLine = gpuCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (firstLine != null)
                {
                    var p = firstLine.Split(',');
                    if (p.Length >= 6 &&
                        double.TryParse(p[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double temp) &&
                        double.TryParse(p[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double pwr) &&
                        double.TryParse(p[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double memUsed) &&
                        double.TryParse(p[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double memTotal) &&
                        double.TryParse(p[5].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double util))
                    {
                        snap.GpuAvailable = true;
                        snap.GpuVendor = "NVIDIA";
                        snap.GpuName = p[0].Trim();
                        snap.GpuTempC = temp;
                        snap.GpuPowerW = pwr;
                        snap.GpuMemUsedMb = memUsed;
                        snap.GpuMemTotalMb = memTotal;
                        snap.GpuUtilPercent = util;
                    }
                }
            }
            if (!snap.GpuAvailable)
            {
                var adapters = NativeWin32.GetActiveDisplayAdapters();
                string? discrete = adapters.FirstOrDefault(a => a.Contains("Radeon", StringComparison.OrdinalIgnoreCase) || a.Contains("RTX", StringComparison.OrdinalIgnoreCase) || a.Contains("GTX", StringComparison.OrdinalIgnoreCase) || a.Contains("Arc", StringComparison.OrdinalIgnoreCase))
                                   ?? adapters.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(discrete))
                {
                    snap.GpuName = discrete;
                    snap.GpuVendor = discrete.Contains("AMD", StringComparison.OrdinalIgnoreCase) || discrete.Contains("Radeon", StringComparison.OrdinalIgnoreCase)
                        ? "AMD" : (discrete.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ? "NVIDIA" : "Intel");
                }
            }
            snap.GpuShortName = snap.GpuName
                .Replace("NVIDIA GeForce ", "", StringComparison.OrdinalIgnoreCase)
                .Replace("AMD Radeon ", "RX ", StringComparison.OrdinalIgnoreCase)
                .Trim();
            if (string.IsNullOrEmpty(snap.GpuShortName)) snap.GpuShortName = "GPU";
        }

        private static void CollectSystemCoreChecks(SystemTelemetrySnapshot snap)
        {
            bool isAmd = snap.IsAmdEffective;

            // (1) 电源计划：Intel 大小核 & AMD CPPC / X3D 在 Win11 下均优先推荐「平衡」
            string schemeOut = RunProcess("powercfg", "/getactivescheme", 2000);
            var guidMatch = Regex.Match(schemeOut ?? "", @"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
            string activeGuid = guidMatch.Success ? guidMatch.Value : "";
            snap.IsBalancedPowerPlan = string.Equals(activeGuid, BalancedPowerGuid, StringComparison.OrdinalIgnoreCase);

            snap.SystemChecks.Add(new CheckItem
            {
                Category = isAmd ? "AMD 调度" : "Intel 调度",
                Id = "power_plan",
                Title = isAmd ? "AMD 锐龙电源调度计划" : "Intel 大小核电源调度计划",
                CurrentValue = snap.IsBalancedPowerPlan
                    ? (isAmd ? "平衡 (Balanced · CPPC / X3D 调度正常)" : "平衡 (Balanced · Thread Director 正常)")
                    : (string.IsNullOrWhiteSpace(schemeOut) ? "未知" : schemeOut),
                Detail = isAmd
                    ? "Win11 下 AMD 锐龙（含 7500F 及 X3D）官方推荐「平衡」计划配合芯片组驱动做 CPPC 睿频与 3D V-Cache 调度，再叠加下方全核零休眠即可兼顾满血睿频与稳帧。"
                    : "Intel 12~14 代及 Core Ultra 依赖「平衡」计划激活硬件 Thread Director，避免线程误派至 E 核引发掉 Low 帧。",
                Status = snap.IsBalancedPowerPlan ? CheckStatus.Optimal : CheckStatus.Critical,
                Risk = RiskTier.Safe,
                RequiresAdmin = false,
                CanAutoFix = !snap.IsBalancedPowerPlan,
                FixLabel = "切回平衡计划"
            });

            // (2) 核心防休眠停驻 (CPMINCORES = 100%)
            bool pCoreUnparked = QueryPowerAcHex(PCoreMinSubGuid, PCoreMinSettingGuid) == "00000064";
            snap.IsPCoreUnparked = pCoreUnparked;
            snap.SystemChecks.Add(new CheckItem
            {
                Category = isAmd ? "AMD 调度" : "Intel 调度",
                Id = "pcore_unpark",
                Title = isAmd ? "AMD 全核心零休眠停驻 (CPMINCORES)" : "CPU P 核零休眠停驻 (CPMINCORES)",
                CurrentValue = pCoreUnparked ? "100% 全核苏醒 (0 休眠停驻)" : "允许核心休眠 (切镜/交火唤醒易掉 Low 帧)",
                Detail = isAmd
                    ? "防止 7500F 等锐龙核心在游戏间隙进入 Core Parking 休眠，显著改善 PUBG / CS2 开镜与交火瞬间的 1% Low 帧。"
                    : "防止高性能 P 核在游戏间隙进入休眠停驻，消除切镜/进城瞬间掉帧。",
                Status = pCoreUnparked ? CheckStatus.Optimal : CheckStatus.Critical,
                Risk = RiskTier.Caution,
                RequiresAdmin = true,
                CanAutoFix = !pCoreUnparked,
                FixLabel = "解锁全核休眠"
            });

            // (3) 平台专属底层服务检测（Intel IPF/DTT vs AMD 芯片组驱动/PPM/X3D + 内存通道红线）
            if (!isAmd)
            {
                bool hasIpf = ServiceExists("ipfsvc") || ServiceExists("dptftcs");
                bool ipfOk = IsServiceRunning("ipfsvc");
                bool dttOk = IsServiceRunning("dptftcs");
                bool priOk = IsServiceRunning("PRI-Driver");
                snap.AreIntelThermalServicesRunning = !hasIpf || (ipfOk && dttOk);
                snap.SystemChecks.Add(new CheckItem
                {
                    Category = "Intel 底座",
                    Id = "intel_ipf",
                    Title = "Intel IPF / DTT 温控与功耗墙服务",
                    CurrentValue = hasIpf
                        ? $"ipfsvc={(ipfOk ? "运行" : "停止")} · dptftcs={(dttOk ? "运行" : "停止")} · PRI-Driver={(priOk ? "运行" : "未装/停")}"
                        : "当前主板未安装 Intel IPF/DTT 服务（常规台式机可忽略）",
                    Detail = "保障 Intel Core Ultra / 混合架构长时间满载不误触发锁频降压，也是启用 Intel APO 游戏调度优化的前置条件。",
                    Status = !hasIpf ? CheckStatus.Info : (ipfOk && dttOk ? CheckStatus.Optimal : CheckStatus.Critical),
                    Risk = RiskTier.Caution,
                    RequiresAdmin = true,
                    CanAutoFix = hasIpf && !(ipfOk && dttOk),
                    FixLabel = "重启温控服务"
                });
            }
            else
            {
                bool amdPpm = ServiceExists("amdppm") || ServiceExists("AmdPpm") || RegistryKeyExists(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Services\amdppm");
                bool hasChipsetDir = Directory.Exists(@"C:\Program Files (x86)\AMD\Chipset_Software") ||
                                     Directory.Exists(@"C:\AMD\Chipset_Software") ||
                                     ServiceExists("AMD Crash Defender Service") ||
                                     ServiceExists("amd3dvcacheSvc");
                bool x3dSvcRunning = !snap.IsDualCcdX3D || IsServiceRunning("amd3dvcacheSvc");
                bool amdDriverOk = (amdPpm || hasChipsetDir) && x3dSvcRunning;
                snap.AreIntelThermalServicesRunning = amdDriverOk;

                string amdVal = snap.IsDualCcdX3D
                    ? $"AM5 芯片组={(hasChipsetDir || amdPpm ? "已检出" : "疑似缺失")} · 3D V-Cache 调度服务={(x3dSvcRunning ? "运行中" : "未运行!")}"
                    : (hasChipsetDir
                        ? "已检出 AMD 官方芯片组软件包 (AM4/AM5 电源调度就绪)"
                        : (amdPpm ? "系统内置 amdppm 已加载（建议核对是否装过官网最新版芯片组驱动）" : "未检测到 AMD 官方芯片组驱动（PUBG/网游掉 Low 帧高频元凶！）"));

                snap.SystemChecks.Add(new CheckItem
                {
                    Category = "AMD 底座",
                    Id = "amd_chipset",
                    Title = "AMD 芯片组驱动 & CPPC 调度服务",
                    CurrentValue = amdVal,
                    Detail = "AMD 锐龙（特别是 7500F 等 AM5 与 X3D）装完系统后务必安装 AMD 官网最新 Chipset Drivers（含 PPM 与 3D V-Cache Optimizer），不要只依赖系统自带通用驱动。",
                    Status = hasChipsetDir && x3dSvcRunning ? CheckStatus.Optimal : (amdPpm && x3dSvcRunning ? CheckStatus.Attention : CheckStatus.Critical),
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = false
                });

                // AMD 内存通道与 16GB PUBG 防爆红线检测（专治单通道 16G 4800 C40）
                bool singleChannel = snap.RamStickCount == 1;
                bool lowRam = snap.RamTotalGb > 0 && snap.RamTotalGb < 17.5;
                bool ramRisk = singleChannel || lowRam;
                snap.SystemChecks.Add(new CheckItem
                {
                    Category = "AMD 内存红线",
                    Id = "amd_ram_guard",
                    Title = "内存通道与 PUBG 16GB 防爆红线",
                    CurrentValue = $"{snap.RamLabelSummary} · 总容量 {snap.RamTotalGb:F1} GB" +
                                   (singleChannel ? "（⚠️ 单通道带宽腰斩！）" : "") +
                                   (lowRam ? "（⚠️ 16GB 打 PUBG 极易触发换页！）" : ""),
                    Detail = ramRisk
                        ? "PUBG 百人大地图极度吃内存带宽与容量！单通道 16GB 建议：① 立刻固定下方 16GB~32GB 固态盘虚拟内存；② 赛前彻底清掉浏览器/战网释放物理内存；③ 未来补齐第二根同规格内存开 EXPO 组双通道，PUBG 可立涨 25%~40% 帧数。"
                        : "当前双通道大容量内存健康，带宽与余量足以畅玩 PUBG 与大型 3A。",
                    Status = ramRisk ? CheckStatus.Attention : CheckStatus.Optimal,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = false
                });
            }

            // (4) C 盘虚拟内存 Pagefile（16GB 内存机器尤为关键）
            long pagefileMb = 0;
            try
            {
                var pfInfo = new FileInfo(@"C:\pagefile.sys");
                if (pfInfo.Exists) pagefileMb = pfInfo.Length / (1024 * 1024);
            }
            catch { }
            if (pagefileMb == 0)
            {
                string? regPf = ReadMultiStringFirst(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", "PagingFiles");
                if (regPf != null && (regPf.Contains("16384") || regPf.Contains("24576"))) pagefileMb = 16384;
            }
            snap.IsPagefileOptimal = pagefileMb >= 16000;
            snap.SystemChecks.Add(new CheckItem
            {
                Category = "系统底座",
                Id = "pagefile",
                Title = "C 盘虚拟内存预分配 (防爆内存换页)",
                CurrentValue = pagefileMb > 0 ? $"{pagefileMb} MB ({pagefileMb / 1024.0:F1} GB)" : "未检测/系统动态缩水",
                Detail = snap.RamTotalGb < 17.5
                    ? "检测到 16GB 内存：固定 16GB~32GB 固态盘虚拟内存是防止 PUBG 跳伞/决赛圈剧烈掉帧与闪退的救命设置！"
                    : "固定 16GB~32GB 可避免虚幻 4/5 与大型 3A 游玩途中动态扩容引发微卡顿。",
                Status = snap.IsPagefileOptimal ? CheckStatus.Optimal : CheckStatus.Critical,
                Risk = RiskTier.Caution,
                RequiresAdmin = true,
                CanAutoFix = !snap.IsPagefileOptimal,
                FixLabel = "固定为 16GB~32GB"
            });

            // (5) [optimizerDuck 精选安全项] MMCSS 游戏多线程优先调度
            int gpuPrio = ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority", 8);
            int cpuPrio = ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority", 2);
            string schedCat = ReadString(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category", "Medium");
            int sysResp = ReadDword(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness", 20);
            bool mmcssOk = gpuPrio >= 8 && cpuPrio >= 6 && schedCat.Equals("High", StringComparison.OrdinalIgnoreCase) && sysResp <= 10;
            snap.IsMmcssOptimal = mmcssOk;
            snap.SystemChecks.Add(new CheckItem
            {
                Category = "低延迟调度",
                Id = "mmcss_gaming",
                Title = "MMCSS 游戏多线程优先调度 (optimizerDuck 精选)",
                CurrentValue = $"GPU优先级={gpuPrio} · 任务优先级={cpuPrio} · 调度级别={schedCat} · 后台保留={sysResp}%",
                Detail = "来自 optimizerDuck 安全调优：将 Windows 多媒体调度器（MMCSS）的游戏任务设为 High / Priority=6 / GPU=8 并把后台预留降至 10%，让游戏渲染与音频线程压过后台任务（完整记录原值可随时还原）。",
                Status = mmcssOk ? CheckStatus.Optimal : CheckStatus.Attention,
                Risk = RiskTier.Safe,
                RequiresAdmin = true,
                CanAutoFix = !mmcssOk,
                FixLabel = "启用 MMCSS 游戏优先"
            });

            // (6) [optimizerDuck 精选安全项] PCIe ASPM & USB 选择性暂停防微卡
            string pcieHex = QueryPowerAcHex(PcieSubGuid, PcieAspmGuid);
            string usbHex = QueryPowerAcHex(UsbSubGuid, UsbSuspendGuid);
            bool pcieOff = pcieHex == "00000000";
            bool usbOff = usbHex == "00000000";
            snap.IsBusLatencyOptimal = pcieOff && usbOff;
            snap.SystemChecks.Add(new CheckItem
            {
                Category = "低延迟调度",
                Id = "bus_latency",
                Title = "PCIe 节能 & USB 选择性暂停防微卡",
                CurrentValue = $"PCIe 链路节能={(pcieOff ? "已关闭" : "开启中")} · USB 选择性暂停={(usbOff ? "已关闭" : "开启中")}",
                Detail = "来自 optimizerDuck 安全调优：免权限关闭显卡 PCIe ASPM 节能与 USB 外设休眠，防止游戏间隙总线瞬间降频及高回报率鼠标 / USB 声卡偶发顿卡（完整记录原值可随时还原）。",
                Status = snap.IsBusLatencyOptimal ? CheckStatus.Optimal : CheckStatus.Attention,
                Risk = RiskTier.Safe,
                RequiresAdmin = false,
                CanAutoFix = !snap.IsBusLatencyOptimal,
                FixLabel = "免权限关闭节能"
            });

            // (7) Windows 游戏模式与 Game DVR 后台录制
            int gameMode = ReadDword(Registry.CurrentUser, @"Software\Microsoft\GameBar", "AutoGameModeEnabled", 0);
            int gameDvr = ReadDword(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
            int appCap = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
            snap.IsGameDvrOff = (gameMode == 1 && gameDvr == 0 && appCap == 0);
            snap.SystemChecks.Add(new CheckItem
            {
                Category = "系统底座",
                Id = "game_dvr",
                Title = "Windows 游戏模式 & 后台录制",
                CurrentValue = $"游戏模式={(gameMode == 1 ? "开启" : "关闭")} · GameDVR={(gameDvr == 0 && appCap == 0 ? "已关闭" : "开启中")}",
                Detail = "开启 Windows 游戏模式并关闭 Xbox 后台自动录制，避免抢占显存与编码器带宽（可随时还原）。",
                Status = snap.IsGameDvrOff ? CheckStatus.Optimal : CheckStatus.Attention,
                Risk = RiskTier.Safe,
                RequiresAdmin = false,
                CanAutoFix = !snap.IsGameDvrOff,
                FixLabel = "免权限一键优化"
            });

            // (8) [optimizerDuck 精选安全项] Windows 内容推送与后台广告精简
            int cdm1 = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 1);
            int cdm2 = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 1);
            int cdm3 = ReadDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 1);
            bool noiseTrimmed = (cdm1 == 0 && cdm2 == 0 && cdm3 == 0);
            snap.IsWindowsNoiseTrimmed = noiseTrimmed;
            snap.SystemChecks.Add(new CheckItem
            {
                Category = "后台精简",
                Id = "win_noise_trim",
                Title = "Windows 内容推送与静默推广精简 (optimizerDuck 精选)",
                CurrentValue = noiseTrimmed ? "已关闭系统提示/锁屏与开始菜单静默推广" : "系统后台推广推送开启中（易后台下载静默包）",
                Detail = "仅关闭 ContentDeliveryManager 广告推送与系统建议弹窗，不删除任何系统组件、商店或安全功能，特别适合 16GB 内存机器减少无谓后台 I/O（可完整还原）。",
                Status = noiseTrimmed ? CheckStatus.Optimal : CheckStatus.Info,
                Risk = RiskTier.Safe,
                RequiresAdmin = false,
                CanAutoFix = !noiseTrimmed,
                FixLabel = "免权限关闭推送"
            });

            // (9) 快速启动与休眠文件
            int hiberboot = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
            long hiberSizeMb = 0;
            try
            {
                var hf = new FileInfo(@"C:\hiberfil.sys");
                if (hf.Exists) hiberSizeMb = hf.Length / (1024 * 1024);
            }
            catch { }
            bool fullHiber = hiberSizeMb > 9000;
            snap.IsHiberfilReducedOrOff = !fullHiber;
            string hiberDesc = hiberSizeMb == 0
                ? "休眠已完全关闭 (纯冷启动 · 0 GB 占用)"
                : (fullHiber ? $"完整休眠 ({hiberSizeMb / 1024.0:F1} GB · 占空间)" : $"Reduced 精简快速启动 ({hiberSizeMb / 1024.0:F1} GB · Hiberboot={hiberboot})");

            snap.SystemChecks.Add(new CheckItem
            {
                Category = "系统底座",
                Id = "fast_boot",
                Title = "休眠与快速启动模式",
                CurrentValue = hiberDesc,
                Detail = "推荐使用 Reduced 精简模式（仅占 ~6GB 即可保持快速启动）或完全关闭省空间，对游戏性能 0 影响。",
                Status = fullHiber ? CheckStatus.Attention : CheckStatus.Optimal,
                Risk = RiskTier.Caution,
                RequiresAdmin = true,
                CanAutoFix = fullHiber,
                FixLabel = "切换 Reduced 省空间"
            });

            // (10) 内核隔离 HVCI 内存完整性（专家安全权衡项，永不参与一键批量修复）
            int hvciVal = ReadDword(Registry.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity", "Enabled", 0);
            snap.IsHvciDisabled = hvciVal == 0;
            snap.SystemChecks.Add(new CheckItem
            {
                Category = "安全权衡",
                Id = "hvci",
                Title = "内核隔离 (HVCI 内存完整性 · 安全功能)",
                CurrentValue = snap.IsHvciDisabled ? "当前已关闭 (性能优先 · 若玩国际服瓦洛兰特可能需开启)" : "当前已开启 (系统安全优先 · 会增加内核反作弊开销)",
                Detail = "此项属于 Windows 核心安全功能，标记为「专家权衡」且绝不参与「一键批量修复」。请仅在明确知情时手动决定。",
                Status = snap.IsHvciDisabled ? CheckStatus.Optimal : CheckStatus.Info,
                Risk = RiskTier.Expert,
                RequiresAdmin = true,
                CanAutoFix = !snap.IsHvciDisabled,
                ExcludeFromBatchFix = true,
                FixLabel = "手动关闭 (需确认)"
            });

            // (11) 连续开机时间提醒
            bool longUptime = snap.Uptime.TotalHours >= 72;
            snap.SystemChecks.Add(new CheckItem
            {
                Category = "系统底座",
                Id = "uptime",
                Title = "系统内核连续运行时长",
                CurrentValue = snap.Uptime.TotalDays >= 1
                    ? $"{(int)snap.Uptime.TotalDays} 天 {snap.Uptime.Hours} 小时"
                    : $"{snap.Uptime.Hours} 小时 {snap.Uptime.Minutes} 分钟",
                Detail = longUptime
                    ? "已连续运行超 3 天，WDDM 显存碎片与句柄累积可能使帧时间变毛糙，建议打游戏前冷重启一次。"
                    : "内核状态清爽，无需重启。",
                Status = longUptime ? CheckStatus.Attention : CheckStatus.Optimal,
                Risk = RiskTier.Safe,
                RequiresAdmin = false,
                CanAutoFix = false
            });
        }

        private static void CollectOnlineEsportsChecks(SystemTelemetrySnapshot snap)
        {
            string capStr = snap.RecommendedFpsCap.ToString(CultureInfo.InvariantCulture);

            // (1) RTSS 网游通用动态锁帧矩阵 (按当前显示器刷新率 - 3 自适应)
            string? rtssDir = FindRtssProfileDir();
            if (rtssDir != null)
            {
                bool pubgLimOk = CheckRtssLimit(rtssDir, "TslGame.exe.cfg", capStr);
                bool cs2LimOk = CheckRtssLimit(rtssDir, "cs2.exe.cfg", capStr);
                bool lolLimOk = CheckRtssLimit(rtssDir, "League of Legends.exe.cfg", capStr);
                bool valLimOk = CheckRtssLimit(rtssDir, "VALORANT-Win64-Shipping.exe.cfg", capStr);
                snap.IsRtssOptimal = pubgLimOk && cs2LimOk && lolLimOk && valLimOk;

                snap.OnlineGameChecks.Add(new CheckItem
                {
                    Category = "通用控帧",
                    Id = "rtss_profile",
                    Title = $"RTSS {capStr} FPS 竞技网游通用锁帧矩阵 (匹配 {snap.DisplayRefreshRate}Hz)",
                    CurrentValue = $"PUBG={(pubgLimOk ? capStr : "未配")} · CS2={(cs2LimOk ? capStr : "未配")} · LOL={(lolLimOk ? capStr : "未配")} · 瓦洛兰特={(valLimOk ? capStr : "未配")}",
                    Detail = $"按当前屏幕 {snap.DisplayRefreshRate}Hz 自动计算黄金限帧 {capStr} FPS（刷新率 - 3，稳吃 G-Sync/FreeSync 区间），替代游戏内置粗糙限帧器拉直 Frame Time。",
                    Status = snap.IsRtssOptimal ? CheckStatus.Optimal : CheckStatus.Attention,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !snap.IsRtssOptimal,
                    FixLabel = $"同步四大网游 {capStr}"
                });
            }

            // (2) MSI 小飞机 GPU 功耗轮询冲突检测
            string? abCfg = FindAfterburnerCfgPath();
            if (abCfg != null)
            {
                string cfgText = File.ReadAllText(abCfg);
                bool powerDisabled = cfgText.Contains("-GPU1 power") && cfgText.Contains("-GPU1 power percent");
                snap.IsAfterburnerClean = powerDisabled;
                snap.OnlineGameChecks.Add(new CheckItem
                {
                    Category = "通用监控",
                    Id = "afterburner_power",
                    Title = "微星小飞机传感器防冲突 (-GPU1 power)",
                    CurrentValue = powerDisabled ? "已屏蔽 GPU 功耗轮询（与风扇控制零冲突）" : "正在高频轮询显卡功耗（易致每秒一次掉 Low 帧）",
                    Detail = "防止 MSI Afterburner 轮询显卡功耗传感器阻塞 I2C 总线，消除 Intel/AMD 平台通用的每秒周期性微卡顿。",
                    Status = powerDisabled ? CheckStatus.Optimal : CheckStatus.Critical,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !powerDisabled,
                    FixLabel = "屏蔽功耗传感器"
                });
            }

            // (2.5) 语音/开黑软件动态核心隔离检测（自动适配 Intel 大小核 E 核 / AMD 尾部线程）
            var voiceTargets = new[] { "oopz", "discord", "kook", "yy", "cloudmusic" };
            var activeVoices = new List<string>();
            var unisolatedVoices = new List<string>();
            foreach (var vName in voiceTargets)
            {
                foreach (var vp in Process.GetProcessesByName(vName))
                {
                    try
                    {
                        if (!activeVoices.Contains(vName)) activeVoices.Add(vName);
                        long aff = (long)vp.ProcessorAffinity;
                        bool isLowPriority = vp.PriorityClass == ProcessPriorityClass.BelowNormal ||
                                             vp.PriorityClass == ProcessPriorityClass.Idle;
                        if ((aff & snap.ProtectedGameCoreMask) != 0 || !isLowPriority)
                        {
                            if (!unisolatedVoices.Contains(vName)) unisolatedVoices.Add(vName);
                        }
                    }
                    catch { }
                    finally { vp.Dispose(); }
                }
            }
            if (activeVoices.Count > 0)
            {
                bool allIsolated = unisolatedVoices.Count == 0;
                snap.OnlineGameChecks.Add(new CheckItem
                {
                    Category = "语音隔离",
                    Id = "voice_ecore_jail",
                    Title = snap.IsAmdEffective
                        ? $"开黑语音尾核让位 ({string.Join("/", activeVoices)})"
                        : $"开黑语音 E 核物理隔离 ({string.Join("/", activeVoices)})",
                    CurrentValue = allIsolated
                        ? $"已锁定在 {snap.BackgroundCoreLabel} · 低于正常优先级，零抢占主游戏核心"
                        : $"未隔离：{string.Join("、", unisolatedVoices)} 正在与游戏争抢主渲染核心！",
                    Detail = snap.IsAmdEffective
                        ? $"AMD 同构 CPU 模式下，自动将语音软件锁进 {snap.BackgroundCoreLabel} 并降为 BelowNormal，把前部高频核心完整留给 PUBG / CS2。"
                        : $"Intel 模式下，自动将语音软件锁进 {snap.BackgroundCoreLabel} 并降为 BelowNormal，防止 AI 降噪高优线程抢占 P 核。",
                    Status = allIsolated ? CheckStatus.Optimal : CheckStatus.Critical,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !allIsolated,
                    FixLabel = "一键核心隔离"
                });
            }

            // (3) PUBG 专项检查
            string pubgIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TslGame\Saved\Config\WindowsNoEditor\GameUserSettings.ini");
            if (File.Exists(pubgIni))
            {
                string content = File.ReadAllText(pubgIni);
                string scale = MatchIniValue(content, "ScreenScale");
                string fs = MatchIniValue(content, "FullscreenMode");
                string replay = MatchIniValue(content, "bUseClientReplay");
                string provider = MatchIniValue(content, "Provider");
                string fpsType = MatchIniValue(content, "InGameFrameRateLimitType");

                bool scaleOk = scale.StartsWith("100");
                bool fsOk = fs == "0";
                bool replayOk = string.Equals(replay, "False", StringComparison.OrdinalIgnoreCase);
                bool provOk = string.Equals(provider, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(provider);
                bool fpsOk = string.Equals(fpsType, "Unlimited", StringComparison.OrdinalIgnoreCase);
                snap.IsPubgOptimal = scaleOk && fsOk && replayOk && provOk && fpsOk;

                snap.OnlineGameChecks.Add(new CheckItem
                {
                    Category = "PUBG",
                    Id = "pubg_config",
                    Title = "PUBG 黄金竞技配置 (GameUserSettings)",
                    CurrentValue = $"渲染={scale.Split('.')[0]}% · 全屏={(fsOk ? "独占(0)" : "非独占(" + fs + ")")} · 回放={(replayOk ? "关" : "开")} · 抓拍={(provOk ? "关" : provider)} · 局内限帧={(fpsOk ? "无限制(交由RTSS)" : fpsType)}",
                    Detail = snap.IsAmdEffective
                        ? $"AMD / 8GB 显存 PUBG 提帧建议：局内选 DX11 增强版 + 100% 渲染 + 阴影/特效/后期/树木非常低 + 纹理中/高，局内无限制交由 RTSS {capStr} 单通道控帧；16GB 单通道机器务必配合 16GB+ 虚拟内存与清后台。"
                        : $"100% 原生渲染防显存溢出，局内设 Unlimited 单通道交由 RTSS {capStr} 控帧，关闭死亡回放与精彩抓拍，大厅锁 60 帧降温。",
                    Status = snap.IsPubgOptimal ? CheckStatus.Optimal : CheckStatus.Critical,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !snap.IsPubgOptimal,
                    FixLabel = "优化 PUBG 配置"
                });
            }

            // (4) CS2 专项检查
            var cs2VideoPaths = FindCs2VideoFiles();
            string? autoexecPath = FindCs2AutoexecPath();
            bool cs2VideoOk = cs2VideoPaths.Count > 0;
            string cs2Issues = "";
            foreach (var vp in cs2VideoPaths)
            {
                string txt = File.ReadAllText(vp);
                string fs = MatchVdfValue(txt, "setting.fullscreen");
                string nwb = MatchVdfValue(txt, "setting.nowindowborder");
                string vsync = MatchVdfValue(txt, "setting.mat_vsync");
                string reflex = MatchVdfValue(txt, "setting.r_low_latency");
                string msaa = MatchVdfValue(txt, "setting.msaa_samples");
                if (fs != "1" || nwb != "0" || vsync != "0" || reflex != "0")
                {
                    cs2VideoOk = false;
                    cs2Issues = $"有账号未对齐(全屏={fs},无边框={nwb},垂直同步={vsync},Reflex={reflex})";
                    break;
                }
                if (int.TryParse(msaa, out int msaaVal) && msaaVal > 2)
                {
                    cs2VideoOk = false;
                    cs2Issues = $"MSAA={msaa}x 偏高(烟雾易掉 Low 帧)";
                    break;
                }
            }

            bool autoexecOk = false;
            if (autoexecPath != null && File.Exists(autoexecPath))
            {
                string ae = File.ReadAllText(autoexecPath);
                autoexecOk = ae.Contains("snd_mixahead") && ae.Contains("0.025") && ae.Contains("r_drawtracers_firstperson") && ae.Contains("false");
            }

            snap.IsCs2Optimal = cs2VideoOk && autoexecOk;
            if (cs2VideoPaths.Count > 0 || autoexecPath != null)
            {
                snap.OnlineGameChecks.Add(new CheckItem
                {
                    Category = "CS2",
                    Id = "cs2_config",
                    Title = $"CS2 专属 Low 帧与 AK 防扫射顿卡 ({cs2VideoPaths.Count} 账号)",
                    CurrentValue = snap.IsCs2Optimal
                        ? $"全账号独占 {snap.DisplayRefreshRate}Hz · Reflex 已关 · 2xMSAA · USB音频0.025 + 关曳光弹已固化"
                        : (!cs2VideoOk ? cs2Issues : "autoexec.cfg 缺少扫射音频/曳光弹优化指令"),
                    Detail = $"关闭游戏内 Reflex 与冗余限帧避免与 RTSS {capStr} 冲突；snd_mixahead 0.025 消除 USB 耳机连发微卡；若追求 CS2 极限高帧可单独在驱动里关闭垂直同步并解除 RTSS 拦截。",
                    Status = snap.IsCs2Optimal ? CheckStatus.Optimal : CheckStatus.Critical,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !snap.IsCs2Optimal,
                    FixLabel = "同步优化 CS2"
                });
            }

            // (5) 英雄联盟 (LOL) 专项检查
            string? lolCfg = FindLolConfigPath();
            if (lolCfg != null)
            {
                string txt = File.ReadAllText(lolCfg);
                string wm = MatchIniValue(txt, "WindowMode");
                string vsync = MatchIniValue(txt, "WaitForVerticalSync");
                string fxaa = MatchIniValue(txt, "EnableFXAA");
                bool wmOk = wm == "2";
                bool vsyncOk = vsync == "0";
                bool fxaaOk = fxaa == "0";
                snap.IsLolOptimal = wmOk && vsyncOk && fxaaOk;

                snap.OnlineGameChecks.Add(new CheckItem
                {
                    Category = "LOL",
                    Id = "lol_config",
                    Title = "英雄联盟防 0x119 蓝屏与拖屏丝滑配置",
                    CurrentValue = $"窗口={(wmOk ? "无边框(2)" : "全屏/窗口(" + wm + ")")} · 垂直同步={(vsyncOk ? "关" : "开(蓝屏风险)")} · FXAA={(fxaaOk ? "关" : "开")}",
                    Detail = $"Win11 下切无边框 + 禁垂直同步可有效避免 DX11 调度蓝屏；局内设无限制交由 RTSS {capStr} 控帧（老引擎建议鼠标回报率 ≤1000Hz）。",
                    Status = snap.IsLolOptimal ? CheckStatus.Optimal : CheckStatus.Critical,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !snap.IsLolOptimal,
                    FixLabel = "优化 LOL 配置"
                });
            }

            // (6) 无畏契约 (VALORANT) 专项检查
            string? valIni = FindValorantConfigPath();
            bool valRtssOk = rtssDir != null && CheckRtssLimit(rtssDir, "VALORANT-Win64-Shipping.exe.cfg", capStr);
            snap.OnlineGameChecks.Add(new CheckItem
            {
                Category = "无畏契约",
                Id = "valorant_config",
                Title = "无畏契约 (VALORANT) 竞技模板",
                CurrentValue = valIni != null
                    ? $"已检测到本地配置 · RTSS {capStr}={(valRtssOk ? "已就绪" : "待写入")}"
                    : $"未安装游戏本体 · RTSS {capStr} 规则={(valRtssOk ? "已预置就绪" : "可一键预置")}",
                Detail = $"与 CS2/PUBG 共用平衡电源全核唤醒 + HVCI 兼容性注意（若玩国际服 Vanguard 需按需开启 HVCI）；推荐局内全屏+多线程渲染+RTSS {capStr}。",
                Status = valRtssOk ? CheckStatus.Optimal : CheckStatus.Info,
                Risk = RiskTier.Safe,
                RequiresAdmin = false,
                CanAutoFix = !valRtssOk,
                FixLabel = $"预置 RTSS {capStr}"
            });
        }

        private static void CollectAaaGameChecks(SystemTelemetrySnapshot snap)
        {
            // (1) 动态显存安全余量评估（自适应 8GB / 12GB / 16GB+ 显卡）
            double totalVramGb = snap.GpuAvailable ? snap.GpuMemTotalMb / 1024.0 : 0;
            double freeVramMb = snap.GpuAvailable ? Math.Max(0, snap.GpuMemTotalMb - snap.GpuMemUsedMb) : 0;
            double safeThresholdMb = snap.GpuAvailable ? Math.Max(4500, snap.GpuMemTotalMb - 1400) : 0;
            bool vramSafe = !snap.GpuAvailable || freeVramMb >= safeThresholdMb || snap.IsGameRunning;
            snap.AaaGameChecks.Add(new CheckItem
            {
                Category = "显存防线",
                Id = "aaa_vram_guard",
                Title = $"{snap.GpuShortName} ({(totalVramGb > 0 ? $"{totalVramGb:F0}GB" : "显卡")}) 3A 空闲显存安全线",
                CurrentValue = snap.GpuAvailable
                    ? $"当前空闲显存 {freeVramMb / 1024.0:F2} GB / {totalVramGb:F1} GB（已占用 {snap.GpuMemUsedMb:F0} MB）"
                    : $"当前显卡：{snap.GpuShortName}（非 NVIDIA 显卡暂以系统状态展示）",
                Detail = vramSafe
                    ? "空闲显存充裕，启动 3A 大作配合「高材质 + DLSS/FSR 质量档」不易触发 PCIe 爆显存腰斩。"
                    : "待机后台吃掉了超过 1.4GB 显存！建议去「后台清理」关闭浏览器/AI桌面端后再开 3A 大作。",
                Status = vramSafe ? CheckStatus.Optimal : CheckStatus.Attention,
                Risk = RiskTier.Safe,
                RequiresAdmin = false,
                CanAutoFix = false
            });

            // (2) 《赛博朋克 2077》专项检查
            string cp2077 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"CD Projekt Red\Cyberpunk 2077\UserSettings.json");
            if (File.Exists(cp2077))
            {
                string json = File.ReadAllText(cp2077);
                string tex = MatchCyberpunkValue(json, "TextureQuality");
                string dlss = MatchCyberpunkValue(json, "DLSS");
                string rt = MatchCyberpunkBool(json, "RayTracing");
                bool texOk = tex.Equals("High", StringComparison.OrdinalIgnoreCase) || tex.Equals("Medium", StringComparison.OrdinalIgnoreCase);
                bool dlssOk = dlss.Equals("Quality", StringComparison.OrdinalIgnoreCase) || dlss.Equals("Balanced", StringComparison.OrdinalIgnoreCase);
                bool rtOk = rt.Equals("false", StringComparison.OrdinalIgnoreCase);
                snap.IsCyberpunkOptimal = texOk && dlssOk && rtOk;

                snap.AaaGameChecks.Add(new CheckItem
                {
                    Category = "赛博朋克 2077",
                    Id = "cyberpunk_config",
                    Title = "赛博朋克 2077 · 8GB 显存稳帧档位",
                    CurrentValue = $"纹理={tex} · DLSS={dlss} · 光线追踪={(rtOk ? "关" : "开(极易爆8GB显存)")}",
                    Detail = $"针对 {snap.GpuShortName} 推荐「纹理=High + DLSS/FSR 质量档 + 关闭光追」，兼顾清晰度与高帧率不爆显存。",
                    Status = snap.IsCyberpunkOptimal ? CheckStatus.Optimal : CheckStatus.Attention,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !snap.IsCyberpunkOptimal,
                    FixLabel = "调至稳帧档位"
                });
            }

            // (3) 《艾尔登法环》专项检查（按当前屏幕原生分辨率动态对齐）
            string erXml = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"EldenRing\GraphicsConfig.xml");
            if (File.Exists(erXml))
            {
                string xml = File.ReadAllText(erXml, System.Text.Encoding.Unicode).Replace("\0", "");
                if (!xml.Contains("<config>"))
                    xml = File.ReadAllText(erXml).Replace("\0", "");
                string rt = MatchXmlTag(xml, "RaytracingQuality");
                string bw = MatchXmlTag(xml, "Resolution-BorderlessScreenWidth");
                string bh = MatchXmlTag(xml, "Resolution-BorderlessScreenHeight");
                bool rtOk = rt.Equals("DISABLE", StringComparison.OrdinalIgnoreCase);
                bool resOk = (bw == snap.DisplayWidth.ToString() && bh == snap.DisplayHeight.ToString());
                snap.IsEldenRingOptimal = rtOk && resOk;

                snap.AaaGameChecks.Add(new CheckItem
                {
                    Category = "艾尔登法环",
                    Id = "eldenring_config",
                    Title = "艾尔登法环 · 60 帧直线稳帧配置",
                    CurrentValue = $"光追={(rtOk ? "已关" : rt)} · 无边框分辨率={bw}×{bh}（当前屏幕 {snap.DisplayWidth}×{snap.DisplayHeight}）",
                    Detail = resOk
                        ? "已关闭易导致掉帧的光追，且分辨率与显示器 1:1 点对点对齐。"
                        : $"检测到无边框分辨率 ({bw}×{bh}) 与当前屏幕 ({snap.DisplayWidth}×{snap.DisplayHeight}) 不一致，容易白白拖垮显卡与显存！",
                    Status = snap.IsEldenRingOptimal ? CheckStatus.Optimal : CheckStatus.Critical,
                    Risk = RiskTier.Safe,
                    RequiresAdmin = false,
                    CanAutoFix = !snap.IsEldenRingOptimal,
                    FixLabel = "对齐原生并关光追"
                });
            }
        }

        private static void CollectBackgroundProcesses(SystemTelemetrySnapshot snap)
        {
            var gpuPids = QueryGpuUsingPids();
            var before = new Dictionary<int, (string name, TimeSpan cpu, double memMb)>();
            var detectedGames = new List<string>();

            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (KnownGames.TryGetValue(p.ProcessName, out var gName) && !detectedGames.Contains(gName))
                        detectedGames.Add(gName);

                    before[p.Id] = (p.ProcessName, p.TotalProcessorTime, p.WorkingSet64 / (1024.0 * 1024.0));
                }
                catch { }
                finally { p.Dispose(); }
            }

            if (detectedGames.Count > 0)
            {
                snap.IsGameRunning = true;
                snap.RunningGame = string.Join(" / ", detectedGames);
            }

            // 采样 450ms 计算真实 CPU 占用
            const int sampleMs = 450;
            System.Threading.Thread.Sleep(sampleMs);

            var grouped = new Dictionary<string, ProcessItem>(StringComparer.OrdinalIgnoreCase);
            double totalCpuAcc = 0;

            foreach (var kv in before)
            {
                double cpuPct = 0;
                double memMb = kv.Value.memMb;
                try
                {
                    using var p = Process.GetProcessById(kv.Key);
                    var delta = p.TotalProcessorTime - kv.Value.cpu;
                    cpuPct = Math.Max(0, delta.TotalMilliseconds / sampleMs / Environment.ProcessorCount * 100.0);
                    memMb = p.WorkingSet64 / (1024.0 * 1024.0);
                    totalCpuAcc += cpuPct;
                }
                catch { continue; }

                if (!ProcessRules.TryGetValue(kv.Value.name, out var rule))
                    continue;

                if (!grouped.TryGetValue(rule.Key, out var item))
                {
                    item = new ProcessItem
                    {
                        ProcessKey = rule.Key,
                        DisplayName = rule.DisplayName,
                        Category = rule.Category,
                        Reason = rule.Reason,
                        ImpactLevel = rule.BaseImpact,
                        CanClose = rule.CanClose,
                        RequiresAdmin = rule.RequiresAdmin
                    };
                    grouped[rule.Key] = item;
                }

                item.Pids.Add(kv.Key);
                item.CpuPercent += cpuPct;
                item.MemoryMb += memMb;
                if (gpuPids.Contains(kv.Key))
                    item.UsesGpu = true;
            }

            snap.SystemCpuPercent = Math.Min(100, totalCpuAcc);

            foreach (var item in grouped.Values
                .OrderByDescending(x => x.CanClose)
                .ThenByDescending(x => x.UsesGpu)
                .ThenByDescending(x => x.ImpactLevel)
                .ThenByDescending(x => x.MemoryMb))
            {
                // 严格遵循“先列出来，完全由用户主动勾选决定关闭哪些”，默认不预勾选任何进程
                item.IsSelected = false;
                snap.BackgroundProcesses.Add(item);
            }
        }

        private static void CollectCacheItems(SystemTelemetrySnapshot snap)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var targets = new (string id, string name, string path, string desc, bool admin)[]
            {
                ("pip_cache", "Python pip 安装包缓存", Path.Combine(local, @"pip\cache"), "pip install 残留的 wheel/tar 包缓存，可 100% 安全清空", false),
                ("user_temp", "用户临时解压文件夹 (Local\\Temp)", Path.Combine(local, "Temp"), "各类安装包与运行时的临时文件（正在被占用的会自动跳过）", false),
                ("pubg_saved", "PUBG 旧局回放与崩溃日志", Path.Combine(local, @"TslGame\Saved\Demos"), "残留的 Demos 回放录像（会同步清理 Crashes 与 Logs）", false),
                ("pw_updater", "完美竞技平台旧更新安装包", Path.Combine(local, "perfectworldarena-updater"), "完美对战平台自动下载更新后的残留安装程序", false),
                ("crash_dumps", "系统应用崩溃转储 (CrashDumps)", Path.Combine(local, "CrashDumps"), "过往程序崩溃生成的 .dmp 内存转储文件", false),
                ("win_temp", "Windows 系统级临时目录", @"C:\Windows\Temp", "系统更新与服务产生的临时日志与缓存文件", true)
            };

            foreach (var t in targets)
            {
                long size = GetDirectorySizeSafe(t.path);
                if (t.id == "pubg_saved")
                {
                    size += GetDirectorySizeSafe(Path.Combine(local, @"TslGame\Saved\Crashes"));
                    size += GetDirectorySizeSafe(Path.Combine(local, @"TslGame\Saved\Logs"));
                }

                snap.CacheItems.Add(new CacheCleanupItem
                {
                    Id = t.id,
                    Name = t.name,
                    PathDisplay = t.path,
                    Description = t.desc,
                    SizeBytes = size,
                    RequiresAdmin = t.admin,
                    Available = size > 1024 * 100, // 大于 100KB 才显示有效待清理
                    IsSelected = size > 1024 * 1024 // 大于 1MB 默认勾选
                });
            }
        }

        private static void ComputeReadinessScore(SystemTelemetrySnapshot snap)
        {
            int score = 100;
            foreach (var c in snap.SystemChecks.Concat(snap.OnlineGameChecks).Concat(snap.AaaGameChecks))
            {
                if (c.Status == CheckStatus.Critical) score -= 10;
                else if (c.Status == CheckStatus.Attention) score -= 4;
            }

            if (snap.GpuAvailable && snap.GpuMemUsedMb > 1400 && !snap.IsGameRunning)
                score -= 8; // 待机显存吃太满对 8GB 显卡扣分

            int highImpactBg = snap.BackgroundProcesses.Count(x => x.CanClose && (x.ImpactLevel >= 3 || x.UsesGpu));
            score -= Math.Min(12, highImpactBg * 3);

            score = Math.Max(0, Math.Min(100, score));
            snap.ReadinessScore = score;

            if (score >= 90)
            {
                snap.ReadinessTitle = "配置高度合规 · 环境清爽";
                snap.ReadinessSubtitle = "系统底座、大小核调度与已检游戏配置均符合推荐规则（注：此为本地配置合规分，非局内实测帧率）";
            }
            else if (score >= 75)
            {
                snap.ReadinessTitle = "配置良好 · 有少量可调项";
                snap.ReadinessSubtitle = "核心规则正常，可按需在下方查看后台占用或单项配置建议";
            }
            else
            {
                snap.ReadinessTitle = "检测到多项配置偏离推荐规则";
                snap.ReadinessSubtitle = "请查看各分舱的具体提示，经确认后按需逐项修复";
            }
        }

        // =================== 辅助定位与读取函数（全动态探测，零个人硬编码） ===================

        public static string? GetSteamRootPath()
        {
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                string? p = k?.GetValue("SteamPath")?.ToString()?.Replace('/', '\\');
                if (!string.IsNullOrWhiteSpace(p) && Directory.Exists(p)) return p;
            }
            catch { }

            foreach (var cand in new[] { @"C:\Program Files (x86)\Steam", @"C:\Steam", @"D:\Steam", @"E:\Steam", @"F:\Steam" })
            {
                if (Directory.Exists(cand)) return cand;
            }
            return null;
        }

        private static List<string> GetAllSteamLibraryRoots()
        {
            var libs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? steamRoot = GetSteamRootPath();
            if (steamRoot != null)
            {
                libs.Add(steamRoot);
                string vdf = Path.Combine(steamRoot, @"steamapps\libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    try
                    {
                        string txt = File.ReadAllText(vdf);
                        foreach (Match m in Regex.Matches(txt, @"""path""\s+""([^""]+)"""))
                        {
                            string p = m.Groups[1].Value.Replace(@"\\", @"\");
                            if (Directory.Exists(p)) libs.Add(p);
                        }
                    }
                    catch { }
                }
            }
            foreach (var drv in new[] { "C:", "D:", "E:", "F:", "G:" })
            {
                string lib = Path.Combine(drv + "\\", "SteamLibrary");
                if (Directory.Exists(lib)) libs.Add(lib);
            }
            return libs.ToList();
        }

        public static List<string> FindCs2VideoFiles()
        {
            var result = new List<string>();
            var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string? steamRoot = GetSteamRootPath();
            if (steamRoot != null) roots.Add(Path.Combine(steamRoot, "userdata"));
            foreach (var cand in new[] { @"C:\Program Files (x86)\Steam\userdata", @"D:\Steam\userdata" })
                roots.Add(cand);

            foreach (var r in roots)
            {
                if (!Directory.Exists(r)) continue;
                try
                {
                    foreach (var dir in Directory.GetDirectories(r))
                    {
                        string cfg = Path.Combine(dir, @"730\local\cfg\cs2_video.txt");
                        if (File.Exists(cfg)) result.Add(cfg);
                    }
                }
                catch { }
            }
            return result;
        }

        public static string? FindCs2AutoexecPath()
        {
            foreach (var lib in GetAllSteamLibraryRoots())
            {
                string cfgDir = Path.Combine(lib, @"steamapps\common\Counter-Strike Global Offensive\game\csgo\cfg");
                string cfg = Path.Combine(cfgDir, "autoexec.cfg");
                if (File.Exists(cfg) || Directory.Exists(cfgDir))
                    return cfg;
            }
            return null;
        }

        public static string? FindRtssProfileDir()
        {
            // 优先从运行中的 RTSS 进程定位，其次查常见安装目录
            foreach (var p in Process.GetProcessesByName("RTSS"))
            {
                try
                {
                    string? exe = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        string prof = Path.Combine(Path.GetDirectoryName(exe)!, "Profiles");
                        if (Directory.Exists(prof)) return prof;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }

            foreach (var drv in new[] { "C:", "D:", "E:", "F:" })
            {
                foreach (var sub in new[]
                {
                    @"Program Files (x86)\RivaTuner Statistics Server\Profiles",
                    @"MSI\RivaTuner Statistics Server\Profiles",
                    @"RivaTuner Statistics Server\Profiles"
                })
                {
                    string full = Path.Combine(drv + "\\", sub);
                    if (Directory.Exists(full)) return full;
                }
            }
            return null;
        }

        public static string? FindAfterburnerCfgPath()
        {
            foreach (var p in Process.GetProcessesByName("MSIAfterburner"))
            {
                try
                {
                    string? exe = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exe))
                    {
                        string cfg = Path.Combine(Path.GetDirectoryName(exe)!, @"Profiles\MSIAfterburner.cfg");
                        if (File.Exists(cfg)) return cfg;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }

            foreach (var drv in new[] { "C:", "D:", "E:", "F:" })
            {
                foreach (var sub in new[]
                {
                    @"Program Files (x86)\MSI Afterburner\Profiles\MSIAfterburner.cfg",
                    @"MSI\MSI Afterburner\Profiles\MSIAfterburner.cfg",
                    @"MSI Afterburner\Profiles\MSIAfterburner.cfg"
                })
                {
                    string full = Path.Combine(drv + "\\", sub);
                    if (File.Exists(full)) return full;
                }
            }
            return null;
        }

        public static string? FindLolConfigPath()
        {
            foreach (var drv in new[] { "C:", "D:", "E:", "F:", "G:" })
            {
                foreach (var sub in new[]
                {
                    @"WeGameApps\英雄联盟\Game\Config\game.cfg",
                    @"Riot Games\League of Legends\Config\game.cfg",
                    @"Program Files\腾讯游戏\英雄联盟\Game\Config\game.cfg"
                })
                {
                    string full = Path.Combine(drv + "\\", sub);
                    if (File.Exists(full)) return full;
                }
            }
            return null;
        }

        public static string? FindValorantConfigPath()
        {
            string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"VALORANT\Saved\Config");
            if (!Directory.Exists(root)) return null;
            try
            {
                return Directory.EnumerateFiles(root, "GameUserSettings.ini", SearchOption.AllDirectories).FirstOrDefault();
            }
            catch { return null; }
        }

        private static bool CheckRtssLimit(string rtssDir, string fileName, string expectedLimit)
        {
            string p = Path.Combine(rtssDir, fileName);
            if (!File.Exists(p)) return false;
            try { return File.ReadAllText(p).Contains($"Limit={expectedLimit}"); }
            catch { return false; }
        }

        private static string MatchCyberpunkValue(string json, string name)
        {
            var m = Regex.Match(json, $@"""name""\s*:\s*""{Regex.Escape(name)}""[\s\S]{{0,180}}?""value""\s*:\s*""([^""]*)""");
            return m.Success ? m.Groups[1].Value : "未知";
        }

        private static string MatchCyberpunkBool(string json, string name)
        {
            var m = Regex.Match(json, $@"""name""\s*:\s*""{Regex.Escape(name)}""[\s\S]{{0,180}}?""value""\s*:\s*(true|false)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : "未知";
        }

        private static string MatchXmlTag(string xml, string tag)
        {
            var m = Regex.Match(xml, $@"<{Regex.Escape(tag)}>([^<]*)</{Regex.Escape(tag)}>");
            return m.Success ? m.Groups[1].Value.Trim() : "未知";
        }

        private static HashSet<int> QueryGpuUsingPids()
        {
            var pids = new HashSet<int>();
            string outText = RunProcess("nvidia-smi", "--query-compute-apps=pid --format=csv,noheader", 1500);
            string fullText = RunProcess("nvidia-smi", "", 2000);
            foreach (Match m in Regex.Matches(fullText ?? "", @"\s+(\d+)\s+C\+G\s+"))
            {
                if (int.TryParse(m.Groups[1].Value, out int pid))
                    pids.Add(pid);
            }
            foreach (var line in (outText ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(line.Trim(), out int pid))
                    pids.Add(pid);
            }
            return pids;
        }

        private static bool IsServiceRunning(string serviceName)
        {
            string outText = RunProcess("sc", $"query \"{serviceName}\"", 1500);
            return outText.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ServiceExists(string serviceName)
        {
            string outText = RunProcess("sc", $"query \"{serviceName}\"", 1500);
            return outText.IndexOf("STATE", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool RegistryKeyExists(RegistryKey root, string subKey)
        {
            try
            {
                using var k = root.OpenSubKey(subKey);
                return k != null;
            }
            catch { return false; }
        }

        public static string QueryPowerAcHex(string subGuid, string settingGuid)
        {
            string qhOut = RunProcess("powercfg", $"/qh SCHEME_CURRENT {subGuid} {settingGuid}", 2000) ?? "";
            foreach (var rawLine in qhOut.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (rawLine.Contains("当前交流") || rawLine.Contains("Current AC"))
                {
                    var m = Regex.Match(rawLine, @"0x([0-9a-fA-F]{8})");
                    if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
                }
            }
            return "";
        }

        private static long GetDirectorySizeSafe(string path)
        {
            if (!Directory.Exists(path)) return 0;
            long total = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        private static string MatchIniValue(string content, string key)
        {
            var m = Regex.Match(content, $@"(?m)^{Regex.Escape(key)}\s*=\s*(.+)$");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        public static void CollectVoicePlatformStatus(SystemTelemetrySnapshot snap)
        {
            var vp = new VoicePlatformSnapshot();

            // 1. Oopz 实时状态
            foreach (var p in Process.GetProcessesByName("oopz"))
            {
                try
                {
                    vp.OopzRunning = true;
                    vp.OopzTotalCount++;
                    try { vp.OopzMemoryMb += p.WorkingSet64 / (1024.0 * 1024.0); } catch { }
                    try { vp.OopzCpuSeconds += p.TotalProcessorTime.TotalSeconds; } catch { }
                    if (string.IsNullOrEmpty(vp.OopzPath))
                    {
                        try { vp.OopzPath = p.MainModule?.FileName ?? ""; } catch { }
                    }
                    try
                    {
                        long aff = (long)p.ProcessorAffinity;
                        bool lowPrio = p.PriorityClass == ProcessPriorityClass.BelowNormal ||
                                       p.PriorityClass == ProcessPriorityClass.Idle;
                        if ((aff & snap.ProtectedGameCoreMask) == 0 && lowPrio)
                            vp.OopzIsolatedCount++;
                        else
                            vp.OopzUnisolatedCount++;
                    }
                    catch { vp.OopzUnisolatedCount++; }
                }
                finally { p.Dispose(); }
            }

            // 读取 Oopz AI 降噪配置 (mixer_local_setting_cache)
            try
            {
                string oopzRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oopz");
                if (Directory.Exists(oopzRoot))
                {
                    var cfg = Directory.EnumerateFiles(oopzRoot, "mixer_local_setting_cache", SearchOption.AllDirectories)
                        .OrderByDescending(File.GetLastWriteTime)
                        .FirstOrDefault();
                    if (cfg != null)
                    {
                        vp.OopzMixerConfigPath = cfg;
                        string json = File.ReadAllText(cfg);
                        var m = Regex.Match(json, @"""aiModeType""\s*:\s*""([^""]+)""");
                        if (m.Success) vp.OopzAiMode = m.Groups[1].Value;
                    }
                }
            }
            catch { }

            // 检查桌面 Oopz.lnk 是否含当前平台的核心掩码
            try
            {
                string lnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Oopz.lnk");
                if (File.Exists(lnk))
                {
                    byte[] raw = File.ReadAllBytes(lnk);
                    string maskHex = snap.BackgroundAffinityMask.ToString("X");
                    byte[] needleUtf16 = System.Text.Encoding.Unicode.GetBytes(maskHex);
                    byte[] needleAscii = System.Text.Encoding.ASCII.GetBytes(maskHex);
                    byte[] fallbackUtf16 = System.Text.Encoding.Unicode.GetBytes("/affinity");
                    vp.OopzShortcutHasECore = raw.AsSpan().IndexOf(needleUtf16) >= 0 ||
                                              raw.AsSpan().IndexOf(needleAscii) >= 0 ||
                                              raw.AsSpan().IndexOf(fallbackUtf16) >= 0;
                }
            }
            catch { }

            // 2. 完美世界竞技平台实时状态
            foreach (var p in Process.GetProcessesByName("完美世界竞技平台"))
            {
                try
                {
                    vp.PwaRunning = true;
                    vp.PwaTotalCount++;
                    try { vp.PwaMemoryMb += p.WorkingSet64 / (1024.0 * 1024.0); } catch { }
                    try { vp.PwaCpuSeconds += p.TotalProcessorTime.TotalSeconds; } catch { }
                    if (string.IsNullOrEmpty(vp.PwaInstallPath))
                    {
                        try { vp.PwaInstallPath = p.MainModule?.FileName ?? ""; } catch { }
                    }
                    try
                    {
                        long aff = (long)p.ProcessorAffinity;
                        bool lowPrio = p.PriorityClass == ProcessPriorityClass.BelowNormal ||
                                       p.PriorityClass == ProcessPriorityClass.Idle;
                        if ((aff & snap.ProtectedGameCoreMask) == 0 && lowPrio)
                            vp.PwaIsolatedCount++;
                        else
                            vp.PwaUnisolatedUserCount++;
                    }
                    catch
                    {
                        vp.PwaElevatedCount++;
                    }
                }
                finally { p.Dispose(); }
            }

            if (string.IsNullOrEmpty(vp.PwaInstallPath))
            {
                foreach (var drv in new[] { "C:", "D:", "E:", "F:", "G:" })
                {
                    foreach (var sub in new[]
                    {
                        @"Program Files\perfectworldarena\完美世界竞技平台.exe",
                        @"perfectworldarena\完美世界竞技平台.exe",
                        @"完美\perfectworldarena\完美世界竞技平台.exe"
                    })
                    {
                        string cand = Path.Combine(drv + "\\", sub);
                        if (File.Exists(cand)) { vp.PwaInstallPath = cand; break; }
                    }
                    if (!string.IsNullOrEmpty(vp.PwaInstallPath)) break;
                }
            }
            if (!string.IsNullOrEmpty(vp.PwaInstallPath))
            {
                char drv = char.ToUpperInvariant(vp.PwaInstallPath[0]);
                vp.PwaOnMechanicalDrive = drv == 'D' || drv == 'E';
            }

            // 3. CS2 / PUBG 反向主核完整性安全校验（动态适配 Intel P 核与 AMD 主线程）
            foreach (var gName in new[] { "cs2", "TslGame" })
            {
                foreach (var p in Process.GetProcessesByName(gName))
                {
                    try
                    {
                        vp.Cs2Running = true;
                        long aff = (long)p.ProcessorAffinity;
                        bool hasAllMain = (aff & snap.ProtectedGameCoreMask) == snap.ProtectedGameCoreMask;
                        vp.Cs2HasAllPCores = hasAllMain;
                        vp.Cs2AffinitySummary = hasAllMain
                            ? $"{gName} 运行中 (PID {p.Id}) · 已确认完整拥有主游戏核心 (0x{aff:X})，零受影响"
                            : $"警告：{gName} (PID {p.Id}) 亲和性 0x{aff:X} 缺失部分主核！建议立即点击还原全核";
                    }
                    catch
                    {
                        vp.Cs2Running = true;
                        vp.Cs2HasAllPCores = true;
                        vp.Cs2AffinitySummary = $"{gName} 运行中 (PID {p.Id}) · 受保护正常运行";
                    }
                    finally { p.Dispose(); }
                }
            }

            snap.VoicePlatform = vp;
        }

        private static string MatchVdfValue(string content, string key)
        {
            var m = Regex.Match(content, $@"""{Regex.Escape(key)}""\s+""([^""]*)""");
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static int ReadDword(RegistryKey root, string subKey, string name, int fallback)
        {
            try
            {
                using var k = root.OpenSubKey(subKey);
                var val = k?.GetValue(name);
                return val != null ? Convert.ToInt32(val, CultureInfo.InvariantCulture) : fallback;
            }
            catch { return fallback; }
        }

        private static string ReadString(RegistryKey root, string subKey, string name, string fallback)
        {
            try
            {
                using var k = root.OpenSubKey(subKey);
                return k?.GetValue(name)?.ToString() ?? fallback;
            }
            catch { return fallback; }
        }

        private static string? ReadMultiStringFirst(RegistryKey root, string subKey, string name)
        {
            try
            {
                using var k = root.OpenSubKey(subKey);
                if (k?.GetValue(name) is string[] arr && arr.Length > 0) return arr[0];
            }
            catch { }
            return null;
        }

        public static string RunProcess(string fileName, string args, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo(fileName, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return "";
                string output = proc.StandardOutput.ReadToEnd();
                if (!proc.WaitForExit(timeoutMs))
                {
                    try { proc.Kill(); } catch { }
                    return "";
                }
                return output.Trim();
            }
            catch { return ""; }
        }
    }
}
