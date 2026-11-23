# 0005. The incident lifecycle lives in a DDD aggregate

- Status: Accepted
- Date: 2026-11-19 (`74153f5`); recorded 2026-11-23
- Deciders: Irfan

## Context and problem statement

An incident moves through open, acknowledged, resolved and closed, and only some moves are allowed. Before
`74153f5` those rules sat in the HTTP endpoints and the anomaly consumer. The master plan asked for "DDD for
Incident, CQRS where useful".

The reasons recorded with the change (`Domain/Incident.cs:7-12`, `IIncidentRepository.cs`, Timeline
2026-11-19): keep the lifecycle rules in one place; store the status as the same text so no migration was
needed; keep the HTTP JSON shape and the 409 messages unchanged.

## Considered options

No alternatives to the aggregate were recorded. The constraints above (no migration, same API) were recorded
as requirements of the change.

## Decision outcome

In `src/Services/IncidentFlow.Services.Incident.Api/Domain/`:

- `Incident` is the aggregate root: private setters, an EF-only constructor, a factory `Open`, and the methods
  `Acknowledge`, `Resolve` and `Close`. Acknowledge only from open; resolve from open or acknowledged; close
  from any status except closed. A refused move raises `IncidentTransitionException` (HTTP 409); a missing
  name raises `ArgumentException` (HTTP 400).
- `IncidentKey` is a value object (service, environment, fingerprint) with validation and value equality.
  One active incident per key is enforced by the database: the partial unique index `IX_Incidents_ActiveKey`
  over the key where the status is open or acknowledged, with nulls not distinct (`Data/IncidentDbContext.cs`).
  The anomaly consumer still looks for an active incident first; a save that loses the race raises
  `ActiveIncidentExistsException`, which the consumer treats as already handled.
- The aggregate records domain events (`Domain/Events/IncidentDomainEvents.cs`). The service dequeues them and
  `Messaging/IncidentEventPublisher.cs` maps them to the shared integration contracts and publishes them through
  MassTransit's EF Core outbox, so the event rows are committed in the same transaction as the aggregate and
  delivered after the commit (ADR 0006). Domain events stay inside the service.
- Commands load and save whole aggregates through `IIncidentRepository`; list queries read the DbContext
  directly.

### Consequences

- Good, because every lifecycle rule is in one class and can be tested without a database or HTTP.
- Good, because an illegal transition cannot be made through any path that uses the aggregate.
- Good, because internal domain events can change without changing the contracts other services consume.
- Good, because queries stay simple and do not load aggregates.
- Good, because an event is published if and only if the change it describes was committed (ADR 0006). Before
  the hardening work the events were published after saving, in a second step, and a crash between the two lost
  them.
- Before the hardening work "one active incident per key" was a read then an insert in the anomaly consumer, and
  two anomalies handled at once could open two incidents (review 2026-11-19). The unique index now refuses the
  second. The migration that adds it (`20261123023646_AddOutboxInboxAndActiveIncidentIndex`) first closes all but
  the earliest active incident per key on existing data, setting `Status` and `ClosedAt` as `Close` does; no
  `IncidentClosed` event is published for those rows.
- Bad, because the active rule is written twice, in the aggregate (`Incident.IsActive`) and in the index filter,
  and the two have to be kept in step.
- Bad, because the pattern is used only here. Ingestion and Timeline use plain entities; whether that was a
  choice was not recorded.
- Neutral: close is allowed straight from open. The product intent of that shortcut is unconfirmed (UAT case
  INC-09). Assign, escalate and reopen exist only in the `IncidentFlow.Core` prototype.

### How it is checked

- `tests/IncidentFlow.UnitTests/Incidents/IncidentAggregateTests.cs`: 13 tests, 27 cases, covering every
  allowed and refused transition, the value object and event dequeueing.
- `tests/IncidentFlow.IntegrationTests/Incidents/IncidentEndpointsTests.cs`: 409 and 400 through HTTP with
  real PostgreSQL, and the JSON shape unchanged.
- `tests/IncidentFlow.Specs/Features/IncidentLifecycle.feature`: the lifecycle and the refused moves in plain
  language, against the aggregate.
- One active incident per key: `tests/IncidentFlow.IntegrationTests/Incidents/IncidentMessagingTests`
  (`TwoSavesForTheSameKey_LeaveOneActiveIncident`, `ConcurrentAnomalies_OpenOneIncident_AndTheLoserIsNotAFault`)
  and `Incidents/ActiveIncidentIndexMigrationTests.Migrating_ADatabaseWithDuplicateActiveIncidents_KeepsTheEarliestActive_AndClosesTheRest`.
- Events through the outbox: `Incidents/IncidentOutboxTests.Acknowledge_WritesTheEventToTheOutbox_WithTheIncident`.
