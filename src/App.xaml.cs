using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using GameStateAssistant.Services;

namespace GameStateAssistant
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            if (e.Args.Contains("--verify", StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var snap = SystemInspector.CollectAll(Models.CpuPlatformMode.Auto);
                    var amdSnap = SystemInspector.CollectAll(Models.CpuPlatformMode.Amd);
                    // 校验主窗口及全部 XAML 资源字典/自定义控件可无异常实例化与渲染
                    var win = new MainWindow();
                    win.Close();
                    var sb = new StringBuilder();
                    sb.AppendLine($"Platform={snap.EffectivePlatform} (Detected={snap.DetectedCpuVendor}, CPU={snap.CpuName}, Cores={snap.LogicalCoreCount}, BgMask=0x{snap.BackgroundAffinityMask:X} [{snap.BackgroundCoreLabel}])");
                    sb.AppendLine($"Score={snap.ReadinessScore} | Title={snap.ReadinessTitle} | Cap={snap.RecommendedFpsCap}FPS");
                    sb.AppendLine($"GPU={snap.GpuName} | Temp={snap.GpuTempC}C | VRAM={snap.GpuMemUsedMb}/{snap.GpuMemTotalMb}MB");
                    sb.AppendLine($"RAM={snap.RamFreeGb:F1}/{snap.RamTotalGb:F1}GB ({snap.RamLabelSummary}) | C_Free={snap.CDriveFreeGb:F1}GB | Display={snap.DisplayInfo}");
                    sb.AppendLine($"SystemChecks={snap.SystemChecks.Count} | OnlineChecks={snap.OnlineGameChecks.Count} | AaaChecks={snap.AaaGameChecks.Count} | BgProcs={snap.BackgroundProcesses.Count}");
                    foreach (var c in snap.SystemChecks) sb.AppendLine($"  [SYS][{c.StatusBadge}][{c.RiskBadge}] {c.Title}: {c.CurrentValue}");
                    foreach (var c in snap.OnlineGameChecks) sb.AppendLine($"  [ONLINE][{c.StatusBadge}][{c.RiskBadge}] {c.Title}: {c.CurrentValue}");
                    foreach (var c in snap.AaaGameChecks) sb.AppendLine($"  [AAA][{c.StatusBadge}][{c.RiskBadge}] {c.Title}: {c.CurrentValue}");
                    sb.AppendLine($"--- AMD Mode Preview ({amdSnap.EffectivePlatform}, BgMask=0x{amdSnap.BackgroundAffinityMask:X} [{amdSnap.BackgroundCoreLabel}]) ---");
                    foreach (var c in amdSnap.SystemChecks.Where(x => x.Id is "power_plan" or "pcore_unpark" or "amd_chipset" or "amd_ram_guard" or "mmcss_gaming" or "bus_latency"))
                        sb.AppendLine($"  [AMD-SYS][{c.StatusBadge}][{c.RiskBadge}] {c.Title}: {c.CurrentValue}");
                    var vp = snap.VoicePlatform;
                    sb.AppendLine($"  [VP] Oopz(Run={vp.OopzRunning}, Iso={vp.OopzIsolatedCount}/{vp.OopzTotalCount}, AI={vp.OopzAiMode}, LnkECore={vp.OopzShortcutHasECore}) | PWA(Run={vp.PwaRunning}, Iso={vp.PwaIsolatedCount}/{vp.PwaTotalCount}, Elev={vp.PwaElevatedCount}, HDD={vp.PwaOnMechanicalDrive}) | Game={vp.Cs2AffinitySummary}");

                    string outFile = Path.Combine(Path.GetTempPath(), "gsa_verify.txt");
                    File.WriteAllText(outFile, sb.ToString(), new UTF8Encoding(false));
                    Shutdown(0);
                    return;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "gsa_verify.txt"), "ERROR: " + ex);
                    Shutdown(1);
                    return;
                }
            }

            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"程序遇到异常：\n{args.Exception.Message}", "GameState Assistant", MessageBoxButton.OK, MessageBoxImage.Warning);
                args.Handled = true;
            };
        }
    }
}
