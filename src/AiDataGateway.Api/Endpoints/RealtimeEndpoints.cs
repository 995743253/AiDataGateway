using System.Text.Json;
using AiDataGateway.Api.Realtime;
using Microsoft.AspNetCore.Authorization;

namespace AiDataGateway.Api.Endpoints;

internal static class RealtimeEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/events", [Authorize] async (HttpContext context, GatewayEventHub eventHub) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache, no-store";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.Headers["X-Accel-Buffering"] = "no";
            await context.Response.WriteAsync("retry: 3000\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            try
            {
                await foreach (var gatewayEvent in eventHub.Subscribe(context.RequestAborted))
                {
                    var json = JsonSerializer.Serialize(gatewayEvent, JsonSerializerOptions.Web);
                    await context.Response.WriteAsync($"event: gateway-change\ndata: {json}\n\n", context.RequestAborted);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                }
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The browser closed or reconnected the event stream.
            }
        });

        return endpoints;
    }
}
