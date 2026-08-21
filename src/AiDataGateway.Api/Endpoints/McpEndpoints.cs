using System.Text.Json;
using AiDataGateway.Api.Security;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.Approvals;
using AiDataGateway.Application.DataSources;
using AiDataGateway.Application.Security;
using AiDataGateway.Application.Sql;
using AiDataGateway.Domain.Approvals;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

namespace AiDataGateway.Api.Endpoints;

internal static class McpEndpoints
{
    private const string LatestProtocolVersion = "2025-06-18";
    private static readonly string[] SupportedProtocolVersions = [LatestProtocolVersion, "2025-03-26", "2024-11-05"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapMcpEndpoints(this IEndpointRouteBuilder endpoints, Uri baseAddress)
    {
        endpoints.MapGet("/.well-known/oauth-authorization-server", () => Results.Ok(new
        {
            issuer = baseAddress.ToString().TrimEnd('/'),
            token_endpoint = new Uri(baseAddress, "/connect/token").ToString(),
            grant_types_supported = new[] { "client_credentials" },
            token_endpoint_auth_methods_supported = new[] { "client_secret_post" },
            scopes_supported = GatewayScopes.AiClientDefaults
        }));

        endpoints.MapGet("/.well-known/oauth-protected-resource", () => Results.Ok(new
        {
            resource = new Uri(baseAddress, "/mcp").ToString(),
            authorization_servers = new[] { baseAddress.ToString().TrimEnd('/') },
            bearer_methods_supported = new[] { "header" },
            scopes_supported = GatewayScopes.AiClientDefaults
        }));

        endpoints.MapPost("/mcp", HandleAsync).RequireAuthorization(new AuthorizeAttribute
        {
            AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme
        });
        endpoints.MapGet("/mcp", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
        endpoints.MapDelete("/mcp", () => Results.StatusCode(StatusCodes.Status405MethodNotAllowed));
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        DataSourceService dataSources,
        QueryService queries,
        ChangeSubmissionService submissions,
        IChangeRequestRepository changes,
        CancellationToken cancellationToken)
    {
        if (context.User.Identity?.IsAuthenticated != true ||
            !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Results.Unauthorized();
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException exception)
        {
            return RpcError(null, -32700, "Parse error", exception.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return RpcError(null, -32600, "Invalid Request", "MCP batch requests are not supported.");
            }

            var id = root.TryGetProperty("id", out var idElement) ? idElement.Clone() : (JsonElement?)null;
            if (!root.TryGetProperty("jsonrpc", out var jsonRpc) || jsonRpc.GetString() != "2.0" ||
                !root.TryGetProperty("method", out var methodElement) || string.IsNullOrWhiteSpace(methodElement.GetString()))
            {
                return RpcError(id, -32600, "Invalid Request");
            }

            var method = methodElement.GetString()!;
            if (!HeaderMatches(context.Request, "Mcp-Method", method))
            {
                return Results.BadRequest(new { message = "Mcp-Method header does not match the JSON-RPC method." });
            }

            var protocolHeader = context.Request.Headers["MCP-Protocol-Version"].ToString();
            if (!string.IsNullOrWhiteSpace(protocolHeader) && !SupportedProtocolVersions.Contains(protocolHeader, StringComparer.Ordinal))
            {
                return Results.BadRequest(new { message = $"Unsupported MCP protocol version '{protocolHeader}'." });
            }

            if (id is null)
            {
                return Results.StatusCode(StatusCodes.Status202Accepted);
            }

            try
            {
                return method switch
                {
                    "initialize" => RpcResult(id, Initialize(root)),
                    "ping" => RpcResult(id, new { }),
                    "tools/list" => RpcResult(id, new { tools = ListTools() }),
                    "tools/call" => await CallToolAsync(id, root, context, dataSources, queries, submissions, changes, cancellationToken),
                    _ => RpcError(id, -32601, "Method not found", method)
                };
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or FormatException)
            {
                return method == "tools/call"
                    ? RpcResult(id, ToolError(exception.Message))
                    : RpcError(id, -32602, "Invalid params", exception.Message);
            }
        }
    }

    private static object Initialize(JsonElement request)
    {
        var requestedVersion = request.TryGetProperty("params", out var parameters) &&
                               parameters.TryGetProperty("protocolVersion", out var version)
            ? version.GetString()
            : null;
        var negotiatedVersion = requestedVersion is not null && SupportedProtocolVersions.Contains(requestedVersion, StringComparer.Ordinal)
            ? requestedVersion
            : LatestProtocolVersion;
        return new
        {
            protocolVersion = negotiatedVersion,
            capabilities = new { tools = new { listChanged = false } },
            serverInfo = new { name = "AiDataGateway", version = typeof(McpEndpoints).Assembly.GetName().Version?.ToString() ?? "1.0.0" },
            instructions = "Use query_database only for read-only SQL. Submit all writes with submit_change; approval is completed by a human in the gateway UI."
        };
    }

    private static object[] ListTools() =>
    [
        new
        {
            name = "list_data_sources",
            description = "List enabled database targets available through the local AI data gateway.",
            inputSchema = new { type = "object", properties = new { }, additionalProperties = false },
            annotations = new { readOnlyHint = true, destructiveHint = false }
        },
        new
        {
            name = "validate_sql",
            description = "Validate and classify SQL without connecting to a database.",
            inputSchema = new
            {
                type = "object",
                properties = new { sql = new { type = "string", minLength = 1, description = "SQL text to validate." } },
                required = new[] { "sql" },
                additionalProperties = false
            },
            annotations = new { readOnlyHint = true, destructiveHint = false }
        },
        new
        {
            name = "query_database",
            description = "Execute a policy-checked read-only SQL query. Table blacklist and row limits are enforced by the gateway.",
            inputSchema = DataSourceSqlSchema(),
            annotations = new { readOnlyHint = true, destructiveHint = false }
        },
        new
        {
            name = "submit_change",
            description = "Submit write or DDL SQL as a pending human-approval ticket. This tool never approves or directly executes the change.",
            inputSchema = DataSourceSqlSchema(),
            annotations = new { readOnlyHint = false, destructiveHint = false }
        },
        new
        {
            name = "get_change_status",
            description = "Read the current status of a previously submitted change ticket.",
            inputSchema = new
            {
                type = "object",
                properties = new { changeId = new { type = "string", format = "uuid", description = "Approval ticket ID." } },
                required = new[] { "changeId" },
                additionalProperties = false
            },
            annotations = new { readOnlyHint = true, destructiveHint = false }
        }
    ];

    private static object DataSourceSqlSchema() => new
    {
        type = "object",
        properties = new
        {
            dataSourceId = new { type = "string", format = "uuid", description = "ID returned by list_data_sources." },
            sql = new { type = "string", minLength = 1, description = "SQL text." }
        },
        required = new[] { "dataSourceId", "sql" },
        additionalProperties = false
    };

    private static async Task<IResult> CallToolAsync(
        JsonElement? id,
        JsonElement request,
        HttpContext context,
        DataSourceService dataSources,
        QueryService queries,
        ChangeSubmissionService submissions,
        IChangeRequestRepository changes,
        CancellationToken cancellationToken)
    {
        var parameters = RequiredObject(request, "params");
        var name = RequiredString(parameters, "name");
        if (!HeaderMatches(context.Request, "Mcp-Name", name))
        {
            return Results.BadRequest(new { message = "Mcp-Name header does not match the requested tool." });
        }
        var arguments = parameters.TryGetProperty("arguments", out var argumentElement) && argumentElement.ValueKind == JsonValueKind.Object
            ? argumentElement
            : default;
        var actor = GatewayPrincipal.Actor(context.User);

        object payload = name switch
        {
            "list_data_sources" => await ListDataSourcesAsync(context, dataSources, cancellationToken),
            "validate_sql" => queries.Validate(RequiredString(arguments, "sql")),
            "query_database" => await QueryAsync(context, queries, arguments, actor, cancellationToken),
            "submit_change" => await SubmitAsync(context, submissions, arguments, actor, cancellationToken),
            "get_change_status" => await GetChangeStatusAsync(context, changes, arguments, cancellationToken),
            _ => throw new ArgumentException($"Unknown tool '{name}'.")
        };
        return RpcResult(id, ToolSuccess(payload));
    }

    private static async Task<object> ListDataSourcesAsync(HttpContext context, DataSourceService service, CancellationToken cancellationToken)
    {
        Demand(context, GatewayScopes.DataSourceRead);
        var items = await service.ListAsync(cancellationToken);
        return new
        {
            items = items.Where(item => item.Enabled).Select(item => new
            {
                item.Id,
                item.Key,
                item.Name,
                provider = item.Provider.ToString(),
                accessMode = item.AccessMode.ToString(),
                item.MaxRows,
                item.BlockedTables
            })
        };
    }

    private static async Task<object> QueryAsync(HttpContext context, QueryService service, JsonElement arguments, string actor, CancellationToken cancellationToken)
    {
        Demand(context, GatewayScopes.QueryExecute);
        return await service.ExecuteReadAsync(RequiredGuid(arguments, "dataSourceId"), RequiredString(arguments, "sql"), actor, cancellationToken);
    }

    private static async Task<object> SubmitAsync(HttpContext context, ChangeSubmissionService service, JsonElement arguments, string actor, CancellationToken cancellationToken)
    {
        Demand(context, GatewayScopes.ChangeSubmit);
        return await service.SubmitAsync(RequiredGuid(arguments, "dataSourceId"), RequiredString(arguments, "sql"), actor, cancellationToken);
    }

    private static async Task<object> GetChangeStatusAsync(HttpContext context, IChangeRequestRepository changes, JsonElement arguments, CancellationToken cancellationToken)
    {
        Demand(context, GatewayScopes.ChangeSubmit);
        var change = await changes.FindAsync(RequiredGuid(arguments, "changeId"), cancellationToken)
            ?? throw new KeyNotFoundException("Change request was not found.");
        var status = change.Status == ChangeStatus.Pending && change.ExpiresAtUtc <= DateTimeOffset.UtcNow
            ? ChangeStatus.Expired
            : change.Status;
        return new
        {
            change.Id,
            status = status.ToString(),
            change.RiskLevel,
            change.CreatedAtUtc,
            change.ExpiresAtUtc,
            change.ReviewedAtUtc,
            change.ExecutedAtUtc,
            change.ExecutionError
        };
    }

    private static void Demand(HttpContext context, string scope)
    {
        if (!GatewayPrincipal.Can(context.User, scope))
        {
            throw new InvalidOperationException($"The access token does not grant '{scope}'.");
        }
    }

    private static object ToolSuccess(object payload) => new
    {
        content = new[] { new { type = "text", text = JsonSerializer.Serialize(payload, JsonOptions) } },
        structuredContent = payload,
        isError = false
    };

    private static object ToolError(string message) => new
    {
        content = new[] { new { type = "text", text = message } },
        isError = true
    };

    private static JsonElement RequiredObject(JsonElement parent, string propertyName)
    {
        if (!parent.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException($"'{propertyName}' must be an object.");
        }
        return value;
    }

    private static string RequiredString(JsonElement parent, string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(propertyName, out var value) ||
            value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new ArgumentException($"'{propertyName}' is required.");
        }
        return value.GetString()!;
    }

    private static Guid RequiredGuid(JsonElement parent, string propertyName) =>
        Guid.TryParse(RequiredString(parent, propertyName), out var value)
            ? value
            : throw new FormatException($"'{propertyName}' must be a UUID.");

    private static bool HeaderMatches(HttpRequest request, string headerName, string expected)
    {
        var actual = request.Headers[headerName].ToString();
        return string.IsNullOrWhiteSpace(actual) || string.Equals(actual, expected, StringComparison.Ordinal);
    }

    private static IResult RpcResult(JsonElement? id, object result) => Results.Json(new { jsonrpc = "2.0", id, result });

    private static IResult RpcError(JsonElement? id, int code, string message, object? data = null) =>
        Results.Json(new { jsonrpc = "2.0", id, error = new { code, message, data } });
}
