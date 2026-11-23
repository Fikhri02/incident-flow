namespace IncidentFlow.Services.Incident.Api.Domain;

/// <summary>
/// Thrown when saving a new incident would give its key a second active incident.
/// The database enforces the rule, so two anomalies racing past the "is one active?" check
/// still end with one active incident; the loser should treat the key as already open.
/// </summary>
public sealed class ActiveIncidentExistsException(IncidentKey key, Exception inner)
    : Exception($"An active incident already exists for {key.ServiceName}/{key.Environment}/{key.Fingerprint}.", inner)
{
    public IncidentKey Key { get; } = key;
}
