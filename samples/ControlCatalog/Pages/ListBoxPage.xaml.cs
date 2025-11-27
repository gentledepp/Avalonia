using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ControlCatalog.ViewModels;

namespace ControlCatalog.Pages
{
    public class ListBoxPage : UserControl
    {
        private DispatcherTimer? _statsTimer;
        private ListBoxPageViewModel? _viewModel;

        public ListBoxPage()
        {
            InitializeComponent();
            _viewModel = new ListBoxPageViewModel();
            DataContext = _viewModel;

            // Set initial virtualization state
            ContentVirtualizationDiagnostics.IsEnabled = _viewModel.EnableVirtualization;

            // Listen to EnableVirtualization changes
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

            // Update cache stats every second
            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += UpdateCacheStats;
            _statsTimer.Start();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ListBoxPageViewModel.EnableVirtualization))
            {
                // Control virtualization globally via diagnostics API
                ContentVirtualizationDiagnostics.IsEnabled = _viewModel!.EnableVirtualization;
            }
        }

        private void UpdateCacheStats(object? sender, EventArgs e)
        {
            var listBox = this.FindControl<ListBox>("MainListBox");
            var statsText = this.FindControl<TextBlock>("CacheStatsText");

            if (listBox != null && statsText != null)
            {
                var stats = ContentVirtualizationDiagnostics.GetPoolStats(listBox);
                if (stats != null && stats.PoolEntries.Any())
                {
                    var entry = stats.PoolEntries.First();
                    statsText.Text = $"Cache: {entry.PooledCount} controls pooled\nType: {entry.RecycleKey}";
                }
                else
                {
                    statsText.Text = "Cache: Empty (virtualization disabled or no scrolling yet)";
                }
            }
        }
    }
}
