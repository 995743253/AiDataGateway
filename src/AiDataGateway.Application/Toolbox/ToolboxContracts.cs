namespace AiDataGateway.Application.Toolbox;

public sealed record WebHookView(
    Guid Id,
    string Name,
    string Token,
    string Description,
    bool Enabled,
    DateTimeOffset CreatedAtUtc,
    int DeliveryCount);

public sealed record WebHookDeliveryView(
    long Id,
    DateTimeOffset ReceivedAtUtc,
    string Method,
    string QueryString,
    string ContentType,
    string HeadersJson,
    string Body,
    bool BodyTruncated);

public sealed record CreateWebHookRequest(string Name, string Description);

public sealed record UpdateWebHookRequest(string Name, string Description, bool Enabled);
