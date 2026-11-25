using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace GameStateAssistant.Controls
{
    public partial class ToastHost : UserControl
    {
        private readonly DispatcherTimer _timer;
        public event EventHandler? OpenLogRequested;

        public ToastHost()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4.5) };
            _timer.Tick += (_, _) =>
            {
                _timer.Stop();
                Visibility = Visibility.Collapsed;
            };
        }

        public void Show(string title, string message, bool isWarning = false)
        {
            _timer.Stop();
            ToastTitle.Text = title;
            ToastMessage.Text = message;
            ToastIcon.Text = isWarning ? "\uE7BA" : "\uE73E";
            ToastIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isWarning ? "#FBBF24" : "#22D3EE"));
            Visibility = Visibility.Visible;
            _timer.Start();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Visibility = Visibility.Collapsed;
        }

        private void ViewLog_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            Visibility = Visibility.Collapsed;
            OpenLogRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
