# 0008. Reporting is a rebuildable projection of the Timeline event log

- Status: Accepted
- Date: 2026-11-23
- Deciders: Irfan

## Context and problem statement

Until 2026-11-23 the `/api/v1/reports/*` routes were answered inside the gateway by `IncidentFlow.Core`, the
single-process prototype. Its figures came from the prototype's in-memory incidents, so they never saw an
incident the Incident service created, and its "deployment regression" count matched the word "Deployment" in
a root-cause string. The reports needed to come from what the services actually record.

## Considered options

- Drop reporting and list it as not built.
- Query the Incident service's database from a reporting endpoint (breaks ADR 0001).
- A Reporting service that keeps its own read model from events: chosen.

## Decision outcome

`Reporting.Api` owns `reporting-db` and one row per incident, built by consuming `IncidentCreated`,
`IncidentAcknowledged`, `IncidentResolved`, `IncidentClosed` and `CorrelationFound`. An incident counts as a
deployment regression only when Correlation links it to a deployment (`CorrelationType == "deployment"`).
The gateway routes `/api/v1/reports/{**catch-all}` to it (ADR 0004), and `IncidentFlow.Core` was deleted.

The read model can be recomputed: `POST /api/v1/reports/rebuild` replays every event Timeline has stored,
through Timeline's paged `GET /api/v1/timeline/events` (ordered by an identity `Sequence`, with the original
message as JSON), into the same projection the consumers use. Timeline is therefore the system's event log, and
Reporting a projection of it.

- By default the rebuild deletes nothing. It replays over the read model through the idempotent upserts, so
  what Timeline stored is applied again and a row Timeline has not stored yet (an event Reporting consumed
  before Timeline did) is kept.
- `?clean=true` empties the read model first, then replays. Run it only once Timeline has caught up with the
  broker, or it drops incidents Timeline has not stored yet; the figures are partial while it runs. The delete
  commits with the first page, and only after that page was read, so a Timeline that cannot be read leaves the
  read model untouched.
- Each page commits in its own short transaction. Live consumers keep running and wait behind one page at
  most, never behind the whole rebuild. A rebuild that fails part way, or is stopped, keeps the pages it
  committed, which is a consistent read model; running it again finishes the job. It runs on the host's
  shutdown token rather than the request's, so a client that gives up does not stop it half way.
- A Postgres session advisory lock, held on its own connection for the whole run, keeps two rebuilds from
  overlapping, on one instance or several: a second `POST /rebuild` while one runs returns `409 Conflict`.

## Consequences

- Good, because the figures come from the same events every other service sees, and the definitions (MTTA and
  MTTR from detection, open = not resolved and not closed) are unchanged from the prototype.
- Good, because a bug in a report can be fixed and the history recomputed with one call, without touching the
  services that own the data.
- Good, because each event is applied as one `INSERT … ON CONFLICT DO UPDATE` that sets only its own columns,
  so events for one incident can arrive in any order and applying one twice changes nothing.
- Bad, because the reports are eventually consistent: an incident is counted only once its `IncidentCreated`
  has been consumed (earlier events are kept and filled in).
- Bad, because the rebuild is the one synchronous call between services (Reporting to Timeline over HTTP). If
  Timeline cannot be read the rebuild returns 502; the pages it had already committed stay.
- Bad, because Timeline rows recorded before the payload column existed have no message to replay; the rebuild
  skips and logs them.
- Bad, because a `Sequence` is assigned on insert, so a row that commits late can land behind a cursor a live
  follower has already passed. The rebuild replays from the start, but an entry whose transaction commits behind
  the rebuild's cursor while it runs is missed by that run; the live consumer still applies it, the default
  rebuild never removes it, and the next rebuild replays it.
- Bad, because a clean rebuild depends on Timeline being complete: run while Timeline's consumer is behind, it
  drops the incidents Timeline has not stored until they arrive again or a later rebuild replays them.

## How it is checked

`tests/IncidentFlow.IntegrationTests`:

- Figures and filters: `Reporting/ReportFiguresTests`.
- Inbox and ordering: `Reporting/ReportingDeliveryTests` (`RedeliveredEvent_IsConsumedOnce_ByTheInbox`,
  `LifecycleEventsBeforeCreated_AreKept_AndCountOnceCreatedArrives`,
  `AllEventsPublishedTogether_GiveTheSameFigures_WhateverOrderTheyAreConsumedIn`).
- Rebuild, with real Timeline and Reporting hosts: `Reporting/ReportRebuildTests`
  (`Rebuild_FromTimeline_EqualsTheLiveProjection`, `Rebuild_KeepsRowsTimelineHasNotStoredYet`,
  `CleanRebuild_ClearsRowsTimelineNeverRecorded`, `SecondRebuild_WhileOneIsRunning_IsRefusedWith409`,
  `LiveEvent_ConsumedWhileARebuildIsRunning_IsAppliedWithoutWaiting_AndKept`,
  `Rebuild_AppliesAnIncidentCreatedWhoseTextFieldsAreNull`).
- Timeline's event log: `Timeline/TimelineEventsEndpointTests`, `Timeline/TimelineSequenceMigrationTests`.
- Gateway routing: `Gateway/GatewayReportingRoutesTests`.
