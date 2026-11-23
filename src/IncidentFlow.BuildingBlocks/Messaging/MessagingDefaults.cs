using MassTransit;

namespace IncidentFlow.BuildingBlocks.Messaging;

/// <summary>
/// How every IncidentFlow service configures MassTransit.
/// </summary>
public static class MessagingDefaults
{
    /// <summary>
    /// Queue names start with the service name: "notification-incident-created",
    /// "timeline-incident-created". Without the prefix, services that each register a consumer
    /// with the same class name (three <c>IncidentCreatedConsumer</c>s) share one queue, and
    /// RabbitMQ gives each message to only one of them instead of to all of them.
    /// </summary>
    public static IEndpointNameFormatter EndpointNames(string serviceName) =>
        new KebabCaseEndpointNameFormatter(serviceName, includeNamespace: false);

    public static void UseServiceEndpointNames(this IBusRegistrationConfigurator bus, string serviceName) =>
        bus.SetEndpointNameFormatter(EndpointNames(serviceName));

    /// <summary>
    /// Three more attempts, after 200 ms, 1 s and 5 s, before a message that keeps failing
    /// moves to its queue's <c>_error</c> queue. Call before <c>ConfigureEndpoints</c>.
    /// </summary>
    public static void UseDefaultRetry(this IBusFactoryConfigurator cfg) =>
        cfg.UseMessageRetry(r => r.Intervals(
            TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));

    /// <summary>
    /// Sets the envelope CorrelationId of every published contract from its payload CorrelationId.
    /// Applies to bus publishes, outbox publishes and publishes from inside a consumer.
    /// Not a scoped filter: the in-memory outbox sends after the consumer's scope is disposed.
    /// </summary>
    public static void UsePayloadCorrelationId(this IBusFactoryConfigurator cfg) =>
        cfg.ConfigurePublish(p => p.ConnectPublishPipeSpecificationObserver(new PayloadCorrelationIdObserver()));
}
