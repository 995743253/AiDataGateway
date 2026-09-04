using AiDataGateway.Extensions;

namespace AiDataGateway.Infrastructure.Extensions;

internal sealed record GatewayExtensionManifest(
    string Id,
    string EntryAssembly,
    string EntryType,
    bool Enabled = true,
    int ContractVersion = GatewayExtensionContract.Version);

internal sealed record GatewayExtensionRegistry(IReadOnlyList<GatewayExtensionRegistryEntry> Modules);

internal sealed record GatewayExtensionRegistryEntry(
    string Id,
    string InstallDirectory,
    bool Enabled,
    DateTimeOffset InstalledAtUtc);
