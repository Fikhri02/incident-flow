using IncidentFlow.BuildingBlocks.Messaging;
using IncidentFlow.Services.Incident.Api.Consumers;
using IncidentFlow.Services.Incident.Api.Data;
using MassTransit;

namespace IncidentFlow.Services.Incident.Api.Messaging;

/// <summary>Incident's consumers and endpoint policy, shared by Program and the tests.</summary>
public static class IncidentMessaging
{
    public static void AddIncidentMessaging(this IBusRegistrationConfigurator x)
    {
        x.UseServiceEndpointNames("incident");

        x.AddEntityFrameworkOutbox<IncidentDbContext>(o =>
        {
            o.UsePostgres();
            // Bus outbox: IPublishEndpoint outside a consumer (the HTTP commands) writes to OutboxMessage,
            // committed by the same SaveChanges as the incident; a background service delivers it.
            o.UseBusOutbox();
        });

        // Consumer outbox + inbox: each message is consumed once per MessageId, and what the consumer
        // publishes is committed with its incident and delivered after the commit.
        x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            endpoint.UseEntityFrameworkOutbox<IncidentDbContext>(context));

        x.AddConsumer<AnomalyDetectedConsumer>();
    }
}
