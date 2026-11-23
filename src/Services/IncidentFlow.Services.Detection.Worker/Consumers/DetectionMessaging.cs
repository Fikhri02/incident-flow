using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;

namespace IncidentFlow.Services.Detection.Worker.Consumers;

/// <summary>Detection's consumers and endpoint policy, shared by Program and the tests.</summary>
public static class DetectionMessaging
{
    public static void AddDetectionMessaging(this IBusRegistrationConfigurator x)
    {
        x.UseServiceEndpointNames("detection");

        // No database here, so the in-memory outbox: what a consumer publishes is sent only after the
        // consume succeeds, and dropped if it throws (a retry then publishes it once).
        x.AddConfigureEndpointsCallback((context, _, endpoint) => endpoint.UseInMemoryOutbox(context));
        x.AddConsumer<ErrorSignalConsumer>();
    }
}
