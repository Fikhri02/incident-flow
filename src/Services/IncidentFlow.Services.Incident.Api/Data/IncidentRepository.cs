using IncidentFlow.Services.Incident.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IncidentFlow.Services.Incident.Api.Data;

public sealed class IncidentRepository(IncidentDbContext db) : IIncidentRepository
{
    public Task<Domain.Incident?> GetAsync(Guid incidentId, CancellationToken cancellationToken = default) =>
        db.Incidents.FirstOrDefaultAsync(i => i.IncidentId == incidentId, cancellationToken);

    public Task<Domain.Incident?> FindActiveAsync(IncidentKey key, CancellationToken cancellationToken = default) =>
        db.Incidents.FirstOrDefaultAsync(Domain.Incident.IsActiveFor(key), cancellationToken);

    public void Add(Domain.Incident incident) => db.Incidents.Add(incident);

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
                                           {
                                               SqlState: PostgresErrorCodes.UniqueViolation,
                                               ConstraintName: IncidentDbContext.ActiveKeyIndex
                                           })
        {
            // Forget the incident that lost, so the rest of the unit of work (inbox, outbox) can still commit.
            var losers = db.ChangeTracker.Entries<Domain.Incident>()
                .Where(e => e.State == EntityState.Added).ToList();
            foreach (var entry in losers) entry.State = EntityState.Detached;
            throw new ActiveIncidentExistsException(losers.Select(e => e.Entity.Key).First(), ex);
        }
    }
}
