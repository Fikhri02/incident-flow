using IncidentFlow.Services.Reporting.Api.Data;
using IncidentFlow.Services.Reporting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Reporting.Api.Reports;

/// <summary>Reads the counted incidents for a filter and hands them to <see cref="ReportCalculator"/>.</summary>
public sealed class ReportQueries(ReportingDbContext db)
{
    public async Task<IncidentSummaryReport> SummaryAsync(ReportFilter filter, CancellationToken ct) =>
        ReportCalculator.Summary(await IncidentsAsync(filter, ct));

    public async Task<IReadOnlyList<ServiceReliabilityRow>> ServiceReliabilityAsync(ReportFilter filter, CancellationToken ct) =>
        ReportCalculator.ServiceReliability(await IncidentsAsync(filter, ct));

    /// <summary>
    /// Incidents whose IncidentCreated has arrived. A row made by an event that overtook it is held back
    /// until then, so every counted incident has a service, a severity and a detection time.
    /// </summary>
    private async Task<List<IncidentReportRow>> IncidentsAsync(ReportFilter filter, CancellationToken ct)
    {
        var query = db.Incidents.AsNoTracking().Where(r => r.DetectedAt != null);

        if (!string.IsNullOrWhiteSpace(filter.ServiceName))
        {
            var service = filter.ServiceName.ToLowerInvariant();
            query = query.Where(r => r.ServiceName!.ToLower() == service);
        }

        if (!string.IsNullOrWhiteSpace(filter.Environment))
        {
            var environment = filter.Environment.ToLowerInvariant();
            query = query.Where(r => r.Environment!.ToLower() == environment);
        }

        return await query.ToListAsync(ct);
    }
}
