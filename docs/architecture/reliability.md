> This document describes the retired single-process prototype. For what runs today see the README and docs/adr.

# Reliability Model

## Idempotency

Signals support explicit `idempotencyKey`. If omitted, the local adapter builds a natural key from signal type, source, service, environment, semantic fields, metadata, and minute bucket.

Consumers record local inbox processing so duplicate message handling can map cleanly to a production `InboxMessages` table.

## Outbox

Every local publish creates an `OutboxMessage` with payload JSON, event type, schema version, correlation ID, causation ID, and publish timestamp. In production, state-owning services should write this record transactionally with their domain change.

## Incident Dedupe

Active incidents are keyed by service, environment, and fingerprint. Matching anomalies update the existing response path instead of creating duplicates.

## Notification Dedupe, Retry, Escalation

Notifications are deduped by incident, channel, and action. The local engine exposes:

- `POST /api/v1/incidents/escalation-sweep`
- `POST /api/v1/notifications/retry-failed`

Production should run these as background workers using Redis timers or persisted notification state.

## Rate Limiting

The local profile limits a source/service/environment stream to 1,000 accepted signals per minute. Production should replace this with Redis sliding windows and gateway policies.

## Observability

Current:

- Correlation IDs on HTTP responses and events
- Health endpoints
- Event stream endpoint
- Timeline projection
- Runtime readiness endpoint

Production target:

- OpenTelemetry traces and metrics
- structured logs with service, environment, correlation ID, event ID, and incident ID
- RabbitMQ and PostgreSQL instrumentation
