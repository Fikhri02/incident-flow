# Service Boundaries

## Ingestion Service

Accepts external signals and normalizes them into `SignalRecord` events.

Current endpoints:

- `POST /api/v1/signals/errors`
- `POST /api/v1/signals/logs`
- `POST /api/v1/signals/metrics`
- `POST /api/v1/signals/traces`
- `POST /api/v1/signals/health-checks`
- `POST /api/v1/signals/deployments`
- `POST /api/v1/signals/user-reports`

Production state owner: `ingestion_db`.

## Detection Service

Consumes signal events and emits `AnomalyDetected`.

Rules currently implemented:

- Error spike
- Log error spike
- Latency metric threshold
- Trace server error
- Trace latency threshold
- Repeated health check failure

Production transient state: Redis windows and dedupe.

## Correlation Service

Consumes anomalies and emits:

- `SignalsCorrelated`
- `IncidentCandidateDetected`
- `RootCauseCandidateIdentified`

Production transient state: Redis hot clusters.

## Incident Service

Owns incident lifecycle and validation.

Commands:

- acknowledge
- assign
- escalate
- resolve
- close
- reopen

Production state owner: `incident_db`.

## Notification Service

Consumes incident events and records notifications. SEV1 incidents fan out to Slack, email, SMS, and in-app channels. Lower severities use Slack and in-app. Local retry and escalation sweep APIs are available.

Production state owner: `notification_db`.

## Timeline Service

Projects operational events into incident timelines.

Production state owner: `timeline_db`.

## Recommendation, Reporting, Postmortem

These services turn incident evidence into action:

- Recommendations use deterministic rules.
- Reporting computes portfolio metrics.
- Postmortems generate editable drafts.

Production state owners: `recommendation_db`, `reporting_db`, and `postmortem_db`.
