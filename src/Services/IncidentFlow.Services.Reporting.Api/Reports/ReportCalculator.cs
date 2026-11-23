using IncidentFlow.Services.Reporting.Api.Models;

namespace IncidentFlow.Services.Reporting.Api.Reports;

/// <summary>
/// The report figures, with the prototype's definitions: open = neither resolved nor closed; MTTA and MTTR
/// are the mean minutes from detection to acknowledged / resolved over the incidents that have one (0 when
/// none do); top failing services are the ten with most incidents. Deployment regressions are the incidents
/// a deployment CorrelationFound named, per service. Dictionaries are ordered by count, then name.
/// </summary>
public static class ReportCalculator
{
    public const int TopFailingServicesCount = 10;

    public static IncidentSummaryReport Summary(IReadOnlyCollection<IncidentReportRow> incidents) => new(
        incidents.Count,
        incidents.Count(i => i.ResolvedAt is null && i.ClosedAt is null),
        incidents.Count(i => i.ResolvedAt is not null),
        MeanMinutes(incidents, i => i.AcknowledgedAt),
        MeanMinutes(incidents, i => i.ResolvedAt),
        CountBy(incidents, i => i.Severity),
        CountBy(incidents, i => i.ServiceName, TopFailingServicesCount),
        CountBy(incidents.Where(i => i.DeploymentLinked), i => i.ServiceName));

    public static IReadOnlyList<ServiceReliabilityRow> ServiceReliability(IReadOnlyCollection<IncidentReportRow> incidents) =>
        incidents
            .GroupBy(i => (Service: i.ServiceName ?? string.Empty, i.Environment))
            .Select(g => new ServiceReliabilityRow(
                g.Key.Service,
                g.Key.Environment,
                g.Count(),
                MeanMinutes(g, i => i.AcknowledgedAt),
                MeanMinutes(g, i => i.ResolvedAt)))
            .OrderByDescending(r => r.Incidents)
            .ThenBy(r => r.ServiceName, StringComparer.Ordinal)
            .ThenBy(r => r.Environment, StringComparer.Ordinal)
            .ToList();

    private static double MeanMinutes(IEnumerable<IncidentReportRow> incidents, Func<IncidentReportRow, DateTimeOffset?> reached)
    {
        var minutes = incidents
            .Where(i => i.DetectedAt is not null && reached(i) is not null)
            .Select(i => (reached(i)!.Value - i.DetectedAt!.Value).TotalMinutes)
            .ToList();
        return minutes.Count == 0 ? 0 : minutes.Average();
    }

    private static Dictionary<string, int> CountBy(
        IEnumerable<IncidentReportRow> incidents, Func<IncidentReportRow, string?> key, int? take = null)
    {
        var groups = incidents
            .GroupBy(i => key(i) ?? string.Empty)
            .Select(g => (Key: g.Key, Count: g.Count()))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key, StringComparer.Ordinal);
        return (take is { } n ? groups.Take(n) : groups).ToDictionary(g => g.Key, g => g.Count);
    }
}
