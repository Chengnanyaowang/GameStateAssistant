using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameStateAssistant.Models;

namespace GameStateAssistant.Views
{
    public partial class OverviewView : UserControl
    {
        public event EventHandler? PreGameBoostRequested;
        public event EventHandler? FixRecommendedRequested;
        public event EventHandler<CheckStatus?>? NavigateToChecksRequested;

        public OverviewView()
        {
            InitializeComponent();
        }

        public void Render(SystemTelemetrySnapshot snap)
        {
            ScoreRingControl.Score = snap.ReadinessScore;
            HeadlineText.Text = snap.ReadinessTitle;
            SubtextText.Text = snap.ReadinessSubtitle;
            LastScanText.Text = $"上次体检 {snap.Timestamp:HH:mm:ss}";

            if (snap.ReadinessScore >= 90)
            {
                StatusPillBg.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2010B981"));
                StatusPillText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399"));
                StatusPillText.Text = "合规状态佳";
            }
            else if (snap.ReadinessScore >= 75)
            {
                StatusPillBg.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22F59E0B"));
                StatusPillText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FBBF24"));
                StatusPillText.Text = "有可调项";
            }
            else
            {
                StatusPillBg.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#24EF4444"));
                StatusPillText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F87171"));
                StatusPillText.Text = "建议核查";
            }

            var allChecks = snap.SystemChecks.Concat(snap.OnlineGameChecks).Concat(snap.AaaGameChecks).ToList();
            CountOkText.Text = $"最佳 {allChecks.Count(c => c.Status == CheckStatus.Optimal)}";
            CountBadText.Text = $"待优化/注意 {allChecks.Count(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention)}";
            CountOptionalText.Text = $"可选 {allChecks.Count(c => c.Status == CheckStatus.Info)}";

            // 动态硬件标题（彻底去硬编码，自动适配 Intel/AMD 与 NVIDIA/Radeon）
            GpuCardTitle.Text = $"{snap.GpuShortName} 显存";
            RamCardTitle.Text = snap.RamLabelSummary;
            CpuCardTitle.Text = $"显示 & {snap.CpuShortName}";

            // GPU
            if (snap.GpuAvailable)
            {
                double vramPct = snap.GpuMemTotalMb > 0 ? Math.Min(100, snap.GpuMemUsedMb / snap.GpuMemTotalMb * 100.0) : 0;
                GpuMainText.Text = $"{snap.GpuMemUsedMb:F0} / {snap.GpuMemTotalMb:F0} MB";
                GpuSubText.Text = $"功耗 {snap.GpuPowerW:F1}W · 负载 {snap.GpuUtilPercent:F0}%";
                GpuTempText.Text = $"{snap.GpuTempC:F0}°C";
                GpuProg.Value = vramPct;
                GpuProg.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(vramPct >= 85 ? "#FBBF24" : "#22D3EE"));
                GpuSpark.PushSample(vramPct);
            }
            else
            {
                GpuMainText.Text = snap.GpuVendor;
                GpuSubText.Text = snap.GpuName;
                GpuTempText.Text = "--";
                GpuProg.Value = 0;
            }

            // RAM
            RamMainText.Text = $"{snap.RamFreeGb:F1} GB 可用";
            RamSubText.Text = $"总计 {snap.RamTotalGb:F1} GB";
            RamPctText.Text = $"{snap.RamUsedPercent:F0}%";
            RamProg.Value = snap.RamUsedPercent;
            RamSpark.PushSample(snap.RamUsedPercent);

            // Disk
            DiskMainText.Text = $"{snap.CDriveFreeGb:F1} GB";
            DiskSubText.Text = $"总容量 {snap.CDriveTotalGb:F0} GB";
            DiskPctText.Text = $"{snap.CDriveUsedPercent:F0}%";
            DiskProg.Value = snap.CDriveUsedPercent;
            DiskSpark.PushSample(snap.CDriveUsedPercent);

            // Display & CPU
            DispMainText.Text = $"{snap.DisplayRefreshRate} Hz";
            DispSubText.Text = snap.DisplayInfo;
            CpuPctText.Text = $"{snap.SystemCpuPercent:F0}%";
            CpuProg.Value = snap.SystemCpuPercent;
            CpuSpark.PushSample(snap.SystemCpuPercent);

            // 优先将待优化项排在快览前面，让一眼看到重点
            MatrixList.ItemsSource = allChecks
                .OrderByDescending(c => c.Status == CheckStatus.Critical)
                .ThenByDescending(c => c.Status == CheckStatus.Attention)
                .Take(8)
                .ToList();
        }

        public void PushLiveSample(double gpuVramPct, double gpuTempC, double gpuPowerW, double gpuUtil, double gpuMemUsed, double gpuMemTotal,
            double ramFreeGb, double ramUsedPct, double cpuPct)
        {
            if (gpuMemTotal > 0)
            {
                GpuMainText.Text = $"{gpuMemUsed:F0} / {gpuMemTotal:F0} MB";
                GpuSubText.Text = $"功耗 {gpuPowerW:F1}W · 负载 {gpuUtil:F0}%";
                GpuTempText.Text = $"{gpuTempC:F0}°C";
                GpuProg.Value = gpuVramPct;
                GpuProg.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(gpuVramPct >= 85 ? "#FBBF24" : "#22D3EE"));
                GpuSpark.PushSample(gpuVramPct);
            }

            RamMainText.Text = $"{ramFreeGb:F1} GB 可用";
            RamPctText.Text = $"{ramUsedPct:F0}%";
            RamProg.Value = ramUsedPct;
            RamSpark.PushSample(ramUsedPct);

            CpuPctText.Text = $"{cpuPct:F0}%";
            CpuProg.Value = cpuPct;
            CpuSpark.PushSample(cpuPct);
        }

        private void PreGameBoost_Click(object sender, RoutedEventArgs e) => PreGameBoostRequested?.Invoke(this, EventArgs.Empty);
        private void FixRecommended_Click(object sender, RoutedEventArgs e) => FixRecommendedRequested?.Invoke(this, EventArgs.Empty);
        private void CountOk_Click(object sender, RoutedEventArgs e) => NavigateToChecksRequested?.Invoke(this, CheckStatus.Optimal);
        private void CountBad_Click(object sender, RoutedEventArgs e) => NavigateToChecksRequested?.Invoke(this, CheckStatus.Critical);
        private void CountOptional_Click(object sender, RoutedEventArgs e) => NavigateToChecksRequested?.Invoke(this, null);
    }
}
