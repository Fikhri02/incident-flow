# Event Flow

## Primary Workflow

1. A client posts a signal to the gateway.
2. Ingestion validates common fields and idempotency.
3. A typed signal event is published.
4. Detection evaluates deterministic rules.
5. Correlation scores related signals and emits an incident candidate.
6. Incident creates or suppresses an incident by problem key.
7. Notification sends simulated channel messages.
8. Timeline records operational evidence.
9. Recommendation generates next actions.
10. Resolution generates reporting updates and a postmortem draft.

## Event Envelope

Every event uses:

- `eventId`
- `eventType`
- `schemaVersion`
- `correlationId`
- `causationId`
- `source`
- `occurredAt`
- `payload`

Contract checks assert these fields for all emitted events.

## Local Adapter Mapping

| Local Adapter | Production Adapter |
|---|---|
| In-memory event list | RabbitMQ exchange and queues |
| Local outbox list | EF Core `OutboxMessages` per service |
| Local inbox list | EF Core `InboxMessages` per consumer |
| Local dedupe dictionary | Redis idempotency keys |
| Local rate limit count | Redis sliding window |
| Local reports | Reporting read model database |
