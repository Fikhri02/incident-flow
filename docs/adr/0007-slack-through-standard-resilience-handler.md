# 0007. Slack is called through the standard HTTP resilience handler

- Status: Accepted
- Date: resilience handler 2026-05-17 (ServiceDefaults); Slack client 2026-11-22 (`db92cf7`); recorded 2026-11-23
- Deciders: Irfan

## Context and problem statement

Until `db92cf7` Notification only logged `[NOTIFY]` for each new incident. While building a reliability
simulation on 2026-11-22 it was noticed that the circuit breaker described for the system had nothing real to
protect. Notification now posts each new incident to a Slack incoming webhook, the system's only outbound HTTP
dependency, and Slack can be slow or down.

The reason recorded in `SlackNotifier.cs:21-27`: go through the standard resilience handler to get "retries,
timeouts and a circuit breaker that fails fast while Slack is down", and throw when a call still fails "so
MassTransit retries the message and finally moves it to notification-incident-created_error, where it can be
redelivered".

## Considered options

No alternatives were recorded, for example a hand-written Polly pipeline or no HTTP-level retry at all.

## Decision outcome

- `ServiceDefaults.cs:26-30` adds `AddStandardResilienceHandler()` (Microsoft.Extensions.Http.Resilience 8.4.0)
  to every HttpClient from `IHttpClientFactory`, in all seven hosts. The package defaults are not overridden.
- Notification registers `SlackNotifier` as a typed client (`Notification.Worker/Program.cs:14-16`), so it gets
  the handler. It posts `{ text }` to `Notifications:Slack:WebhookUrl` (kept in user-secrets); with no URL it only
  logs.
- A non-success response throws. MassTransit then retries the message (ADR 0002) and finally parks it in
  `notification-incident-created_error`.

### Consequences

- Good, because there is no hand-written retry or breaker code, and every future outbound client gets the same
  protection by default.
- Good, because Slack failures stay inside Notification: it has its own queue, so Correlation and Timeline keep
  working (checked by hand on a real RabbitMQ, 2026-11-22).
- Good, because a post that cannot be delivered is parked with the message, not dropped.
- Bad, because the retry layers multiply: four HTTP attempts inside each of four message attempts gives 16 calls,
  about 45 s against a Slack stub answering 503, before the message reaches `_error`.
- Bad, because the package defaults were not tuned for this client. The breaker needs at least 100 calls in a
  30 s window before it can open, which a low incident rate rarely reaches, so in practice the retries do the
  work and the breaker seldom trips.
- Bad, because the handler retries the POST. If Slack accepted a post but the response was lost, the retry posts
  the same message again. Message-level dedupe (ADR 0006) does not cover this.
- Bad, because YARP's proxy client in the gateway does not use this handler (ADR 0004).
- Neutral: PagerDuty and email notifications are not built.

### How it is checked

- `tests/IncidentFlow.UnitTests/Notifications/SlackNotifierTests.cs`: `Posts_the_incident_to_the_webhook`,
  `Without_a_webhook_it_only_logs`, `A_failed_post_throws_so_the_message_is_retried` (a recording
  `HttpMessageHandler`, no network).
- The resilience handler wiring and the breaker behaviour are not covered by an automated test. The 16 calls and
  45 s were measured by hand on 2026-11-22.
