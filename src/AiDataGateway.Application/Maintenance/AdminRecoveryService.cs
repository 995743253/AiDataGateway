using System.Security.Cryptography;
using System.Text;
using AiDataGateway.Application.Abstractions;

namespace AiDataGateway.Application.Maintenance;

public sealed class AdminRecoveryService(
    IMaintenanceSettingsRepository repository,
    ICredentialProtector protector,
    IAuditWriter auditWriter)
{
    private const string DefaultResetPassword = "admin";

    public async Task<AdminRecoveryStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await repository.GetAsync(cancellationToken);
        return new AdminRecoveryStatus(string.IsNullOrEmpty(settings.ProtectedAdminResetPassword));
    }

    public async Task<bool> VerifyAsync(string password, string actor, CancellationToken cancellationToken = default)
    {
        var settings = await repository.GetAsync(cancellationToken);
        var expected = string.IsNullOrEmpty(settings.ProtectedAdminResetPassword)
            ? DefaultResetPassword
            : protector.Unprotect(settings.ProtectedAdminResetPassword);
        var success = FixedTimeEquals(expected, password ?? string.Empty);
        await auditWriter.WriteAsync(actor, "auth.admin-password-reset.verify", success ? "success" : "failure",
            detail: success ? "recovery-password-valid" : "invalid-recovery-password", cancellationToken: cancellationToken);
        return success;
    }

    public async Task<AdminRecoveryStatus> UpdateAsync(string newPassword, string actor, CancellationToken cancellationToken = default)
    {
        var normalized = newPassword?.Trim() ?? string.Empty;
        if (normalized.Length is < 4 or > 128)
            throw new ArgumentException("管理员重置口令长度必须为 4 到 128 位。", nameof(newPassword));
        var settings = await repository.GetAsync(cancellationToken);
        settings.SetProtectedAdminResetPassword(protector.Protect(normalized));
        await repository.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(actor, "settings.admin-recovery.update", "success",
            detail: "administrator recovery password updated", cancellationToken: cancellationToken);
        return new AdminRecoveryStatus(false);
    }

    private static bool FixedTimeEquals(string expected, string provided)
    {
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }
}

public sealed record AdminRecoveryStatus(bool UsesDefaultPassword);
