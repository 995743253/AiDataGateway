namespace AiDataGateway.Domain.Toolbox;

public sealed class WebHookDefinition
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
}

public sealed class WebHookDelivery
{
    public long Id { get; set; }
    public Guid WebHookId { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Method { get; set; } = "POST";
    public string QueryString { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string HeadersJson { get; set; } = "{}";
    public string Body { get; set; } = string.Empty;
    public bool BodyTruncated { get; set; }
}
