namespace AiDataGateway.Domain.Projects;

public sealed class ProjectDataSourceLink
{
    private ProjectDataSourceLink()
    {
    }

    public ProjectDataSourceLink(Guid projectId, Guid dataSourceId)
    {
        ProjectId = projectId;
        DataSourceId = dataSourceId;
    }

    public Guid ProjectId { get; private set; }
    public Guid DataSourceId { get; private set; }
}
