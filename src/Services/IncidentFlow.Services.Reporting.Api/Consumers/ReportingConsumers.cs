using IncidentFlow.BuildingBlocks.Contracts;
using IncidentFlow.Services.Reporting.Api.Data;
using IncidentFlow.Services.Reporting.Api.Projection;
using MassTransit;

namespace IncidentFlow.Services.Reporting.Api.Consumers;

// Each consumer runs inside the EF inbox transaction (ReportingMessaging): the projected row and the
// InboxState row commit together, so a redelivered message is skipped rather than applied again.
// Lifecycle events for one incident travel on different queues, so any of them can arrive first.

public sealed class IncidentCreatedConsumer(ReportingDbContext db) : IConsumer<IncidentCreated>
{
    public Task Consume(ConsumeContext<IncidentCreated> context) => Project.Apply(db, context.Message, context.CancellationToken);
}

public sealed class IncidentAcknowledgedConsumer(ReportingDbContext db) : IConsumer<IncidentAcknowledged>
{
    public Task Consume(ConsumeContext<IncidentAcknowledged> context) => Project.Apply(db, context.Message, context.CancellationToken);
}

public sealed class IncidentResolvedConsumer(ReportingDbContext db) : IConsumer<IncidentResolved>
{
    public Task Consume(ConsumeContext<IncidentResolved> context) => Project.Apply(db, context.Message, context.CancellationToken);
}

public sealed class IncidentClosedConsumer(ReportingDbContext db) : IConsumer<IncidentClosed>
{
    public Task Consume(ConsumeContext<IncidentClosed> context) => Project.Apply(db, context.Message, context.CancellationToken);
}

public sealed class CorrelationFoundConsumer(ReportingDbContext db) : IConsumer<CorrelationFound>
{
    public Task Consume(ConsumeContext<CorrelationFound> context) => Project.Apply(db, context.Message, context.CancellationToken);
}

file static class Project
{
    internal static async Task Apply(ReportingDbContext db, object message, CancellationToken ct)
    {
        // The projection writes with SQL in the inbox transaction; the inbox commits it with its InboxState row.
        await new IncidentProjection(db).ApplyAsync(message, ct);
    }
}
