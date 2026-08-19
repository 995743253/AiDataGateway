using AiDataGateway.Application.DataSources;
using AiDataGateway.Application.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace AiDataGateway.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<DataSourceService>();
        services.AddScoped<QueryService>();
        services.AddSingleton<ISqlSafetyAnalyzer, SqlSafetyAnalyzer>();
        return services;
    }
}
