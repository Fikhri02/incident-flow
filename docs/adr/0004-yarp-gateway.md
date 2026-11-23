# 0004. A YARP reverse proxy as the single HTTP entry

- Status: Accepted
- Date: chosen 2026-04-28 (master plan); built 2026-05-17 (`af0bdff`, `c9efc11`); recorded 2026-11-23;
  reports route added and prototype routes removed 2026-11-23
- Deciders: Irfan

## Context and problem statement

Clients need one address for the signal, incident, timeline and reporting APIs, and a single place for edge
concerns: the correlation id and access control. The master plan named "Gateway: YARP reverse proxy". On
2026-05-17 the prototype's `HttpListener` gateway was replaced with an ASP.NET Core host, and as each service
was split out its routes moved from in-gateway handlers to YARP proxying.

Why YARP was chosen over another gateway (a separate proxy such as nginx or Envoy, or another .NET gateway
library) was not recorded.

## Considered options

No alternatives were recorded when this was chosen.

## Decision outcome

`src/IncidentFlow.ApiGateway` hosts YARP 2.2.0 with four routes, configured in code and loaded in memory:

- `/api/v1/signals/{**remainder}` to Ingestion, `/api/v1/incidents/{**remainder}` to Incident,
  `/api/v1/timeline/{**remainder}` to Timeline, `/api/v1/reports/{**catch-all}` to Reporting
  (`Program.cs:40-66`).
- Destinations come from the `services:<name>` configuration that Aspire injects (`Program.cs:20-38`).
- The gateway answers nothing itself except `/` (a description of the workflow) and the health endpoints.
  It used to host the `IncidentFlow.Core` prototype in process and serve 14 routes from it (reports, the
  outbox and inbox views, notification retry, readiness), which read in-memory stores the services never
  fed. Reports moved to the Reporting service; the rest, and the prototype, were removed (2026-11-23).
- Gateway middleware: a correlation-id middleware and an API-key guard (`x-api-key` checked against
  `INCIDENTFLOW_API_KEY`). The key stands in for the Identity service, which is deferred until the core
  workflow is stable (master plan, Identity And Access overview).
- The per-source rate limit is deliberately not in the gateway: the source is in the request body, which YARP
  forwards without reading (`SignalRateLimiter.cs`, `b4ff312`).

### Consequences

- Good, because clients see one address and one API-key check.
- Good, because the gateway is an ordinary ASP.NET Core app: its middleware is C#, and it gets the same
  ServiceDefaults (OpenTelemetry, health endpoints) as the services.
- Good, because routes live in code next to the destinations Aspire provides, with no separate proxy config.
- Bad, because the services' own endpoints are reachable directly and have no authentication, so a direct call
  skips the API key. The key is read from an environment variable and compared as a plain string; with it
  unset, everything is open.
- Before the hardening work (at `db92cf7`) a correlation id the gateway generated was returned to the client but
  not forwarded, so Ingestion generated a second one (review 2026-11-19). Now the gateway puts the id on the
  incoming request, so YARP forwards it and the services stamp it on their events, and sets it on the response
  only when the service has not already echoed it, so the client sees one value.
- Bad, because YARP's own proxy HttpClient does not go through the standard resilience handler (ADR 0007).
- Bad, because edge concerns that need the body, such as the rate limit, cannot live here without the gateway
  reading and buffering every request.

### How it is checked

- `tests/IncidentFlow.IntegrationTests/Gateway/GatewayCorrelationIdTests.cs` hosts the gateway with
  `WebApplicationFactory` and proxies `/api/v1/incidents/...` to a stub service on a real port. It checks that a
  generated id and a client's id are both forwarded and returned once, and that the gateway's own routes return
  an id.
- `tests/IncidentFlow.IntegrationTests/Gateway/GatewayReportingRoutesTests.cs` proxies every
  `/api/v1/reports/*` route (summary, mtta, mttr, severity-breakdown, top-failing-services,
  service-reliability, deployment-regressions, and `POST rebuild`) to a stub Reporting service with its query
  string intact, checks that each retired prototype route now answers 404, and that `/` no longer lists
  postmortems or recommendations.
- Not covered: the signals and timeline routes, and the API-key guard. This is a gap.
- The proxied services are tested directly (`tests/IncidentFlow.IntegrationTests`), not through the gateway.
