using IncidentFlow.BuildingBlocks.Messaging;
using IncidentFlow.Services.Timeline.Api.Data;
using MassTransit;

namespace IncidentFlow.Services.Timeline.Api.Consumers;

/// <summary>Timeline's consumers and endpoint policy, shared by Program and the tests.</summary>
public static class TimelineMessaging
{
    public static void AddTimelineMessaging(this IBusRegistrationConfigurator x)
    {
        x.UseServiceEndpointNames("timeline");

        // Inbox: every consumer runs inside a TimelineDbContext transaction that also records the
        // MessageId in InboxState, so a redelivered message is recognised and not appended twice.
        x.AddEntityFrameworkOutbox<TimelineDbContext>(o => o.UsePostgres());
        x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            endpoint.UseEntityFrameworkOutbox<TimelineDbContext>(context));

        x.AddConsumer<IncidentCreatedConsumer>();
        x.AddConsumer<IncidentAcknowledgedConsumer>();
        x.AddConsumer<IncidentResolvedConsumer>();
        x.AddConsumer<IncidentClosedConsumer>();
        x.AddConsumer<AnomalyDetectedConsumer>();
        x.AddConsumer<ErrorSignalConsumer>();
        x.AddConsumer<CorrelationFoundConsumer>();
    }
}
