using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;

namespace IncidentFlow.IntegrationTests.Shared;

/// <summary>
/// The in-memory transport with the same bus-level settings every service uses on RabbitMQ,
/// so harness tests run a service's real consumer and endpoint registration.
/// </summary>
public static class TestBus
{
    public static void UsingTestTransport(this IBusRegistrationConfigurator x, bool retry = true)
    {
        x.SetTestTimeouts(testTimeout: TimeSpan.FromSeconds(30), testInactivityTimeout: TimeSpan.FromSeconds(1));
        x.UsingInMemory((ctx, cfg) =>
        {
            if (retry) cfg.UseDefaultRetry();
            cfg.UsePayloadCorrelationId();
            cfg.ConfigureEndpoints(ctx);
        });
    }
}
