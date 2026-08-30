namespace AiDataGateway.Domain.Projects;

public sealed class ProjectDefinition
{
    private ProjectDefinition()
    {
    }

    public ProjectDefinition(string code, string name, string? description, bool enabled = true)
    {
        Id = Guid.NewGuid();
        CreatedAtUtc = DateTimeOffset.UtcNow;
        Update(code, name, description, enabled);
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public bool Enabled { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string code, string name, string? description, bool enabled)
    {
        Code = Require(code, nameof(code), 100).ToLowerInvariant();
        Name = Require(name, nameof(name), 200);
        Description = (description ?? string.Empty).Trim();
        if (Description.Length > 2_000)
        {
            throw new ArgumentException("Project description cannot exceed 2000 characters.", nameof(description));
        }

        Enabled = enabled;
        UpdatedAtUtc = DateTimeOffset.UtcNow;
    }

    private static string Require(string value, string parameterName, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength
            ? trimmed
            : throw new ArgumentException($"Value cannot exceed {maxLength} characters.", parameterName);
    }
}
