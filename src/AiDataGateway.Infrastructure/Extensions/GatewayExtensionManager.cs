using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AiDataGateway.Application.Abstractions;
using AiDataGateway.Application.Monitoring;
using AiDataGateway.Application.Projects;
using AiDataGateway.Application.Sql;
using AiDataGateway.Application.Logs;
using AiDataGateway.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AiDataGateway.Infrastructure.Extensions;

public sealed class GatewayExtensionManager : IDisposable
{
    public const string ManifestFileName = "gateway-extension.json";
    private const long MaximumPackageBytes = 100 * 1024 * 1024;
    private static readonly Regex SafeId = new("^[a-z][a-z0-9.-]{2,79}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SafeOperation = new("^[a-z][a-z0-9_]{1,63}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly string _root;
    private readonly string _registryPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, RuntimeModule> _modules = new(StringComparer.OrdinalIgnoreCase);
    private List<GatewayExtensionRegistryEntry> _registry = [];

    public GatewayExtensionManager(IOptions<GatewayStorageOptions> storage, IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _root = Path.Combine(storage.Value.BasePath, "extensions");
        _registryPath = Path.Combine(_root, "registry.json");
        Directory.CreateDirectory(_root);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var registry = await ReadRegistryAsync(cancellationToken);
            var modules = registry.Where(item => item.Enabled)
                .ToDictionary(item => item.Id, Load, StringComparer.OrdinalIgnoreCase);
            lock (_modules)
            {
                foreach (var current in _modules.Values) current.Dispose();
                _modules.Clear();
                foreach (var pair in modules) _modules[pair.Key] = pair.Value;
                _registry = registry;
            }
        }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<GatewayExtensionModuleView> List()
    {
        lock (_modules)
        {
            return _registry.Select(entry => ToView(entry, _modules.GetValueOrDefault(entry.Id)))
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public GatewayExtensionToolView? FindTool(string publicName) => List()
        .Where(module => module.Enabled && module.Loaded)
        .SelectMany(module => module.Tools)
        .FirstOrDefault(tool => string.Equals(tool.PublicName, publicName, StringComparison.Ordinal));

    public async Task<GatewayExtensionModuleView> InstallAsync(Stream package, string actor, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        var staging = Path.Combine(_root, ".staging-" + Guid.NewGuid().ToString("N"));
        string? installedDirectory = null;
        try
        {
            Directory.CreateDirectory(staging);
            await ExtractPackageAsync(package, staging, cancellationToken);
            var manifest = await ReadManifestAsync(staging, cancellationToken);
            ValidateManifest(manifest, staging);

            var installName = $"{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
            var moduleRoot = Path.Combine(_root, manifest.Id);
            var installDirectory = Path.Combine(moduleRoot, installName);
            Directory.CreateDirectory(moduleRoot);
            Directory.Move(staging, installDirectory);
            installedDirectory = installDirectory;

            var relativeDirectory = Path.GetRelativePath(_root, installDirectory);
            var entry = new GatewayExtensionRegistryEntry(manifest.Id, relativeDirectory, manifest.Enabled, DateTimeOffset.UtcNow);
            var oldEntry = _registry.FirstOrDefault(item => string.Equals(item.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
            var oldRuntime = _modules.GetValueOrDefault(manifest.Id);
            var validatedRuntime = Load(entry);
            if (validatedRuntime.Instance is null)
            {
                var loadError = validatedRuntime.LoadError;
                validatedRuntime.Dispose();
                throw new InvalidOperationException($"Extension failed to load: {loadError}");
            }
            var runtime = manifest.Enabled ? validatedRuntime : null;
            if (!manifest.Enabled) validatedRuntime.Dispose();

            lock (_modules)
            {
                _registry.RemoveAll(item => string.Equals(item.Id, manifest.Id, StringComparison.OrdinalIgnoreCase));
                _registry.Add(entry);
                if (runtime is null) _modules.Remove(manifest.Id); else _modules[manifest.Id] = runtime;
            }
            await WriteRegistryAsync(cancellationToken);
            oldRuntime?.Dispose();
            if (oldEntry is not null) TryDeleteDirectory(ResolveInstallDirectory(oldEntry));
            await AuditAsync(actor, "extension.install", "success", new { manifest.Id, installDirectory = relativeDirectory }, cancellationToken);
            return ToView(entry, runtime);
        }
        catch
        {
            TryDeleteDirectory(staging);
            if (installedDirectory is not null) TryDeleteDirectory(installedDirectory);
            throw;
        }
        finally { _gate.Release(); }
    }

    public async Task<GatewayExtensionModuleView> SetEnabledAsync(string id, bool enabled, string actor, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var index = _registry.FindIndex(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            if (index < 0) throw new KeyNotFoundException("Extension module was not found.");
            var entry = _registry[index] with { Enabled = enabled };
            RuntimeModule? selectedRuntime;
            if (enabled)
            {
                selectedRuntime = Load(entry);
                if (selectedRuntime.Instance is null)
                {
                    var loadError = selectedRuntime.LoadError;
                    selectedRuntime.Dispose();
                    throw new InvalidOperationException($"Extension failed to load: {loadError}");
                }
            }
            else selectedRuntime = null;
            RuntimeModule? previousRuntime;
            lock (_modules)
            {
                _registry[index] = entry;
                previousRuntime = _modules.GetValueOrDefault(entry.Id);
                if (selectedRuntime is null) _modules.Remove(entry.Id); else _modules[entry.Id] = selectedRuntime;
            }
            previousRuntime?.Dispose();
            await WriteRegistryAsync(cancellationToken);
            await AuditAsync(actor, "extension.enable", "success", new { entry.Id, enabled }, cancellationToken);
            return ToView(entry, selectedRuntime);
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(string id, string actor, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var entry = _registry.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("Extension module was not found.");
            RuntimeModule? runtime;
            lock (_modules)
            {
                _registry.Remove(entry);
                _modules.Remove(entry.Id, out runtime);
            }
            runtime?.Dispose();
            await WriteRegistryAsync(cancellationToken);
            TryDeleteDirectory(Path.Combine(_root, entry.Id));
            await AuditAsync(actor, "extension.delete", "success", new { entry.Id }, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    public async Task<JsonElement> InvokeAsync(
        string moduleId,
        string operation,
        JsonElement arguments,
        string actor,
        bool uiInvocation,
        CancellationToken cancellationToken = default)
    {
        RuntimeModule runtime;
        GatewayExtensionToolDefinition tool;
        lock (_modules)
        {
            runtime = _modules.GetValueOrDefault(moduleId) ?? throw new KeyNotFoundException("Enabled extension module was not found.");
            if (runtime.Instance is null) throw new InvalidOperationException(runtime.LoadError ?? "Extension module is not loaded.");
            tool = runtime.Instance.Definition.Tools.FirstOrDefault(item => string.Equals(item.Name, operation, StringComparison.Ordinal))
                ?? throw new KeyNotFoundException("Extension operation was not found.");
            if (uiInvocation && !tool.VisibleInUi) throw new InvalidOperationException("This extension operation is not available to the UI.");
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var context = new GatewayExtensionHostContext(actor, tool.Capability,
            scope.ServiceProvider.GetRequiredService<ProjectService>(),
            scope.ServiceProvider.GetRequiredService<QueryService>(),
            scope.ServiceProvider.GetRequiredService<LogSourceService>(),
            scope.ServiceProvider.GetRequiredService<MonitoringService>());
        var audit = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
        try
        {
            var result = await runtime.Instance.InvokeAsync(operation, arguments, context, cancellationToken);
            await audit.WriteAsync(actor, "extension.invoke", "success",
                detail: JsonSerializer.Serialize(new { moduleId, operation, tool.Capability }), cancellationToken: cancellationToken);
            return result.Clone();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await audit.WriteAsync(actor, "extension.invoke", "failure",
                detail: JsonSerializer.Serialize(new { moduleId, operation, tool.Capability, error = exception.GetBaseException().Message }), cancellationToken: cancellationToken);
            throw;
        }
    }

    public async Task<JsonElement> InvokePublicToolAsync(string publicName, JsonElement arguments, string actor, CancellationToken cancellationToken = default)
    {
        var module = List().FirstOrDefault(item => item.Enabled && item.Loaded && item.Tools.Any(tool => tool.PublicName == publicName))
            ?? throw new KeyNotFoundException("Extension MCP tool was not found.");
        var tool = module.Tools.First(item => item.PublicName == publicName);
        return await InvokeAsync(module.Id, tool.Name, arguments, actor, false, cancellationToken);
    }

    public bool TryResolveAsset(string id, string? requestedPath, out string fullPath)
    {
        fullPath = string.Empty;
        GatewayExtensionRegistryEntry? entry;
        RuntimeModule? runtime;
        lock (_modules)
        {
            entry = _registry.FirstOrDefault(item => item.Enabled && string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            runtime = _modules.GetValueOrDefault(id);
        }
        var frontendEntry = runtime?.Instance?.Definition.FrontendEntry;
        if (entry is null || runtime?.Instance is null || string.IsNullOrWhiteSpace(frontendEntry)) return false;

        var installDirectory = ResolveInstallDirectory(entry);
        var frontendRoot = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(installDirectory, frontendEntry)))!;
        var relativePath = string.IsNullOrWhiteSpace(requestedPath) ? Path.GetFileName(frontendEntry) : requestedPath.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(frontendRoot, relativePath));
        if (!candidate.StartsWith(frontendRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(candidate, frontendRoot, StringComparison.OrdinalIgnoreCase)) return false;
        if (!File.Exists(candidate)) return false;
        fullPath = candidate;
        return true;
    }

    public void Dispose()
    {
        lock (_modules)
        {
            foreach (var runtime in _modules.Values) runtime.Dispose();
            _modules.Clear();
        }
        _gate.Dispose();
    }

    private RuntimeModule Load(GatewayExtensionRegistryEntry entry)
    {
        GatewayExtensionLoadContext? loadContext = null;
        try
        {
            var installDirectory = ResolveInstallDirectory(entry);
            var manifest = ReadManifestAsync(installDirectory, CancellationToken.None).GetAwaiter().GetResult();
            ValidateManifest(manifest, installDirectory);
            var assemblyPath = Path.GetFullPath(Path.Combine(installDirectory, manifest.EntryAssembly));
            loadContext = new GatewayExtensionLoadContext(assemblyPath);
            var assembly = loadContext.LoadManagedAssembly(assemblyPath);
            var type = assembly.GetType(manifest.EntryType, throwOnError: true, ignoreCase: false)!;
            if (!typeof(IGatewayExtension).IsAssignableFrom(type)) throw new InvalidOperationException($"Entry type '{manifest.EntryType}' does not implement IGatewayExtension.");
            var instance = Activator.CreateInstance(type) as IGatewayExtension ?? throw new InvalidOperationException("Extension entry type requires a public parameterless constructor.");
            ValidateDefinition(manifest, instance.Definition, installDirectory);
            return new RuntimeModule(loadContext, instance, null);
        }
        catch (Exception exception)
        {
            loadContext?.Unload();
            return new RuntimeModule(null, null, exception.GetBaseException().Message);
        }
    }

    private static void ValidateDefinition(GatewayExtensionManifest manifest, GatewayExtensionDefinition definition, string installDirectory)
    {
        if (!string.Equals(manifest.Id, definition.Id, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Manifest ID and extension definition ID do not match.");
        if (string.IsNullOrWhiteSpace(definition.Name) || string.IsNullOrWhiteSpace(definition.Version)) throw new InvalidOperationException("Extension name and version are required.");
        if (definition.Tools.Count > 50) throw new InvalidOperationException("An extension cannot register more than 50 tools.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in definition.Tools)
        {
            if (!SafeOperation.IsMatch(tool.Name) || !names.Add(tool.Name)) throw new InvalidOperationException($"Invalid or duplicate extension tool name '{tool.Name}'.");
            if (string.IsNullOrWhiteSpace(tool.Description)) throw new InvalidOperationException($"Tool '{tool.Name}' requires a description.");
            if (tool.InputSchema.ValueKind != JsonValueKind.Object) throw new InvalidOperationException($"Tool '{tool.Name}' requires an object JSON Schema.");
        }
        if (!string.IsNullOrWhiteSpace(definition.FrontendEntry)) EnsureContainedFile(installDirectory, definition.FrontendEntry);
    }

    private static void ValidateManifest(GatewayExtensionManifest manifest, string installDirectory)
    {
        if (!SafeId.IsMatch(manifest.Id ?? string.Empty)) throw new InvalidOperationException("Extension ID must start with a letter and contain only lowercase letters, numbers, dots or hyphens.");
        if (manifest.ContractVersion != GatewayExtensionContract.Version) throw new InvalidOperationException($"Unsupported extension contract version {manifest.ContractVersion}; this gateway supports version {GatewayExtensionContract.Version}.");
        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly) || string.IsNullOrWhiteSpace(manifest.EntryType)) throw new InvalidOperationException("Entry assembly and entry type are required.");
        EnsureContainedFile(installDirectory, manifest.EntryAssembly);
    }

    private static void EnsureContainedFile(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidOperationException($"Package file '{relativePath}' is missing or outside the package.");
    }

    private async Task<GatewayExtensionManifest> ReadManifestAsync(string directory, CancellationToken cancellationToken)
    {
        var path = Path.Combine(directory, ManifestFileName);
        if (!File.Exists(path)) throw new InvalidOperationException($"Package root must contain {ManifestFileName}.");
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<GatewayExtensionManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Extension manifest is invalid.");
    }

    private static async Task ExtractPackageAsync(Stream package, string staging, CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count > 2_000 || archive.Entries.Sum(item => item.Length) > MaximumPackageBytes)
            throw new InvalidOperationException("Extension package is too large.");
        var root = Path.GetFullPath(staging) + Path.DirectorySeparatorChar;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = Path.GetFullPath(Path.Combine(staging, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Extension package contains an unsafe path.");
            if (string.IsNullOrEmpty(entry.Name)) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private async Task<List<GatewayExtensionRegistryEntry>> ReadRegistryAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_registryPath)) return [];
        await using var stream = File.OpenRead(_registryPath);
        return (await JsonSerializer.DeserializeAsync<GatewayExtensionRegistry>(stream, JsonOptions, cancellationToken))?.Modules.ToList() ?? [];
    }

    private async Task WriteRegistryAsync(CancellationToken cancellationToken)
    {
        var temporary = _registryPath + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            await JsonSerializer.SerializeAsync(stream, new GatewayExtensionRegistry(_registry), JsonOptions, cancellationToken);
        File.Move(temporary, _registryPath, true);
    }

    private GatewayExtensionModuleView ToView(GatewayExtensionRegistryEntry entry, RuntimeModule? runtime)
    {
        var definition = runtime?.Instance?.Definition;
        var tools = definition?.Tools.Select(tool => new GatewayExtensionToolView(tool.Name, PublicToolName(entry.Id, tool.Name),
            tool.Description, tool.InputSchema.Clone(), tool.Capability, tool.VisibleInUi, tool.ReadOnly)).ToArray() ?? [];
        return new GatewayExtensionModuleView(entry.Id, definition?.Name ?? entry.Id, definition?.Version ?? "—",
            definition?.Description ?? string.Empty, entry.Enabled, runtime?.Instance is not null, runtime?.LoadError,
            definition?.PageTitle, string.IsNullOrWhiteSpace(definition?.FrontendEntry) ? null : $"/custom-modules/{entry.Id}/ui/",
            entry.InstalledAtUtc, tools);
    }

    private string ResolveInstallDirectory(GatewayExtensionRegistryEntry entry)
    {
        var fullRoot = Path.GetFullPath(_root) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(Path.Combine(_root, entry.InstallDirectory));
        if (!directory.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Extension registry contains an unsafe path.");
        return directory;
    }

    private static string PublicToolName(string moduleId, string operation) =>
        "custom_" + Regex.Replace(moduleId, "[^a-z0-9]", "_") + "_" + operation;

    private async Task AuditAsync(string actor, string action, string outcome, object detail, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAuditWriter>().WriteAsync(actor, action, outcome,
            detail: JsonSerializer.Serialize(detail), cancellationToken: cancellationToken);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed class RuntimeModule(GatewayExtensionLoadContext? loadContext, IGatewayExtension? instance, string? loadError) : IDisposable
    {
        public IGatewayExtension? Instance { get; } = instance;
        public string? LoadError { get; } = loadError;
        public void Dispose() { loadContext?.Unload(); }
    }
}
