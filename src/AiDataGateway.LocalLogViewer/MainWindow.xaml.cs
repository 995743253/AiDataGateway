using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using AiDataGateway.LocalLogViewer.Models;
using AiDataGateway.LocalLogViewer.ViewModels;

namespace AiDataGateway.LocalLogViewer
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;
        private readonly Dictionary<string, DetachedWorkspaceWindow> _detachedWindows = new Dictionary<string, DetachedWorkspaceWindow>(StringComparer.OrdinalIgnoreCase);

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = new MainViewModel();
            DataContext = _viewModel;

            SourceInitialized += (_, _) =>
            {
                var handle = CustomWindowChrome.HookMinMaxInfo(this);
                CustomWindowChrome.ApplyVisuals(handle);
            };
            StateChanged += (_, _) => UpdateMaximizeGlyph();
        }

        private void UpdateMaximizeGlyph()
        {
            if (MaximizeButton != null) MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        }

        private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

        private void OnMaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void BrowseLogDirectory_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "选择日志目录中的任意日志文件", Filter = "日志文件|*.log;*.txt|所有文件|*.*", CheckFileExists = true };
            if (_viewModel.LogEditor != null && Directory.Exists(_viewModel.LogEditor.RootDirectory)) dialog.InitialDirectory = _viewModel.LogEditor.RootDirectory;
            if (dialog.ShowDialog(this) == true) _viewModel.LogEditor.RootDirectory = Path.GetDirectoryName(dialog.FileName);
            RefreshEditors();
        }

        private void BrowseNLogConfig_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Title = "选择 NLog 配置文件", Filter = "NLog 配置|NLog.config;*.config|所有文件|*.*", CheckFileExists = true };
            if (dialog.ShowDialog(this) == true) _viewModel.LogEditor.NLogConfigurationPath = dialog.FileName;
            RefreshEditors();
        }

        private void DatabasePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _viewModel.DatabasePasswordInput = ((PasswordBox)sender).Password;
        }

        private void SaveDatabase_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.SaveDatabaseCommand.Execute(null);
            DatabasePasswordBox.Clear();
        }

        private async void TestDatabase_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = await _viewModel.TestDatabaseAsync();
                _viewModel.ShowAlert("连接成功", result);
            }
            catch (Exception exception)
            {
                _viewModel.ShowAlert("连接失败", exception.Message);
            }
        }

        private void Provider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var option = ((ComboBox)sender).SelectedItem as ProviderOption;
            if (option != null && _viewModel.DatabaseEditor != null) _viewModel.DatabaseEditor.Port = option.DefaultPort;
        }

        private void RefreshEditors()
        {
            var current = _viewModel.LogEditor;
            _viewModel.LogEditor = null;
            _viewModel.LogEditor = current;
        }

        private void DetachWorkspace_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as FrameworkElement;
            var tab = button == null ? null : button.DataContext as WorkspaceTabItem;
            if (tab == null) return;
            var point = button.PointToScreen(new Point(button.ActualWidth / 2, button.ActualHeight));
            DetachWorkspace(tab, point);
            e.Handled = true;
        }

        private void WorkspaceTabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            QueueSelectedTabIntoView();
        }

        private void WorkspaceTabStrip_Loaded(object sender, RoutedEventArgs e) => QueueSelectedTabIntoView();

        private void QueueSelectedTabIntoView()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                var selected = WorkspaceTabStrip.SelectedItem;
                if (selected == null) return;
                WorkspaceTabStrip.UpdateLayout();
                WorkspaceTabStrip.ScrollIntoView(selected);
                var container = WorkspaceTabStrip.ItemContainerGenerator.ContainerFromItem(selected) as FrameworkElement;
                container?.BringIntoView();
            }));
        }

        private void DetachWorkspace(WorkspaceTabItem tab, Point screenPoint)
        {
            if (tab == null || _detachedWindows.ContainsKey(tab.Key)) return;
            if (!_viewModel.DetachWorkspaceTab(tab)) return;

            var detached = new DetachedWorkspaceWindow(tab)
            {
                Owner = this,
                Left = Math.Max(SystemParameters.VirtualScreenLeft, Math.Min(screenPoint.X - 120, SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 900)),
                Top = Math.Max(SystemParameters.VirtualScreenTop, Math.Min(screenPoint.Y - 28, SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 600))
            };
            detached.ReturnRequested += Detached_ReturnRequested;
            _detachedWindows[tab.Key] = detached;
            detached.Show();
            detached.Activate();
        }

        private void Detached_ReturnRequested(object sender, EventArgs e)
        {
            var detached = sender as DetachedWorkspaceWindow;
            if (detached == null || !_detachedWindows.Remove(detached.Tab.Key)) return;
            detached.ReturnRequested -= Detached_ReturnRequested;
            _viewModel.AttachWorkspaceTab(detached.Tab);
            detached.CloseAfterReturn();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        internal bool VerifyWorkspaceDetachRoundTrip()
        {
            var first = new LogEntry { Timestamp = DateTime.Now.AddSeconds(-1), Level = "Info", Message = "第一条日志详情", RawText = "第一条日志详情" };
            var second = new LogEntry { Timestamp = DateTime.Now, Level = "Info", Message = "第二条日志详情", RawText = "第二条日志详情" };
            _viewModel.OpenLogDetailCommand.Execute(first);
            _viewModel.OpenLogDetailCommand.Execute(second);
            var detailTabs = _viewModel.WorkspaceTabs.Where(item => item.PageKey == "log-detail").ToList();
            if (detailTabs.Count < 2 || detailTabs[0].Key == detailTabs[1].Key || !ReferenceEquals(detailTabs[0].Entry, first) || !ReferenceEquals(detailTabs[1].Entry, second)) return false;
            var tab = detailTabs[1];
            DetachWorkspace(tab, PointToScreen(new Point(180, 90)));
            DetachedWorkspaceWindow detached;
            if (!_detachedWindows.TryGetValue(tab.Key, out detached) || _viewModel.WorkspaceTabs.Contains(tab)) return false;
            detached.Close();
            var frame = new DispatcherFrame();
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
            return _viewModel.WorkspaceTabs.Contains(tab) && _viewModel.SelectedWorkspaceTab == tab && ReferenceEquals(tab.Entry, second);
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            foreach (var detached in _detachedWindows.Values)
            {
                detached.ReturnRequested -= Detached_ReturnRequested;
                detached.CloseAfterReturn();
            }
            _detachedWindows.Clear();
            base.OnClosing(e);
        }
    }
}
