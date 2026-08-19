namespace AiDataGateway.Application.Abstractions;

public interface ICredentialProtector
{
    string Protect(string plaintext);
    string Unprotect(string protectedValue);
}
