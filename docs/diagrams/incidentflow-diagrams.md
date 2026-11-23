> This document describes the retired single-process prototype. For what runs today see the README and docs/adr.

# IncidentFlow Diagrams

## System Context

```mermaid
C4Context
  title IncidentFlow System Context
  Person(engineer, "On-call Engineer", "Investigates, acknowledges, resolves, and reviews incidents")
  System_Ext(prod, "Production Systems", "Applications, jobs, APIs, and infrastructure emitting signals")
  System(incidentflow, "IncidentFlow", "Signal ingestion, detection, incident response, reporting, and postmortems")
  System_Ext(channels, "Notification Channels", "Slack, email, SMS, in-app")
  Rel(prod, incidentflow, "Sends production signals")
  Rel(incidentflow, channels, "Sends notifications")
  Rel(engineer, incidentflow, "Manages incidents")
```

## Container Diagram

```mermaid
flowchart LR
  Gateway[API Gateway]
  Ingestion[Ingestion]
  Detection[Detection]
  Correlation[Correlation]
  Incident[Incident]
  Notification[Notification]
  Timeline[Timeline]
  Recommendation[Recommendation]
  Reporting[Reporting]
  Postmortem[Postmortem]
  Bus[(Event Bus)]
  Redis[(Redis)]
  Db[(Service Databases)]

  Gateway --> Ingestion
  Gateway --> Incident
  Gateway --> Timeline
  Ingestion --> Bus
  Detection --> Bus
  Correlation --> Bus
  Incident --> Bus
  Notification --> Bus
  Recommendation --> Bus
  Reporting --> Bus
  Postmortem --> Bus
  Bus --> Detection
  Bus --> Correlation
  Bus --> Incident
  Bus --> Notification
  Bus --> Timeline
  Bus --> Recommendation
  Bus --> Reporting
  Bus --> Postmortem
  Ingestion --> Redis
  Detection --> Redis
  Correlation --> Redis
  Incident --> Db
  Timeline --> Db
  Reporting --> Db
  Postmortem --> Db
```

## Event Flow

```mermaid
flowchart TD
  A[Signal Received] --> B[Validate and Dedupe]
  B --> C[Publish Signal Event]
  C --> D[Detection Rule]
  D --> E[AnomalyDetected]
  E --> F[Correlation]
  F --> G[IncidentCandidateDetected]
  G --> H[IncidentCreated]
  H --> I[NotificationSent]
  H --> J[TimelineEntryRecorded]
  H --> K[RecommendationGenerated]
  H --> L[ReportingProjectionUpdated]
  L --> M[PostmortemCreated on resolution]
```

## Incident Lifecycle

```mermaid
stateDiagram-v2
  [*] --> Open
  Open --> Acknowledged: acknowledge
  Open --> Mitigating: escalation sweep
  Acknowledged --> Mitigating: escalate
  Mitigating --> Resolved: resolve
  Acknowledged --> Resolved: resolve
  Resolved --> Closed: close
  Resolved --> Reopened: reopen
  Closed --> Reopened: reopen
  Reopened --> Acknowledged: acknowledge
```

## Outbox And Inbox Reliability

```mermaid
sequenceDiagram
  participant API as Service API
  participant DB as Service DB
  participant Outbox as OutboxMessages
  participant Bus as Event Bus
  participant Inbox as InboxMessages
  participant Worker as Consumer

  API->>DB: Save domain change
  API->>Outbox: Save event envelope
  Outbox->>Bus: Dispatch event
  Bus->>Worker: Deliver event
  Worker->>Inbox: Check message id
  Worker->>DB: Apply projection or command
  Worker->>Inbox: Mark processed
```

## Data Ownership

```mermaid
flowchart TB
  IngestionDb[(ingestion_db)]
  DetectionDb[(detection_db)]
  IncidentDb[(incident_db)]
  NotificationDb[(notification_db)]
  TimelineDb[(timeline_db)]
  ReportingDb[(reporting_db)]
  PostmortemDb[(postmortem_db)]

  Ingestion --> IngestionDb
  Detection --> DetectionDb
  Incident --> IncidentDb
  Notification --> NotificationDb
  Timeline --> TimelineDb
  Reporting --> ReportingDb
  Postmortem --> PostmortemDb
```

## Payment Regression Sequence

```mermaid
sequenceDiagram
  participant Deploy as Deploy Bot
  participant Web as Checkout Web
  participant IF as IncidentFlow
  participant Eng as On-call Engineer

  Deploy->>IF: DeploymentSignalReceived
  loop 5 payment timeouts
    Web->>IF: ErrorSignalReceived
  end
  IF->>IF: ErrorSpikeRule
  IF->>IF: IncidentCreated
  IF->>Eng: NotificationSent
  IF->>IF: RecommendationGenerated
  Eng->>IF: Acknowledge
  Eng->>IF: Resolve with rollback summary
  IF->>IF: ReportingProjectionUpdated
  IF->>IF: PostmortemCreated
```
