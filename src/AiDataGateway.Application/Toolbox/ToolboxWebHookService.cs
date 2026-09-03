using System.Security.Cryptography;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Domain.Toolbox;

namespace AiDataGateway.Application.Toolbox;

public sealed class ToolboxWebHookService(IToolboxWebHookRepository webHooks)
{
    public const int MaxBodyCharacters = 262144;
    private const int DeliveryListLimit = 500;

    public async Task<IReadOnlyList<WebHookView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var hooks = await webHooks.ListAsync(cancellationToken);
        var views = new List<WebHookView>();
        foreach (var hook in hooks.OrderByDescending(item => item.CreatedAtUtc))
        {
            views.Add(new WebHookView(hook.Id, hook.Name, hook.Token, hook.Description, hook.Enabled,
                hook.CreatedAtUtc, await webHooks.CountDeliveriesAsync(hook.Id, cancellationToken)));
        }

        return views;
    }

    public async Task<WebHookView> CreateAsync(string name, string description, CancellationToken cancellationToken = default)
    {
        var trimmedName = RequireName(name);
        var hook = new WebHookDefinition
        {
            Name = trimmedName,
            Description = description?.Trim() ?? string.Empty,
            Token = RandomNumberGenerator.GetHexString(16),
        };
        await webHooks.AddAsync(hook, cancellationToken);
        await webHooks.SaveChangesAsync(cancellationToken);
        return new WebHookView(hook.Id, hook.Name, hook.Token, hook.Description, hook.Enabled, hook.CreatedAtUtc, 0);
    }

    public async Task<WebHookView> UpdateAsync(Guid id, string name, string description, bool enabled, CancellationToken cancellationToken = default)
    {
        var hook = await webHooks.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("WebHook was not found.");
        hook.Name = RequireName(name);
        hook.Description = description?.Trim() ?? string.Empty;
        hook.Enabled = enabled;
        await webHooks.SaveChangesAsync(cancellationToken);
        return new WebHookView(hook.Id, hook.Name, hook.Token, hook.Description, hook.Enabled, hook.CreatedAtUtc,
            await webHooks.CountDeliveriesAsync(hook.Id, cancellationToken));
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var hook = await webHooks.FindAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("WebHook was not found.");
        await webHooks.DeleteAsync(hook, cancellationToken);
    }

    public async Task ClearDeliveriesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await webHooks.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("WebHook was not found.");
        await webHooks.ClearDeliveriesAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyList<WebHookDeliveryView>> ListDeliveriesAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _ = await webHooks.FindAsync(id, cancellationToken) ?? throw new KeyNotFoundException("WebHook was not found.");
        var deliveries = await webHooks.ListDeliveriesAsync(id, DeliveryListLimit, cancellationToken);
        return deliveries.Select(item => new WebHookDeliveryView(item.Id, item.ReceivedAtUtc, item.Method,
            item.QueryString, item.ContentType, item.HeadersJson, item.Body, item.BodyTruncated)).ToArray();
    }

    public async Task<bool> IngestAsync(string token, string method, string queryString, string contentType,
        string headersJson, string body, CancellationToken cancellationToken = default)
    {
        var hook = await webHooks.FindByTokenAsync(token, cancellationToken);
        if (hook is null || !hook.Enabled) return false;

        var truncated = body.Length > MaxBodyCharacters;
        var delivery = new WebHookDelivery
        {
            WebHookId = hook.Id,
            ReceivedAtUtc = DateTimeOffset.UtcNow,
            Method = method,
            QueryString = queryString ?? string.Empty,
            ContentType = contentType ?? string.Empty,
            HeadersJson = string.IsNullOrWhiteSpace(headersJson) ? "{}" : headersJson,
            Body = truncated ? body[..MaxBodyCharacters] : body,
            BodyTruncated = truncated,
        };
        await webHooks.AddDeliveryAsync(delivery, cancellationToken);
        return true;
    }

    private static string RequireName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        return trimmed.Length > 0 ? trimmed[..Math.Min(trimmed.Length, 100)] : throw new ArgumentException("WebHook 名称不能为空。");
    }
}
