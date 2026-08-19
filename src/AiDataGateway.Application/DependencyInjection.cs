using AiDataGateway.Application.DataSources;
using AiDataGateway.Application.Maintenance;
using AiDataGateway.Application.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace AiDataGateway.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DataSourceService>();
        services.AddScoped<QueryService>();
        services.AddScoped<MaintenanceService>();
        services.AddSingleton<ISqlSafetyAnalyzer, SqlSafetyAnalyzer>();
        services.AddSingleton<ISqlTableAccessGuard, SqlTableAccessGuard>();
        return services;
    }
}
