namespace IncidentFlow.Services.Incident.Api.Domain;

/// <summary>Loads and saves whole Incident aggregates. Read-only list queries go straight to the DbContext.</summary>
public interface IIncidentRepository
{
    Task<Incident?> GetAsync(Guid incidentId, CancellationToken cancellationToken = default);

    /// <summary>The open or acknowledged incident for this key, if there is one.</summary>
    Task<Incident?> FindActiveAsync(IncidentKey key, CancellationToken cancellationToken = default);

    void Add(Incident incident);

    /// <exception cref="ActiveIncidentExistsException">A new incident's key already has an active incident.</exception>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
