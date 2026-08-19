using AiDataGateway.Domain.Sql;

namespace AiDataGateway.Application.Sql;

public sealed record SqlAnalysis(
    bool Allowed,
    bool IsReadOnly,
    SqlRiskLevel RiskLevel,
    string Operation,
    IReadOnlyList<string> Reasons);

public interface ISqlSafetyAnalyzer
{
    SqlAnalysis Analyze(string sql);
}
