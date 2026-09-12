using System;
using System.IO;
using System.Linq;
using System.Threading;
using AiDataGateway.LocalLogViewer.Models;

namespace AiDataGateway.LocalLogViewer.Services
{
    internal static class SelfTestRunner
    {
        public static string Run()
        {
            var root = Path.Combine(Path.GetTempPath(), "AiDataGateway.LocalLogViewer.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                TestCredentialStorage(root);
                TestLogParsing(root);
                TestSqlAnalysis();
                return "PASS: DPAPI credential storage, NLog parsing, function recognition, SQL parameter binding and safety self-test completed.";
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void TestCredentialStorage(string root)
        {
            const string password = "SelfTest-Password-123";
            var protector = new CredentialProtector();
            var store = new ConfigurationStore(Path.Combine(root, "Config"));
            var profile = new DatabaseProfile { Name = "测试数据库", Database = "demo", ProtectedPassword = protector.Protect(password) };
            store.SaveDatabaseProfiles(new[] { profile });
            var serialized = File.ReadAllText(store.DatabaseProfilesPath);
            if (serialized.Contains(password)) throw new InvalidOperationException("数据库密码以明文写入配置文件。");
            var loaded = store.LoadDatabaseProfiles().Single();
            if (protector.Unprotect(loaded.ProtectedPassword) != password) throw new InvalidOperationException("DPAPI 密码解密校验失败。");
        }

        private static void TestLogParsing(string root)
        {
            var logs = Path.Combine(root, "Logs", "2026-09-12");
            Directory.CreateDirectory(logs);
            var logPath = Path.Combine(logs, "INFO_2026-09-12.log");
            File.WriteAllText(logPath,
                "2026-09-12 10:11:12.123|Info|Demo.OrderService.Load|{\"Topic\":\"Order\",\"Payload\":\"{\\\"Id\\\":7}\"}|\r\n" +
                "2026-09-12 10:12:13.456|Error|Demo.OrderService.Save|第二条消息|System.InvalidOperationException: demo\r\n   at Demo.Run()\r\n");
            var profile = new LogProfile
            {
                Name = "自检",
                RootDirectory = Path.Combine(root, "Logs"),
                FilePattern = "*.log",
                LayoutOverride = "${longdate}|${level}|${callsite}|${message}|${exception:format=tostring}",
                MaximumReadMegabytesPerFile = 10
            };
            var result = new LocalLogReader().ReadAsync(profile, new DateTime(2026, 9, 12), new DateTime(2026, 9, 12), "全部", string.Empty, CancellationToken.None).GetAwaiter().GetResult();
            if (result.Items.Count != 2) throw new InvalidOperationException("日志解析数量不正确，期望 2，实际 " + result.Items.Count + "。");
            var json = result.Items.Single(item => item.Level == "Info");
            if (!json.Properties.ContainsKey("_messageJson")) throw new InvalidOperationException("消息中的嵌套 JSON 未结构化解析。");
            if (json.FunctionName != "Demo.OrderService.Load") throw new InvalidOperationException("NLog callsite 未被识别为打印函数。");
            var error = result.Items.Single(item => item.Level == "Error");
            if (string.IsNullOrWhiteSpace(error.Exception) || error.Exception.IndexOf("Demo.Run", StringComparison.Ordinal) < 0) throw new InvalidOperationException("多行异常日志未被保留。");

            var legacyLogs = Path.Combine(root, "LegacyLogs");
            Directory.CreateDirectory(legacyLogs);
            File.WriteAllText(Path.Combine(legacyLogs, "DEBUG_2026-03-06-17.log"),
                "2026-03-06 17:00:33.5606|DEBUG[197][ServicesSTD.Module_WIP.warehouse_fail_eaiqueue_info_get] [2.1.0.00000000] Service Start|\r\n" +
                "2026-03-06 17:00:33.7766|DEBUG[197][ServicesSTD.Module_WIP.warehouse_fail_eaiqueue_info_get] [2.1.0.00000000] [warehouse_fail_eaiqueue_info_get.warehouse_fail_eaiqueue_info_get_utility.GetLight]select count(1) from Demo|\r\n");
            var legacyResult = new LocalLogReader().ReadAsync(new LogProfile
            {
                Name = "现有格式",
                RootDirectory = legacyLogs,
                FilePattern = "*.log",
                MaximumReadMegabytesPerFile = 10
            }, new DateTime(2026, 3, 6), new DateTime(2026, 3, 6), "全部", string.Empty, CancellationToken.None).GetAwaiter().GetResult();
            if (legacyResult.Items.Count != 2) throw new InvalidOperationException("现有日志格式解析数量不正确。");
            if (!legacyResult.Items.Any(item => item.FunctionName == "ServicesSTD.Module_WIP.warehouse_fail_eaiqueue_info_get")) throw new InvalidOperationException("消息前缀中的服务函数未被识别。");
            if (!legacyResult.Items.Any(item => item.FunctionName == "warehouse_fail_eaiqueue_info_get.warehouse_fail_eaiqueue_info_get_utility.GetLight")) throw new InvalidOperationException("消息前缀中的具体打印函数未被识别。");
            var recentResult = new LocalLogReader().ReadRecentAsync(new LogProfile
            {
                Name = "最近日志",
                RootDirectory = legacyLogs,
                FilePattern = "*.log",
                MaximumReadMegabytesPerFile = 10
            }, TimeSpan.FromMilliseconds(100), "全部", string.Empty, CancellationToken.None).GetAwaiter().GetResult();
            if (recentResult.Items.Count != 1 || !recentResult.RangeEnd.HasValue || recentResult.RangeEnd.Value != new DateTime(2026, 3, 6, 17, 0, 33, 776).AddTicks(6000))
                throw new InvalidOperationException("按日志最新时间计算最近区间失败。");
        }

        private static void TestSqlAnalysis()
        {
            var service = new LogSqlService();
            var entry = new LogEntry
            {
                Message = "执行数据库查询",
                RawText = "trace",
                Properties = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "Payload", new System.Collections.Generic.Dictionary<string, object> { { "commandText", "select id, name from users where id = ?" } } }
                }
            };
            service.Populate(entry);
            if (!entry.HasSql || !entry.SqlIsReadOnly || service.CountPlaceholders(entry.SqlText) != 1) throw new InvalidOperationException("嵌套 SQL 提取或只读识别失败。");
            if (entry.FormattedSql.IndexOf("users .", StringComparison.OrdinalIgnoreCase) >= 0) throw new InvalidOperationException("SQL 格式化错误地拆分了限定字段名。");
            var substituted = service.Substitute(entry.SqlText, new[] { "7" });
            if (substituted.IndexOf("id = 7", StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException("SQL 参数替换失败。");
            const string parameterized = "select * from users where id = @id and state = :state and rank = $1 and note = ? and memo = '@ignored' and id2 = @id";
            var placeholders = service.GetPlaceholders(parameterized);
            if (placeholders.Count != 4) throw new InvalidOperationException("SQL 命名或位置参数识别失败。");
            var prepared = service.Prepare(parameterized, new object[] { 7L, "active", 2L, "demo" }, "System.Data.SqlClient");
            if (prepared.Parameters.Count != 4 || prepared.Sql.IndexOf("@p0", StringComparison.Ordinal) < 0 || prepared.Sql.Contains("@id")) throw new InvalidOperationException("SQL 数据库参数绑定准备失败。");
            var formattedParameterized = service.Format("select * from users where created_at >= :fromDate and id = @id");
            if (formattedParameterized.Contains("> =") || formattedParameterized.Contains(": fromDate")) throw new InvalidOperationException("SQL 格式化拆分了运算符或参数名。");
            if (service.IsReadOnly("select * from users; delete from users")) throw new InvalidOperationException("SQL 多语句写操作未被阻断。");
            if (service.IsReadOnly("select * into users_copy from users")) throw new InvalidOperationException("SELECT INTO 未被阻断。");
        }
    }
}
