using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AiDataGateway.LocalLogViewer.Services;

namespace AiDataGateway.LocalLogViewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (e.Args.Any(item => string.Equals(item, "--self-test", StringComparison.OrdinalIgnoreCase)))
            {
                var output = e.Args.SkipWhile(item => !string.Equals(item, "--self-test-output", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                try
                {
                    var message = SelfTestRunner.Run();
                    var window = new MainWindow { Left = -10000, Top = -10000, ShowInTaskbar = false, ShowActivated = false };
                    MainWindow = window;
                    window.Show();
                    window.UpdateLayout();
                    if (!window.VerifyWorkspaceDetachRoundTrip()) throw new InvalidOperationException("多详情标签页、分离与回收自检失败。");
                    window.Close();
                    message += " Multiple document tabs and docking round-trip completed.";
                    if (!string.IsNullOrWhiteSpace(output)) File.WriteAllText(output, message);
                    Shutdown(0);
                }
                catch (Exception exception)
                {
                    if (!string.IsNullOrWhiteSpace(output)) File.WriteAllText(output, exception.ToString());
                    Shutdown(1);
                }
                return;
            }

            var previewOutput = e.Args.SkipWhile(item => !string.Equals(item, "--render-preview", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(previewOutput))
            {
                var previewPage = e.Args.SkipWhile(item => !string.Equals(item, "--preview-page", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
                RenderPreview(previewOutput, previewPage,
                    e.Args.Any(item => string.Equals(item, "--preview-dialog", StringComparison.OrdinalIgnoreCase)),
                    e.Args.Any(item => string.Equals(item, "--preview-json", StringComparison.OrdinalIgnoreCase)));
                Shutdown(0);
                return;
            }

            MainWindow = new MainWindow();
            MainWindow.Show();
        }

        private static void RenderPreview(string output, string page, bool showDialog, bool showJsonParameters)
        {
            var window = new MainWindow
            {
                Width = 1500,
                Height = 900,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -10000,
                Top = -10000,
                ShowInTaskbar = false,
                ShowActivated = false
            };
            var viewModel = window.DataContext as ViewModels.MainViewModel;
            if (viewModel != null && string.Equals(page, "multi-tabs", StringComparison.OrdinalIgnoreCase))
            {
                for (var index = 1; index <= 9; index++)
                {
                    var functionName = "DemoService.LoadPage" + index;
                    var entry = new Models.LogEntry
                    {
                        Timestamp = DateTime.Now.AddSeconds(index - 10),
                        Level = index % 3 == 0 ? "Warn" : "Info",
                        FunctionName = functionName,
                        Message = "第 " + index + " 个独立日志详情",
                        RawText = "2026-09-12 " + (index % 3 == 0 ? "WARN" : "INFO") + " [" + functionName + "] 第 " + index + " 个独立日志详情"
                    };
                    viewModel.OpenLogDetailCommand.Execute(entry);
                }
            }
            else if (viewModel != null && string.Equals(page, "log-sql", StringComparison.OrdinalIgnoreCase))
            {
                var entry = new Models.LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = "Info",
                    Message = "查询用户及其最近订单",
                    RawText = "select u.Id, u.Name, count(o.Id) as OrderCount from Users u left join Orders o on o.UserId = u.Id where u.Status = @status and u.CreatedAt >= :fromDate and u.TenantId = ? group by u.Id, u.Name order by OrderCount desc"
                };
                new LogSqlService().Populate(entry);
                viewModel.OpenLogSqlCommand.Execute(entry);
            }
            else if (viewModel != null && !string.IsNullOrWhiteSpace(page)) viewModel.CurrentPage = page;
            if (viewModel != null && showJsonParameters) viewModel.SqlParameterMode = "JSON";
            if (showDialog)
            {
                viewModel?.ShowAlert("连接成功", "数据库连接测试已通过。凭据只在当前进程内短暂解密，配置文件中保存的是 Windows DPAPI 密文。");
            }
            window.Show();
            window.UpdateLayout();
            window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ContextIdle, new Action(() => { }));
            window.UpdateLayout();
            var width = Math.Max(1, (int)Math.Round(window.ActualWidth));
            var height = Math.Max(1, (int)Math.Round(window.ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(window);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = File.Create(Path.GetFullPath(output))) encoder.Save(stream);
            window.Close();
        }
    }
}
