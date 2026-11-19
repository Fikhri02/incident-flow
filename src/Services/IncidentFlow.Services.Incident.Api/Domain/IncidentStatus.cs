namespace IncidentFlow.Services.Incident.Api.Domain;

public enum IncidentStatus
{
    Open,
    Acknowledged,
    Resolved,
    Closed
}

public static class IncidentStatusExtensions
{
    /// <summary>The lowercase name used in the database column and the HTTP API.</summary>
    public static string ToWire(this IncidentStatus status) => status switch
    {
        IncidentStatus.Open => "open",
        IncidentStatus.Acknowledged => "acknowledged",
        IncidentStatus.Resolved => "resolved",
        IncidentStatus.Closed => "closed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null)
    };

    public static IncidentStatus FromWire(string value) => value switch
    {
        "open" => IncidentStatus.Open,
        "acknowledged" => IncidentStatus.Acknowledged,
        "resolved" => IncidentStatus.Resolved,
        "closed" => IncidentStatus.Closed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown incident status.")
    };

    /// <summary>Open and acknowledged incidents are still being worked; new anomalies fold into them.</summary>
    public static bool IsActive(this IncidentStatus status) =>
        status is IncidentStatus.Open or IncidentStatus.Acknowledged;
}
