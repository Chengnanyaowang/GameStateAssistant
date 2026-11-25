using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GameStateAssistant.Controls;
using GameStateAssistant.Models;
using GameStateAssistant.Services;

namespace GameStateAssistant
{
    public partial class MainWindow : Window
    {
        private SystemTelemetrySnapshot? _snapshot;
        private bool _isBusy;
        private bool _isLoaded;
        private CpuPlatformMode _platformMode = CpuPlatformMode.Auto;
        private readonly DispatcherTimer _liveTimer;
        private readonly StringBuilder _sessionLog = new StringBuilder();
        private CheckStatus? _pendingFilter;

        public MainWindow()
        {
            InitializeComponent();

            // 绑定事件
            ViewOverview.PreGameBoostRequested += async (_, _) => await HandlePreGameBoostAsync();
            ViewOverview.FixRecommendedRequested += async (_, _) => await HandleFixRecommendedAsync();
            ViewOverview.NavigateToChecksRequested += (_, filter) => JumpFromOverview(filter);

            ViewChecks.SingleFixRequested += async (_, item) => await HandleSingleFixAsync(item);
            ViewChecks.BatchFixRequested += async (_, items) => await HandleBatchFixAsync(items, "分舱批量优化确认");

            ViewCleanup.CloseProcessesRequested += async (_, procs) => await HandleCloseProcessesAsync(procs);
            ViewCleanup.CleanCachesRequested += async (_, caches) => await HandleCleanCachesAsync(caches);

            ViewVoicePlatform.IsolateBothRequested += async (_, _) => await HandleVoicePlatformActionAsync("isolate_both");
            ViewVoicePlatform.IsolateOopzRequested += async (_, _) => await HandleVoicePlatformActionAsync("isolate_oopz");
            ViewVoicePlatform.IsolatePwaRequested += async (_, _) => await HandleVoicePlatformActionAsync("isolate_pwa");
            ViewVoicePlatform.SetOopzAiModeRequested += async (_, mode) => await HandleVoicePlatformActionAsync($"oopz_ai_{mode}");
            ViewVoicePlatform.MinimizeWindowsRequested += async (_, _) => await HandleVoicePlatformActionAsync("minimize_vp");
            ViewVoicePlatform.RestoreAffinityRequested += async (_, _) => await HandleVoicePlatformActionAsync("restore_vp");

            Toast.OpenLogRequested += (_, _) => LogDrawer.Visibility = Visibility.Visible;

            // 轻量遥测定时器（仅窗口在前台且未检测到游戏运行时采样，守住 CPU < 0.5% 预算）
            _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _liveTimer.Tick += async (_, _) => await SampleLiveTelemetryAsync();

            Loaded += async (_, _) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (NativeWin32.EnableWin11Mica(hwnd))
                {
                    // Mica 成功开启，允许底层合成渗透
                    RootBackdrop.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CC0E1117"));
                }

                _isLoaded = true;
                await RefreshAllAsync();
                _liveTimer.Start();
            };

            StateChanged += (_, _) =>
            {
                BtnMaximize.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
                UpdateQuietPolicy();
            };

            Activated += (_, _) => UpdateQuietPolicy();
            Deactivated += (_, _) => UpdateQuietPolicy();
        }

        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseWindow_Click(object sender, RoutedEventArgs e) => Close();

        private async void PlatformMode_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            _platformMode = PlatformIntel.IsChecked == true ? CpuPlatformMode.Intel
                : PlatformAmd.IsChecked == true ? CpuPlatformMode.Amd
                : CpuPlatformMode.Auto;
            string modeName = _platformMode switch
            {
                CpuPlatformMode.Intel => "Intel 大小核模式",
                CpuPlatformMode.Amd => "AMD 锐龙模式",
                _ => "自动硬件识别模式"
            };
            await RefreshAllAsync($"已切换为 {modeName}，正在重新按该平台规则体检...", force: true);
        }

        private void ToggleLogDrawer_Click(object sender, RoutedEventArgs e)
        {
            LogDrawer.Visibility = LogDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Nav_Changed(object sender, RoutedEventArgs e)
        {
            if (ViewOverview == null) return;
            ViewOverview.Visibility = NavOverview.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewChecks.Visibility = (NavSystem.IsChecked == true || NavOnline.IsChecked == true || NavAaa.IsChecked == true)
                ? Visibility.Visible : Visibility.Collapsed;
            ViewVoicePlatform.Visibility = NavVoicePlatform.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            ViewCleanup.Visibility = NavCleanup.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

            int navIndex = NavOverview.IsChecked == true ? 0
                : NavSystem.IsChecked == true ? 1
                : NavOnline.IsChecked == true ? 2
                : NavAaa.IsChecked == true ? 3
                : NavVoicePlatform.IsChecked == true ? 4 : 5;
            AnimateNavIndicator(navIndex);

            if (_snapshot != null)
            {
                var f = _pendingFilter;
                _pendingFilter = null;
                LoadCurrentChecksSection(_snapshot, f);
            }
        }

        private void AnimateNavIndicator(int index)
        {
            // 每个导航项约高 42px
            double targetY = index * 42.0;
            if (ScoreRing.AnimationsEnabled && SystemParameters.ClientAreaAnimation)
            {
                var anim = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            else
            {
                NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, null);
                NavIndicatorTransform.Y = targetY;
            }
        }

        private void JumpFromOverview(CheckStatus? filter)
        {
            _pendingFilter = filter;
            if (_snapshot == null) return;

            // 智能跳到有待优化项的分舱，否则默认进入系统底座
            if (_snapshot.OnlineGameChecks.Any(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention))
                NavOnline.IsChecked = true;
            else if (_snapshot.AaaGameChecks.Any(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention))
                NavAaa.IsChecked = true;
            else
                NavSystem.IsChecked = true;
        }

        private void LoadCurrentChecksSection(SystemTelemetrySnapshot snap, CheckStatus? filter = null)
        {
            if (NavSystem.IsChecked == true)
            {
                string platTag = snap.IsAmdEffective
                    ? $"AMD 锐龙模式 · {snap.CpuShortName} (实测硬件: {snap.DetectedCpuVendor})"
                    : $"Intel 大小核模式 · {snap.CpuShortName} (实测硬件: {snap.DetectedCpuVendor})";
                ViewChecks.LoadSection(
                    "system",
                    $"系统底座优化 ({platTag})",
                    snap.IsAmdEffective
                        ? "针对 AMD 锐龙（7500F / 7800X3D / 9800X3D 等）优化：芯片组与 CPPC 调度、16GB 单通道红线、核心零休眠、MMCSS 游戏优先、PCIe/USB 节能与完整可回滚基线。"
                        : "针对 Intel 混合架构优化：平衡计划保护 Thread Director、P 核零休眠、IPF/DTT 服务、MMCSS 游戏优先、PCIe/USB 节能与完整可回滚基线（HVCI 永不参与批量）。",
                    "批量修复系统异常项",
                    snap.SystemChecks,
                    snap.IsAmdEffective ? "AMD 平台提帧关键提示 (含 7500F + 3070 场景)" : "Intel 大小核与安全回滚铁律",
                    snap.IsAmdEffective
                        ? "• 芯片组与内存：务必安装 AMD 官网最新 AM4/AM5 Chipset 驱动；若为 16GB 单通道内存玩 PUBG，必须固定 16GB+ 固态盘分页文件并赛前清掉浏览器，硬件升级优先补齐第二根内存开 EXPO。"
                        : "• 大小核调度：12~14 代及 Core Ultra 保持「平衡」电源计划 + P 核零休眠，语音与对战平台放进后置纯 E 核。",
                    "• 完整可回滚：本工具内置 optimizerDuck 风格风险分级与原值 JSON 快照，系统项修改前自动保存原始值，可随时通过左下角「还原原始设置」撤回。",
                    filter);
            }
            else if (NavOnline.IsChecked == true)
            {
                int cap = snap.RecommendedFpsCap;
                ViewChecks.LoadSection(
                    "online",
                    $"竞技网游专场 (PUBG · CS2 · 英雄联盟 · 无畏契约 · 目标 {cap} FPS)",
                    $"按当前 {snap.DisplayRefreshRate}Hz 屏自动对齐 RTSS {cap} FPS 单通道控帧 + 免权限游戏配置调优 + 语音让核，全面拉高 1% Low 帧。",
                    "批量修复网游异常项",
                    snap.OnlineGameChecks,
                    "竞技网游通用控帧与外设铁律",
                    $"• 单通道控帧：PUBG / CS2 / LOL 局内均设「无限制」并关闭垂直同步，统一由 RTSS {cap} FPS 控帧稳吃 {snap.DisplayRefreshRate}Hz G-Sync/FreeSync。",
                    snap.IsAmdEffective
                        ? "• AMD + 8GB 显卡打 PUBG：局内优先选「DX11 增强版」、渲染 100%、纹理中/高、阴影/后期/特效/树木全部非常低，比 DX12 更省显存且 1% Low 更稳。"
                        : "• 外设细节：打 LOL 等老引擎网游鼠标建议 ≤1000Hz；使用 USB DSP 耳机打 CS2 保持 snd_mixahead 0.025。",
                    filter);
            }
            else if (NavAaa.IsChecked == true)
            {
                string vramLabel = snap.GpuAvailable ? $"{snap.GpuMemTotalMb / 1024.0:F0}GB" : "显卡";
                ViewChecks.LoadSection(
                    "aaa",
                    $"3A 大作专场 ({snap.GpuShortName} · {vramLabel} 显存稳帧防线)",
                    $"动态监控 {snap.GpuShortName} 显存安全线、杜绝 PCIe 爆显存腰斩，精准对齐 {snap.DisplayWidth}×{snap.DisplayHeight} 原生点对点。",
                    "批量修复 3A 配置异常项",
                    snap.AaaGameChecks,
                    "8GB / 中高端显卡通杀 3A 稳帧法则",
                    "• 贴图质量选「高 (High)」而非「超高」：肉眼几乎无差，立省 1.5~2GB 显存，彻底杜绝玩半小时后掉帧。",
                    "• 善用 DLSS / FSR 质量档并将总显存控制在总容量 90% 以内；8GB 显存显卡开启帧生成或游玩 2K 3A 时建议关闭光线追踪。",
                    filter);
            }
        }

        private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAllAsync();

        private async Task RefreshAllAsync(string? customStatus = null, bool force = false)
        {
            if (_isBusy && !force) return;
            SetBusy(true, customStatus ?? "正在扫描系统底座、显卡遥测与游戏配置...");

            try
            {
                var mode = _platformMode;
                var snap = await Task.Run(() => SystemInspector.CollectAll(mode));
                _snapshot = snap;
                OptimizationEngine.ActiveContext = snap;
                RenderAll(snap);
                if (customStatus == null)
                    StatusFooterText.Text = $"✔ 全盘体检完成 ({snap.Timestamp:HH:mm:ss}) · [{snap.EffectivePlatform} 规则] · 配置合规分 {snap.ReadinessScore}/100";
            }
            catch (Exception ex)
            {
                StatusFooterText.Text = $"❌ 检测异常: {ex.Message}";
                AppendLog($"[错误] 扫描异常: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RenderAll(SystemTelemetrySnapshot snap)
        {
            PrivilegeText.Text = snap.IsProcessElevated ? "管理员模式 (免弹窗)" : "标准权限 (按需自动提权)";
            PrivilegeBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(snap.IsProcessElevated ? "#2010B981" : "#14FFFFFF"));

            SideGameText.Text = snap.RunningGame;
            SideUptimeText.Text = snap.Uptime.TotalDays >= 1
                ? $"连续运行: {(int)snap.Uptime.TotalDays}天 {snap.Uptime.Hours}小时"
                : $"连续运行: {snap.Uptime.Hours}小时 {snap.Uptime.Minutes}分";
            BtnRestore.IsEnabled = snap.HasSavedRestoreState;

            UpdateBadges(snap);
            UpdateQuietPolicy();

            ViewOverview.Render(snap);
            ViewVoicePlatform.Render(snap);
            ViewCleanup.Render(snap);
            LoadCurrentChecksSection(snap);
        }

        private void UpdateBadges(SystemTelemetrySnapshot snap)
        {
            int sysBad = snap.SystemChecks.Count(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention);
            int onlineBad = snap.OnlineGameChecks.Count(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention);
            int aaaBad = snap.AaaGameChecks.Count(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention);
            int vpBad = snap.VoicePlatform.PendingCount;

            BadgeSystem.Visibility = sysBad > 0 ? Visibility.Visible : Visibility.Collapsed;
            BadgeSystemText.Text = sysBad.ToString();

            BadgeOnline.Visibility = onlineBad > 0 ? Visibility.Visible : Visibility.Collapsed;
            BadgeOnlineText.Text = onlineBad.ToString();

            BadgeAaa.Visibility = aaaBad > 0 ? Visibility.Visible : Visibility.Collapsed;
            BadgeAaaText.Text = aaaBad.ToString();

            BadgeVoicePlatform.Visibility = vpBad > 0 ? Visibility.Visible : Visibility.Collapsed;
            BadgeVoicePlatformText.Text = vpBad.ToString();
        }

        private void UpdateQuietPolicy()
        {
            bool inGame = _snapshot?.IsGameRunning == true;
            ScoreRing.AnimationsEnabled = !inGame;
            QuietModeBadge.Visibility = inGame ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task SampleLiveTelemetryAsync()
        {
            // 仅在助手处于前台激活、未最小化、非忙碌且没有游戏在运行时采样，守住零抢占预算
            if (_isBusy || _snapshot == null || _snapshot.IsGameRunning) return;
            if (WindowState == WindowState.Minimized || !IsActive) return;
            if (NavOverview.IsChecked != true) return;

            var sample = await Task.Run(() =>
            {
                var mem = NativeWin32.GetMemoryInfo();
                string gpuCsv = SystemInspector.RunProcess("nvidia-smi", "--query-gpu=temperature.gpu,power.draw,memory.used,memory.total,utilization.gpu --format=csv,noheader,nounits", 1200);
                double t = 0, pwr = 0, used = 0, total = 0, util = 0;
                if (!string.IsNullOrWhiteSpace(gpuCsv))
                {
                    var parts = gpuCsv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split(',');
                    if (parts != null && parts.Length >= 5)
                    {
                        double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out t);
                        double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out pwr);
                        double.TryParse(parts[2].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out used);
                        double.TryParse(parts[3].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out total);
                        double.TryParse(parts[4].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out util);
                    }
                }
                double vramPct = total > 0 ? Math.Min(100, used / total * 100.0) : 0;
                return (vramPct, t, pwr, util, used, total, mem.freeGb, mem.usedPercent);
            });

            ViewOverview.PushLiveSample(sample.vramPct, sample.t, sample.pwr, sample.util, sample.used, sample.total,
                sample.freeGb, sample.usedPercent, _snapshot.SystemCpuPercent);
        }

        // =================== 交互事件处理（全部经 ConfirmDialog 逐项确认） ===================

        private async Task HandlePreGameBoostAsync()
        {
            if (_snapshot == null || _isBusy) return;

            var entries = new List<ConfirmActionEntry>();

            foreach (var c in _snapshot.SystemChecks.Where(c => !c.RequiresAdmin && c.CanAutoFix && !c.ExcludeFromBatchFix)
                .Concat(_snapshot.OnlineGameChecks.Where(c => c.CanAutoFix && !c.ExcludeFromBatchFix)))
            {
                entries.Add(new ConfirmActionEntry
                {
                    Kind = "check",
                    Key = c.Id,
                    Title = c.Title,
                    Detail = c.CurrentValue,
                    TagText = c.RequiresAdmin ? "需提权" : "免权限",
                    RequiresAdmin = c.RequiresAdmin,
                    IsSelected = true
                });
            }

            foreach (var p in _snapshot.BackgroundProcesses.Where(x => x.CanClose && x.IsSelected))
            {
                entries.Add(new ConfirmActionEntry
                {
                    Kind = "proc",
                    Key = p.ProcessKey,
                    Title = $"关闭后台：{p.DisplayName}",
                    Detail = $"{p.Reason}（当前占用 {p.MemoryText}）",
                    TagText = p.TagText,
                    RequiresAdmin = p.RequiresAdmin,
                    Payload = p,
                    IsSelected = true
                });
            }

            if (entries.Count == 0)
            {
                Toast.Show("赛前环境已就绪", "免权限游戏配置全部最佳，且你未勾选任何需要关闭的后台进程。");
                return;
            }

            var confirmed = await ConfirmModal.ShowAsync(
                "赛前备战 · 执行确认清单",
                "请逐项确认以下拟执行的配置校验与后台关闭操作（可取消不需要的项目）：",
                entries);
            if (confirmed == null || confirmed.Count == 0) return;

            await ExecuteConfirmedEntriesAsync("赛前备战完成", confirmed);
        }

        private async Task HandleFixRecommendedAsync()
        {
            if (_snapshot == null || _isBusy) return;
            var targets = _snapshot.SystemChecks
                .Concat(_snapshot.OnlineGameChecks)
                .Concat(_snapshot.AaaGameChecks)
                .Where(c => c.CanAutoFix && !c.ExcludeFromBatchFix)
                .ToList();

            await HandleBatchFixAsync(targets, "批量修复推荐项 · 确认清单");
        }

        private async Task HandleSingleFixAsync(CheckItem item)
        {
            if (_isBusy) return;
            await HandleBatchFixAsync(new[] { item }, "单项配置修改确认");
        }

        private async Task HandleBatchFixAsync(IEnumerable<CheckItem> items, string dialogTitle)
        {
            if (_isBusy) return;
            var entries = items.Select(c => new ConfirmActionEntry
            {
                Kind = "check",
                Key = c.Id,
                Title = c.Title,
                Detail = $"{c.CurrentValue} → {c.FixLabel}",
                TagText = c.ExcludeFromBatchFix ? "安全权衡" : (c.RequiresAdmin ? "需提权" : "免权限"),
                RequiresAdmin = c.RequiresAdmin,
                IsSelected = true
            }).ToList();

            if (entries.Count == 0)
            {
                Toast.Show("无需修复", "当前分舱的所有自动推荐项都已处于最佳状态。");
                return;
            }

            var confirmed = await ConfirmModal.ShowAsync(
                dialogTitle,
                "你可以取消勾选不想改动的项目，再点击确认执行：",
                entries);
            if (confirmed == null || confirmed.Count == 0) return;

            await ExecuteConfirmedEntriesAsync("配置优化完成", confirmed);
        }

        private async Task HandleCloseProcessesAsync(List<ProcessItem> picked)
        {
            if (_isBusy) return;
            if (picked.Count == 0)
            {
                Toast.Show("未勾选进程", "请先在列表中勾选你想关闭的后台程序。", isWarning: true);
                return;
            }

            var entries = picked.Select(p => new ConfirmActionEntry
            {
                Kind = "proc",
                Key = p.ProcessKey,
                Title = $"关闭：{p.DisplayName}",
                Detail = $"占用 {p.MemoryText} · {p.Reason}（请确保工作已保存）",
                TagText = p.RequiresAdmin ? "提权停止服务" : "结束进程",
                RequiresAdmin = p.RequiresAdmin,
                Payload = p,
                IsSelected = true
            }).ToList();

            var confirmed = await ConfirmModal.ShowAsync(
                "关闭所选后台 · 最终确认",
                "关闭前请确认这些程序中没有未保存的聊天或文档：",
                entries);
            if (confirmed == null || confirmed.Count == 0) return;

            await ExecuteConfirmedEntriesAsync("后台清理完成", confirmed);
        }

        private async Task HandleCleanCachesAsync(List<CacheCleanupItem> picked)
        {
            if (_isBusy) return;
            if (picked.Count == 0)
            {
                Toast.Show("未勾选缓存", "没有选中可清理的缓存目录。", isWarning: true);
                return;
            }

            var entries = picked.Select(c => new ConfirmActionEntry
            {
                Kind = "cache",
                Key = c.Id,
                Title = $"清理：{c.Name}",
                Detail = $"{c.PathDisplay}（{c.SizeText}）",
                TagText = c.RequiresAdmin ? "需提权" : "安全删除",
                RequiresAdmin = c.RequiresAdmin,
                Payload = c,
                IsSelected = true
            }).ToList();

            var confirmed = await ConfirmModal.ShowAsync(
                "C 盘缓存瘦身 · 确认清单",
                "仅清理可再生缓存，不会影响项目代码与个人文档：",
                entries);
            if (confirmed == null || confirmed.Count == 0) return;

            await ExecuteConfirmedEntriesAsync("缓存瘦身完成", confirmed);
        }

        private async Task ExecuteConfirmedEntriesAsync(string toastTitle, List<ConfirmActionEntry> confirmed)
        {
            SetBusy(true, "正在按确认清单执行...");
            try
            {
                var logs = new List<string>();
                bool anyWarn = false;

                await Task.Run(() =>
                {
                    var checkIds = confirmed.Where(e => e.Kind == "check").Select(e => e.Key).ToList();
                    if (checkIds.Count > 0)
                    {
                        var r = OptimizationEngine.ApplyFixBatch(checkIds);
                        logs.AddRange(r.Logs);
                        if (!r.Success || r.CancelledUac) anyWarn = true;
                    }

                    var procs = confirmed.Where(e => e.Kind == "proc" && e.Payload is ProcessItem).Select(e => (ProcessItem)e.Payload!).ToList();
                    if (procs.Count > 0)
                    {
                        var r = OptimizationEngine.CloseSelectedProcesses(procs);
                        logs.AddRange(r.Logs);
                        if (!r.Success || r.CancelledUac) anyWarn = true;
                    }

                    var caches = confirmed.Where(e => e.Kind == "cache" && e.Payload is CacheCleanupItem).Select(e => (CacheCleanupItem)e.Payload!).ToList();
                    if (caches.Count > 0)
                    {
                        var r = OptimizationEngine.CleanSelectedCaches(caches);
                        logs.AddRange(r.Logs);
                        if (!r.Success || r.CancelledUac) anyWarn = true;
                    }
                });

                string summary = string.Join("\n", logs);
                AppendLog(summary);
                await RefreshAllAsync(summary.Replace("\n", "  |  "), force: true);
                Toast.Show(toastTitle, summary, anyWarn);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async Task HandleVoicePlatformActionAsync(string actionKey)
        {
            if (_isBusy) return;

            var vp = _snapshot?.VoicePlatform ?? new VoicePlatformSnapshot();
            string coreLabel = _snapshot?.BackgroundCoreLabel ?? "后台核心";
            string bgHex = $"0x{(_snapshot?.BackgroundAffinityMask ?? 0xF00):X}";
            string fullHex = $"0x{(_snapshot?.FullCpuMask ?? 0xFFF):X}";
            ConfirmActionEntry entry = actionKey switch
            {
                "isolate_both" => new ConfirmActionEntry
                {
                    Kind = "vp",
                    Key = actionKey,
                    Title = $"一键将 Oopz 与完美世界竞技平台锁进 {coreLabel}",
                    Detail = "同时设为 BelowNormal 优先级；自动跳过受反作弊内核保护进程，并反向确认 CS2 / PUBG 保留全部主核心。",
                    TagText = vp.PwaElevatedCount > 0 ? "含 1 次提权" : "免权限",
                    RequiresAdmin = vp.PwaElevatedCount > 0,
                    IsSelected = true
                },
                "isolate_oopz" => new ConfirmActionEntry
                {
                    Kind = "vp",
                    Key = actionKey,
                    Title = $"将 Oopz 语音锁进 {coreLabel}",
                    Detail = $"将运行中的 oopz.exe 亲和性设为 {bgHex} + BelowNormal，禁止抢占游戏主核。",
                    TagText = "免权限",
                    IsSelected = true
                },
                "isolate_pwa" => new ConfirmActionEntry
                {
                    Kind = "vp",
                    Key = actionKey,
                    Title = $"将完美世界竞技平台界面进程锁进 {coreLabel}",
                    Detail = "锁定全部可访问的 Electron 界面/渲染进程并降为 BelowNormal，自动安全跳过反作弊内核，绝不传染游戏进程。",
                    TagText = vp.PwaElevatedCount > 0 ? "需提权(1次)" : "免权限",
                    RequiresAdmin = vp.PwaElevatedCount > 0,
                    IsSelected = true
                },
                "oopz_ai_0" => new ConfirmActionEntry
                {
                    Kind = "vp",
                    Key = actionKey,
                    Title = "将 Oopz AI 降噪设为「关闭 (0)」",
                    Detail = "修改 mixer_local_setting_cache 中的 aiModeType 为 0（彻底停用高占用 AI 降噪线程；需先退出 Oopz）。",
                    TagText = "免权限",
                    IsSelected = true
                },
                "oopz_ai_1" => new ConfirmActionEntry
                {
                    Kind = "vp",
                    Key = actionKey,
                    Title = "将 Oopz AI 降噪设为「轻度 (1)」",
                    Detail = "修改 mixer_local_setting_cache 中的 aiModeType 为 1（兼顾环境降噪与低 CPU 占用；需先退出 Oopz）。",
                    TagText = "免权限",
                    IsSelected = true
                },
                "minimize_vp" => new ConfirmActionEntry
                {
                    Kind = "vp",
                    Key = actionKey,
                    Title = "最小化 Oopz 与完美平台主窗口",
                    Detail = "降低前台动画渲染负载，不会退出语音房间或中断完美平台匹配。",
                    TagText = "免权限",
                    IsSelected = true
                },
                _ => new ConfirmActionEntry
                {
                    Kind = "vp",
                    Key = "restore_vp",
                    Title = "还原 Oopz 与完美平台为系统默认全核调度",
                    Detail = $"恢复为全核 ({fullHex}) + Normal 优先级，方便对比测试或还原。",
                    TagText = vp.PwaElevatedCount > 0 ? "含 1 次提权" : "免权限",
                    RequiresAdmin = vp.PwaElevatedCount > 0,
                    IsSelected = true
                }
            };

            var confirmed = await ConfirmModal.ShowAsync(
                "语音与平台降占 · 操作确认",
                "请确认以下针对 Oopz / 完美世界竞技平台的降占操作：",
                new List<ConfirmActionEntry> { entry });
            if (confirmed == null || confirmed.Count == 0) return;

            SetBusy(true, "正在执行语音与平台降占操作...");
            try
            {
                var rep = await Task.Run(() => actionKey switch
                {
                    "isolate_both" => OptimizationEngine.ApplyPwaECoreIsolation(includeOopz: true),
                    "isolate_oopz" => OptimizationEngine.ApplyOopzOnlyECoreIsolation(),
                    "isolate_pwa" => OptimizationEngine.ApplyPwaECoreIsolation(includeOopz: false),
                    "oopz_ai_0" => OptimizationEngine.SetOopzAiMode("0"),
                    "oopz_ai_1" => OptimizationEngine.SetOopzAiMode("1"),
                    "minimize_vp" => OptimizationEngine.MinimizeVoicePlatformWindows(),
                    _ => OptimizationEngine.RestoreVoicePlatformAffinity()
                });

                AppendLog(rep.Summary);
                await RefreshAllAsync(rep.Summary.Replace("\n", "  |  "), force: true);
                Toast.Show(rep.Success ? "降占操作完成" : "操作提示", rep.Summary, !rep.Success || rep.CancelledUac);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void Restore_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy) return;
            var entry = new ConfirmActionEntry
            {
                Kind = "restore",
                Title = "按 JSON 原值快照完整还原系统设置",
                Detail = "还原优化前记录的电源计划、PCIe/USB 节能、Windows 录制、内容推送；若改过核心停驻或 MMCSS 调度也将一并提权还原至原值。",
                TagText = "完整回滚",
                IsSelected = true
            };

            var confirmed = await ConfirmModal.ShowAsync(
                "还原原始设置",
                "请确认是否还原初次修改前保存的系统基线：",
                new List<ConfirmActionEntry> { entry },
                "提示：游戏配置文件均已在原目录生成 .bak 时间戳备份；HVCI 与虚拟内存需在系统设置中单独管理。");

            if (confirmed == null || confirmed.Count == 0) return;

            SetBusy(true, "正在恢复原始配置...");
            try
            {
                var rep = await Task.Run(() => OptimizationEngine.RestoreBaseline());
                AppendLog(rep.Summary);
                await RefreshAllAsync(rep.Summary.Replace("\n", "  |  "), force: true);
                Toast.Show("还原操作完成", rep.Summary, !rep.Success);
            }
            finally { SetBusy(false); }
        }

        private void AppendLog(string message)
        {
            _sessionLog.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
            LogTextBox.Text = _sessionLog.ToString();
            LogTextBox.ScrollToEnd();
        }

        private void SetBusy(bool busy, string? status = null)
        {
            _isBusy = busy;
            BtnRefresh.IsEnabled = !busy;
            BusyBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (status != null)
                StatusFooterText.Text = status;
        }
    }
}
