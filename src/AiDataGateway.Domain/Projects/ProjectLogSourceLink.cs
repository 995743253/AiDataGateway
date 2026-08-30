namespace AiDataGateway.Domain.Projects;

public sealed class ProjectLogSourceLink
{
    private ProjectLogSourceLink()
    {
    }

    public ProjectLogSourceLink(Guid projectId, Guid logSourceId)
    {
        ProjectId = projectId;
        LogSourceId = logSourceId;
    }

    public Guid ProjectId { get; private set; }
    public Guid LogSourceId { get; private set; }
}
