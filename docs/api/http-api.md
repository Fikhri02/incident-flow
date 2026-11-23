> This document describes the retired single-process prototype. For what runs today see the README and docs/adr.

# HTTP API

Base URL for the local profile: `http://localhost:5089`.

If `INCIDENTFLOW_API_KEY` is set, all non-health API requests must include:

```http
x-api-key: your-local-key
```

## Health And Runtime

```http
GET /health/live
GET /health/ready
GET /api/v1/runtime/readiness
```

## Signals

```http
POST /api/v1/signals/errors
POST /api/v1/signals/logs
POST /api/v1/signals/metrics
POST /api/v1/signals/traces
POST /api/v1/signals/health-checks
POST /api/v1/signals/deployments
POST /api/v1/signals/user-reports
GET  /api/v1/signals
```

All signal requests require:

- `sourceSystem`
- `serviceName`
- `environment`

Optional common fields:

- `occurredAt`
- `region`
- `metadata`
- `idempotencyKey`

## Incidents

```http
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
```

## Timeline, Notifications, Recommendations, Postmortems

```http
GET  /api/v1/incidents/{id}/timeline
GET  /api/v1/incidents/{id}/notifications
GET  /api/v1/incidents/{id}/recommendations
POST /api/v1/incidents/{id}/recommendations/{recommendationId}/status
POST /api/v1/incidents/{id}/postmortem
PUT  /api/v1/incidents/{id}/postmortem
GET  /api/v1/incidents/{id}/postmortem
POST /api/v1/notifications/retry-failed
```

## Reporting

```http
GET  /api/v1/reports/summary
POST /api/v1/reports/rebuild
GET  /api/v1/reports/service-reliability
GET  /api/v1/reports/mtta
GET  /api/v1/reports/mttr
GET  /api/v1/reports/severity-breakdown
GET  /api/v1/reports/top-failing-services
GET  /api/v1/reports/deployment-regressions
```

Report endpoints support optional query filters:

```http
?serviceName=Payment%20API&environment=production
```

## Event Debugging

```http
GET /api/v1/events
GET /api/v1/events/outbox
GET /api/v1/events/inbox
```
