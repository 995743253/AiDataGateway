using AiDataGateway.Application.Toolbox;
using Microsoft.AspNetCore.Authorization;

namespace AiDataGateway.Api.Endpoints;

internal static class ToolboxEndpoints
{
    public static IEndpointRouteBuilder MapToolboxEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var webhooks = endpoints.MapGroup("/api/toolbox/webhooks").RequireAuthorization();
        webhooks.MapGet("/", ListAsync);
        webhooks.MapPost("/", CreateAsync);
        webhooks.MapPut("/{id:guid}", UpdateAsync);
        webhooks.MapDelete("/{id:guid}", DeleteAsync);
        webhooks.MapGet("/{id:guid}/deliveries", ListDeliveriesAsync);
        webhooks.MapPost("/{id:guid}/deliveries/clear", ClearDeliveriesAsync);

        // Anonymous by design: external systems push test payloads here.
        endpoints.MapPost("/toolbox/hook/{token}", IngestAsync).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> ListAsync(ToolboxWebHookService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListAsync(cancellationToken));

    private static async Task<IResult> CreateAsync(CreateWebHookRequest request, ToolboxWebHookService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.CreateAsync(request.Name, request.Description, cancellationToken));

    private static async Task<IResult> UpdateAsync(Guid id, UpdateWebHookRequest request, ToolboxWebHookService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.UpdateAsync(id, request.Name, request.Description, request.Enabled, cancellationToken));

    private static async Task<IResult> DeleteAsync(Guid id, ToolboxWebHookService service, CancellationToken cancellationToken)
    {
        await service.DeleteAsync(id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListDeliveriesAsync(Guid id, ToolboxWebHookService service, CancellationToken cancellationToken) =>
        Results.Ok(await service.ListDeliveriesAsync(id, cancellationToken));

    private static async Task<IResult> ClearDeliveriesAsync(Guid id, ToolboxWebHookService service, CancellationToken cancellationToken)
    {
        await service.ClearDeliveriesAsync(id, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> IngestAsync(string token, HttpContext context, ToolboxWebHookService service, CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength > 8 * 1024 * 1024)
        {
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        string body;
        using (var reader = new StreamReader(context.Request.Body))
        {
            body = await reader.ReadToEndAsync(cancellationToken);
        }

        var headers = System.Text.Json.JsonSerializer.Serialize(
            context.Request.Headers.ToDictionary(item => item.Key, item => item.Value.ToString()));
        var accepted = await service.IngestAsync(token, context.Request.Method,
            context.Request.QueryString.Value ?? string.Empty,
            context.Request.ContentType ?? string.Empty, headers, body, cancellationToken);
        return accepted
            ? Results.Json(new { accepted = true }, statusCode: StatusCodes.Status202Accepted)
            : Results.NotFound(new { accepted = false });
    }
}
