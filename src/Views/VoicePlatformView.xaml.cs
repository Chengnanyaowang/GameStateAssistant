using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameStateAssistant.Models;

namespace GameStateAssistant.Views
{
    public partial class VoicePlatformView : UserControl
    {
        public event EventHandler? IsolateBothRequested;
        public event EventHandler? IsolateOopzRequested;
        public event EventHandler? IsolatePwaRequested;
        public event EventHandler<string>? SetOopzAiModeRequested;
        public event EventHandler? MinimizeWindowsRequested;
        public event EventHandler? RestoreAffinityRequested;

        public VoicePlatformView()
        {
            InitializeComponent();
        }

        public void Render(SystemTelemetrySnapshot snap)
        {
            var vp = snap.VoicePlatform;
            string targetLabel = snap.BackgroundCoreLabel;
            string modeDesc = snap.IsAmdEffective
                ? $"当前为 AMD 模式 ({snap.CpuShortName} · {snap.LogicalCoreCount} 线程)，将语音与对战平台锁进 {targetLabel} + BelowNormal，把前部主线程留给 PUBG / CS2。"
                : $"当前为 Intel 模式 ({snap.CpuShortName} · {snap.LogicalCoreCount} 线程)，将语音与对战平台物理锁进 {targetLabel} + BelowNormal，绝不抢占游戏 P 核。";
            VpSubtitleText.Text = modeDesc;
            BtnIsolateBothLabel.Text = snap.IsAmdEffective ? "一键双隔离进尾核" : "一键双隔离进纯 E 核";
            BtnIsolateOopz.Content = snap.IsAmdEffective ? "锁定尾部线程 (免权限)" : "锁定纯 E 核 (免权限)";
            BtnIsolatePwa.Content = snap.IsAmdEffective ? "将完美平台界面锁进尾核" : "将完美平台全部界面进程锁进纯 E 核";

            // --- 1. Oopz 卡片 ---
            OopzPathText.Text = string.IsNullOrEmpty(vp.OopzPath)
                ? "未检测到运行中的 Oopz 进程"
                : vp.OopzPath;

            if (!vp.OopzRunning)
            {
                SetBadge(OopzBadge, OopzBadgeText, "未运行", "Brush.Status.NeutralBg", "Brush.Status.Neutral");
                OopzRamText.Text = "0 MB";
                OopzCpuText.Text = "0 s";
                OopzDetailText.Text = $"核心绑定状态：当前未运行（启动后点击下方按钮即可锁定至 {targetLabel}）。";
                BtnIsolateOopz.IsEnabled = false;
            }
            else if (vp.OopzUnisolatedCount == 0)
            {
                SetBadge(OopzBadge, OopzBadgeText, "核心已隔离", "Brush.Status.OkBg", "Brush.Status.Ok");
                OopzRamText.Text = FormatMem(vp.OopzMemoryMb);
                OopzCpuText.Text = FormatCpu(vp.OopzCpuSeconds);
                OopzDetailText.Text = $"核心绑定状态：{vp.OopzIsolatedCount}/{vp.OopzTotalCount} 个进程已安全锁进 {targetLabel} · BelowNormal，零抢占主游戏核心。";
                BtnIsolateOopz.IsEnabled = true;
            }
            else
            {
                SetBadge(OopzBadge, OopzBadgeText, "正在抢占主核", "Brush.Status.BadBg", "Brush.Status.Bad");
                OopzRamText.Text = FormatMem(vp.OopzMemoryMb);
                OopzCpuText.Text = FormatCpu(vp.OopzCpuSeconds);
                OopzDetailText.Text = $"核心绑定状态：发现 {vp.OopzUnisolatedCount} 个未隔离进程正在与游戏争抢主核心，建议立即锁定至 {targetLabel}！";
                BtnIsolateOopz.IsEnabled = true;
            }

            OopzAiText.Text = vp.OopzAiMode switch
            {
                "0" => "关闭 (最佳)",
                "1" => "轻度 (折中)",
                "2" => "强力 (高占!)",
                _ => "未检出"
            };
            OopzAiText.Foreground = (Brush)FindResource(vp.OopzAiMode == "2" ? "Brush.Status.Bad" :
                vp.OopzAiMode == "0" ? "Brush.Status.Ok" : "Brush.Status.Warn");

            OopzShortcutText.Text = vp.OopzShortcutHasECore
                ? "桌面直启快捷方式：已配置纯 E 核直启 (/affinity 3FC00 + BelowNormal)"
                : "桌面直启快捷方式：普通启动（运行后需手动点一次隔离）";

            // --- 2. 完美世界竞技平台卡片 ---
            PwaPathText.Text = string.IsNullOrEmpty(vp.PwaInstallPath)
                ? "未检测到完美世界竞技平台"
                : vp.PwaInstallPath;

            if (!vp.PwaRunning)
            {
                SetBadge(PwaBadge, PwaBadgeText, "未运行", "Brush.Status.NeutralBg", "Brush.Status.Neutral");
                PwaRamText.Text = "0 个 / 0 MB";
                PwaCpuText.Text = "0 s";
                PwaLockText.Text = "--";
                PwaLockText.Foreground = (Brush)FindResource("Brush.Text.Secondary");
                PwaDetailText.Text = "隔离明细：当前未检测到运行中的完美平台进程。";
                BtnIsolatePwa.IsEnabled = false;
            }
            else
            {
                PwaRamText.Text = $"{vp.PwaTotalCount} 进程 / {FormatMem(vp.PwaMemoryMb)}";
                PwaCpuText.Text = vp.PwaCpuSeconds > 0 ? FormatCpu(vp.PwaCpuSeconds) : "需提权统计";
                PwaLockText.Text = $"{vp.PwaIsolatedCount} / {vp.PwaTotalCount} 已锁";
                BtnIsolatePwa.IsEnabled = true;

                if (vp.PwaUnisolatedUserCount == 0 && vp.PwaElevatedCount == 0)
                {
                    SetBadge(PwaBadge, PwaBadgeText, "全部纯 E 核", "Brush.Status.OkBg", "Brush.Status.Ok");
                    PwaLockText.Foreground = (Brush)FindResource("Brush.Status.Ok");
                    PwaDetailText.Text = $"隔离明细：全部 {vp.PwaIsolatedCount} 个可访问界面/渲染进程已锁定在 Core 10~17 纯 E 核 + BelowNormal。";
                }
                else if (vp.PwaElevatedCount > 0)
                {
                    SetBadge(PwaBadge, PwaBadgeText, "需提权锁全部", "Brush.Status.WarnBg", "Brush.Status.Warn");
                    PwaLockText.Foreground = (Brush)FindResource("Brush.Status.Warn");
                    PwaDetailText.Text = $"隔离明细：已锁 {vp.PwaIsolatedCount} 个，免权待锁 {vp.PwaUnisolatedUserCount} 个，另有 {vp.PwaElevatedCount} 个管理员级界面进程需点下方按钮一次 UAC 提权锁定（自动跳过反作弊内核）。";
                }
                else
                {
                    SetBadge(PwaBadge, PwaBadgeText, "正在抢占 P 核", "Brush.Status.BadBg", "Brush.Status.Bad");
                    PwaLockText.Foreground = (Brush)FindResource("Brush.Status.Bad");
                    PwaDetailText.Text = $"隔离明细：发现 {vp.PwaUnisolatedUserCount} 个渲染/界面进程未锁入纯 E 核，建议点击下方按钮一键锁定。";
                }
            }

            if (string.IsNullOrEmpty(vp.PwaInstallPath))
            {
                PwaDiskText.Text = "磁盘健康度：未检出安装目录";
                PwaDiskText.Foreground = (Brush)FindResource("Brush.Text.Tertiary");
            }
            else if (vp.PwaOnMechanicalDrive)
            {
                PwaDiskText.Text = $"磁盘提醒：当前装在机械硬盘 ({vp.PwaInstallPath.Substring(0, 2)})，后台频繁写缓存易唤醒磁头导致微卡，建议抽空迁至 F 盘固态。";
                PwaDiskText.Foreground = (Brush)FindResource("Brush.Status.Warn");
            }
            else
            {
                PwaDiskText.Text = $"磁盘健康度：位于高速固态硬盘 ({vp.PwaInstallPath.Substring(0, 2)})，无机械盘唤醒风险。";
                PwaDiskText.Foreground = (Brush)FindResource("Brush.Status.Ok");
            }

            // --- 3. CS2 反向安全校验 ---
            Cs2VerifyText.Text = vp.Cs2AffinitySummary;
            Cs2CheckIconBg.Background = (Brush)FindResource(vp.Cs2HasAllPCores ? "Brush.Status.OkBg" : "Brush.Status.BadBg");
        }

        private void SetBadge(Border border, TextBlock text, string label, string bgKey, string fgKey)
        {
            text.Text = label;
            border.Background = (Brush)FindResource(bgKey);
            text.Foreground = (Brush)FindResource(fgKey);
        }

        private static string FormatMem(double mb) => mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F0} MB";
        private static string FormatCpu(double sec) => sec >= 60 ? $"{sec / 60.0:F1} 分钟" : $"{sec:F0} 秒";

        private void IsolateBoth_Click(object sender, RoutedEventArgs e) => IsolateBothRequested?.Invoke(this, EventArgs.Empty);
        private void IsolateOopz_Click(object sender, RoutedEventArgs e) => IsolateOopzRequested?.Invoke(this, EventArgs.Empty);
        private void IsolatePwa_Click(object sender, RoutedEventArgs e) => IsolatePwaRequested?.Invoke(this, EventArgs.Empty);
        private void OopzAiOff_Click(object sender, RoutedEventArgs e) => SetOopzAiModeRequested?.Invoke(this, "0");
        private void OopzAiLight_Click(object sender, RoutedEventArgs e) => SetOopzAiModeRequested?.Invoke(this, "1");
        private void MinimizeWindows_Click(object sender, RoutedEventArgs e) => MinimizeWindowsRequested?.Invoke(this, EventArgs.Empty);
        private void RestoreAffinity_Click(object sender, RoutedEventArgs e) => RestoreAffinityRequested?.Invoke(this, EventArgs.Empty);
    }
}
