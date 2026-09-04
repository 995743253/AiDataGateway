using AiDataGateway.Extensions;

namespace AiDataGateway.Infrastructure.Extensions;

public sealed record GatewayExtensionModuleView(
    string Id,
    string Name,
    string Version,
    string Description,
    bool Enabled,
    bool Loaded,
    string? LoadError,
    string? PageTitle,
    string? PageUrl,
    DateTimeOffset InstalledAtUtc,
    IReadOnlyList<GatewayExtensionToolView> Tools);

public sealed record GatewayExtensionToolView(
    string Name,
    string PublicName,
    string Description,
    System.Text.Json.JsonElement InputSchema,
    GatewayExtensionCapability Capability,
    bool VisibleInUi,
    bool ReadOnly);
