# 0002. RabbitMQ through MassTransit, one queue per service, retry before `_error`

- Status: Accepted
- Date: RabbitMQ and MassTransit 2026-05-17; queue naming and retry 2026-11-22 (`9b45621`); recorded 2026-11-23
- Deciders: Irfan

## Context and problem statement

Services must hear about each other's state changes without calling each other. The master plan picked
RabbitMQ for messaging and asked for "dead-letter queues and retry policy from the start of messaging work".
The README states the rule: "All inter-service communication is asynchronous via MassTransit over RabbitMQ.
No service calls another directly."

Why MassTransit was chosen over the plain RabbitMQ client, or over another library, was not recorded.

A code review on 2026-11-19 found a High issue: no service set an endpoint name formatter, so the three
`IncidentCreatedConsumer` classes (Correlation, Notification, Timeline) shared one queue named after the class.
RabbitMQ gave each message to only one of them. The same was true for `ErrorSignal` and `AnomalyDetected`.

## Considered options

For queue naming (recorded in the review and in `MessagingDefaults.cs`):

- MassTransit's default names (one queue per consumer class name): rejected, because consumers with the same
  class name in different services compete for messages.
- A service-name prefix on every queue: chosen.

For the transport library, no alternatives were recorded.

## Decision outcome

- MassTransit 8.3.6 with the RabbitMQ transport in all six services.
- Shared setup in `src/IncidentFlow.BuildingBlocks/Messaging/MessagingDefaults.cs`, added "so every service
  shares one queue-naming and retry setup" (Timeline 2026-11-22):
  - `UseServiceEndpointNames("<service>")`: queues are `<service>-<consumer>` in kebab case, for example
    `notification-incident-created` and `detection-error-signal`. Every service gets its own copy of every
    event it consumes.
  - `UseDefaultRetry()`: a failing message is retried after 200 ms, 1 s and 5 s, then MassTransit moves it to
    `<queue>_error`. The retry was set for transient failures; the `_error` queue parks poison messages.

### Consequences

- Good, because each event reaches every service that consumes it (fan-out), which was not true before `9b45621`.
- Good, because a failing consumer only fills its own queue. With Slack answering 503, Notification's message
  reached `notification-incident-created_error` after about 45 s while Correlation kept consuming (checked by
  hand on a real RabbitMQ, 2026-11-22).
- Good, because a message that keeps failing is parked, not lost, and the conventions live in one file.
- Bad, because the retries are immediate, in-process and short (about 6 s in total). An outage longer than
  that sends every message to `_error`; there is no delayed redelivery and no tooling to move messages back.
- Bad, because retry layers multiply. Notification's HTTP resilience handler retries inside each message
  attempt, so one failing Slack post costs 16 calls (ADR 0007).
- Bad, because delivery is at least once, so every consumer has to absorb redeliveries. Before the hardening
  work (at `db92cf7`) Timeline appended a duplicate row and Notification would post twice. Now Incident and
  Timeline skip a MessageId they already consumed (EF Core inbox), and Notification skips a message it already
  posted (a Redis marker), so a redelivery no longer posts twice; the limits of both are in ADR 0006.
- Bad, because brokers created before `9b45621` keep the old shared queues (`IncidentCreated`, `ErrorSignal`,
  `AnomalyDetected`), which have to be deleted by hand.
- Bad, because the services depend on MassTransit's conventions (message URNs from the contract namespace,
  its envelope). Moving or renaming a contract type changes its exchange.
- Note (not from the project records): the MassTransit project announced in 2025 that version 9 is
  commercially licensed, while v8 stays open source. Check licensing before upgrading past 8.x.

### How it is checked

- `tests/IncidentFlow.UnitTests/Messaging/EndpointNameTests.cs`: `Queue_names_start_with_the_service_name` and
  `Same_consumer_class_in_three_services_gets_three_queues`.
- `tests/IncidentFlow.Contracts.Tests` pins the JSON shape of every contract that crosses the broker.
- The integration tests run each service's real consumer and endpoint registration (`AddIngestionMessaging`,
  `AddIncidentMessaging`, `AddTimelineMessaging`, `AddDetectionMessaging`, `AddCorrelationMessaging`,
  `AddNotificationMessaging`) on MassTransit's test harness with the in-memory transport, configured with the
  same `UseDefaultRetry` and correlation-id filter as the services (`tests/IncidentFlow.IntegrationTests/Shared/TestBus.cs`).
  The retry is exercised by `Notifications/NotificationDedupeTests.FailedPost_IsRetried_AndNotMarkedAsSent`.
- Not automated: the move to `_error` and the queue names on a real broker. No test runs a real RabbitMQ round
  trip; these were checked by hand on a real RabbitMQ on 2026-11-22.
