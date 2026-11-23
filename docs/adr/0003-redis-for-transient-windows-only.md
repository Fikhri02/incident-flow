# 0003. Redis only for transient time windows

- Status: Accepted
- Date: rule 2026-04-28 (master plan); Redis windows 2026-05-17 (`88aaf60`); recorded 2026-11-23
- Deciders: Irfan

## Context and problem statement

Detection raises an anomaly when the same error repeats five times within five minutes. Correlation groups
incidents that share a deployment or a fingerprint within ten minutes. Both need a short-lived, shared window.

The first Detection worker kept the window in process memory (`af0bdff`). It was replaced by a Redis sorted set
because a Redis window "survives worker restarts and has no cold-start race" (README, Timeline 2026-05-17).

The master plan set the boundary: "Redis is used for transient state only: dedupe, rate limits, windows,
candidate clusters, notification timers" and "PostgreSQL is the source of truth for service-owned state".
The reason recorded is that this state is disposable.

## Considered options

- An in-memory window in each worker: the first version; replaced because it was lost on restart and
  started empty.
- A Redis sorted set shared by all worker instances: chosen.

## Decision outcome

Redis holds only windows that can be lost without losing data:

- Detection: `spike:{service}:{env}:{fingerprint}`, scored by the signal's `occurredAt`. Each signal adds
  itself, trims entries older than five minutes and counts; at five the key is deleted and an anomaly is raised
  (`src/Services/IncidentFlow.Services.Detection.Worker/Detection/SpikeDetector.cs`).
- Correlation: `corr:deploy:{env}:{deploymentId}` and `corr:fingerprint:{env}:{fingerprint}`, ten minutes,
  threshold two (`Correlation.Worker/Correlation/CorrelationEngine.cs`).
- Notification (added with the hardening work): `notification:sent:{messageId}`, a dedupe marker. It is
  "in-flight" for 90 s while the Slack post runs, then "sent" for 24 h
  (`Notification.Worker/Notifications/SentNotifications.cs`). Losing it can at worst let one post repeat; it is
  not a record of what was sent.
- Detection, Correlation and Notification reference Redis (`src/IncidentFlow.AppHost/Program.cs`). Every record
  of what happened is in a service's PostgreSQL database.

### Consequences

- Good, because a worker restart keeps the window, and several worker instances share one window.
- Good, because Redis can be flushed or lost at any time: the cost is the windows in progress, not data.
- Good, because the signal id is the sorted-set member, so a redelivered signal does not count twice.
- Good, because Detection's window update is one Lua script (add, expire, trim, count, and clear at the threshold), so Redis
  runs it as a single step and each five signals raise one anomaly however they are consumed. Until 2026-11-23
  it was separate commands; signals consumed at the same moment each counted five, and the AppHost run that day
  raised five anomalies from one burst of five (first noted in the Signal Intelligence review, 2026-11-19).
- Bad, because Correlation's window update is still separate commands, so concurrent incidents for one key can
  each see the group (see its republishing in the limits).
- Before the hardening work (at `db92cf7`) the window keys had no TTL: an idle key stayed until the next write
  trimmed it. Now every write sets a TTL of the window plus one minute (`SpikeDetector.KeyTtl`,
  `CorrelationEngine.KeyTtl`). Correlation keys are still not deleted when they fire; they expire.
- Bad, because the TTL is wall-clock time from the last write while the window is event time (scored by the
  signal's `occurredAt` or the incident's `DetectedAt`). When delivery lags the events by more than the
  one-minute margin (a broker outage, a replay from `_error`), a key can expire while its events would still
  count, and a spike or correlation is missed. Notification's 24 h "sent" marker is wall-clock too: a replay
  from `_error` more than a day after the post posts again.
- Bad, because losing Redis mid-window can miss a spike that was in progress.
- Bad, because it is one more piece of infrastructure to run, for two workers.
- Neutral: the rule lists more uses than exist. Ingestion dedupe is in PostgreSQL and the rate limit is in
  Ingestion's memory, per instance (`SignalRateLimiter.cs`). The rule limits what Redis may hold; it is not a
  list of what it holds.

### How it is checked

- `tests/IncidentFlow.IntegrationTests/Detection/SpikeDetectorTests.cs` and
  `Correlation/CorrelationEngineTests.cs` run the real window code against a Testcontainers Redis.
- `tests/IncidentFlow.Specs/Features/DetectionThreshold.feature` states the threshold in plain language and
  runs it against a real Redis.
- One anomaly per five signals under concurrency: `Detection/SpikeDetectorTests.ConcurrentBurstOfFive_RaisesExactlyOneSpike`
  and `ConcurrentBurstOfTen_RaisesTwoSpikes`.
- Window TTLs: `Detection/SpikeDetectorTests.WindowKey_ExpiresShortlyAfterTheWindow` and
  `Correlation/CorrelationEngineTests.WindowKeys_ExpireShortlyAfterTheWindow`.
- Notification markers: `Notifications/NotificationDedupeTests` (against a Testcontainers Redis), including
  `ClaimWhilePosting_IsShortLived_ThenBecomesSentForADay` and `SentMarker_ExpiresAfterADay`.
- "Transient only" is not checked by a test. It holds because only Detection, Correlation and Notification
  reference Redis, and each writes only its windows or markers there, all with a TTL.
