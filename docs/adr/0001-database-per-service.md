# 0001. One database per service

- Status: Accepted
- Date: decided 2026-04-28 (master plan), built 2026-05-17; recorded 2026-11-23
- Deciders: Irfan

## Context and problem statement

Four services keep state: Ingestion (signals), Incident (incidents), Timeline (timeline entries) and Reporting
(an incident read model, added 2026-11-23 — see ADR 0008).
The question was whether they share one database or each own one.

The master plan set two rules from the start: "Keep service databases separate from day one, even if all run
in one PostgreSQL container" and "Services do not directly query another service database". The reason given
is ownership from day one. No comparison with a shared database or a shared schema was recorded.

## Considered options

No alternatives were recorded when this was chosen.

## Decision outcome

Each stateful service owns one PostgreSQL database, and no service reads another's.

- The Aspire AppHost starts one PostgreSQL server with four databases, `ingestion-db`, `incident-db`,
  `timeline-db` and `reporting-db` (`src/IncidentFlow.AppHost/Program.cs:12-15`). Each API is given only its own
  (`Program.cs:17-42`).
- Each service owns its EF Core model and migrations, applies them at startup, and ships an
  `IDesignTimeDbContextFactory` so `dotnet-ef` works without the Aspire host (README, Timeline 2026-05-18).
- The workers (Detection, Correlation, Notification) have no database. Data moves between services only as
  events over RabbitMQ (ADR 0002).

### Consequences

- Good, because a service can change its tables and migrations without coordinating with another service.
- Good, because no service can couple to another's tables: the only way to share data is a published contract,
  which is pinned by contract tests (`tests/IncidentFlow.Contracts.Tests`).
- Good, because the databases can move to separate servers later by changing connection strings only.
- Bad, because cross-service views are eventually consistent. Timeline learns about an incident only when the
  event arrives, and its signal, anomaly and correlation rows carry no incident id; they are linked through
  the correlation id instead.
- Bad, because a service that saves and then publishes does so in two steps. Until the outbox lands (ADR 0006)
  a crash between the two loses the event.
- Bad, because locally all four databases share one server, so it is separation of ownership, not of failure:
  the server going down takes all four with it.
- Bad, because the local PostgreSQL container has no data volume, so local data is lost when the container
  is recreated.
- Bad, because migrations run at application startup. With several instances of one service starting together,
  that would need a lock or a separate migration step.
- Neutral: the original plan named seven databases (`docs/architecture/service-boundaries.md`); four exist
  because only four services keep state today.

### How it is checked

- Not by an automated test. The separation is visible in `src/IncidentFlow.AppHost/Program.cs`, where each
  project gets a reference to its own database only, and in each service's `*DbContext`.
- The integration tests start each API against its own Testcontainers PostgreSQL
  (`tests/IncidentFlow.IntegrationTests/Ingestion/IngestionApiFactory.cs`,
  `tests/IncidentFlow.IntegrationTests/Incidents/IncidentApiFactory.cs`), so a service that needed another
  service's tables would fail there.
- Gap: nothing stops a future change from giving a service a second connection string. An architecture test
  over the AppHost references would close that.
