using System;
using System.ComponentModel;
using System.Windows;
using AiDataGateway.LocalLogViewer.ViewModels;

namespace AiDataGateway.LocalLogViewer
{
    public partial class DetachedWorkspaceWindow : Window
    {
        private bool _allowClose;
        private bool _returnQueued;

        public DetachedWorkspaceWindow(WorkspaceTabItem tab)
        {
            InitializeComponent();
            Tab = tab ?? throw new ArgumentNullException(nameof(tab));
            DataContext = tab;
            Title = tab.Title + " - 本地日志查看器";
            DocumentTitle.Text = tab.Title;

            SourceInitialized += (_, _) =>
            {
                var handle = CustomWindowChrome.HookMinMaxInfo(this);
                CustomWindowChrome.ApplyVisuals(handle);
            };
            StateChanged += (_, _) =>
            {
                if (MaximizeButton != null) MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            };
        }

        public WorkspaceTabItem Tab { get; }
        public event EventHandler ReturnRequested;

        public void CloseAfterReturn()
        {
            _allowClose = true;
            Close();
        }

        private void ReturnToMain_Click(object sender, RoutedEventArgs e) => ReturnRequested?.Invoke(this, EventArgs.Empty);

        private void OnMaximizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_allowClose)
            {
                e.Cancel = true;
                if (!_returnQueued)
                {
                    _returnQueued = true;
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _returnQueued = false;
                        if (!_allowClose) ReturnRequested?.Invoke(this, EventArgs.Empty);
                    }));
                }
                return;
            }
            base.OnClosing(e);
        }
    }
}
