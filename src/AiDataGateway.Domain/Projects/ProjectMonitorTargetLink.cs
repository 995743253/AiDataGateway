namespace AiDataGateway.Domain.Projects;

public sealed class ProjectMonitorTargetLink
{
    private ProjectMonitorTargetLink()
    {
    }

    public ProjectMonitorTargetLink(Guid projectId, Guid monitorTargetId)
    {
        ProjectId = projectId;
        MonitorTargetId = monitorTargetId;
    }

    public Guid ProjectId { get; private set; }
    public Guid MonitorTargetId { get; private set; }
}
