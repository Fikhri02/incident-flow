> This document describes the retired single-process prototype. For what runs today see the README and docs/adr.

# IncidentFlow Architecture Overview

IncidentFlow turns production signals into a managed incident response workflow.

```text
Signals -> Detection -> Correlation -> Incident -> Notification
        -> Timeline -> Recommendation -> Resolution -> Postmortem -> Reporting
```

## Runtime Profiles

| Profile | Current State | Production Target |
|---|---|---|
| Local portfolio profile | .NET 6, `HttpListener`, in-memory adapters | Kept as a dependency-free demo and regression profile |
| Production adapter profile | Documented adapter boundaries | .NET 8, Aspire, YARP, RabbitMQ, Redis, PostgreSQL, EF Core, OpenTelemetry |

The local profile is intentionally complete enough to prove behavior without package restore or Docker. The production adapter profile should replace adapters behind the same event contracts, endpoint shapes, and test scenarios.

## Core Boundaries

| Boundary | Responsibility |
|---|---|
| Ingestion | Accepts error, log, metric, trace, health, deployment, and user-report signals |
| Detection | Applies deterministic rules for spikes, latency, trace failures, and health outages |
| Correlation | Groups related signals and emits incident candidates plus root-cause candidates |
| Incident | Owns incident lifecycle, assignment, escalation, resolution, close, and reopen rules |
| Notification | Routes simulated Slack, email, SMS, and in-app notifications with dedupe/retry hooks |
| Timeline | Projects signal, anomaly, incident, notification, recommendation, and postmortem events |
| Recommendation | Generates rollback, dependency, database, configuration, capacity, and observability suggestions |
| Reporting | Builds MTTA, MTTR, severity, service, and deployment-regression summaries |
| Postmortem | Generates and edits structured postmortem drafts from incident evidence |

## Key Quality Rules

- Events use a common `EventEnvelope`.
- Every public request receives or creates a correlation ID.
- Local dev auth can require `x-api-key` when `INCIDENTFLOW_API_KEY` is set.
- Every published event is mirrored into the local outbox.
- Every consumer step records local inbox processing.
- Duplicate signals are suppressed by natural keys or explicit idempotency keys.
- Source rate limiting is enforced in the local adapter and maps to Redis for production.
- Incidents are deduped by service, environment, and fingerprint.
