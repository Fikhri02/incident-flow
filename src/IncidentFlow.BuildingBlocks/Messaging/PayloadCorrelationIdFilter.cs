using IncidentFlow.BuildingBlocks.Contracts;
using MassTransit;
using MassTransit.Configuration;

namespace IncidentFlow.BuildingBlocks.Messaging;

/// <summary>
/// Copies a contract's business CorrelationId into MassTransit's envelope CorrelationId, so brokers,
/// traces and tools that only read the envelope can follow one request across services.
/// The envelope field is a Guid: an id that is not a Guid stays in the payload only.
/// </summary>
public sealed class PayloadCorrelationIdFilter<T> : IFilter<PublishContext<T>>
    where T : class
{
    public Task Send(PublishContext<T> context, IPipe<PublishContext<T>> next)
    {
        if (context.Message is ICorrelatedEvent correlated &&
            Guid.TryParse(correlated.CorrelationId, out var correlationId))
            context.CorrelationId = correlationId;

        return next.Send(context);
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("payloadCorrelationId");
}

/// <summary>
/// Adds <see cref="PayloadCorrelationIdFilter{T}"/> to every message type's publish pipe. The filter holds no
/// state, so it is not scoped: outboxes that send after the consumer's scope is gone can still run it.
/// </summary>
public sealed class PayloadCorrelationIdObserver : IPublishPipeSpecificationObserver
{
    public void MessageSpecificationCreated<T>(IMessagePublishPipeSpecification<T> specification)
        where T : class =>
        specification.UseFilter(new PayloadCorrelationIdFilter<T>());
}
