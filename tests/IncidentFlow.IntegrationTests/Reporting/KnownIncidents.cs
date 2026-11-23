using IncidentFlow.BuildingBlocks.Contracts;

namespace IncidentFlow.IntegrationTests.Reporting;

/// <summary>
/// Five incidents with known lifecycle times, and the figures every report must compute from them.
/// <code>
///   A payment-api prod    Critical  ack +10  resolved +60  closed +90   deployment-linked
///   B payment-api prod    High      ack +20  resolved +120              fingerprint-linked only
///   C payment-api staging High      ack +30                             open
///   D search-api  prod    Low                                           open, deployment-linked
///   E auth-api    prod    Critical                         closed +5    closed without resolving
/// </code>
/// </summary>
public static class KnownIncidents
{
    public static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static readonly Guid A = Guid.NewGuid();
    public static readonly Guid B = Guid.NewGuid();
    public static readonly Guid C = Guid.NewGuid();
    public static readonly Guid D = Guid.NewGuid();
    public static readonly Guid E = Guid.NewGuid();

    public static IncidentCreated Created(Guid id, string service, string environment, string severity,
        DateTimeOffset? detectedAt = null) =>
        new(id, service, environment, null, "fp", null, severity, "ErrorSpike", $"{service} failing",
            detectedAt ?? T0, Corr());

    public static IncidentAcknowledged Acknowledged(Guid id, string service, string environment, int minutes) =>
        new(id, service, environment, "alice", T0.AddMinutes(minutes), Corr());

    public static IncidentResolved Resolved(Guid id, string service, string environment, int minutes) =>
        new(id, service, environment, "bob", T0.AddMinutes(minutes), Corr());

    public static IncidentClosed Closed(Guid id, string service, string environment, int minutes) =>
        new(id, service, environment, T0.AddMinutes(minutes), Corr());

    public static CorrelationFound Correlated(string type, string environment, params Guid[] incidentIds) =>
        new(Guid.NewGuid(), type, $"{type}-key", environment, type == "deployment" ? "deploy-42" : null,
            type == "fingerprint" ? "fp" : null, incidentIds, T0.AddMinutes(1), Corr());

    public static object[] Events() =>
    [
        Created(A, "payment-api", "prod", "Critical"),
        Created(B, "payment-api", "prod", "High"),
        Created(C, "payment-api", "staging", "High"),
        Created(D, "search-api", "prod", "Low"),
        Created(E, "auth-api", "prod", "Critical"),
        Acknowledged(A, "payment-api", "prod", 10),
        Acknowledged(B, "payment-api", "prod", 20),
        Acknowledged(C, "payment-api", "staging", 30),
        Resolved(A, "payment-api", "prod", 60),
        Resolved(B, "payment-api", "prod", 120),
        Closed(A, "payment-api", "prod", 90),
        Closed(E, "auth-api", "prod", 5),
        Correlated("deployment", "prod", A, D),
        Correlated("fingerprint", "prod", B)
    ];

    private static string Corr() => Guid.NewGuid().ToString("N");
}
