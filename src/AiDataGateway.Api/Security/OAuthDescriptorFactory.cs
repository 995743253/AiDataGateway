using AiDataGateway.Application.Security;
using OpenIddict.Abstractions;

namespace AiDataGateway.Api.Security;

internal static class OAuthDescriptorFactory
{
    public static OpenIddictApplicationDescriptor CreateAiClient(string clientId, string displayName, string secret, IEnumerable<string>? scopes = null)
    {
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            DisplayName = displayName,
            ClientType = OpenIddictConstants.ClientTypes.Confidential,
            ConsentType = OpenIddictConstants.ConsentTypes.Implicit
        };

        descriptor.Permissions.Add(OpenIddictConstants.Permissions.Endpoints.Token);
        descriptor.Permissions.Add(OpenIddictConstants.Permissions.GrantTypes.ClientCredentials);
        foreach (var scope in scopes ?? GatewayScopes.AiClientDefaults)
        {
            descriptor.Permissions.Add(OpenIddictConstants.Permissions.Prefixes.Scope + scope);
        }

        return descriptor;
    }
}
