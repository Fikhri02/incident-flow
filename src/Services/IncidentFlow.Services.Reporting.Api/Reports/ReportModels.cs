namespace IncidentFlow.Services.Reporting.Api.Reports;

/// <summary>Same shape and JSON names as the prototype's report (IncidentFlow.Core IncidentSummaryReport).</summary>
public sealed record IncidentSummaryReport(
    int TotalIncidents,
    int OpenIncidents,
    int ResolvedIncidents,
    double MeanTimeToAcknowledgeMinutes,
    double MeanTimeToResolveMinutes,
    IReadOnlyDictionary<string, int> SeverityBreakdown,
    IReadOnlyDictionary<string, int> TopFailingServices,
    IReadOnlyDictionary<string, int> DeploymentRegressionStats);

/// <summary>Incidents and mean response times for one service in one environment.</summary>
public sealed record ServiceReliabilityRow(
    string ServiceName,
    string? Environment,
    int Incidents,
    double MeanTimeToAcknowledgeMinutes,
    double MeanTimeToResolveMinutes);

/// <summary>Optional, case-insensitive narrowing of every report.</summary>
public sealed record ReportFilter(string? ServiceName = null, string? Environment = null);
