using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameStateAssistant.Models;

namespace GameStateAssistant.Views
{
    public partial class ChecksView : UserControl
    {
        private List<CheckItem> _allItems = new List<CheckItem>();
        public string SectionKey { get; private set; } = "";

        public event EventHandler<CheckItem>? SingleFixRequested;
        public event EventHandler<List<CheckItem>>? BatchFixRequested;

        public ChecksView()
        {
            InitializeComponent();
        }

        public void LoadSection(string sectionKey, string title, string subtitle, string batchLabel,
            List<CheckItem> items, string? tipsTitle = null, string? tip1 = null, string? tip2 = null, CheckStatus? initialFilter = null)
        {
            SectionKey = sectionKey;
            SectionTitle.Text = title;
            SectionSubtitle.Text = subtitle;
            BtnBatchFix.Content = batchLabel;
            _allItems = items;

            int fixableCount = _allItems.Count(c => c.CanAutoFix && !c.ExcludeFromBatchFix);
            BtnBatchFix.IsEnabled = fixableCount > 0;

            if (!string.IsNullOrEmpty(tipsTitle))
            {
                TipsTitle.Text = tipsTitle;
                TipsLine1.Text = tip1 ?? "";
                TipsLine2.Text = tip2 ?? "";
                TipsBox.Visibility = Visibility.Visible;
            }
            else
            {
                TipsBox.Visibility = Visibility.Collapsed;
            }

            if (initialFilter == CheckStatus.Optimal) FilterOptimal.IsChecked = true;
            else if (initialFilter == CheckStatus.Critical || initialFilter == CheckStatus.Attention) FilterAttention.IsChecked = true;
            else FilterAll.IsChecked = true;

            ApplyFilter();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

        private void ApplyFilter()
        {
            if (ItemsList == null) return;

            int badCount = _allItems.Count(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention);
            int okCount = _allItems.Count(c => c.Status == CheckStatus.Optimal);
            FilterAll.Content = $"全部 ({_allItems.Count})";
            FilterAttention.Content = $"待优化 / 注意 ({badCount})";
            FilterOptimal.Content = $"最佳 ({okCount})";

            IEnumerable<CheckItem> filtered = _allItems;
            if (FilterAttention.IsChecked == true)
                filtered = _allItems.Where(c => c.Status == CheckStatus.Critical || c.Status == CheckStatus.Attention);
            else if (FilterOptimal.IsChecked == true)
                filtered = _allItems.Where(c => c.Status == CheckStatus.Optimal);

            // 把待优化项永远排在最前
            var list = filtered
                .OrderByDescending(c => c.Status == CheckStatus.Critical)
                .ThenByDescending(c => c.Status == CheckStatus.Attention)
                .ToList();

            ItemsList.ItemsSource = list;
            EmptyStateBox.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Card_FixRequested(object? sender, CheckItem item) => SingleFixRequested?.Invoke(this, item);

        private void BatchFix_Click(object sender, RoutedEventArgs e)
        {
            var targets = _allItems.Where(c => c.CanAutoFix && !c.ExcludeFromBatchFix).ToList();
            BatchFixRequested?.Invoke(this, targets);
        }
    }
}
