using AiDataGateway.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;

namespace AiDataGateway.Infrastructure.Security;

internal sealed class DataProtectionCredentialProtector(IDataProtectionProvider provider) : ICredentialProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("AiDataGateway.DatabaseCredentials.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
