using IncidentFlow.BuildingBlocks.Messaging;
using IncidentFlow.Services.Ingestion.Api.Data;
using MassTransit;

namespace IncidentFlow.Services.Ingestion.Api.Messaging;

/// <summary>Ingestion's messaging setup, shared by Program and the tests.</summary>
public static class IngestionMessaging
{
    public static void AddIngestionMessaging(this IBusRegistrationConfigurator x)
    {
        x.UseServiceEndpointNames("ingestion");

        // Bus outbox: IPublishEndpoint writes to OutboxMessage in IngestionDbContext, so the signal and its
        // event commit in one transaction; a background service delivers the event to RabbitMQ afterwards.
        x.AddEntityFrameworkOutbox<IngestionDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
        });
    }
}
