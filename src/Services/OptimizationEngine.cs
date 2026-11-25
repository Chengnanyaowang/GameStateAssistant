using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GameStateAssistant.Models;
using Microsoft.Win32;

namespace GameStateAssistant.Services
{
    internal sealed class ExecutionReport
    {
        public bool Success { get; set; }
        public bool CancelledUac { get; set; }
        public List<string> Logs { get; } = new List<string>();
        public string Summary => string.Join("\n", Logs);
    }

    internal sealed class RestoreBaselineV4
    {
        public DateTime CapturedAt { get; set; } = DateTime.Now;
        public string PowerGuid { get; set; } = "";
        public string AutoGameModeEnabled { get; set; } = "missing";
        public string AllowAutoGameMode { get; set; } = "missing";
        public string GameDVR_Enabled { get; set; } = "missing";
        public string AppCaptureEnabled { get; set; } = "missing";
        public string PcieAspmAcHex { get; set; } = "";
        public string UsbSuspendAcHex { get; set; } = "";
        public string CoreParkMinAcHex { get; set; } = "";
        public string Cdm338388 { get; set; } = "missing";
        public string CdmSystemPane { get; set; } = "missing";
        public string CdmSoftLanding { get; set; } = "missing";
        public string MmcssGpuPriority { get; set; } = "missing";
        public string MmcssPriority { get; set; } = "missing";
        public string MmcssSchedCategory { get; set; } = "missing";
        public string MmcssSystemResp { get; set; } = "missing";
        public bool TouchedAdminItems { get; set; }
    }

    internal static class OptimizationEngine
    {
        private static readonly string StateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameStateAssistant");
        private static readonly string LegacyRestoreFile = Path.Combine(StateDir, "restore_v2.state");
        private static readonly string RestoreJsonFile = Path.Combine(StateDir, "restore_v4.json");

        public static SystemTelemetrySnapshot? ActiveContext { get; set; }

        public static bool HasSavedRestoreState => File.Exists(RestoreJsonFile) || File.Exists(LegacyRestoreFile);

        private static SystemTelemetrySnapshot GetCtx() => ActiveContext ?? SystemInspector.CollectAll();

        public static ExecutionReport ApplySingleFix(string fixId)
        {
            var report = new ExecutionReport { Success = true };
            var ctx = GetCtx();
            try
            {
                switch (fixId)
                {
                    case "power_plan":
                        CaptureBaselineIfNeeded(isAdminTouch: false);
                        SystemInspector.RunProcess("powercfg", $"/setactive {SystemInspector.BalancedPowerGuid}", 3000);
                        SystemInspector.RunProcess("powercfg", $"/setacvalueindex {SystemInspector.BalancedPowerGuid} 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0", 3000);
                        SystemInspector.RunProcess("powercfg", $"/setactive {SystemInspector.BalancedPowerGuid}", 3000);
                        report.Logs.Add("✅ 已切回「平衡 (Balanced)」调度计划，并关闭 PCIe 节能。");
                        break;

                    case "bus_latency":
                        CaptureBaselineIfNeeded(isAdminTouch: false);
                        SystemInspector.RunProcess("powercfg", "/setacvalueindex SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 0", 3000);
                        SystemInspector.RunProcess("powercfg", "/setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0", 3000);
                        SystemInspector.RunProcess("powercfg", "/setactive SCHEME_CURRENT", 3000);
                        report.Logs.Add("✅ 已关闭当前电源计划的 PCIe 链路节能 (ASPM) 与 USB 选择性暂停（原值已记录，可完整还原）。");
                        break;

                    case "game_dvr":
                        CaptureBaselineIfNeeded(isAdminTouch: false);
                        WriteHkcuDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1);
                        WriteHkcuDword(@"Software\Microsoft\GameBar", "AllowAutoGameMode", 1);
                        WriteHkcuDword(@"System\GameConfigStore", "GameDVR_Enabled", 0);
                        WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                        report.Logs.Add("✅ 已开启 Windows 游戏模式，并禁用 Game DVR 后台录制。");
                        break;

                    case "win_noise_trim":
                        CaptureBaselineIfNeeded(isAdminTouch: false);
                        WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", 0);
                        WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", 0);
                        WriteHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", 0);
                        report.Logs.Add("✅ 已关闭 Windows 开始菜单/锁屏静默推广与系统建议推送（零破坏，原值已备份可随时还原）。");
                        break;

                    case "pubg_config":
                        ApplyPubgOptimization(report, ctx);
                        break;

                    case "cs2_config":
                        ApplyCs2Optimization(report, ctx);
                        break;

                    case "lol_config":
                        ApplyLolOptimization(report, ctx);
                        break;

                    case "valorant_config":
                        ApplyRtssSingle(report, "VALORANT-Win64-Shipping.exe.cfg", ctx.RecommendedFpsCap);
                        break;

                    case "rtss_profile":
                        ApplyRtssProfiles(report, ctx.RecommendedFpsCap);
                        break;

                    case "afterburner_power":
                        ApplyAfterburnerClean(report);
                        break;

                    case "voice_ecore_jail":
                        ApplyVoiceECoreIsolation(report, ctx);
                        break;

                    case "cyberpunk_config":
                        ApplyCyberpunkOptimization(report);
                        break;

                    case "eldenring_config":
                        ApplyEldenRingOptimization(report, ctx);
                        break;

                    case "pcore_unpark":
                    case "mmcss_gaming":
                    case "hvci":
                    case "pagefile":
                    case "intel_ipf":
                    case "fast_boot":
                        return RunElevatedFixes(new[] { fixId });

                    default:
                        report.Success = false;
                        report.Logs.Add($"未知修复项: {fixId}");
                        break;
                }
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.Logs.Add($"❌ 执行出错: {ex.Message}");
            }
            return report;
        }

        private static readonly HashSet<string> AdminFixIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "pcore_unpark", "mmcss_gaming", "hvci", "pagefile", "intel_ipf", "fast_boot"
        };

        /// <summary>
        /// 严格按传入的 fixIds 列表执行，绝不越界修改未传入的模块。
        /// 非管理员项直接执行；管理员项合并为单次 UAC 提权执行。
        /// </summary>
        public static ExecutionReport ApplyFixBatch(IEnumerable<string> fixIds)
        {
            var report = new ExecutionReport { Success = true };
            var distinctIds = fixIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (distinctIds.Count == 0)
            {
                report.Logs.Add("✅ 没有需要执行的修改项。");
                return report;
            }

            var userIds = distinctIds.Where(id => !AdminFixIds.Contains(id)).ToList();
            var adminIds = distinctIds.Where(id => AdminFixIds.Contains(id)).ToList();

            foreach (var id in userIds)
            {
                var r = ApplySingleFix(id);
                report.Logs.AddRange(r.Logs);
                if (!r.Success) report.Success = false;
            }

            if (adminIds.Count > 0)
            {
                var elev = RunElevatedFixes(adminIds);
                report.Logs.AddRange(elev.Logs);
                report.CancelledUac = elev.CancelledUac;
                if (!elev.Success && !elev.CancelledUac)
                    report.Success = false;
            }

            return report;
        }

        public static ExecutionReport ApplyAllRecommended(SystemTelemetrySnapshot snap)
        {
            // 仅收集允许自动批量修复的项（严格排除 HVCI 等安全权衡项 ExcludeFromBatchFix）
            var ids = snap.SystemChecks
                .Concat(snap.OnlineGameChecks)
                .Concat(snap.AaaGameChecks)
                .Where(c => c.CanAutoFix && !c.ExcludeFromBatchFix)
                .Select(c => c.Id)
                .ToList();

            return ApplyFixBatch(ids);
        }

        public static ExecutionReport CloseSelectedProcesses(IEnumerable<ProcessItem> items)
        {
            var report = new ExecutionReport { Success = true };
            var adminServices = new List<string>();
            int closedCount = 0;

            foreach (var item in items)
            {
                if (!item.CanClose) continue;
                if (item.RequiresAdmin)
                {
                    adminServices.Add(item.ProcessKey);
                    continue;
                }

                bool anyOk = false;
                foreach (int pid in item.Pids)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        if (proc.Id == Environment.ProcessId) continue;
                        bool sentClose = proc.MainWindowHandle != IntPtr.Zero && proc.CloseMainWindow();
                        if (sentClose && proc.WaitForExit(800))
                        {
                            anyOk = true;
                            continue;
                        }
                        proc.Kill(true);
                        anyOk = true;
                    }
                    catch { }
                }

                if (anyOk)
                {
                    closedCount++;
                    report.Logs.Add($"✅ 已关闭后台程序：{item.DisplayName}（释放 {item.MemoryText}）");
                }
            }

            if (adminServices.Count > 0)
            {
                var scriptLines = new List<string>();
                foreach (var key in adminServices)
                {
                    if (key.Equals("mspcmanagerservice", StringComparison.OrdinalIgnoreCase))
                    {
                        scriptLines.Add("Stop-Service -Name 'MSPCManagerService' -Force -ErrorAction SilentlyContinue");
                        scriptLines.Add("Set-Service -Name 'MSPCManagerService' -StartupType Manual -ErrorAction SilentlyContinue");
                        scriptLines.Add("Stop-Process -Name 'MSPCManager','MSPCManagerCore' -Force -ErrorAction SilentlyContinue");
                    }
                    else if (key.Equals("gameviewerservice", StringComparison.OrdinalIgnoreCase))
                    {
                        scriptLines.Add("Stop-Service -Name 'GameViewerService' -Force -ErrorAction SilentlyContinue");
                        scriptLines.Add("Stop-Process -Name 'GameViewer' -Force -ErrorAction SilentlyContinue");
                    }
                }
                var elev = ExecuteElevatedPowerShell(scriptLines, "停止受保护的系统后台服务");
                report.Logs.AddRange(elev.Logs);
                report.CancelledUac = elev.CancelledUac;
            }

            if (closedCount == 0 && adminServices.Count == 0)
                report.Logs.Add("未勾选可关闭的进程。");

            return report;
        }

        public static ExecutionReport CleanSelectedCaches(IEnumerable<CacheCleanupItem> items)
        {
            var report = new ExecutionReport { Success = true };
            long freedBytes = 0;
            bool needWinTemp = false;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            foreach (var item in items.Where(x => x.IsSelected && x.Available))
            {
                if (item.RequiresAdmin && item.Id == "win_temp")
                {
                    needWinTemp = true;
                    continue;
                }

                long before = GetDirSize(item.PathDisplay);
                CleanDirectoryContents(item.PathDisplay);
                if (item.Id == "pubg_saved")
                {
                    CleanDirectoryContents(Path.Combine(local, @"TslGame\Saved\Crashes"));
                    CleanDirectoryContents(Path.Combine(local, @"TslGame\Saved\Logs"));
                }
                long after = GetDirSize(item.PathDisplay);
                long delta = Math.Max(0, before - after);
                freedBytes += delta;
                report.Logs.Add($"🧹 已清理「{item.Name}」，释放 {(delta / (1024.0 * 1024.0)):F1} MB");
            }

            if (needWinTemp)
            {
                var elev = ExecuteElevatedPowerShell(new[]
                {
                    "Remove-Item 'C:\\Windows\\Temp\\*' -Recurse -Force -ErrorAction SilentlyContinue"
                }, "清理系统级 Windows\\Temp 临时文件");
                report.Logs.AddRange(elev.Logs);
            }

            if (freedBytes > 0)
                report.Logs.Add($"✨ 本次总计释放 C 盘空间：{(freedBytes / (1024.0 * 1024.0)):F1} MB");
            else if (report.Logs.Count == 0)
                report.Logs.Add("没有可清理的选中缓存项。");

            return report;
        }

        public static ExecutionReport RestoreBaseline()
        {
            var report = new ExecutionReport { Success = true };
            if (!HasSavedRestoreState)
            {
                report.Success = false;
                report.Logs.Add("未找到之前保存的原状态备份。");
                return report;
            }

            if (File.Exists(RestoreJsonFile))
            {
                try
                {
                    var b = JsonSerializer.Deserialize<RestoreBaselineV4>(File.ReadAllText(RestoreJsonFile));
                    if (b != null)
                    {
                        if (!string.IsNullOrWhiteSpace(b.PowerGuid))
                            SystemInspector.RunProcess("powercfg", $"/setactive {b.PowerGuid}", 3000);

                        if (int.TryParse(b.PcieAspmAcHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int pcieVal))
                            SystemInspector.RunProcess("powercfg", $"/setacvalueindex SCHEME_CURRENT 501a4d13-42af-4429-9fd1-a8218c268e20 ee12f906-d277-404b-b6da-e5fa1a576df5 {pcieVal}", 3000);
                        if (int.TryParse(b.UsbSuspendAcHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int usbVal))
                            SystemInspector.RunProcess("powercfg", $"/setacvalueindex SCHEME_CURRENT 2a737441-1930-4402-8d77-b2bebba308a3 48e6b7a6-50f5-4782-a5d4-53bb8f07e226 {usbVal}", 3000);
                        SystemInspector.RunProcess("powercfg", "/setactive SCHEME_CURRENT", 3000);

                        RestoreHkcuDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", b.AutoGameModeEnabled);
                        RestoreHkcuDword(@"Software\Microsoft\GameBar", "AllowAutoGameMode", b.AllowAutoGameMode);
                        RestoreHkcuDword(@"System\GameConfigStore", "GameDVR_Enabled", b.GameDVR_Enabled);
                        RestoreHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", b.AppCaptureEnabled);

                        RestoreHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled", b.Cdm338388);
                        RestoreHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled", b.CdmSystemPane);
                        RestoreHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled", b.CdmSoftLanding);

                        report.Logs.Add("↩️ 已完整还原电源计划、PCIe/USB 节能、Windows 录制与内容推送至优化前原值。");

                        if (b.TouchedAdminItems)
                        {
                            var cmds = new List<string>();
                            if (int.TryParse(b.CoreParkMinAcHex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int cpVal))
                            {
                                cmds.Add($"powercfg /setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 {cpVal}");
                                cmds.Add("powercfg /setactive SCHEME_CURRENT");
                            }
                            if (int.TryParse(b.MmcssGpuPriority, out int mg))
                                cmds.Add($"Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games' -Name 'GPU Priority' -Value {mg} -Type DWord");
                            if (int.TryParse(b.MmcssPriority, out int mp))
                                cmds.Add($"Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games' -Name 'Priority' -Value {mp} -Type DWord");
                            if (!string.IsNullOrEmpty(b.MmcssSchedCategory) && b.MmcssSchedCategory != "missing")
                                cmds.Add($"Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games' -Name 'Scheduling Category' -Value '{b.MmcssSchedCategory.Replace("'", "''")}' -Type String");
                            if (int.TryParse(b.MmcssSystemResp, out int mr))
                                cmds.Add($"Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile' -Name 'SystemResponsiveness' -Value {mr} -Type DWord");

                            if (cmds.Count > 0)
                            {
                                var elev = ExecuteElevatedPowerShell(cmds, "还原核心停驻与 MMCSS 调度原值");
                                report.Logs.AddRange(elev.Logs);
                                report.CancelledUac = elev.CancelledUac;
                            }
                        }

                        if (!report.CancelledUac)
                            File.Delete(RestoreJsonFile);
                        return report;
                    }
                }
                catch (Exception ex)
                {
                    report.Success = false;
                    report.Logs.Add($"❌ 读取 v4 还原快照失败：{ex.Message}");
                    return report;
                }
            }

            if (File.Exists(LegacyRestoreFile))
            {
                var dict = File.ReadAllLines(LegacyRestoreFile)
                    .Where(l => l.Contains('='))
                    .Select(l => l.Split('=', 2))
                    .ToDictionary(a => a[0], a => a[1], StringComparer.OrdinalIgnoreCase);

                if (dict.TryGetValue("PowerGuid", out var guid) && !string.IsNullOrWhiteSpace(guid))
                    SystemInspector.RunProcess("powercfg", $"/setactive {guid}", 3000);

                RestoreHkcuDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", dict.GetValueOrDefault("AutoGameModeEnabled", "missing"));
                RestoreHkcuDword(@"Software\Microsoft\GameBar", "AllowAutoGameMode", dict.GetValueOrDefault("AllowAutoGameMode", "missing"));
                RestoreHkcuDword(@"System\GameConfigStore", "GameDVR_Enabled", dict.GetValueOrDefault("GameDVR_Enabled", "missing"));
                RestoreHkcuDword(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", dict.GetValueOrDefault("AppCaptureEnabled", "missing"));

                File.Delete(LegacyRestoreFile);
                report.Logs.Add("↩️ 已从旧版基线恢复电源计划与 Windows 录制设置。");
            }
            return report;
        }

        // =================== 游戏与工具配置写入 ===================

        private static void ApplyPubgOptimization(ExecutionReport report, SystemTelemetrySnapshot ctx)
        {
            string pubgIni = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"TslGame\Saved\Config\WindowsNoEditor\GameUserSettings.ini");
            if (!File.Exists(pubgIni))
            {
                report.Logs.Add("⚠️ 未找到 PUBG 配置文件 GameUserSettings.ini。");
                return;
            }

            File.Copy(pubgIni, pubgIni + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}", true);
            string text = File.ReadAllText(pubgIni);

            text = ReplaceIniLine(text, "ScreenScale", "100.000000");
            text = ReplaceIniLine(text, "FullscreenMode", "0");
            text = ReplaceIniLine(text, "PreferredFullscreenMode", "0");
            text = ReplaceIniLine(text, "bUseClientReplay", "False");
            text = ReplaceIniLine(text, "Provider", "None");
            text = ReplaceIniLine(text, "InGameFrameRateLimitType", "Unlimited");
            text = ReplaceIniLine(text, "LobbyFrameRateLimitType", "Custom");
            text = ReplaceIniLine(text, "LobbyCustomFrameRateLimit", "60.000000");

            File.WriteAllText(pubgIni, text, new UTF8Encoding(false));
            report.Logs.Add($"✅ 已优化 PUBG 配置：100% 原生渲染、独占全屏、关闭回放与抓拍、局内无限制（交由 RTSS {ctx.RecommendedFpsCap} 控帧）/大厅锁 60 帧（原文件已备份）。");
        }

        private static void ApplyCs2Optimization(ExecutionReport report, SystemTelemetrySnapshot ctx)
        {
            var videoFiles = SystemInspector.FindCs2VideoFiles();
            int hzNum = ctx.DisplayRefreshRate == 260 ? 259999 : Math.Max(60, ctx.DisplayRefreshRate) * 1000;
            foreach (var vf in videoFiles)
            {
                File.Copy(vf, vf + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}", true);
                string txt = File.ReadAllText(vf);
                txt = ReplaceOrSetVdfKey(txt, "setting.fullscreen", "1");
                txt = ReplaceOrSetVdfKey(txt, "setting.nowindowborder", "0");
                txt = ReplaceOrSetVdfKey(txt, "setting.coop_fullscreen", "0");
                txt = ReplaceOrSetVdfKey(txt, "setting.mat_vsync", "0");
                txt = ReplaceOrSetVdfKey(txt, "setting.r_low_latency", "0");
                txt = ReplaceOrSetVdfKey(txt, "setting.msaa_samples", "2");
                txt = ReplaceOrSetVdfKey(txt, "setting.videocfg_particle_detail", "0");
                txt = ReplaceOrSetVdfKey(txt, "setting.videocfg_ao_detail", "0");
                txt = ReplaceOrSetVdfKey(txt, "setting.videocfg_hdr_detail", "3");
                txt = ReplaceOrSetVdfKey(txt, "setting.refreshrate_numerator", hzNum.ToString(CultureInfo.InvariantCulture));
                txt = ReplaceOrSetVdfKey(txt, "setting.refreshrate_denominator", "1000");
                File.WriteAllText(vf, txt, new UTF8Encoding(false));
            }

            string? autoexec = SystemInspector.FindCs2AutoexecPath();
            if (autoexec != null)
            {
                string existing = File.Exists(autoexec) ? File.ReadAllText(autoexec) : "";
                var lines = existing.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                    .Where(l => !Regex.IsMatch(l.Trim(), @"^(snd_mixahead|r_drawtracers_firstperson|cl_net_buffer_ticks|fps_max|engine_low_latency_sleep_after_client_tick|host_writeconfig)\b", RegexOptions.IgnoreCase))
                    .ToList();

                lines.Add("");
                lines.Add("// GameStateAssistant - 扫射防卡顿与 1% Low 帧优化");
                lines.Add("snd_mixahead \"0.025\"");
                lines.Add("r_drawtracers_firstperson \"false\"");
                lines.Add("cl_net_buffer_ticks \"0\"");
                lines.Add("fps_max \"0\"");
                lines.Add("engine_low_latency_sleep_after_client_tick \"true\"");
                lines.Add("host_writeconfig");

                File.WriteAllText(autoexec, string.Join("\r\n", lines).TrimStart() + "\r\n", new UTF8Encoding(false));
            }

            report.Logs.Add($"✅ 已优化 {videoFiles.Count} 个 Steam 账号的 CS2 视频配置（匹配 {ctx.DisplayRefreshRate}Hz）及 autoexec.cfg。");
        }

        private static void ApplyRtssProfiles(ExecutionReport report, int targetFps)
        {
            string? dir = SystemInspector.FindRtssProfileDir();
            if (dir == null)
            {
                report.Logs.Add("⚠️ 未检测到 RTSS Profiles 目录。");
                return;
            }

            string cap = targetFps.ToString(CultureInfo.InvariantCulture);
            string template = $@"[OSD]
EnableOSD=1
EnableBgnd=1
EnableFill=0
EnableStat=0
BaseColor=00FF8000
BgndColor=00000000
FillColor=80000000
PositionX=1
PositionY=1
ZoomRatio=2
[Framerate]
Limit={cap}
LimitDenominator=1
LimitTime=0
LimitTimeDenominator=1
SyncDisplay=0
SyncScanline0=0
SyncScanline1=0
SyncPeriods=0
SyncLimiter=0
PassiveWait=1
ReflexSleep=0
ReflexSetLatencyMarker=1
[Hooking]
EnableHooking=1
HookDirect3D9=1
HookDXGI=1
HookDirect3D12=1
HookVulkan=1
InjectionDelay=15000
";
            foreach (var exe in new[] { "TslGame.exe.cfg", "cs2.exe.cfg", "League of Legends.exe.cfg", "VALORANT-Win64-Shipping.exe.cfg" })
            {
                WriteRtssFile(dir, exe, template, cap);
            }
            report.Logs.Add($"✅ 已同步写入四大竞技网游（PUBG / CS2 / LOL / 无畏契约）的 RTSS {cap} FPS 规则。如 RTSS 运行中请重启生效。");
        }

        private static void ApplyRtssSingle(ExecutionReport report, string exeCfgName, int targetFps)
        {
            string? dir = SystemInspector.FindRtssProfileDir();
            if (dir == null)
            {
                report.Logs.Add("⚠️ 未检测到 RTSS Profiles 目录。");
                return;
            }
            string cap = targetFps.ToString(CultureInfo.InvariantCulture);
            string template = $"[OSD]\r\nEnableOSD=1\r\nEnableBgnd=1\r\n[Framerate]\r\nLimit={cap}\r\nLimitDenominator=1\r\nSyncLimiter=0\r\nPassiveWait=1\r\nReflexSleep=0\r\nReflexSetLatencyMarker=1\r\n[Hooking]\r\nEnableHooking=1\r\nHookDXGI=1\r\nHookDirect3D12=1\r\n";
            WriteRtssFile(dir, exeCfgName, template, cap);
            report.Logs.Add($"✅ 已为 {exeCfgName} 写入 RTSS {cap} FPS 规则。");
        }

        private static void WriteRtssFile(string dir, string exe, string template, string cap)
        {
            string target = Path.Combine(dir, exe);
            if (File.Exists(target))
            {
                string txt = File.ReadAllText(target);
                txt = Regex.Replace(txt, @"(?m)^Limit=.*$", $"Limit={cap}");
                txt = Regex.Replace(txt, @"(?m)^SyncLimiter=.*$", "SyncLimiter=0");
                txt = Regex.Replace(txt, @"(?m)^ReflexSleep=.*$", "ReflexSleep=0");
                File.WriteAllText(target, txt, new UTF8Encoding(false));
            }
            else
            {
                File.WriteAllText(target, template, new UTF8Encoding(false));
            }
        }

        private static void ApplyLolOptimization(ExecutionReport report, SystemTelemetrySnapshot ctx)
        {
            string? cfg = SystemInspector.FindLolConfigPath();
            if (cfg == null)
            {
                report.Logs.Add("⚠️ 未找到英雄联盟 game.cfg。");
                return;
            }
            File.Copy(cfg, cfg + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}", true);
            string txt = File.ReadAllText(cfg);
            txt = ReplaceIniLine(txt, "WindowMode", "2");
            txt = ReplaceIniLine(txt, "WaitForVerticalSync", "0");
            txt = ReplaceIniLine(txt, "EnableFXAA", "0");
            txt = ReplaceIniLine(txt, "EnableHUDAnimations", "0");
            File.WriteAllText(cfg, txt, new UTF8Encoding(false));
            ApplyRtssSingle(report, "League of Legends.exe.cfg", ctx.RecommendedFpsCap);
            report.Logs.Add($"✅ 已优化英雄联盟：切换无边框模式(防 0x119 蓝屏)、关闭垂直同步与 FXAA，并联动 RTSS {ctx.RecommendedFpsCap} 锁帧。");
        }

        private static void ApplyCyberpunkOptimization(ExecutionReport report)
        {
            string cp2077 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"CD Projekt Red\Cyberpunk 2077\UserSettings.json");
            if (!File.Exists(cp2077)) return;
            File.Copy(cp2077, cp2077 + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}", true);
            string json = File.ReadAllText(cp2077);
            json = Regex.Replace(json, @"(""name""\s*:\s*""TextureQuality""[\s\S]{0,180}?""value""\s*:\s*"")[^""]*("")", "${1}High${2}");
            json = Regex.Replace(json, @"(""name""\s*:\s*""DLSS""[\s\S]{0,180}?""value""\s*:\s*"")[^""]*("")", "${1}Quality${2}");
            json = Regex.Replace(json, @"(""name""\s*:\s*""RayTracing""[\s\S]{0,180}?""value""\s*:\s*)(true|false)", "${1}false", RegexOptions.IgnoreCase);
            File.WriteAllText(cp2077, json, new UTF8Encoding(false));
            report.Logs.Add("✅ 已将《赛博朋克 2077》调至 8GB 显存稳帧档：纹理=High、DLSS=Quality、关闭光追。");
        }

        private static void ApplyEldenRingOptimization(ExecutionReport report, SystemTelemetrySnapshot ctx)
        {
            string erXml = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"EldenRing\GraphicsConfig.xml");
            if (!File.Exists(erXml)) return;
            File.Copy(erXml, erXml + $".bak_{DateTime.Now:yyyyMMdd_HHmmss}", true);
            string xml = File.ReadAllText(erXml, Encoding.Unicode).Replace("\0", "");
            if (!xml.Contains("<config>")) xml = File.ReadAllText(erXml).Replace("\0", "");
            xml = Regex.Replace(xml, @"<RaytracingQuality>[^<]*</RaytracingQuality>", "<RaytracingQuality>DISABLE</RaytracingQuality>");
            xml = Regex.Replace(xml, @"<Resolution-BorderlessScreenWidth>[^<]*</Resolution-BorderlessScreenWidth>", $"<Resolution-BorderlessScreenWidth>{ctx.DisplayWidth}</Resolution-BorderlessScreenWidth>");
            xml = Regex.Replace(xml, @"<Resolution-BorderlessScreenHeight>[^<]*</Resolution-BorderlessScreenHeight>", $"<Resolution-BorderlessScreenHeight>{ctx.DisplayHeight}</Resolution-BorderlessScreenHeight>");
            File.WriteAllText(erXml, xml, Encoding.Unicode);
            report.Logs.Add($"✅ 已修复《艾尔登法环》配置：关闭光追，并将无边框分辨率对齐当前屏幕原生 {ctx.DisplayWidth}×{ctx.DisplayHeight}。");
        }

        private static void ApplyAfterburnerClean(ExecutionReport report)
        {
            string? cfg = SystemInspector.FindAfterburnerCfgPath();
            if (cfg == null) return;
            string txt = File.ReadAllText(cfg);
            txt = txt.Replace("+GPU1 power percent", "-GPU1 power percent")
                     .Replace("+GPU1 power,", "-GPU1 power,")
                     .Replace("+GPU1 power\r", "-GPU1 power\r");
            File.WriteAllText(cfg, txt, new UTF8Encoding(false));
            report.Logs.Add("✅ 已屏蔽 MSI Afterburner 对显卡功耗传感器的轮询，消除每秒一次微卡顿。");
        }

        private static void ApplyVoiceECoreIsolation(ExecutionReport report, SystemTelemetrySnapshot ctx)
        {
            long bgMask = ctx.BackgroundAffinityMask;
            var targets = new[] { "oopz", "discord", "kook", "yy", "cloudmusic" };
            int count = 0;

            foreach (var name in targets)
            {
                foreach (var proc in Process.GetProcessesByName(name))
                {
                    try
                    {
                        proc.ProcessorAffinity = (IntPtr)bgMask;
                        proc.PriorityClass = ProcessPriorityClass.BelowNormal;
                        count++;
                        report.Logs.Add($"🔒 已将 {proc.ProcessName} (PID {proc.Id}) 锁定至 {ctx.BackgroundCoreLabel} (0x{bgMask:X}) + BelowNormal，禁止抢占游戏主核。");
                    }
                    catch (Exception ex)
                    {
                        report.Logs.Add($"⚠️ 无法隔离 {proc.ProcessName} (PID {proc.Id}): {ex.Message}");
                    }
                    finally { proc.Dispose(); }
                }
            }

            if (count == 0)
                report.Logs.Add("当前未检测到正在运行的语音开黑进程。");
        }

        /// <summary>
        /// 将完美世界竞技平台 UI/渲染进程锁进后台核（Intel 纯 E 核 / AMD 尾部线程）+ BelowNormal。
        /// 仅操作可访问进程，自动跳过受反作弊内核保护的进程，并反向确认游戏进程拥有全部主核。
        /// </summary>
        public static ExecutionReport ApplyPwaECoreIsolation(bool includeOopz = false)
        {
            var report = new ExecutionReport { Success = true };
            var ctx = GetCtx();
            long bgMask = ctx.BackgroundAffinityMask;
            long mainMask = ctx.ProtectedGameCoreMask;
            long fullMask = ctx.FullCpuMask;

            if (includeOopz)
            {
                int oopzLocked = 0;
                foreach (var p in Process.GetProcessesByName("oopz"))
                {
                    try
                    {
                        p.ProcessorAffinity = (IntPtr)bgMask;
                        p.PriorityClass = ProcessPriorityClass.BelowNormal;
                        oopzLocked++;
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
                if (oopzLocked > 0)
                    report.Logs.Add($"🔒 已将 Oopz ({oopzLocked} 个进程) 锁定至 {ctx.BackgroundCoreLabel} (0x{bgMask:X}) + BelowNormal。");
            }

            int pwaLockedDirect = 0;
            int pwaNeedAdmin = 0;
            foreach (var p in Process.GetProcessesByName("完美世界竞技平台"))
            {
                try
                {
                    p.ProcessorAffinity = (IntPtr)bgMask;
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    pwaLockedDirect++;
                }
                catch
                {
                    pwaNeedAdmin++;
                }
                finally { p.Dispose(); }
            }

            if (pwaLockedDirect > 0)
                report.Logs.Add($"🔒 已免权限将完美平台 {pwaLockedDirect} 个沙盒/GPU 进程锁定至 {ctx.BackgroundCoreLabel} + BelowNormal。");

            if (pwaNeedAdmin > 0)
            {
                var cmds = new List<string>
                {
                    "$procs = Get-Process -Name '完美世界竞技平台' -ErrorAction SilentlyContinue",
                    $"foreach ($p in $procs) {{ try {{ $p.ProcessorAffinity = [IntPtr]0x{bgMask:X}; $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::BelowNormal }} catch {{}} }}",
                    "$games = Get-Process -Name 'cs2','TslGame' -ErrorAction SilentlyContinue",
                    $"foreach ($g in $games) {{ try {{ if (([int64]$g.ProcessorAffinity -band 0x{mainMask:X}) -ne 0x{mainMask:X}) {{ $g.ProcessorAffinity = [IntPtr]0x{fullMask:X} }} }} catch {{}} }}"
                };
                var elev = ExecuteElevatedPowerShell(cmds, $"提权将完美平台 {pwaNeedAdmin} 个管理员界面进程锁定至 {ctx.BackgroundCoreLabel} (自动跳过反作弊受保护内核)");
                report.Logs.AddRange(elev.Logs);
                report.CancelledUac = elev.CancelledUac;
                if (!elev.Success && !elev.CancelledUac) report.Success = false;
            }
            else if (pwaLockedDirect == 0 && !includeOopz)
            {
                report.Logs.Add("当前未检测到正在运行的完美世界竞技平台进程。");
            }

            // 反向安全校验 cs2 / TslGame 保证绝不缺失主游戏核
            foreach (var gName in new[] { "cs2", "TslGame" })
            {
                foreach (var g in Process.GetProcessesByName(gName))
                {
                    try
                    {
                        long aff = (long)g.ProcessorAffinity;
                        if ((aff & mainMask) != mainMask)
                        {
                            g.ProcessorAffinity = (IntPtr)fullMask;
                            report.Logs.Add($"🛡️ 已自动恢复 {gName}.exe 全部主核心权限 (0x{fullMask:X})。");
                        }
                    }
                    catch { }
                    finally { g.Dispose(); }
                }
            }

            return report;
        }

        /// <summary>
        /// 仅锁定 Oopz 进动态后台核心（免管理员权限）。
        /// </summary>
        public static ExecutionReport ApplyOopzOnlyECoreIsolation()
        {
            var report = new ExecutionReport { Success = true };
            var ctx = GetCtx();
            long bgMask = ctx.BackgroundAffinityMask;
            int locked = 0;

            foreach (var p in Process.GetProcessesByName("oopz"))
            {
                try
                {
                    p.ProcessorAffinity = (IntPtr)bgMask;
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    locked++;
                    report.Logs.Add($"🔒 已将 oopz (PID {p.Id}) 锁定至 {ctx.BackgroundCoreLabel} (0x{bgMask:X}) + BelowNormal。");
                }
                catch (Exception ex)
                {
                    report.Success = false;
                    report.Logs.Add($"⚠️ 锁定 oopz (PID {p.Id}) 失败：{ex.Message}");
                }
                finally { p.Dispose(); }
            }

            if (locked == 0)
                report.Logs.Add("当前未检测到正在运行的 Oopz 进程。");
            return report;
        }

        /// <summary>
        /// 修改 Oopz 本地 AI 降噪档位 ("0"=关闭, "1"=轻度)。仅在 Oopz 已退出时写入以防被覆盖。
        /// </summary>
        public static ExecutionReport SetOopzAiMode(string mode)
        {
            var report = new ExecutionReport { Success = true };
            if (Process.GetProcessesByName("oopz").Length > 0)
            {
                report.Success = false;
                report.Logs.Add("⚠️ 检测到 Oopz 正在运行：请先右键托盘彻底退出 Oopz 后再切换 AI 降噪档位（否则退出时会被客户端覆盖）。");
                return report;
            }

            string oopzRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Oopz");
            if (!Directory.Exists(oopzRoot))
            {
                report.Success = false;
                report.Logs.Add("⚠️ 未找到 Oopz 本地配置目录。");
                return report;
            }

            var cfg = Directory.EnumerateFiles(oopzRoot, "mixer_local_setting_cache", SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
            if (cfg == null)
            {
                report.Success = false;
                report.Logs.Add("⚠️ 未找到 Oopz 调音台配置文件 mixer_local_setting_cache。");
                return report;
            }

            string txt = File.ReadAllText(cfg);
            string updated = Regex.IsMatch(txt, @"""aiModeType""\s*:\s*""[^""]*""")
                ? Regex.Replace(txt, @"""aiModeType""\s*:\s*""[^""]*""", $"\"aiModeType\":\"{mode}\"")
                : $"{{\"aiModeType\":\"{mode}\",\"isFreeSpeak\":\"true\"}}";
            File.WriteAllText(cfg, updated, new UTF8Encoding(false));

            string label = mode == "0" ? "关闭 (最低占用)" : mode == "1" ? "轻度降噪 (折中)" : $"档位 {mode}";
            report.Logs.Add($"✅ 已将 Oopz AI 降噪档位修改为：{label}（下次打开 Oopz 即生效）。");
            return report;
        }

        /// <summary>
        /// 最小化 Oopz 与完美世界竞技平台主窗口以降低 Flutter/Electron 渲染开销。
        /// </summary>
        public static ExecutionReport MinimizeVoicePlatformWindows()
        {
            var report = new ExecutionReport { Success = true };
            int count = 0;
            foreach (var name in new[] { "oopz", "完美世界竞技平台" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            NativeWin32.ShowWindow(p.MainWindowHandle, NativeWin32.SW_MINIMIZE);
                            count++;
                        }
                    }
                    catch { }
                    finally { p.Dispose(); }
                }
            }

            report.Logs.Add(count > 0
                ? $"✅ 已最小化 {count} 个语音/平台主窗口（建议进一步点右上角 × 缩进系统托盘以彻底停止界面渲染）。"
                : "未检测到处于展开状态的 Oopz 或完美平台主窗口（可能已最小化到托盘）。");
            return report;
        }

        /// <summary>
        /// 将 Oopz、完美世界竞技平台与游戏进程全部还原回系统默认全核调度。
        /// </summary>
        public static ExecutionReport RestoreVoicePlatformAffinity()
        {
            var report = new ExecutionReport { Success = true };
            var ctx = GetCtx();
            long fullMask = ctx.FullCpuMask;
            int directRestored = 0;
            int needAdmin = 0;

            foreach (var name in new[] { "oopz", "完美世界竞技平台", "cs2", "TslGame" })
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        p.ProcessorAffinity = (IntPtr)fullMask;
                        if (name != "cs2" && name != "TslGame")
                            p.PriorityClass = ProcessPriorityClass.Normal;
                        directRestored++;
                    }
                    catch
                    {
                        if (name == "完美世界竞技平台") needAdmin++;
                    }
                    finally { p.Dispose(); }
                }
            }

            if (directRestored > 0)
                report.Logs.Add($"✅ 已将 {directRestored} 个进程恢复为全核默认调度 (0x{fullMask:X})。");

            if (needAdmin > 0)
            {
                var cmds = new List<string>
                {
                    "$procs = Get-Process -Name '完美世界竞技平台' -ErrorAction SilentlyContinue",
                    $"foreach ($p in $procs) {{ try {{ $p.ProcessorAffinity = [IntPtr]0x{fullMask:X}; $p.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::Normal }} catch {{}} }}"
                };
                var elev = ExecuteElevatedPowerShell(cmds, $"恢复完美平台 {needAdmin} 个管理员进程为默认全核调度");
                report.Logs.AddRange(elev.Logs);
                report.CancelledUac = elev.CancelledUac;
                if (!elev.Success && !elev.CancelledUac) report.Success = false;
            }

            if (directRestored == 0 && needAdmin == 0)
                report.Logs.Add("当前没有运行中的 Oopz 或完美平台进程需要还原。");

            return report;
        }

        // =================== 一次性 UAC 提权统一执行器 ===================

        private static ExecutionReport RunElevatedFixes(IEnumerable<string> fixIds)
        {
            var cmds = new List<string>();
            var descList = new List<string>();
            var list = fixIds.Distinct().ToList();

            if (list.Any(id => id == "pcore_unpark" || id == "mmcss_gaming"))
                CaptureBaselineIfNeeded(isAdminTouch: true);

            foreach (var id in list)
            {
                switch (id)
                {
                    case "pcore_unpark":
                        cmds.Add("powercfg /setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318583 100");
                        cmds.Add("powercfg /setacvalueindex SCHEME_CURRENT 54533251-82be-4824-96c1-47b60b740d00 0cc5b647-c1df-4637-891a-dec35c318584 100");
                        cmds.Add("powercfg /setactive SCHEME_CURRENT");
                        descList.Add("解除 CPU 核心休眠停驻 (CPMINCORES=100%)");
                        break;

                    case "mmcss_gaming":
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile' -Name 'SystemResponsiveness' -Value 10 -Type DWord -ErrorAction SilentlyContinue");
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games' -Name 'GPU Priority' -Value 8 -Type DWord -ErrorAction SilentlyContinue");
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games' -Name 'Priority' -Value 6 -Type DWord -ErrorAction SilentlyContinue");
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\Games' -Name 'Scheduling Category' -Value 'High' -Type String -ErrorAction SilentlyContinue");
                        descList.Add("启用 MMCSS 游戏多线程优先调度");
                        break;

                    case "hvci":
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity' -Name 'Enabled' -Value 0 -Type DWord -ErrorAction SilentlyContinue");
                        descList.Add("关闭内核隔离内存完整性 (HVCI)");
                        break;

                    case "pagefile":
                        cmds.Add("$cs = Get-CimInstance Win32_ComputerSystem; if ($cs.AutomaticManagedPagefile) { Set-CimInstance -InputObject $cs -Property @{ AutomaticManagedPagefile = $false } }");
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management' -Name 'PagingFiles' -Value @('C:\\pagefile.sys 16384 32768') -Type MultiString");
                        descList.Add("固定 C 盘虚拟内存为 16GB~32GB");
                        break;

                    case "intel_ipf":
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Services\\WUDFRd' -Name 'Start' -Value 2 -Type DWord -ErrorAction SilentlyContinue");
                        cmds.Add("Restart-Service -Name 'ipfsvc' -Force -ErrorAction SilentlyContinue");
                        cmds.Add("Restart-Service -Name 'dptftcs' -Force -ErrorAction SilentlyContinue");
                        cmds.Add("Restart-Service -Name 'PRI-Driver' -Force -ErrorAction SilentlyContinue");
                        descList.Add("修复并重启 Intel IPF/DTT 温控服务");
                        break;

                    case "fast_boot":
                        cmds.Add("powercfg /hibernate on");
                        cmds.Add("powercfg /hibernate /type reduced");
                        cmds.Add("Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power' -Name 'HiberbootEnabled' -Value 1 -Type DWord");
                        descList.Add("将休眠切换为 Reduced 精简快速启动");
                        break;
                }
            }

            if (cmds.Count == 0)
                return new ExecutionReport { Success = true };

            return ExecuteElevatedPowerShell(cmds, string.Join("、", descList));
        }

        private static ExecutionReport ExecuteElevatedPowerShell(IEnumerable<string> commands, string actionDesc)
        {
            var report = new ExecutionReport { Success = true };
            string tempScript = Path.Combine(Path.GetTempPath(), $"GSA_Admin_{Guid.NewGuid():N}.ps1");

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
                foreach (var c in commands)
                    sb.AppendLine(c);

                File.WriteAllText(tempScript, sb.ToString(), new UTF8Encoding(true));

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{tempScript}\"",
                    UseShellExecute = !NativeWin32.IsElevated(),
                    CreateNoWindow = NativeWin32.IsElevated(),
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                if (!NativeWin32.IsElevated())
                    psi.Verb = "runas";

                using var proc = Process.Start(psi);
                proc?.WaitForExit(15000);
                report.Logs.Add($"🛡️ 已完成管理员级底层操作：{actionDesc}（注：HVCI 与虚拟内存若为首次修改，将在下次重启后完全生效）。");
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                report.Success = false;
                report.CancelledUac = true;
                report.Logs.Add($"⚠️ 你取消了管理员授权弹窗，跳过：{actionDesc}。");
            }
            catch (Exception ex)
            {
                report.Success = false;
                report.Logs.Add($"❌ 管理员操作执行失败：{ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempScript)) File.Delete(tempScript); } catch { }
            }

            return report;
        }

        // =================== 辅助文本与目录函数 ===================

        private static void CaptureBaselineIfNeeded(bool isAdminTouch)
        {
            Directory.CreateDirectory(StateDir);
            if (File.Exists(RestoreJsonFile))
            {
                if (isAdminTouch)
                {
                    try
                    {
                        var existing = JsonSerializer.Deserialize<RestoreBaselineV4>(File.ReadAllText(RestoreJsonFile));
                        if (existing != null && !existing.TouchedAdminItems)
                        {
                            existing.TouchedAdminItems = true;
                            File.WriteAllText(RestoreJsonFile, JsonSerializer.Serialize(existing, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                        }
                    }
                    catch { }
                }
                return;
            }

            string schemeOut = SystemInspector.RunProcess("powercfg", "/getactivescheme", 2000);
            var m = Regex.Match(schemeOut ?? "", @"[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}");
            var baseline = new RestoreBaselineV4
            {
                CapturedAt = DateTime.Now,
                PowerGuid = m.Success ? m.Value : "",
                AutoGameModeEnabled = ReadHkcuOptional(@"Software\Microsoft\GameBar", "AutoGameModeEnabled"),
                AllowAutoGameMode = ReadHkcuOptional(@"Software\Microsoft\GameBar", "AllowAutoGameMode"),
                GameDVR_Enabled = ReadHkcuOptional(@"System\GameConfigStore", "GameDVR_Enabled"),
                AppCaptureEnabled = ReadHkcuOptional(@"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled"),
                PcieAspmAcHex = SystemInspector.QueryPowerAcHex("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5"),
                UsbSuspendAcHex = SystemInspector.QueryPowerAcHex("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226"),
                CoreParkMinAcHex = SystemInspector.QueryPowerAcHex("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583"),
                Cdm338388 = ReadHkcuOptional(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SubscribedContent-338388Enabled"),
                CdmSystemPane = ReadHkcuOptional(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SystemPaneSuggestionsEnabled"),
                CdmSoftLanding = ReadHkcuOptional(@"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager", "SoftLandingEnabled"),
                MmcssGpuPriority = ReadHklmOptional(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "GPU Priority"),
                MmcssPriority = ReadHklmOptional(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Priority"),
                MmcssSchedCategory = ReadHklmStringOptional(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games", "Scheduling Category"),
                MmcssSystemResp = ReadHklmOptional(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile", "SystemResponsiveness"),
                TouchedAdminItems = isAdminTouch
            };
            File.WriteAllText(RestoreJsonFile, JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        }

        private static string ReadHklmOptional(string subKey, string name)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(subKey);
                var val = k?.GetValue(name);
                return val != null ? Convert.ToInt32(val).ToString() : "missing";
            }
            catch { return "missing"; }
        }

        private static string ReadHklmStringOptional(string subKey, string name)
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(subKey);
                return k?.GetValue(name)?.ToString() ?? "missing";
            }
            catch { return "missing"; }
        }

        private static string ReplaceIniLine(string text, string key, string value)
        {
            string pattern = $@"(?m)^{Regex.Escape(key)}\s*=.*$";
            if (Regex.IsMatch(text, pattern))
                return Regex.Replace(text, pattern, $"{key}={value}");
            return text.TrimEnd() + $"\r\n{key}={value}\r\n";
        }

        private static string ReplaceOrSetVdfKey(string text, string key, string value)
        {
            string pattern = $@"(?m)^(\s*""{Regex.Escape(key)}""\s+)""[^""]*""";
            if (Regex.IsMatch(text, pattern))
                return Regex.Replace(text, pattern, $"$1\"{value}\"");
            int lastBrace = text.LastIndexOf('}');
            if (lastBrace > 0)
                return text.Insert(lastBrace, $"\t\"{key}\"\t\t\"{value}\"\r\n");
            return text;
        }

        private static void WriteHkcuDword(string subKey, string name, int value)
        {
            using var k = Registry.CurrentUser.CreateSubKey(subKey);
            k?.SetValue(name, value, RegistryValueKind.DWord);
        }

        private static string ReadHkcuOptional(string subKey, string name)
        {
            using var k = Registry.CurrentUser.OpenSubKey(subKey);
            var val = k?.GetValue(name);
            return val != null ? Convert.ToInt32(val).ToString() : "missing";
        }

        private static void RestoreHkcuDword(string subKey, string name, string val)
        {
            using var k = Registry.CurrentUser.CreateSubKey(subKey);
            if (k == null) return;
            if (val == "missing") k.DeleteValue(name, false);
            else if (int.TryParse(val, out int num)) k.SetValue(name, num, RegistryValueKind.DWord);
        }

        private static long GetDirSize(string path)
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

        private static void CleanDirectoryContents(string path)
        {
            if (!Directory.Exists(path)) return;
            foreach (var f in Directory.GetFiles(path))
            {
                try { File.Delete(f); } catch { }
            }
            foreach (var d in Directory.GetDirectories(path))
            {
                try { Directory.Delete(d, true); } catch { }
            }
        }
    }
}
