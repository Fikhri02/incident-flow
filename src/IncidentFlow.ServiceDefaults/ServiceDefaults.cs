namespace IncidentFlow.ServiceDefaults;

public static class ServiceDefaults
{
    public const string CorrelationIdHeader = "x-correlation-id";
    public const string LocalEnvironmentName = "local";

    public static string NewCorrelationId()
    {
        return Guid.NewGuid().ToString("N");
    }
}
