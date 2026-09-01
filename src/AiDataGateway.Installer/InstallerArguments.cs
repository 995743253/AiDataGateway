namespace AiDataGateway.Installer;

internal sealed record InstallerArguments(
    bool Silent,
    bool Verify,
    bool Update,
    bool Uninstall,
    bool Launch,
    int? WaitPid,
    string? InstallPath,
    string? DataPath)
{
    public static InstallerArguments Parse(IReadOnlyList<string> args)
    {
        string? Value(string name)
        {
            var index = args.ToList().FindIndex(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index >= 0 && index + 1 < args.Count ? args[index + 1] : null;
        }

        return new InstallerArguments(
            args.Any(item => item.Equals("--silent", StringComparison.OrdinalIgnoreCase)),
            args.Any(item => item.Equals("--verify", StringComparison.OrdinalIgnoreCase)),
            args.Any(item => item.Equals("--update", StringComparison.OrdinalIgnoreCase)),
            args.Any(item => item.Equals("--uninstall", StringComparison.OrdinalIgnoreCase)),
            args.Any(item => item.Equals("--launch", StringComparison.OrdinalIgnoreCase)),
            int.TryParse(Value("--wait-pid"), out var pid) ? pid : null,
            Value("--install-dir"),
            Value("--data-dir"));
    }
}
