> This document describes the retired single-process prototype. For what runs today see the README and docs/adr.

# Payment Regression Demo

This demo proves the main IncidentFlow workflow with no external dependencies.

## What the prototype's smoke runner covered

The smoke runner (`tests/IncidentFlow.SmokeTests`) was deleted with the prototype. To run the services today,
start the Aspire AppHost (`dotnet run --project src/IncidentFlow.AppHost`) and use the suites listed in the
README (`dotnet test IncidentFlow.sln`). The runner covered:

- deployment signal
- error spike
- incident creation
- notification fanout
- recommendation generation
- acknowledgement and resolution
- postmortem generation and edit
- duplicate suppression
- log and trace signal paths
- SEV1 health outage escalation
- reporting filters
- outbox and inbox tracking

## Run With HTTP API

```bash
dotnet run --project src/IncidentFlow.ApiGateway
```

Then run:

```bash
./docs/demos/payment-regression.sh
```

## Expected Outcome

Repeated payment authorization timeouts create one incident for `Payment API`, attach deployment context, generate rollback and dependency recommendations, send simulated notifications, and produce reporting/postmortem outputs after resolution.
