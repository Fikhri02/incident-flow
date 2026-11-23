> This document describes the retired single-process prototype. For what runs today see the README and docs/adr.

# Event Contracts

IncidentFlow integration events are immutable versioned contracts. All events use `schemaVersion: 1` in the local profile.

## Envelope

```json
{
  "eventId": "4a0d8e62-2e7a-45e7-a69f-330e69f80dc1",
  "eventType": "IncidentCreated",
  "schemaVersion": 1,
  "correlationId": "0f6d0f1f4cf34539bb5229a3ccdd2b51",
  "causationId": "e0f61d16-3b1a-4d65-9e16-bb1b4dcfb891",
  "source": "IncidentFlow.Incident",
  "occurredAt": "2026-04-30T03:30:00+00:00",
  "payload": {}
}
```

## Current Event Types

Signal:

- `ErrorSignalReceived`
- `LogSignalReceived`
- `MetricSignalReceived`
- `TraceSignalReceived`
- `HealthCheckSignalReceived`
- `DeploymentSignalReceived`
- `UserReportSignalReceived`

Detection and correlation:

- `AnomalyDetected`
- `SignalsCorrelated`
- `IncidentCandidateDetected`
- `RootCauseCandidateIdentified`

Incident operations:

- `IncidentCreated`
- `IncidentAcknowledged`
- `IncidentAssigned`
- `IncidentEscalated`
- `IncidentEscalationTimerFired`
- `IncidentResolved`
- `IncidentClosed`
- `IncidentReopened`

Notification, recommendation, reporting, postmortem:

- `NotificationSent`
- `NotificationRetried`
- `RecommendationGenerated`
- `RecommendationStatusUpdated`
- `ReportingProjectionUpdated`
- `ReportingProjectionRebuilt`
- `PostmortemCreated`
- `PostmortemUpdated`

## Compatibility Rule

Do not rename or remove envelope fields. Breaking payload changes require a new schema version and updated contract examples.
