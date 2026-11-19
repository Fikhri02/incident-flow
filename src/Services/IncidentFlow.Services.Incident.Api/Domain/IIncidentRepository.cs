namespace IncidentFlow.Services.Incident.Api.Domain;

/// <summary>Loads and saves whole Incident aggregates. Read-only list queries go straight to the DbContext.</summary>
public interface IIncidentRepository
{
    Task<Incident?> GetAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>The open or acknowledged incident for this key, if there is one.</summary>
    Task<Incident?> FindActiveAsync(IncidentKey key, CancellationToken cancellationToken = default);

    void Add(Incident incident);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
