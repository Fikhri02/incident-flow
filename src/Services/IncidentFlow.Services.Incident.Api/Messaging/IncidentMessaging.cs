using IncidentFlow.BuildingBlocks.Contracts;
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
        {
            // Anomalies for one incident key are consumed one at a time. A later one then finds the
            // incident the first one opened and stops before saving, instead of every one passing the
            // active-incident check and all but one being refused by the unique index. Different keys
            // still run in parallel. The partitioner sits outside the consumer outbox, so each anomaly's
            // transaction has committed before the next one for its key starts.
            endpoint.UsePartitioner<AnomalyDetected>(endpoint.CreatePartitioner(16), c => PartitionKey(c.Message));
            endpoint.UseEntityFrameworkOutbox<IncidentDbContext>(context);
        });

        x.AddConsumer<AnomalyDetectedConsumer>();
    }

    /// <summary>The incident key as a stable Guid (MassTransit partitions by Guid), so one key always lands in the same partition.</summary>
    internal static Guid PartitionKey(AnomalyDetected anomaly) =>
        new(System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{anomaly.ServiceName}\n{anomaly.Environment}\n{anomaly.Fingerprint}")));
}
