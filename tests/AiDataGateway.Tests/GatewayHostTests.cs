using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AiDataGateway.Api;
using LocalMonitorReportExtension;
using Microsoft.Data.Sqlite;

namespace AiDataGateway.Tests;

public sealed class GatewayHostTests
{
    [Fact]
    public async Task Setup_state_is_preserved_after_host_restart()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "AiDataGateway.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        GatewayWebHost? host = null;

        try
        {
            host = await StartHostAsync(tempPath);
            using (var client = new HttpClient { BaseAddress = host.BaseAddress })
            {
                var setupResponse = await client.PostAsJsonAsync("/api/setup", new
                {
                    userName = "restart-admin",
                    email = "restart-admin@example.local",
                    displayName = "Restart Administrator",
                    password = "StrongPassword10",
                    aiClientName = "Restart Test Client"
                });
                var setupBody = await setupResponse.Content.ReadAsStringAsync();
                Assert.True(setupResponse.IsSuccessStatusCode, setupBody);
            }

            await host.DisposeAsync();
            host = null;
            SqliteConnection.ClearAllPools();

            await using (var connection = new SqliteConnection($"Data Source={Path.Combine(tempPath, "gateway.db")}"))
            {
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO GatewayAuditEntries (Id, Actor, Action, Outcome, DataSourceId, Detail, CreatedAtUtc) VALUES ($id, 'test', 'startup.cleanup.test', 'success', NULL, NULL, $created)";
                command.Parameters.AddWithValue("$id", Guid.NewGuid());
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.AddDays(-10));
                await command.ExecuteNonQueryAsync();
            }

            host = await StartHostAsync(tempPath);
            using var restartedClient = new HttpClient { BaseAddress = host.BaseAddress };
            var setupStatus = await restartedClient.GetFromJsonAsync<JsonElement>("/api/setup/status");

            Assert.False(setupStatus.GetProperty("needsSetup").GetBoolean());
            Assert.True(File.Exists(Path.Combine(tempPath, "gateway.db")));

            var oldRecordCount = 1L;
            for (var attempt = 0; attempt < 30 && oldRecordCount > 0; attempt++)
            {
                await Task.Delay(100);
                await using var connection = new SqliteConnection($"Data Source={Path.Combine(tempPath, "gateway.db")}");
                await connection.OpenAsync();
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM GatewayAuditEntries WHERE Action = 'startup.cleanup.test'";
                oldRecordCount = (long)(await command.ExecuteScalarAsync())!;
            }
            Assert.Equal(0, oldRecordCount);
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync();
            }

            DeleteTemporaryStorage(tempPath);
        }
    }

    [Fact]
    public async Task Host_supports_setup_login_and_client_credentials()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "AiDataGateway.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        var port = GetAvailablePort();
        GatewayWebHost? host = null;

        try
        {
            host = await GatewayWebHost.StartAsync(new GatewayHostOptions
            {
                Port = port,
                StoragePath = tempPath,
                WebRootPath = Path.Combine(tempPath, "wwwroot"),
                UseEphemeralCertificates = true
            });

            var cookies = new CookieContainer();
            using var handler = new HttpClientHandler { CookieContainer = cookies };
            using var client = new HttpClient(handler) { BaseAddress = host.BaseAddress };

            var health = await client.GetAsync("/api/health");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            var setupStatus = await client.GetFromJsonAsync<JsonElement>("/api/setup/status");
            Assert.True(setupStatus.GetProperty("needsSetup").GetBoolean());

            var invalidSetupResponse = await client.PostAsJsonAsync("/api/setup", new
            {
                userName = "admin",
                email = "",
                displayName = "Administrator",
                password = "weak",
                aiClientName = "Integration Test Client"
            });
            Assert.Equal(HttpStatusCode.BadRequest, invalidSetupResponse.StatusCode);
            var invalidSetup = await invalidSetupResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("邮箱", invalidSetup.GetProperty("message").GetString());

            var setupResponse = await client.PostAsJsonAsync("/api/setup", new
            {
                userName = "admin",
                email = "admin@example.local",
                displayName = "Administrator",
                password = "StrongPassword10",
                aiClientName = "Integration Test Client"
            });
            setupResponse.EnsureSuccessStatusCode();
            var setup = await setupResponse.Content.ReadFromJsonAsync<JsonElement>();
            var clientId = setup.GetProperty("clientId").GetString();
            var clientSecret = setup.GetProperty("clientSecret").GetString();

            var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                userName = "admin",
                password = "StrongPassword10",
                rememberMe = false
            });
            var loginBody = await loginResponse.Content.ReadAsStringAsync();
            Assert.True(loginResponse.IsSuccessStatusCode, loginBody);

            var pendingApprovals = await client.GetAsync("/api/approvals/pending");
            var pendingApprovalsBody = await pendingApprovals.Content.ReadAsStringAsync();
            Assert.True(pendingApprovals.IsSuccessStatusCode, pendingApprovalsBody);

            var maintenanceSettings = await client.GetFromJsonAsync<JsonElement>("/api/settings/maintenance");
            Assert.True(maintenanceSettings.GetProperty("cleanupEnabled").GetBoolean());
            Assert.Equal(3, maintenanceSettings.GetProperty("retentionDays").GetInt32());
            Assert.Equal("03:00", maintenanceSettings.GetProperty("cleanupTimeLocal").GetString());
            Assert.Equal(15, maintenanceSettings.GetProperty("approvalExpirationMinutes").GetInt32());

            await using (var gatewayConnection = new SqliteConnection($"Data Source={Path.Combine(tempPath, "gateway.db")}"))
            {
                await gatewayConnection.OpenAsync();
                await using var insertOldAudit = gatewayConnection.CreateCommand();
                insertOldAudit.CommandText =
                    "INSERT INTO GatewayAuditEntries (Id, Actor, Action, Outcome, DataSourceId, Detail, CreatedAtUtc) VALUES ($id, 'test', 'old.test', 'success', NULL, NULL, $created)";
                insertOldAudit.Parameters.AddWithValue("$id", Guid.NewGuid());
                insertOldAudit.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.AddDays(-10));
                await insertOldAudit.ExecuteNonQueryAsync();

                await using var insertMetricSamples = gatewayConnection.CreateCommand();
                insertMetricSamples.CommandText =
                    """
                    INSERT INTO GatewayServerMetricSamples
                    (MonitorTargetId, CollectedAtUtc, CpuPercent, MemoryUsedBytes, MemoryTotalBytes, DiskUsedBytes, DiskTotalBytes, NetworkReceivedBytes, NetworkSentBytes, ProcessWorkingSetBytes, SystemUptimeSeconds, ExtendedMetricsJson)
                    SELECT Id, $old, 10, 100, 1000, 200, 2000, 300, 400, 500, 600, '{}' FROM GatewayMonitorTargets WHERE Key = 'local';
                    INSERT INTO GatewayServerMetricSamples
                    (MonitorTargetId, CollectedAtUtc, CpuPercent, MemoryUsedBytes, MemoryTotalBytes, DiskUsedBytes, DiskTotalBytes, NetworkReceivedBytes, NetworkSentBytes, ProcessWorkingSetBytes, SystemUptimeSeconds, ExtendedMetricsJson)
                    SELECT Id, $recent, 20, 100, 1000, 200, 2000, 300, 400, 500, 600, '{}' FROM GatewayMonitorTargets WHERE Key = 'local';
                    """;
                insertMetricSamples.Parameters.AddWithValue("$old", DateTimeOffset.UtcNow.AddDays(-10));
                insertMetricSamples.Parameters.AddWithValue("$recent", DateTimeOffset.UtcNow);
                Assert.Equal(2, await insertMetricSamples.ExecuteNonQueryAsync());
            }

            var cleanupResponse = await client.PostAsync("/api/settings/maintenance/cleanup-now", null);
            var cleanupResult = await cleanupResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(cleanupResponse.IsSuccessStatusCode, cleanupResult.ToString());
            Assert.Equal(1, cleanupResult.GetProperty("auditLogsDeleted").GetInt32());
            Assert.Equal(1, cleanupResult.GetProperty("metricSamplesDeleted").GetInt32());

            await using (var gatewayConnection = new SqliteConnection($"Data Source={Path.Combine(tempPath, "gateway.db")}"))
            {
                await gatewayConnection.OpenAsync();
                await using var remainingMetrics = gatewayConnection.CreateCommand();
                remainingMetrics.CommandText = "SELECT COUNT(*) FROM GatewayServerMetricSamples WHERE CpuPercent = 20";
                Assert.Equal(1L, (long)(await remainingMetrics.ExecuteScalarAsync())!);
            }

            var updateMaintenanceResponse = await client.PutAsJsonAsync("/api/settings/maintenance", new
            {
                cleanupEnabled = true,
                retentionDays = 7,
                cleanupTimeLocal = "04:30",
                approvalExpirationMinutes = 45
            });
            var updatedMaintenance = await updateMaintenanceResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(updateMaintenanceResponse.IsSuccessStatusCode, updatedMaintenance.ToString());
            Assert.Equal(7, updatedMaintenance.GetProperty("retentionDays").GetInt32());
            Assert.Equal("04:30", updatedMaintenance.GetProperty("cleanupTimeLocal").GetString());
            Assert.Equal(45, updatedMaintenance.GetProperty("approvalExpirationMinutes").GetInt32());

            var currentUser = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
            var deleteCurrentUser = await client.DeleteAsync($"/api/admin/users/{currentUser.GetProperty("id").GetGuid()}");
            Assert.Equal(HttpStatusCode.BadRequest, deleteCurrentUser.StatusCode);

            var createDisposableUser = await client.PostAsJsonAsync("/api/admin/users", new
            {
                userName = "disposable-user",
                email = "disposable@example.local",
                displayName = "Disposable User",
                password = "StrongPassword10",
                roles = new[] { "Viewer" }
            });
            Assert.Equal(HttpStatusCode.Created, createDisposableUser.StatusCode);
            var disposableUser = await createDisposableUser.Content.ReadFromJsonAsync<JsonElement>();
            var disposableUserId = disposableUser.GetProperty("id").GetGuid();
            var disableDisposableUser = await client.PutAsJsonAsync($"/api/admin/users/{disposableUserId}", new
            {
                displayName = "Disposable User",
                enabled = false,
                roles = new[] { "Viewer" }
            });
            Assert.Equal(HttpStatusCode.NoContent, disableDisposableUser.StatusCode);
            var deleteDisposableUser = await client.DeleteAsync($"/api/admin/users/{disposableUserId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteDisposableUser.StatusCode);

            var createHistoryUser = await client.PostAsJsonAsync("/api/admin/users", new
            {
                userName = "history-user",
                email = "history@example.local",
                displayName = "History User",
                password = "StrongPassword10",
                roles = new[] { "Viewer" }
            });
            var historyUser = await createHistoryUser.Content.ReadFromJsonAsync<JsonElement>();
            await using (var gatewayConnection = new SqliteConnection($"Data Source={Path.Combine(tempPath, "gateway.db")}"))
            {
                await gatewayConnection.OpenAsync();
                await using var insertHistory = gatewayConnection.CreateCommand();
                insertHistory.CommandText =
                    "INSERT INTO GatewayAuditEntries (Id, Actor, Action, Outcome, DataSourceId, Detail, CreatedAtUtc) VALUES ($id, 'history-user', 'history.test', 'success', NULL, NULL, $created)";
                insertHistory.Parameters.AddWithValue("$id", Guid.NewGuid());
                insertHistory.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow);
                await insertHistory.ExecuteNonQueryAsync();
            }
            var deleteHistoryUser = await client.DeleteAsync($"/api/admin/users/{historyUser.GetProperty("id").GetGuid()}");
            Assert.Equal(HttpStatusCode.Conflict, deleteHistoryUser.StatusCode);

            var removeLastAdministrator = await client.PutAsJsonAsync($"/api/admin/users/{currentUser.GetProperty("id").GetGuid()}", new
            {
                displayName = "Administrator",
                enabled = true,
                roles = new[] { "Developer" }
            });
            Assert.Equal(HttpStatusCode.BadRequest, removeLastAdministrator.StatusCode);

            var createDisposableClient = await client.PostAsJsonAsync("/api/admin/oauth-clients", new
            {
                displayName = "Disposable OAuth Client",
                scopes = new[] { "gateway.datasource.read" }
            });
            createDisposableClient.EnsureSuccessStatusCode();
            var disposableClient = await createDisposableClient.Content.ReadFromJsonAsync<JsonElement>();
            var disposableClientId = disposableClient.GetProperty("clientId").GetString()!;
            var disposableClientSecret = disposableClient.GetProperty("clientSecret").GetString()!;
            var disposableTokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = disposableClientId,
                ["client_secret"] = disposableClientSecret,
                ["scope"] = "gateway.datasource.read"
            }));
            disposableTokenResponse.EnsureSuccessStatusCode();
            var disposableToken = await disposableTokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            using var disposableAiClient = new HttpClient { BaseAddress = host.BaseAddress };
            disposableAiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", disposableToken.GetProperty("access_token").GetString());
            Assert.Equal(HttpStatusCode.OK, (await disposableAiClient.GetAsync("/api/gateway/datasources")).StatusCode);

            var updateDisposableClient = await client.PutAsJsonAsync($"/api/admin/oauth-clients/{disposableClientId}", new
            {
                displayName = "Updated OAuth Client",
                scopes = new[] { "gateway.logs.read" }
            });
            Assert.Equal(HttpStatusCode.NoContent, updateDisposableClient.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await disposableAiClient.GetAsync("/api/gateway/datasources")).StatusCode);
            var managedClients = await client.GetFromJsonAsync<JsonElement>("/api/admin/oauth-clients");
            var updatedClient = Assert.Single(managedClients.EnumerateArray(), item => item.GetProperty("clientId").GetString() == disposableClientId);
            Assert.Equal("Updated OAuth Client", updatedClient.GetProperty("displayName").GetString());
            Assert.Equal("gateway.logs.read", Assert.Single(updatedClient.GetProperty("scopes").EnumerateArray()).GetString());
            var updatedTokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = disposableClientId,
                ["client_secret"] = disposableClientSecret,
                ["scope"] = "gateway.logs.read"
            }));
            updatedTokenResponse.EnsureSuccessStatusCode();
            var updatedToken = await updatedTokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            using var updatedAiClient = new HttpClient { BaseAddress = host.BaseAddress };
            updatedAiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", updatedToken.GetProperty("access_token").GetString());
            Assert.Equal(HttpStatusCode.OK, (await updatedAiClient.GetAsync("/api/log-sources")).StatusCode);

            var deleteDisposableClient = await client.DeleteAsync($"/api/admin/oauth-clients/{disposableClientId}");
            Assert.Equal(HttpStatusCode.NoContent, deleteDisposableClient.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await disposableAiClient.GetAsync("/api/gateway/datasources")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await updatedAiClient.GetAsync("/api/log-sources")).StatusCode);
            var tokenAfterDeletion = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = disposableClientId,
                ["client_secret"] = disposableClientSecret,
                ["scope"] = "gateway.datasource.read"
            }));
            Assert.Equal(HttpStatusCode.Unauthorized, tokenAfterDeletion.StatusCode);

            var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId!,
                ["client_secret"] = clientSecret!,
                ["scope"] = "gateway.datasource.read gateway.query.execute gateway.change.submit gateway.metrics.read"
            }));
            var tokenBody = await tokenResponse.Content.ReadAsStringAsync();
            Assert.True(tokenResponse.IsSuccessStatusCode, tokenBody);
            var token = JsonSerializer.Deserialize<JsonElement>(tokenBody);
            var accessToken = token.GetProperty("access_token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(accessToken));

            using var aiClient = new HttpClient { BaseAddress = host.BaseAddress };
            aiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            var dataSources = await aiClient.GetAsync("/api/gateway/datasources");
            Assert.Equal(HttpStatusCode.OK, dataSources.StatusCode);

            using (var unauthenticatedMcpClient = new HttpClient { BaseAddress = host.BaseAddress })
            {
                var unauthorizedMcp = await unauthenticatedMcpClient.PostAsJsonAsync("/mcp", new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "test", version = "1.0" } }
                });
                Assert.Equal(HttpStatusCode.Unauthorized, unauthorizedMcp.StatusCode);
            }

            var initializeMcp = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "initialize",
                @params = new { protocolVersion = "2025-06-18", capabilities = new { }, clientInfo = new { name = "integration-test", version = "1.0" } }
            });
            var initializeMcpBody = await initializeMcp.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(initializeMcp.IsSuccessStatusCode, initializeMcpBody.ToString());
            Assert.Equal("2025-06-18", initializeMcpBody.GetProperty("result").GetProperty("protocolVersion").GetString());

            var listMcpTools = await aiClient.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
            var mcpToolsBody = await listMcpTools.Content.ReadFromJsonAsync<JsonElement>();
            var mcpToolNames = mcpToolsBody.GetProperty("result").GetProperty("tools").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray();
            Assert.Contains("query_database", mcpToolNames);
            Assert.Contains("submit_change", mcpToolNames);
            Assert.Contains("list_projects", mcpToolNames);
            Assert.Contains("list_log_sources", mcpToolNames);
            Assert.Contains("query_logs", mcpToolNames);
            Assert.Contains("query_server_metrics", mcpToolNames);

            var sqlitePath = Path.Combine(tempPath, "target.db");
            var createDataSource = await client.PostAsJsonAsync("/api/admin/datasources/", new
            {
                key = "local-sqlite",
                name = "Local SQLite",
                provider = 4,
                host = "localhost",
                port = 1,
                database = sqlitePath,
                username = "local",
                password = "local-secret",
                accessMode = 2,
                maxRows = 100,
                commandTimeoutSeconds = 10,
                enabled = true,
                blockedTables = new[] { "secret_records" }
            });
            var createBody = await createDataSource.Content.ReadAsStringAsync();
            Assert.True(createDataSource.IsSuccessStatusCode, createBody);
            var created = JsonSerializer.Deserialize<JsonElement>(createBody);
            var dataSourceId = created.GetProperty("id").GetGuid();
            Assert.Equal("secret_records", Assert.Single(created.GetProperty("blockedTables").EnumerateArray()).GetString());

            var applicationLogPath = Path.Combine(tempPath, "sample-application.log");
            var applicationLogTime = DateTime.Now.AddMinutes(-1);
            await File.WriteAllTextAsync(applicationLogPath,
                $"{applicationLogTime:yyyy-MM-dd HH:mm:ss.ffff}|Info|Sample|started|\n" +
                $"{applicationLogTime.AddSeconds(1):yyyy-MM-dd HH:mm:ss.ffff}|Error|Sample|request failed\ncontinued message|System.InvalidOperationException: broken\n" +
                $"{applicationLogTime.AddSeconds(2):yyyy-MM-dd HH:mm:ss.ffff}|Warning|Sample||");
            var createLogSource = await client.PostAsJsonAsync("/api/admin/log-sources", new
            {
                key = "sample-nlog",
                name = "Sample NLog",
                type = 1,
                endpoint = applicationLogPath,
                nLogConfiguration = "",
                nLogTargetName = "",
                nLogLayout = "${longdate}|${level}|${logger}|${message}|${exception}",
                apiKey = "",
                enabled = true,
                projectIds = Array.Empty<Guid>()
            });
            var createLogSourceBody = await createLogSource.Content.ReadAsStringAsync();
            Assert.True(createLogSource.IsSuccessStatusCode, createLogSourceBody);
            var logSourceId = JsonSerializer.Deserialize<JsonElement>(createLogSourceBody).GetProperty("id").GetGuid();

            var testLogSource = await client.PostAsync($"/api/admin/log-sources/{logSourceId}/test", null);
            var testLogSourceBody = await testLogSource.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(testLogSource.IsSuccessStatusCode, testLogSourceBody.ToString());
            Assert.True(testLogSourceBody.GetProperty("success").GetBoolean(), testLogSourceBody.ToString());

            var createMonitorTarget = await client.PostAsJsonAsync("/api/admin/monitoring/targets", new
            {
                key = "sample-server",
                name = "Sample Server",
                enabled = true,
                projectIds = Array.Empty<Guid>(),
                metricKeys = new[] { "cpu.percent", "memory.percent", "disk.percent", "system.uptime_seconds", "network.receive_bytes_per_second", "process.thread_count" }
            });
            var createMonitorBody = await createMonitorTarget.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(createMonitorTarget.IsSuccessStatusCode, createMonitorBody.ToString());
            var monitorTargetId = createMonitorBody.GetProperty("target").GetProperty("id").GetGuid();
            var monitorSecret = createMonitorBody.GetProperty("ingestSecret").GetString()!;

            var metricCatalog = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/metric-catalog");
            Assert.True(metricCatalog.GetProperty("items").GetArrayLength() >= 20);
            using (var configurationRequest = new HttpRequestMessage(HttpMethod.Get, "/api/monitoring/ingest/sample-server/configuration"))
            {
                configurationRequest.Headers.Add("X-Monitor-Key", monitorSecret);
                var configurationResponse = await client.SendAsync(configurationRequest);
                var configuration = await configurationResponse.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(configurationResponse.IsSuccessStatusCode, configuration.ToString());
                Assert.Contains(configuration.GetProperty("metricKeys").EnumerateArray(), item => item.GetString() == "network.receive_bytes_per_second");
            }

            using (var badIngest = new HttpRequestMessage(HttpMethod.Post, "/api/monitoring/ingest/sample-server"))
            {
                badIngest.Headers.Add("X-Monitor-Key", "wrong-secret");
                badIngest.Content = JsonContent.Create(new { collectedAtUtc = DateTimeOffset.UtcNow, hostName = "remote-01", osDescription = "Windows", cpuPercent = 12.5, memoryUsedBytes = 400L, memoryTotalBytes = 1000L, diskUsedBytes = 500L, diskTotalBytes = 2000L, networkReceivedBytes = 100L, networkSentBytes = 50L, processWorkingSetBytes = 20L, systemUptimeSeconds = 3600L });
                Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(badIngest)).StatusCode);
            }

            using (var ingest = new HttpRequestMessage(HttpMethod.Post, "/api/monitoring/ingest/sample-server"))
            {
                ingest.Headers.Add("X-Monitor-Key", monitorSecret);
                ingest.Content = JsonContent.Create(new { collectedAtUtc = DateTimeOffset.UtcNow, hostName = "remote-01", osDescription = "Windows 11", cpuPercent = 12.5, memoryUsedBytes = 400L, memoryTotalBytes = 1000L, diskUsedBytes = 500L, diskTotalBytes = 2000L, networkReceivedBytes = 100L, networkSentBytes = 50L, processWorkingSetBytes = 20L, systemUptimeSeconds = 3600L, extendedMetrics = new Dictionary<string, double> { ["network.receive_bytes_per_second"] = 1234, ["process.thread_count"] = 17, ["gc.heap_size_bytes"] = 9999 } });
                Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(ingest)).StatusCode);
            }

            var monitorTargets = await client.GetFromJsonAsync<JsonElement>("/api/monitoring/targets");
            Assert.Contains(monitorTargets.EnumerateArray(), item => item.GetProperty("key").GetString() == "local");
            var remoteMonitor = monitorTargets.EnumerateArray().Single(item => item.GetProperty("key").GetString() == "sample-server");
            Assert.True(remoteMonitor.GetProperty("online").GetBoolean());
            Assert.Equal(12.5, remoteMonitor.GetProperty("latest").GetProperty("cpuPercent").GetDouble());
            Assert.Equal(1234, remoteMonitor.GetProperty("latest").GetProperty("metrics").GetProperty("network.receive_bytes_per_second").GetDouble());
            Assert.False(remoteMonitor.GetProperty("latest").GetProperty("metrics").TryGetProperty("gc.heap_size_bytes", out _));

            var trendResponse = await client.GetAsync($"/api/monitoring/targets/{monitorTargetId}/trend?fromUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddHours(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(1).ToString("O"))}&maxPoints=100");
            var trend = await trendResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(trendResponse.IsSuccessStatusCode, trend.ToString());
            Assert.Equal(1, trend.GetProperty("sourceCount").GetInt32());
            Assert.Single(trend.GetProperty("items").EnumerateArray());

            var createProject = await client.PostAsJsonAsync("/api/admin/projects", new
            {
                code = "sample-project",
                name = "Sample Project",
                description = "Integration project",
                enabled = true,
                dataSourceIds = new[] { dataSourceId },
                logSourceIds = new[] { logSourceId },
                monitorTargetIds = new[] { monitorTargetId }
            });
            var createProjectBody = await createProject.Content.ReadAsStringAsync();
            Assert.True(createProject.IsSuccessStatusCode, createProjectBody);
            var project = JsonSerializer.Deserialize<JsonElement>(createProjectBody);
            Assert.Equal(dataSourceId, Assert.Single(project.GetProperty("dataSources").EnumerateArray()).GetProperty("id").GetGuid());
            Assert.Equal(logSourceId, Assert.Single(project.GetProperty("logSources").EnumerateArray()).GetProperty("id").GetGuid());
            Assert.Equal(monitorTargetId, Assert.Single(project.GetProperty("monitorTargets").EnumerateArray()).GetProperty("id").GetGuid());

            var createSecondProject = await client.PostAsJsonAsync("/api/admin/projects", new
            {
                code = "sample-project-two",
                name = "Sample Project Two",
                description = "Shares the same log source",
                enabled = true,
                dataSourceIds = Array.Empty<Guid>(),
                logSourceIds = new[] { logSourceId }
            });
            var createSecondProjectBody = await createSecondProject.Content.ReadAsStringAsync();
            Assert.True(createSecondProject.IsSuccessStatusCode, createSecondProjectBody);
            var secondProjectId = JsonSerializer.Deserialize<JsonElement>(createSecondProjectBody).GetProperty("id").GetGuid();
            var managedLogSources = await client.GetFromJsonAsync<JsonElement>("/api/admin/log-sources");
            var managedLogSource = Assert.Single(managedLogSources.EnumerateArray());
            Assert.Equal(2, managedLogSource.GetProperty("projects").GetArrayLength());

            var applicationLogsResponse = await client.PostAsJsonAsync("/api/logs/query", new
            {
                logSourceId,
                query = "request failed",
                page = 1,
                pageSize = 20
            });
            var applicationLogs = await applicationLogsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(applicationLogsResponse.IsSuccessStatusCode, applicationLogs.ToString());
            var applicationLog = Assert.Single(applicationLogs.GetProperty("items").EnumerateArray());
            Assert.Contains("continued message", applicationLog.GetProperty("message").GetString());
            Assert.Contains("InvalidOperationException", applicationLog.GetProperty("exception").GetString());

            var logSqlProjectsResponse = await client.GetAsync($"/api/logs/{logSourceId}/sql/projects");
            var logSqlProjects = await logSqlProjectsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(logSqlProjectsResponse.IsSuccessStatusCode, logSqlProjects.ToString());
            var firstLogProject = logSqlProjects.EnumerateArray().Single(item => item.GetProperty("code").GetString() == "sample-project");
            Assert.Equal(dataSourceId, Assert.Single(firstLogProject.GetProperty("dataSources").EnumerateArray()).GetProperty("id").GetGuid());

            var logSqlQueryResponse = await client.PostAsJsonAsync($"/api/logs/{logSourceId}/sql/query", new
            {
                projectId = project.GetProperty("id").GetGuid(),
                dataSourceId,
                sql = "select 1 as value"
            });
            var logSqlQuery = await logSqlQueryResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(logSqlQueryResponse.IsSuccessStatusCode, logSqlQuery.ToString());
            Assert.Equal(1, logSqlQuery.GetProperty("rows")[0].GetProperty("value").GetInt64());

            var unlinkedLogSqlQuery = await client.PostAsJsonAsync($"/api/logs/{logSourceId}/sql/query", new
            {
                projectId = secondProjectId,
                dataSourceId,
                sql = "select 1 as value"
            });
            Assert.Equal(HttpStatusCode.BadRequest, unlinkedLogSqlQuery.StatusCode);

            using (var streamTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(12)))
            using (var streamRequest = new HttpRequestMessage(HttpMethod.Get,
                       $"/api/logs/stream?logSourceId={logSourceId}&fromUtc={Uri.EscapeDataString(DateTimeOffset.UtcNow.ToString("O"))}"))
            {
                var streamResponseTask = client.SendAsync(streamRequest, HttpCompletionOption.ResponseHeadersRead, streamTimeout.Token);
                using var streamResponse = await streamResponseTask.WaitAsync(TimeSpan.FromSeconds(2), streamTimeout.Token);
                streamResponse.EnsureSuccessStatusCode();
                await using var eventStream = await streamResponse.Content.ReadAsStreamAsync(streamTimeout.Token);
                using var eventReader = new StreamReader(eventStream);
                Assert.Equal(": connected", await eventReader.ReadLineAsync(streamTimeout.Token));
                Assert.Equal(string.Empty, await eventReader.ReadLineAsync(streamTimeout.Token));

                await File.AppendAllTextAsync(applicationLogPath,
                    $"\n{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.ffff}|Info|Sample|realtime marker|", streamTimeout.Token);
                var eventLine = await eventReader.ReadLineAsync(streamTimeout.Token);
                Assert.NotNull(eventLine);
                Assert.StartsWith("data: ", eventLine);
                var realtimeEvent = JsonSerializer.Deserialize<JsonElement>(eventLine[6..]);
                Assert.False(string.IsNullOrWhiteSpace(realtimeEvent.GetProperty("id").GetString()));
                Assert.Contains("realtime marker", realtimeEvent.GetProperty("message").GetString());
            }

            var mcpProjectsResponse = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 21,
                method = "tools/call",
                @params = new { name = "list_projects", arguments = new { } }
            });
            var mcpProjects = await mcpProjectsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(mcpProjects.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains(mcpProjects.GetProperty("result").GetProperty("structuredContent").GetProperty("items").EnumerateArray(),
                item => item.GetProperty("code").GetString() == "sample-project");

            var mcpLogsResponse = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 22,
                method = "tools/call",
                @params = new { name = "query_logs", arguments = new { projectCode = "sample-project", logSourceKey = "sample-nlog", query = "started", count = 20 } }
            });
            var mcpLogs = await mcpLogsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(mcpLogs.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Single(mcpLogs.GetProperty("result").GetProperty("structuredContent").GetProperty("items").EnumerateArray());

            var mcpMetricsResponse = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 23,
                method = "tools/call",
                @params = new { name = "query_server_metrics", arguments = new { projectCode = "sample-project", targetKey = "sample-server", count = 20 } }
            });
            var mcpMetrics = await mcpMetricsResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(mcpMetrics.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Equal(12.5, Assert.Single(mcpMetrics.GetProperty("result").GetProperty("structuredContent").GetProperty("items").EnumerateArray()).GetProperty("cpuPercent").GetDouble());

            var blockedQueryResponse = await aiClient.PostAsJsonAsync("/api/gateway/query", new
            {
                dataSourceId,
                sql = "select * from secret_records"
            });
            Assert.Equal(HttpStatusCode.BadRequest, blockedQueryResponse.StatusCode);
            var blockedQuery = await blockedQueryResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("黑名单", blockedQuery.GetProperty("message").GetString());

            var testConnection = await client.PostAsync($"/api/admin/datasources/{dataSourceId}/test", null);
            var testResult = await testConnection.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(testConnection.IsSuccessStatusCode, testResult.ToString());
            Assert.True(testResult.GetProperty("success").GetBoolean(), testResult.ToString());

            var queryResponse = await aiClient.PostAsJsonAsync("/api/gateway/query", new
            {
                dataSourceId,
                sql = "select 1 as value"
            });
            var queryBody = await queryResponse.Content.ReadAsStringAsync();
            Assert.True(queryResponse.IsSuccessStatusCode, queryBody);
            var queryResult = JsonSerializer.Deserialize<JsonElement>(queryBody);
            Assert.Single(queryResult.GetProperty("rows").EnumerateArray());

            var mcpQueryResponse = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 3,
                method = "tools/call",
                @params = new { name = "query_database", arguments = new { dataSourceId, sql = "select 1 as value" } }
            });
            var mcpQueryBody = await mcpQueryResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(mcpQueryResponse.IsSuccessStatusCode, mcpQueryBody.ToString());
            Assert.False(mcpQueryBody.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Single(mcpQueryBody.GetProperty("result").GetProperty("structuredContent").GetProperty("rows").EnumerateArray());

            var nullQueryResponse = await aiClient.PostAsJsonAsync("/api/gateway/query", new
            {
                dataSourceId,
                sql = "select null as value"
            });
            var nullQueryBody = await nullQueryResponse.Content.ReadAsStringAsync();
            Assert.True(nullQueryResponse.IsSuccessStatusCode, nullQueryBody);
            var nullQueryResult = JsonSerializer.Deserialize<JsonElement>(nullQueryBody);
            Assert.Equal(JsonValueKind.Null, nullQueryResult.GetProperty("rows")[0].GetProperty("value").ValueKind);

            var submitChangeResponse = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 4,
                method = "tools/call",
                @params = new { name = "submit_change", arguments = new { dataSourceId, sql = "create table approval_history_test (id integer primary key, name text)" } }
            });
            var submitChangeBody = await submitChangeResponse.Content.ReadAsStringAsync();
            Assert.Equal(HttpStatusCode.OK, submitChangeResponse.StatusCode);
            var submittedChange = JsonSerializer.Deserialize<JsonElement>(submitChangeBody);
            var submittedChangeContent = submittedChange.GetProperty("result").GetProperty("structuredContent");
            var changeId = submittedChangeContent.GetProperty("id").GetGuid();
            var expiresAt = submittedChangeContent.GetProperty("expiresAtUtc").GetDateTimeOffset();
            Assert.InRange(expiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(44), TimeSpan.FromMinutes(46));

            var mcpStatusResponse = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 5,
                method = "tools/call",
                @params = new { name = "get_change_status", arguments = new { changeId } }
            });
            var mcpStatus = await mcpStatusResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Pending", mcpStatus.GetProperty("result").GetProperty("structuredContent").GetProperty("status").GetString());

            var approvalHistory = await client.GetFromJsonAsync<JsonElement>("/api/approvals?page=1&pageSize=100");
            Assert.Equal(1, approvalHistory.GetProperty("total").GetInt32());
            var pendingApproval = Assert.Single(approvalHistory.GetProperty("items").EnumerateArray());
            Assert.Equal("Pending", pendingApproval.GetProperty("status").GetString());
            Assert.Contains("approval_history_test", pendingApproval.GetProperty("sql").GetString());

            var approvalDetail = await client.GetFromJsonAsync<JsonElement>($"/api/approvals/{changeId}");
            Assert.Equal(changeId, approvalDetail.GetProperty("id").GetGuid());
            Assert.Equal("High", approvalDetail.GetProperty("riskLevel").GetString());

            var reviewResponse = await client.PostAsJsonAsync($"/api/approvals/{changeId}/review", new
            {
                approved = true,
                comment = "integration test approval"
            });
            var reviewBody = await reviewResponse.Content.ReadAsStringAsync();
            Assert.True(reviewResponse.IsSuccessStatusCode, reviewBody);

            approvalHistory = await client.GetFromJsonAsync<JsonElement>("/api/approvals?status=Succeeded&keyword=approval_history_test&page=1&pageSize=10");
            Assert.Equal(1, approvalHistory.GetProperty("total").GetInt32());
            var completedApproval = Assert.Single(approvalHistory.GetProperty("items").EnumerateArray());
            Assert.Equal("Succeeded", completedApproval.GetProperty("status").GetString());
            Assert.Equal("integration test approval", completedApproval.GetProperty("reviewComment").GetString());

            approvalHistory = await client.GetFromJsonAsync<JsonElement>($"/api/approvals?dataSourceId={dataSourceId}&page=1&pageSize=10");
            Assert.Equal(1, approvalHistory.GetProperty("total").GetInt32());
            Assert.Equal(changeId, Assert.Single(approvalHistory.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
            approvalHistory = await client.GetFromJsonAsync<JsonElement>($"/api/approvals?dataSourceId={Guid.NewGuid()}&page=1&pageSize=10");
            Assert.Equal(0, approvalHistory.GetProperty("total").GetInt32());

            var auditLogs = await client.GetFromJsonAsync<JsonElement>("/api/audit/logs?keyword=select&page=1&pageSize=100");
            Assert.True(auditLogs.GetProperty("total").GetInt32() >= 2);
            var auditItems = auditLogs.GetProperty("items").EnumerateArray().ToArray();
            Assert.Contains(auditItems, item =>
                item.GetProperty("action").GetString() == "query.execute" &&
                item.GetProperty("detail").GetString()!.Contains("select 1 as value", StringComparison.Ordinal));
            var queryAudit = auditItems.First(item =>
                item.GetProperty("action").GetString() == "query.execute" &&
                item.GetProperty("detail").GetString()!.Contains("select 1 as value", StringComparison.Ordinal));
            var queryDetail = JsonSerializer.Deserialize<JsonElement>(queryAudit.GetProperty("detail").GetString()!);
            Assert.Equal("select 1 as value", queryDetail.GetProperty("sql").GetString());
            Assert.Equal(1, queryDetail.GetProperty("rowCount").GetInt32());
            Assert.Equal(1, queryDetail.GetProperty("rows")[0].GetProperty("value").GetInt64());

            auditLogs = await client.GetFromJsonAsync<JsonElement>("/api/audit/logs?action=change.execute&outcome=success&page=1&pageSize=1");
            Assert.Equal(1, auditLogs.GetProperty("total").GetInt32());
            var executeAudit = Assert.Single(auditLogs.GetProperty("items").EnumerateArray());
            var executeDetail = JsonSerializer.Deserialize<JsonElement>(executeAudit.GetProperty("detail").GetString()!);
            Assert.Contains("approval_history_test", executeDetail.GetProperty("sql").GetString());
            Assert.Equal(0, executeDetail.GetProperty("affectedRows").GetInt32());

            var blockedAuditLogs = await client.GetFromJsonAsync<JsonElement>("/api/audit/logs?action=query.blocked&page=1&pageSize=10");
            Assert.Equal(1, blockedAuditLogs.GetProperty("total").GetInt32());
        }
        finally
        {
            if (host is not null)
            {
                await host.DisposeAsync();
            }
            DeleteTemporaryStorage(tempPath);
        }
    }

    [Fact]
    public async Task Administrator_password_can_be_recovered_and_recovery_password_can_be_changed()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "AiDataGateway.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        GatewayWebHost? host = null;
        try
        {
            host = await StartHostAsync(tempPath);
            var cookies = new CookieContainer();
            using var handler = new HttpClientHandler { CookieContainer = cookies };
            using var client = new HttpClient(handler) { BaseAddress = host.BaseAddress };
            var setup = await client.PostAsJsonAsync("/api/setup", new
            {
                userName = "recovery-admin",
                email = "recovery-admin@example.local",
                displayName = "Recovery Administrator",
                password = "StrongPassword10",
                aiClientName = "Recovery Test Client"
            });
            Assert.True(setup.IsSuccessStatusCode, await setup.Content.ReadAsStringAsync());
            var login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "recovery-admin", password = "StrongPassword10", rememberMe = true });
            Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());
            Assert.Contains(login.Headers.GetValues("Set-Cookie"), value => value.Contains("expires=", StringComparison.OrdinalIgnoreCase));

            var status = await client.GetFromJsonAsync<JsonElement>("/api/settings/admin-recovery");
            Assert.True(status.GetProperty("usesDefaultPassword").GetBoolean());
            await client.PostAsync("/api/auth/logout", null);

            var defaultReset = await client.PostAsJsonAsync("/api/auth/reset-admin-password", new
            {
                userName = "recovery-admin",
                resetPassword = "admin",
                newPassword = "DefaultReset20"
            });
            Assert.Equal(HttpStatusCode.NoContent, defaultReset.StatusCode);
            login = await client.PostAsJsonAsync("/api/auth/login", new { userName = "recovery-admin", password = "DefaultReset20" });
            Assert.True(login.IsSuccessStatusCode, await login.Content.ReadAsStringAsync());

            var update = await client.PutAsJsonAsync("/api/settings/admin-recovery", new { newResetPassword = "PrivateRecovery42" });
            Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
            Assert.False((await update.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("usesDefaultPassword").GetBoolean());
            await client.PostAsync("/api/auth/logout", null);

            var wrong = await client.PostAsJsonAsync("/api/auth/reset-admin-password", new
            {
                userName = "recovery-admin",
                resetPassword = "admin",
                newPassword = "新密码abc"
            });
            Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);

            var weak = await client.PostAsJsonAsync("/api/auth/reset-admin-password", new
            {
                userName = "recovery-admin",
                resetPassword = "PrivateRecovery42",
                newPassword = "weak"
            });
            Assert.Equal(HttpStatusCode.BadRequest, weak.StatusCode);

            var reset = await client.PostAsJsonAsync("/api/auth/reset-admin-password", new
            {
                userName = "recovery-admin",
                resetPassword = "PrivateRecovery42",
                newPassword = "新密码abc"
            });
            Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

            var oldLogin = await client.PostAsJsonAsync("/api/auth/login", new { userName = "recovery-admin", password = "StrongPassword10" });
            Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
            oldLogin = await client.PostAsJsonAsync("/api/auth/login", new { userName = "recovery-admin", password = "DefaultReset20" });
            Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
            var newLogin = await client.PostAsJsonAsync("/api/auth/login", new { userName = "recovery-admin", password = "新密码abc" });
            Assert.True(newLogin.IsSuccessStatusCode, await newLogin.Content.ReadAsStringAsync());
        }
        finally
        {
            if (host is not null) await host.DisposeAsync().AsTask();
            DeleteTemporaryStorage(tempPath);
        }
    }

    [Fact]
    public async Task Trusted_extension_package_adds_page_and_dynamic_mcp_tools()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "AiDataGateway.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        GatewayWebHost? host = null;
        try
        {
            host = await StartHostAsync(tempPath);
            var cookies = new CookieContainer();
            using var handler = new HttpClientHandler { CookieContainer = cookies };
            using var client = new HttpClient(handler) { BaseAddress = host.BaseAddress };
            var setupResponse = await client.PostAsJsonAsync("/api/setup", new
            {
                userName = "extension-admin",
                email = "extension-admin@example.local",
                displayName = "Extension Administrator",
                password = "StrongPassword10",
                aiClientName = "Extension Test Client"
            });
            var setupBody = await setupResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(setupResponse.IsSuccessStatusCode, setupBody.ToString());
            var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                userName = "extension-admin",
                password = "StrongPassword10",
                rememberMe = false
            });
            Assert.True(loginResponse.IsSuccessStatusCode, await loginResponse.Content.ReadAsStringAsync());

            using var package = BuildExtensionPackage();
            using var multipart = new MultipartFormDataContent();
            multipart.Add(new StreamContent(package), "package", "local-monitor-report.zip");
            var installResponse = await client.PostAsync("/api/admin/custom-modules/install", multipart);
            var install = await installResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(installResponse.IsSuccessStatusCode, install.ToString());
            Assert.True(install.GetProperty("loaded").GetBoolean());

            var modules = await client.GetFromJsonAsync<JsonElement>("/api/custom-modules");
            var module = Assert.Single(modules.EnumerateArray());
            Assert.Equal("local-monitor-report", module.GetProperty("id").GetString());
            Assert.Equal(2, module.GetProperty("tools").GetArrayLength());

            var page = await client.GetAsync("/custom-modules/local-monitor-report/ui/");
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            Assert.Contains("本机监控分析报告", await page.Content.ReadAsStringAsync());

            var uiInvocation = await client.PostAsJsonAsync("/api/custom-modules/local-monitor-report/invoke/list_targets", new { });
            var uiResult = await uiInvocation.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(uiInvocation.IsSuccessStatusCode, uiResult.ToString());
            Assert.Contains(uiResult.GetProperty("items").EnumerateArray(), item => item.GetProperty("key").GetString() == "local");

            var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = setupBody.GetProperty("clientId").GetString()!,
                ["client_secret"] = setupBody.GetProperty("clientSecret").GetString()!,
                ["scope"] = "gateway.metrics.read"
            }));
            var token = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(tokenResponse.IsSuccessStatusCode, token.ToString());
            using var aiClient = new HttpClient { BaseAddress = host.BaseAddress };
            aiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.GetProperty("access_token").GetString());

            var toolListResponse = await aiClient.PostAsJsonAsync("/mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list", @params = new { } });
            var toolList = await toolListResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(toolListResponse.IsSuccessStatusCode, toolList.ToString());
            var names = toolList.GetProperty("result").GetProperty("tools").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray();
            Assert.Contains("custom_local_monitor_report_generate_report", names);

            aiClient.DefaultRequestHeaders.Add("Mcp-Name", "custom_local_monitor_report_list_targets");
            var toolCallResponse = await aiClient.PostAsJsonAsync("/mcp", new
            {
                jsonrpc = "2.0",
                id = 2,
                method = "tools/call",
                @params = new { name = "custom_local_monitor_report_list_targets", arguments = new { } }
            });
            var toolCall = await toolCallResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(toolCallResponse.IsSuccessStatusCode, toolCall.ToString());
            Assert.False(toolCall.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Contains(toolCall.GetProperty("result").GetProperty("structuredContent").GetProperty("items").EnumerateArray(), item => item.GetProperty("key").GetString() == "local");
        }
        finally
        {
            if (host is not null) await host.DisposeAsync().AsTask();
            host = null;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            DeleteTemporaryStorage(tempPath);
        }
    }

    private static MemoryStream BuildExtensionPackage()
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifest = archive.CreateEntry("gateway-extension.json");
            using (var writer = new StreamWriter(manifest.Open(), new UTF8Encoding(false)))
                writer.Write("{\"id\":\"local-monitor-report\",\"entryAssembly\":\"LocalMonitorReportExtension.dll\",\"entryType\":\"LocalMonitorReportExtension.LocalMonitorReportModule\",\"enabled\":true,\"contractVersion\":1}");
            var assembly = archive.CreateEntry("LocalMonitorReportExtension.dll");
            using (var input = File.OpenRead(typeof(LocalMonitorReportModule).Assembly.Location))
            using (var output = assembly.Open()) input.CopyTo(output);
            var page = archive.CreateEntry("wwwroot/index.html");
            using (var writer = new StreamWriter(page.Open(), new UTF8Encoding(false)))
                writer.Write("<!doctype html><html><head><meta charset=\"utf-8\"><title>本机监控分析报告</title></head><body>本机监控分析报告</body></html>");
        }
        stream.Position = 0;
        return stream;
    }

    [Fact]
    public async Task Toolbox_webhooks_support_ingest_history_clear_and_cascade_delete()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "AiDataGateway.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);
        GatewayWebHost? host = null;
        try
        {
            host = await StartHostAsync(tempPath);
            using var client = new HttpClient { BaseAddress = host.BaseAddress };
            var setupResponse = await client.PostAsJsonAsync("/api/setup", new
            {
                userName = "toolbox-admin",
                email = "toolbox-admin@example.local",
                displayName = "Toolbox Administrator",
                password = "StrongPassword10",
            });
            Assert.True(setupResponse.IsSuccessStatusCode, await setupResponse.Content.ReadAsStringAsync());
            var loginResponse = await client.PostAsJsonAsync("/api/auth/login", new
            {
                userName = "toolbox-admin",
                password = "StrongPassword10",
                rememberMe = false,
            });
            Assert.True(loginResponse.IsSuccessStatusCode, await loginResponse.Content.ReadAsStringAsync());

            var createResponse = await client.PostAsJsonAsync("/api/toolbox/webhooks", new { name = "debug hook", description = "集成测试" });
            Assert.True(createResponse.IsSuccessStatusCode, await createResponse.Content.ReadAsStringAsync());
            var hook = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
            var token = hook.GetProperty("token").GetString();
            var hookId = hook.GetProperty("id").GetGuid();

            // listing must not trip over DateTimeOffset ordering on SQLite
            var listResponse = await client.GetAsync("/api/toolbox/webhooks");
            Assert.True(listResponse.IsSuccessStatusCode, await listResponse.Content.ReadAsStringAsync());
            var listed = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, listed.GetArrayLength());
            Assert.Equal("debug hook", listed[0].GetProperty("name").GetString());

            using var anonymous = new HttpClient { BaseAddress = host.BaseAddress };
            var ingest = await anonymous.PostAsync($"/toolbox/hook/{token}", new StringContent("{\"ping\":1}", System.Text.Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Accepted, ingest.StatusCode);

            var missing = await anonymous.PostAsJsonAsync("/toolbox/hook/deadbeefdead", new { x = 1 });
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            var deliveries = await client.GetFromJsonAsync<JsonElement>($"/api/toolbox/webhooks/{hookId}/deliveries");
            Assert.Equal(1, deliveries.GetArrayLength());
            Assert.Equal("POST", deliveries[0].GetProperty("method").GetString());
            Assert.Contains("ping", deliveries[0].GetProperty("body").GetString());

            var disable = await client.PutAsJsonAsync($"/api/toolbox/webhooks/{hookId}", new { name = "debug hook", description = "集成测试", enabled = false });
            Assert.True(disable.IsSuccessStatusCode, await disable.Content.ReadAsStringAsync());
            var disabledIngest = await anonymous.PostAsJsonAsync($"/toolbox/hook/{token}", new { x = 1 });
            Assert.Equal(HttpStatusCode.NotFound, disabledIngest.StatusCode);

            await client.PostAsync($"/api/toolbox/webhooks/{hookId}/deliveries/clear", null);
            deliveries = await client.GetFromJsonAsync<JsonElement>($"/api/toolbox/webhooks/{hookId}/deliveries");
            Assert.Equal(0, deliveries.GetArrayLength());

            var delete = await client.DeleteAsync($"/api/toolbox/webhooks/{hookId}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            var afterDelete = await anonymous.PostAsJsonAsync($"/toolbox/hook/{token}", new { x = 1 });
            Assert.Equal(HttpStatusCode.NotFound, afterDelete.StatusCode);
        }
        finally
        {
            if (host is not null) await host.DisposeAsync().AsTask();
            DeleteTemporaryStorage(tempPath);
        }
    }

    private static Task<GatewayWebHost> StartHostAsync(string storagePath)
    {
        return GatewayWebHost.StartAsync(new GatewayHostOptions
        {
            Port = GetAvailablePort(),
            StoragePath = storagePath,
            WebRootPath = Path.Combine(storagePath, "wwwroot"),
            UseEphemeralCertificates = true
        });
    }

    private static void DeleteTemporaryStorage(string path)
    {
        SqliteConnection.ClearAllPools();
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        var resolved = Path.GetFullPath(path);
        if (resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(resolved))
        {
            Directory.Delete(resolved, recursive: true);
        }
    }

    private static int GetAvailablePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
