using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GameStateAssistant.Models
{
    public enum CheckStatus
    {
        Optimal = 0,   // 最佳
        Attention = 1, // 需留意
        Critical = 2,  // 需修复
        Info = 3       // 信息
    }

    public enum CpuPlatformMode
    {
        Auto = 0,  // 自动识别本机 CPU
        Intel = 1, // Intel 大小核模式
        Amd = 2    // AMD 锐龙模式
    }

    public enum RiskTier
    {
        Safe = 0,    // 安全（零副作用，完整可撤销）
        Caution = 1, // 谨慎（系统级底层改动，需重启或提权）
        Expert = 2   // 专家权衡（涉及系统安全特性，绝不参与批量）
    }

    public sealed class CheckItem
    {
        public string Category { get; set; } = "";
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string CurrentValue { get; set; } = "";
        public string Detail { get; set; } = "";
        public CheckStatus Status { get; set; }
        public RiskTier Risk { get; set; } = RiskTier.Safe;
        public bool RequiresAdmin { get; set; }
        public bool CanAutoFix { get; set; }
        public bool ExcludeFromBatchFix { get; set; }
        public string FixLabel { get; set; } = "";

        public string StatusBadge => Status switch
        {
            CheckStatus.Optimal => "最佳",
            CheckStatus.Attention => "注意",
            CheckStatus.Critical => "待优化",
            _ => "可选"
        };

        public string RiskBadge => Risk switch
        {
            RiskTier.Safe => "安全",
            RiskTier.Caution => "谨慎",
            _ => "专家权衡"
        };

        public string PermissionBadge => !CanAutoFix
            ? $"{RiskBadge} · 只读检测"
            : (ExcludeFromBatchFix
                ? $"{RiskBadge} · 仅手动"
                : (RequiresAdmin ? $"{RiskBadge} · 需提权" : $"{RiskBadge} · 免权限"));
    }

    public sealed class ProcessItem : INotifyPropertyChanged
    {
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ProcessKey { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Category { get; set; } = "";
        public string Reason { get; set; } = "";
        public List<int> Pids { get; } = new List<int>();
        public int ProcessCount => Pids.Count;
        public double CpuPercent { get; set; }
        public double MemoryMb { get; set; }
        public bool UsesGpu { get; set; }
        public bool CanClose { get; set; }
        public bool RequiresAdmin { get; set; }
        public int ImpactLevel { get; set; } // 1=低, 2=中, 3=高

        public string ImpactText => ImpactLevel >= 3 ? "高影响" : ImpactLevel == 2 ? "中影响" : "低影响";
        public string MemoryText => MemoryMb >= 1024 ? $"{MemoryMb / 1024.0:F2} GB" : $"{MemoryMb:F0} MB";
        public string CpuText => $"{CpuPercent:F1}%";
        public string TagText => UsesGpu ? "占用显存/屏幕钩子" : (RequiresAdmin ? "系统后台服务" : "后台驻留");
        public string ActionText => !CanClose ? "白名单保护" : (RequiresAdmin ? "提权停止" : "一键关闭");

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class CacheCleanupItem : INotifyPropertyChanged
    {
        private bool _isSelected = true;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string PathDisplay { get; set; } = "";
        public string Description { get; set; } = "";
        public long SizeBytes { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool Available { get; set; } = true;

        public string SizeText
        {
            get
            {
                if (SizeBytes <= 0) return "0 MB";
                double mb = SizeBytes / (1024.0 * 1024.0);
                return mb >= 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class SystemTelemetrySnapshot
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public bool IsProcessElevated { get; set; }

        // 战备评分 (0-100)
        public int ReadinessScore { get; set; }
        public string ReadinessTitle { get; set; } = "检测中";
        public string ReadinessSubtitle { get; set; } = "";

        // 平台与硬件自适应信息 (v4.0 AMD / Intel 双平台引擎)
        public CpuPlatformMode RequestedPlatformMode { get; set; } = CpuPlatformMode.Auto;
        public string DetectedCpuVendor { get; set; } = "Intel"; // "Intel" / "AMD"
        public string EffectivePlatform { get; set; } = "Intel"; // 当前生效规则："Intel" / "AMD"
        public bool IsAmdEffective => EffectivePlatform.Equals("AMD", StringComparison.OrdinalIgnoreCase);
        public string CpuName { get; set; } = "CPU";
        public string CpuShortName { get; set; } = "CPU";
        public int LogicalCoreCount { get; set; } = Environment.ProcessorCount;
        public bool IsAmdX3D { get; set; }
        public bool IsDualCcdX3D { get; set; }

        // 动态计算的核心隔离掩码（绝不越界核心数）
        public long FullCpuMask { get; set; } = 0xFFF;
        public long BackgroundAffinityMask { get; set; } = 0xF00;
        public long ProtectedGameCoreMask { get; set; } = 0xFF;
        public string BackgroundCoreLabel { get; set; } = "尾部线程";

        // 硬件与系统核心指标
        public string RunningGame { get; set; } = "未检测到运行中的游戏";
        public bool IsGameRunning { get; set; }
        public string DisplayInfo { get; set; } = "读取中";
        public int DisplayWidth { get; set; } = 1920;
        public int DisplayHeight { get; set; } = 1080;
        public int DisplayRefreshRate { get; set; } = 144;
        public int RecommendedFpsCap { get; set; } = 141;

        public string GpuVendor { get; set; } = "NVIDIA";
        public string GpuName { get; set; } = "GPU";
        public string GpuShortName { get; set; } = "GPU";
        public bool GpuAvailable { get; set; }
        public double GpuTempC { get; set; }
        public double GpuPowerW { get; set; }
        public double GpuUtilPercent { get; set; }
        public double GpuMemUsedMb { get; set; }
        public double GpuMemTotalMb { get; set; }

        public double RamUsedPercent { get; set; }
        public double RamFreeGb { get; set; }
        public double RamTotalGb { get; set; }
        public int RamStickCount { get; set; }
        public int RamClockMhz { get; set; }
        public string RamLabelSummary { get; set; } = "系统内存";

        public double CDriveFreeGb { get; set; }
        public double CDriveTotalGb { get; set; }
        public double CDriveUsedPercent { get; set; }

        public double SystemCpuPercent { get; set; }
        public TimeSpan Uptime { get; set; }
        public bool HasSavedRestoreState { get; set; }

        // 各专项状态汇总
        public List<CheckItem> SystemChecks { get; } = new List<CheckItem>();
        public List<CheckItem> OnlineGameChecks { get; } = new List<CheckItem>();
        public List<CheckItem> AaaGameChecks { get; } = new List<CheckItem>();
        public List<ProcessItem> BackgroundProcesses { get; } = new List<ProcessItem>();
        public List<CacheCleanupItem> CacheItems { get; } = new List<CacheCleanupItem>();
        public VoicePlatformSnapshot VoicePlatform { get; set; } = new VoicePlatformSnapshot();

        // 关键布尔标识
        public bool IsBalancedPowerPlan { get; set; }
        public bool IsPCoreUnparked { get; set; }
        public bool IsHvciDisabled { get; set; }
        public bool IsPagefileOptimal { get; set; }
        public bool AreIntelThermalServicesRunning { get; set; }
        public bool IsGameDvrOff { get; set; }
        public bool IsPubgOptimal { get; set; }
        public bool IsCs2Optimal { get; set; }
        public bool IsLolOptimal { get; set; }
        public bool IsRtssOptimal { get; set; }
        public bool IsAfterburnerClean { get; set; }
        public bool IsHiberfilReducedOrOff { get; set; }
        public bool IsCyberpunkOptimal { get; set; }
        public bool IsEldenRingOptimal { get; set; }
        public bool IsMmcssOptimal { get; set; }
        public bool IsBusLatencyOptimal { get; set; }
        public bool IsWindowsNoiseTrimmed { get; set; }
    }

    public sealed class VoicePlatformSnapshot
    {
        // Oopz 实时状态
        public bool OopzRunning { get; set; }
        public int OopzTotalCount { get; set; }
        public int OopzIsolatedCount { get; set; }
        public int OopzUnisolatedCount { get; set; }
        public double OopzMemoryMb { get; set; }
        public double OopzCpuSeconds { get; set; }
        public string OopzPath { get; set; } = "";
        public string OopzAiMode { get; set; } = "未知"; // "0", "1", "2"
        public string OopzMixerConfigPath { get; set; } = "";
        public bool OopzShortcutHasECore { get; set; }

        // 完美世界竞技平台实时状态
        public bool PwaRunning { get; set; }
        public int PwaTotalCount { get; set; }
        public int PwaIsolatedCount { get; set; }
        public int PwaUnisolatedUserCount { get; set; }
        public int PwaElevatedCount { get; set; }
        public double PwaMemoryMb { get; set; }
        public double PwaCpuSeconds { get; set; }
        public string PwaInstallPath { get; set; } = "";
        public bool PwaOnMechanicalDrive { get; set; }

        // CS2 反向安全校验（确保游戏绝不被错误锁进 E 核）
        public bool Cs2Running { get; set; }
        public bool Cs2HasAllPCores { get; set; } = true;
        public string Cs2AffinitySummary { get; set; } = "未运行（进局后自动校验 P 核完整性）";

        // 侧边栏待优化角标计数
        public int PendingCount
        {
            get
            {
                int n = 0;
                if (OopzRunning && OopzUnisolatedCount > 0) n++;
                if (OopzAiMode == "2") n++;
                if (PwaRunning && (PwaUnisolatedUserCount > 0 || PwaElevatedCount > 0)) n++;
                return n;
            }
        }
    }
}
