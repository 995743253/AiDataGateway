using AiDataGateway.Application.DataSources;
using AiDataGateway.Application.Approvals;
using AiDataGateway.Application.Maintenance;
using AiDataGateway.Application.Sql;
using AiDataGateway.Application.Projects;
using AiDataGateway.Application.Logs;
using AiDataGateway.Application.Toolbox;
using AiDataGateway.Application.Monitoring;
using Microsoft.Extensions.DependencyInjection;

namespace AiDataGateway.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DataSourceService>();
        services.AddScoped<QueryService>();
        services.AddScoped<MaintenanceService>();
        services.AddScoped<ChangeSubmissionService>();
        services.AddScoped<ProjectService>();
        services.AddScoped<LogSourceService>();
        services.AddScoped<ToolboxWebHookService>();
        services.AddScoped<LogSqlTraceService>();
        services.AddScoped<MonitoringService>();
        services.AddSingleton<ISqlSafetyAnalyzer, SqlSafetyAnalyzer>();
        services.AddSingleton<ISqlTableAccessGuard, SqlTableAccessGuard>();
        return services;
    }
}
