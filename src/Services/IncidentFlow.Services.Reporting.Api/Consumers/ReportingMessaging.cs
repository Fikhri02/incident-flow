using IncidentFlow.BuildingBlocks.Messaging;
using IncidentFlow.Services.Reporting.Api.Data;
using System.Data;
using MassTransit;

namespace IncidentFlow.Services.Reporting.Api.Consumers;

/// <summary>Reporting's consumers and endpoint policy, shared by Program and the tests.</summary>
public static class ReportingMessaging
{
    public static void AddReportingMessaging(this IBusRegistrationConfigurator x)
    {
        x.UseServiceEndpointNames("reporting");

        // Inbox: every consumer runs inside a ReportingDbContext transaction that also records the
        // MessageId in InboxState, so a redelivered message is recognised and not projected twice.
        x.AddEntityFrameworkOutbox<ReportingDbContext>(o =>
        {
            o.UsePostgres();
            // The projection's upserts (INSERT .. ON CONFLICT) resolve two events racing for one incident
            // under READ COMMITTED; under the default REPEATABLE READ the loser fails with a serialization error.
            o.IsolationLevel = IsolationLevel.ReadCommitted;
        });
        x.AddConfigureEndpointsCallback((context, _, endpoint) =>
            endpoint.UseEntityFrameworkOutbox<ReportingDbContext>(context));

        x.AddConsumer<IncidentCreatedConsumer>();
        x.AddConsumer<IncidentAcknowledgedConsumer>();
        x.AddConsumer<IncidentResolvedConsumer>();
        x.AddConsumer<IncidentClosedConsumer>();
        x.AddConsumer<CorrelationFoundConsumer>();
    }
}
