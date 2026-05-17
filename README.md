# IncidentFlow

IncidentFlow is a .NET incident response platform that turns production signals into incidents, notifications, timelines, recommendations, reporting, and postmortem drafts.

This implementation is a runnable local portfolio profile for the full IncidentFlow workflow. It uses dependency-free .NET 6, an in-memory event bus, local outbox/inbox records, and in-memory stores because this workspace currently has the .NET 6 SDK and restricted package/network access. The service boundaries, contracts, workflow, and endpoints are structured so a production adapter pass can replace local adapters with .NET 8, Aspire, YARP, RabbitMQ, Redis, PostgreSQL, EF Core, OpenTelemetry, and full test packages.

## Workflow

```text
Signals
  -> Detection
  -> Correlation
  -> Incident Creation
  -> Notification
  -> Timeline
  -> Recommendation
  -> Resolution
  -> Postmortem
  -> Reporting
```

## Projects

| Project | Purpose |
|---|---|
| `src/IncidentFlow.ApiGateway` | Runnable HTTP API and local gateway/service host |
| `src/IncidentFlow.Core` | Contracts, event envelope, in-memory bus/store, domain workflow |
| `src/IncidentFlow.ServiceDefaults` | Shared constants/default helpers |
| `src/IncidentFlow.AppHost` | Lightweight local runtime instructions |
| `tests/IncidentFlow.SmokeTests` | Dependency-free smoke workflow runner |
| `tests/IncidentFlow.ContractTests` | Dependency-free event envelope and contract runner |

## Run

From the memory-core repo root, use the repo-local .NET 8 SDK:

```bash
source scripts/use-dotnet8.sh
dotnet --version
```

For one-off commands without changing the current shell:

```bash
scripts/dotnet8 build incidentflow/IncidentFlow.sln
scripts/dotnet8 run --project incidentflow/tests/IncidentFlow.SmokeTests
```

```bash
dotnet build
dotnet run --project tests/IncidentFlow.SmokeTests
dotnet run --project tests/IncidentFlow.ContractTests
dotnet run --project src/IncidentFlow.ApiGateway
```

Set `INCIDENTFLOW_API_KEY` to require `x-api-key` on non-health API requests.

## Core Endpoints

```http
POST /api/v1/signals/errors
POST /api/v1/signals/logs
POST /api/v1/signals/metrics
POST /api/v1/signals/traces
POST /api/v1/signals/health-checks
POST /api/v1/signals/deployments
POST /api/v1/signals/user-reports
GET  /api/v1/signals

GET  /api/v1/incidents
GET  /api/v1/incidents/open
GET  /api/v1/incidents/{id}
POST /api/v1/incidents/{id}/acknowledge
POST /api/v1/incidents/{id}/assign
POST /api/v1/incidents/{id}/escalate
POST /api/v1/incidents/{id}/resolve
POST /api/v1/incidents/{id}/close
POST /api/v1/incidents/{id}/reopen
POST /api/v1/incidents/escalation-sweep

GET  /api/v1/incidents/{id}/timeline
GET  /api/v1/incidents/{id}/notifications
GET  /api/v1/incidents/{id}/recommendations
POST /api/v1/incidents/{id}/recommendations/{recommendationId}/status
POST /api/v1/incidents/{id}/postmortem
PUT  /api/v1/incidents/{id}/postmortem
GET  /api/v1/incidents/{id}/postmortem
POST /api/v1/notifications/retry-failed

GET  /api/v1/reports/summary
POST /api/v1/reports/rebuild
GET  /api/v1/reports/service-reliability
GET  /api/v1/reports/mtta
GET  /api/v1/reports/mttr
GET  /api/v1/reports/severity-breakdown
GET  /api/v1/reports/top-failing-services
GET  /api/v1/reports/deployment-regressions

GET  /api/v1/events
GET  /api/v1/events/outbox
GET  /api/v1/events/inbox
GET  /api/v1/runtime/readiness
```

## Implemented Capabilities

- Signal ingestion for errors, logs, metrics, traces, health checks, deployments, and user reports.
- Idempotency-key and natural-key duplicate suppression.
- Local source rate limiting.
- Detection rules for error spikes, log spikes, latency, trace failures, and repeated health failures.
- Correlation events, incident candidates, and root-cause candidates.
- Incident lifecycle commands for acknowledge, assign, escalate, resolve, close, and reopen.
- Notification routing, dedupe, retry hook, and SEV1 escalation sweep.
- Timeline projection for major workflow events.
- Recommendation rules for rollback, dependency, database, configuration, capacity, known fingerprint, and observability gaps.
- Reporting summaries with service/environment filters and rebuild command.
- Postmortem generation and edit workflow.
- Local outbox/inbox records for reliability modeling.
- Smoke and contract runners.
- Architecture docs, API docs, event examples, demo docs, Mermaid diagrams, and CI workflow.

## Documentation

| Document | Purpose |
|---|---|
| `docs/architecture/overview.md` | Architecture, runtime profiles, and core boundaries |
| `docs/architecture/service-boundaries.md` | Service responsibilities and state ownership |
| `docs/architecture/event-flow.md` | Event workflow and adapter mapping |
| `docs/architecture/reliability.md` | Idempotency, outbox/inbox, rate limiting, notifications, observability |
| `docs/api/events.md` | Event envelope and event type catalogue |
| `docs/api/http-api.md` | HTTP endpoint reference |
| `docs/demos/payment-regression.md` | Demo walkthrough |
| `docs/diagrams/incidentflow-diagrams.md` | System, container, event, lifecycle, reliability, data, and sequence diagrams |

## Upgrade Path

- Install .NET 8 SDK and pin it with `global.json`.
- Replace `HttpListener` routing with ASP.NET Core and YARP.
- Replace in-memory event publishing with RabbitMQ.
- Replace in-memory dedupe/windowing with Redis.
- Replace in-memory stores with PostgreSQL service databases and EF Core.
- Replace local event/outbox/inbox lists with service-owned transactional tables.
- Replace the dependency-free runners with xUnit, Testcontainers, Reqnroll, and CI test result publishing.
- Replace lightweight AppHost with .NET Aspire once SDK and package restore support are available.
