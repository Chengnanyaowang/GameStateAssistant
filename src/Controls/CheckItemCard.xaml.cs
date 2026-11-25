using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameStateAssistant.Models;

namespace GameStateAssistant.Controls
{
    public partial class CheckItemCard : UserControl
    {
        public event EventHandler<CheckItem>? FixRequested;
        private bool _expanded;

        public CheckItemCard()
        {
            InitializeComponent();
            DataContextChanged += (_, _) =>
            {
                // 待优化项默认展开，最佳项默认折叠保持列表紧凑
                if (DataContext is CheckItem item && item.Status == CheckStatus.Critical)
                    SetExpanded(true);
                else
                    SetExpanded(false);
            };
        }

        private void Header_Click(object sender, MouseButtonEventArgs e)
        {
            SetExpanded(!_expanded);
        }

        private void SetExpanded(bool expand)
        {
            _expanded = expand;
            DetailPanel.Visibility = expand ? Visibility.Visible : Visibility.Collapsed;
            Chevron.Text = expand ? "\uE70E" : "\uE70D"; // Up / Down chevron
        }

        private void FixBtn_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (DataContext is CheckItem item)
                FixRequested?.Invoke(this, item);
        }
    }
}
