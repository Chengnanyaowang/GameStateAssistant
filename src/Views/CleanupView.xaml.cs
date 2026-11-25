using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameStateAssistant.Models;

namespace GameStateAssistant.Views
{
    public partial class CleanupView : UserControl
    {
        private List<ProcessItem> _processes = new List<ProcessItem>();
        private List<CacheCleanupItem> _caches = new List<CacheCleanupItem>();

        public event EventHandler<List<ProcessItem>>? CloseProcessesRequested;
        public event EventHandler<List<CacheCleanupItem>>? CleanCachesRequested;

        public CleanupView()
        {
            InitializeComponent();
        }

        public void Render(SystemTelemetrySnapshot snap)
        {
            _processes = snap.BackgroundProcesses;
            _caches = snap.CacheItems;
            ProcessList.ItemsSource = _processes;
            CacheList.ItemsSource = _caches;
        }

        private void SelectHighImpact_Click(object sender, RoutedEventArgs e)
        {
            foreach (var p in _processes)
            {
                if (p.CanClose &&
                    !p.ProcessKey.Equals("msedgewebview2", StringComparison.OrdinalIgnoreCase) &&
                    (p.UsesGpu || p.ImpactLevel >= 3 || p.MemoryMb >= 400))
                {
                    p.IsSelected = true;
                }
            }
        }

        private void CloseProcesses_Click(object sender, RoutedEventArgs e)
        {
            var picked = _processes.Where(x => x.CanClose && x.IsSelected).ToList();
            CloseProcessesRequested?.Invoke(this, picked);
        }

        private void CleanCaches_Click(object sender, RoutedEventArgs e)
        {
            var picked = _caches.Where(x => x.Available && x.IsSelected).ToList();
            CleanCachesRequested?.Invoke(this, picked);
        }
    }
}
