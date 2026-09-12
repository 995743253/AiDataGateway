using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Data.SqlClient;
using System.Data;
using System.Threading.Tasks;
using AiDataGateway.LocalLogViewer.Models;

namespace AiDataGateway.LocalLogViewer.Services
{
    public sealed class DatabaseConnectionService
    {
        private readonly CredentialProtector _protector;
        public DatabaseConnectionService(CredentialProtector protector) { _protector = protector; }

        public async Task<string> TestAsync(DatabaseProfile profile, string temporaryPassword)
        {
            var password = string.IsNullOrEmpty(temporaryPassword) ? _protector.Unprotect(profile.ProtectedPassword) : temporaryPassword;
            try
            {
                var factory = GetFactory(profile.ProviderInvariantName);
                using (var connection = factory.CreateConnection())
                {
                    if (connection == null) throw new InvalidOperationException("数据库提供程序未能创建连接对象。");
                    connection.ConnectionString = BuildConnectionString(profile, password);
                    await connection.OpenAsync();
                    return "连接成功：" + connection.Database + " / " + connection.ServerVersion;
                }
            }
            catch (Exception exception)
            {
                var message = exception.Message;
                if (!string.IsNullOrEmpty(password)) message = message.Replace(password, "******");
                throw new InvalidOperationException(message, exception);
            }
            finally
            {
                password = null;
            }
        }

        public async Task<SqlQueryResult> ExecuteReadOnlyAsync(DatabaseProfile profile, string sql, int maximumRows, IList<SqlBoundParameter> parameters = null)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!new LogSqlService().IsReadOnly(sql)) throw new InvalidOperationException("只允许执行单条 SELECT 或 WITH 只读语句。");
            var password = _protector.Unprotect(profile.ProtectedPassword);
            try
            {
                var factory = GetFactory(profile.ProviderInvariantName);
                using (var connection = factory.CreateConnection())
                {
                    if (connection == null) throw new InvalidOperationException("数据库提供程序未能创建连接对象。");
                    connection.ConnectionString = BuildConnectionString(profile, password);
                    await connection.OpenAsync();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = sql;
                        command.CommandType = CommandType.Text;
                        command.CommandTimeout = Math.Max(3, Math.Min(60, profile.ConnectionTimeoutSeconds * 2));
                        foreach (var item in parameters ?? new SqlBoundParameter[0])
                        {
                            var parameter = command.CreateParameter();
                            parameter.ParameterName = (item.Name ?? string.Empty).TrimStart('@', ':');
                            parameter.Value = item.Value ?? DBNull.Value;
                            command.Parameters.Add(parameter);
                        }
                        using (var reader = await command.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.SingleResult))
                        {
                            var table = new DataTable("QueryResult");
                            for (var index = 0; index < reader.FieldCount; index++) table.Columns.Add(UniqueColumnName(table, reader.GetName(index), index), typeof(object));
                            var truncated = false;
                            var count = 0;
                            while (await reader.ReadAsync())
                            {
                                if (count >= maximumRows) { truncated = true; break; }
                                var values = new object[reader.FieldCount];
                                reader.GetValues(values);
                                table.Rows.Add(values);
                                count++;
                            }
                            return new SqlQueryResult { Table = table, Truncated = truncated };
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                var message = exception.Message;
                if (!string.IsNullOrEmpty(password)) message = message.Replace(password, "******");
                throw new InvalidOperationException(message, exception);
            }
            finally { password = null; }
        }

        private static DbProviderFactory GetFactory(string invariantName)
        {
            return string.Equals(invariantName, "System.Data.SqlClient", StringComparison.OrdinalIgnoreCase)
                ? SqlClientFactory.Instance
                : DbProviderFactories.GetFactory(invariantName);
        }

        private static string UniqueColumnName(DataTable table, string requested, int index)
        {
            var baseName = string.IsNullOrWhiteSpace(requested) ? "Column" + (index + 1) : requested;
            var name = baseName; var suffix = 2;
            while (table.Columns.Contains(name)) name = baseName + "_" + suffix++;
            return name;
        }

        private static string BuildConnectionString(DatabaseProfile profile, string password)
        {
            if (string.Equals(profile.ProviderInvariantName, "System.Data.SqlClient", StringComparison.OrdinalIgnoreCase))
            {
                return new SqlConnectionStringBuilder
                {
                    DataSource = profile.Port > 0 ? profile.Host + "," + profile.Port : profile.Host,
                    InitialCatalog = profile.Database,
                    IntegratedSecurity = profile.IntegratedSecurity,
                    UserID = profile.IntegratedSecurity ? string.Empty : profile.UserName,
                    Password = profile.IntegratedSecurity ? string.Empty : password,
                    ConnectTimeout = Math.Max(1, profile.ConnectionTimeoutSeconds),
                    Encrypt = true,
                    TrustServerCertificate = true
                }.ConnectionString;
            }

            var builder = new DbConnectionStringBuilder();
            var invariant = profile.ProviderInvariantName ?? string.Empty;
            if (invariant.IndexOf("MySql", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["Server"] = profile.Host; builder["Port"] = profile.Port; builder["Database"] = profile.Database; builder["User ID"] = profile.UserName; builder["Password"] = password;
            }
            else if (invariant.IndexOf("Npgsql", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["Host"] = profile.Host; builder["Port"] = profile.Port; builder["Database"] = profile.Database; builder["Username"] = profile.UserName; builder["Password"] = password;
            }
            else if (invariant.IndexOf("Oracle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["Data Source"] = profile.Host + ":" + profile.Port + "/" + profile.Database; builder["User Id"] = profile.UserName; builder["Password"] = password;
            }
            else if (invariant.IndexOf("SQLite", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                builder["Data Source"] = profile.Database; if (!string.IsNullOrEmpty(password)) builder["Password"] = password;
            }
            else
            {
                builder["Server"] = profile.Host; builder["Port"] = profile.Port; builder["Database"] = profile.Database; builder["User ID"] = profile.UserName; builder["Password"] = password;
            }
            builder["Connection Timeout"] = Math.Max(1, profile.ConnectionTimeoutSeconds);
            return builder.ConnectionString;
        }
    }
}
