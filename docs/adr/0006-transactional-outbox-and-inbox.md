# 0006. Transactional outbox and inbox through MassTransit's EF Core support

- Status: Accepted (2026-11-23, with the reliable-messaging work)
- Date: rule 2026-04-28 (master plan); approach chosen 2026-11-23 (hardening design); recorded 2026-11-23
- Deciders: Irfan

## Context and problem statement

At `db92cf7` every service that changes state saves, then publishes, as two steps:
`Ingestion.Api/Endpoints/SignalEndpoints.cs:142-144`, `Incident.Api/Consumers/AnomalyDetectedConsumer.cs:31-33`
and `Incident.Api/Endpoints/IncidentEndpoints.cs:78-79`. A crash between the two loses the event.
RabbitMQ delivers at least once (ADR 0002), and redeliveries are not absorbed everywhere: Timeline appends a
duplicate row, Notification would post to Slack again, and the Incident consumer can open two incidents for
one key under concurrency.

The master plan set the rules on day one: "Any service that saves state and publishes an event must use
outbox" and "Every message consumer must be idempotent". Until now the outbox and inbox existed only as
simulated lists in the `IncidentFlow.Core` prototype.

## Considered options

Recorded in the hardening design (2026-11-23):

- MassTransit's built-in EF Core outbox and inbox: chosen, because it is less code and battle-tested.
- A hand-rolled outbox table and dispatcher: not chosen. It would show the pattern more explicitly to a reader
  of the code, which is its one advantage here.

## Decision outcome

- Transactional outbox (MassTransit `AddEntityFrameworkOutbox` with the bus outbox) where a service saves and
  publishes: Ingestion API, Incident API and its anomaly consumer. The event row is written in the same
  transaction as the state change and delivered afterwards.
- Consumer inbox (MassTransit EF Core) where a consumer writes to its own database: Incident and Timeline. A
  redelivered message with the same MessageId is skipped while its inbox row is kept: MassTransit's default
  `DuplicateDetectionWindow` of 30 minutes, which neither service overrides (`IncidentMessaging.cs`,
  `TimelineMessaging.cs`).
- Workers without a database (Detection, Correlation) use MassTransit's in-memory outbox, so they publish only
  after the consume succeeds.
- Notification dedupes by MessageId in Redis with a two-state marker (`SentNotifications.cs`): SET NX
  "in-flight" for 90 s (longer than the 30 s total timeout of the standard resilience handler), then "sent" for
  24 h once Slack accepted the post. A redelivery that finds "sent" is skipped; one that finds "in-flight" throws,
  so the message is retried (or parked in `_error`) instead of acked; a failed post gives the claim back.
- A partial unique index on the active incident key (service, environment, fingerprint where the status is open
  or acknowledged) closes the duplicate-incident race.
- MassTransit's envelope (MessageId, CorrelationId, ConversationId, SentTime, MessageType) is the message
  envelope; the unused `IntegrationEvent` record is deleted.

### Consequences

- Good, because an event is published if and only if the state change it describes was committed.
- Good, because redeliveries are absorbed where they would do harm: one timeline row, one incident, one Slack
  post, within the windows below.
- Good, because the code to maintain is configuration, not a custom dispatcher.
- Bad, because each database gains outbox and inbox tables and a migration, and they need cleaning up
  (MassTransit removes delivered rows; inbox rows are kept for the 30-minute duplicate window).
- Bad, because delivery from the outbox is asynchronous, which adds a small delay between commit and publish.
- Bad, because the pattern is hidden behind MassTransit configuration; a reader sees less of how it works than
  with a hand-rolled outbox, and the persistence model is tied to MassTransit.
- Bad, because the inbox remembers a MessageId for 30 minutes after it was consumed. A redelivery later than
  that, typically a replay from `_error`, is consumed again: Timeline appends a second row, and Incident skips it
  only while the incident is still active (if it was resolved or closed meanwhile, a new incident opens).
- Bad, because Notification is at least once, not exactly once. A worker that dies after Slack accepted the post
  but before writing "sent" leaves the "in-flight" claim to expire, and the redelivery posts again. The other
  choice, acking a redelivery that finds "in-flight", could lose the notification for good if the claimer died
  before posting; a rare duplicate was judged better than a lost alert. MassTransit's retries span about 6 s,
  shorter than the 90 s claim, so such a message usually lands in `notification-incident-created_error` and is
  posted when it is replayed.
- Bad, because the Notification dedupe lives in Redis. Losing Redis within the TTL could let one duplicate post
  through, and it catches redeliveries of the same message only, not two different messages about one incident.
- Bad, because the in-memory outbox in the workers holds publishes only for the current consume; the workers'
  own state changes (Redis windows) are not rolled back if the consume fails.

### How it is checked

Each of these was written to fail first (`tests/IncidentFlow.IntegrationTests` unless noted):

- Outbox in the same transaction: `Ingestion/IngestionOutboxTests.PostError_WritesTheEventToTheOutbox_WithTheSignal`,
  `Ingestion/IngestionOutboxTests.FailedSave_LeavesNoOutboxMessage`,
  `Incidents/IncidentOutboxTests.Acknowledge_WritesTheEventToTheOutbox_WithTheIncident`.
- Inbox: `Timeline/TimelineInboxTests.RedeliveredMessage_AppendsOneRow`,
  `Incidents/IncidentMessagingTests.RedeliveredAnomaly_DoesNotOpenASecondIncident`.
- One active incident per key: `Incidents/IncidentMessagingTests.TwoSavesForTheSameKey_LeaveOneActiveIncident`,
  `Incidents/IncidentMessagingTests.ConcurrentAnomalies_OpenOneIncident_AndTheLoserIsNotAFault`.
- Notification dedupe: `Notifications/NotificationDedupeTests.RedeliveredIncident_PostsToSlackOnce`,
  `FailedPost_IsRetried_AndNotMarkedAsSent`, `RedeliveryWhileClaimIsInFlight_Faults_InsteadOfBeingSkipped`,
  `RedeliveryWhileClaimIsInFlight_IsRetried_AndPostsOnceTheClaimExpires`,
  `ClaimWhilePosting_IsShortLived_ThenBecomesSentForADay`, `ReleaseFailure_StillSurfacesTheOriginalException`.
- Existing duplicates before the unique index: `Incidents/ActiveIncidentIndexMigrationTests`.
- Correlation id forwarded by the gateway: `Gateway/GatewayCorrelationIdTests`.
- The 30-minute inbox window is MassTransit's default and is not covered by a test.
- In-memory outbox for workers without a database:
  `Messaging/InMemoryOutboxTests.ConsumerThatThrowsAfterPublishing_PublishesNothing` (Detection and Correlation).
- Correlation id in the envelope: `Ingestion/IngestionOutboxTests.PublishedEvent_CarriesTheRequestCorrelationIdInTheEnvelope`,
  `Incidents/IncidentOutboxTests.PublishedEvent_CarriesTheIncidentCorrelationIdInTheEnvelope`.
