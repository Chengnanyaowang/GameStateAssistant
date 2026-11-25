using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace GameStateAssistant.Controls
{
    public sealed class ConfirmActionEntry : INotifyPropertyChanged
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
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        public string Kind { get; set; } = ""; // "check" | "proc" | "cache"
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string Detail { get; set; } = "";
        public string TagText { get; set; } = "";
        public bool RequiresAdmin { get; set; }
        public object? Payload { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    public partial class ConfirmDialog : UserControl
    {
        private TaskCompletionSource<List<ConfirmActionEntry>?>? _tcs;
        private List<ConfirmActionEntry> _entries = new List<ConfirmActionEntry>();
        private string? _customWarning;

        public ConfirmDialog()
        {
            InitializeComponent();
        }

        public Task<List<ConfirmActionEntry>?> ShowAsync(string title, string subtitle, List<ConfirmActionEntry> entries, string? customAdminWarning = null)
        {
            _entries = entries;
            _customWarning = customAdminWarning;
            DialogTitle.Text = title;
            DialogSubtitle.Text = subtitle;
            ActionListControl.ItemsSource = _entries;

            UpdateSelectionSummary();
            Visibility = Visibility.Visible;

            _tcs = new TaskCompletionSource<List<ConfirmActionEntry>?>();
            return _tcs.Task;
        }

        private void ItemCheck_Changed(object sender, RoutedEventArgs e) => UpdateSelectionSummary();

        private void UpdateSelectionSummary()
        {
            int count = _entries.Count(x => x.IsSelected);
            bool anyAdmin = _entries.Any(x => x.IsSelected && x.RequiresAdmin);
            SelectedSummaryText.Text = $"已选中 {count} / {_entries.Count} 项";
            BtnConfirm.IsEnabled = count > 0;
            BtnConfirm.Content = $"确认执行 ({count} 项)";

            if (!string.IsNullOrWhiteSpace(_customWarning))
            {
                AdminWarningText.Text = _customWarning;
                AdminWarningBox.Visibility = Visibility.Visible;
            }
            else if (anyAdmin)
            {
                AdminWarningText.Text = "所选项目中包含管理员级系统修改，执行时将统一弹出 1 次 UAC 授权，且不会随「还原原始设置」自动撤销。";
                AdminWarningBox.Visibility = Visibility.Visible;
            }
            else
            {
                AdminWarningBox.Visibility = Visibility.Collapsed;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(_entries.Where(x => x.IsSelected).ToList());
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Visibility = Visibility.Collapsed;
            _tcs?.TrySetResult(null);
        }
    }
}
