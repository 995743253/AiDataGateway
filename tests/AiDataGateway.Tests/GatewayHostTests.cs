using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using AiDataGateway.Api;
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

            host = await StartHostAsync(tempPath);
            using var restartedClient = new HttpClient { BaseAddress = host.BaseAddress };
            var setupStatus = await restartedClient.GetFromJsonAsync<JsonElement>("/api/setup/status");

            Assert.False(setupStatus.GetProperty("needsSetup").GetBoolean());
            Assert.True(File.Exists(Path.Combine(tempPath, "gateway.db")));
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

            var tokenResponse = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId!,
                ["client_secret"] = clientSecret!,
                ["scope"] = "gateway.datasource.read gateway.query.execute gateway.change.submit"
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
                accessMode = 1,
                maxRows = 100,
                commandTimeoutSeconds = 10,
                enabled = true
            });
            var createBody = await createDataSource.Content.ReadAsStringAsync();
            Assert.True(createDataSource.IsSuccessStatusCode, createBody);
            var created = JsonSerializer.Deserialize<JsonElement>(createBody);
            var dataSourceId = created.GetProperty("id").GetGuid();

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

            var nullQueryResponse = await aiClient.PostAsJsonAsync("/api/gateway/query", new
            {
                dataSourceId,
                sql = "select null as value"
            });
            var nullQueryBody = await nullQueryResponse.Content.ReadAsStringAsync();
            Assert.True(nullQueryResponse.IsSuccessStatusCode, nullQueryBody);
            var nullQueryResult = JsonSerializer.Deserialize<JsonElement>(nullQueryBody);
            Assert.Equal(JsonValueKind.Null, nullQueryResult.GetProperty("rows")[0].GetProperty("value").ValueKind);
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
