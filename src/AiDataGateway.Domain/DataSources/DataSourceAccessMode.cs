namespace AiDataGateway.Domain.DataSources;

public enum DataSourceAccessMode
{
    Disabled = 0,
    ReadOnly = 1,
    ReadWriteWithApproval = 2,
    Development = 3
}
