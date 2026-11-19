namespace IncidentFlow.Services.Incident.Api.Domain;

/// <summary>
/// Value object: what makes two anomalies "the same problem".
/// One active incident may exist per key at a time.
/// </summary>
public sealed record IncidentKey
{
    public IncidentKey(string serviceName, string environment, string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(serviceName))
            throw new ArgumentException("Service name is required.", nameof(serviceName));
        if (string.IsNullOrWhiteSpace(environment))
            throw new ArgumentException("Environment is required.", nameof(environment));

        ServiceName = serviceName;
        Environment = environment;
        Fingerprint = fingerprint;
    }

    public string ServiceName { get; }
    public string Environment { get; }
    public string? Fingerprint { get; }
}
