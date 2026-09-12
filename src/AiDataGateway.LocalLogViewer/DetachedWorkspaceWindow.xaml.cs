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
        }

        public WorkspaceTabItem Tab { get; }
        public event EventHandler ReturnRequested;

        public void CloseAfterReturn()
        {
            _allowClose = true;
            Close();
        }

        private void ReturnToMain_Click(object sender, RoutedEventArgs e) => ReturnRequested?.Invoke(this, EventArgs.Empty);

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
