using IncidentFlow.Services.Incident.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Incident.Api.Data;

public sealed class IncidentRepository(IncidentDbContext db) : IIncidentRepository
{
    public Task<Domain.Incident?> GetAsync(Guid incidentId, CancellationToken cancellationToken = default) =>
        db.Incidents.FirstOrDefaultAsync(i => i.IncidentId == incidentId, cancellationToken);

    public Task<Domain.Incident?> FindActiveAsync(IncidentKey key, CancellationToken cancellationToken = default) =>
        db.Incidents.FirstOrDefaultAsync(Domain.Incident.IsActiveFor(key), cancellationToken);

    public void Add(Domain.Incident incident) => db.Incidents.Add(incident);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        db.SaveChangesAsync(cancellationToken);
}
