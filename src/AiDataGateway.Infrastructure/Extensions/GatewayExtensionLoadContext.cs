using System.Reflection;
using System.Runtime.Loader;
using AiDataGateway.Extensions;

namespace AiDataGateway.Infrastructure.Extensions;

internal sealed class GatewayExtensionLoadContext(string entryAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(entryAssemblyPath);

    public Assembly LoadManagedAssembly(string path)
    {
        // Loading from a byte stream avoids holding a Windows file lock for the lifetime of
        // the extension, so an administrator can upgrade or remove a module without restart.
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var image = new MemoryStream();
        file.CopyTo(image);
        image.Position = 0;
        return LoadFromStream(image);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (string.Equals(assemblyName.Name, typeof(IGatewayExtension).Assembly.GetName().Name, StringComparison.Ordinal))
            return null;

        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadManagedAssembly(path);
    }

    protected override nint LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? nint.Zero : LoadUnmanagedDllFromPath(path);
    }
}
