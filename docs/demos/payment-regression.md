# Payment Regression Demo

This demo proves the main IncidentFlow workflow with no external dependencies.

## Run With Smoke Runner

```bash
dotnet run --project tests/IncidentFlow.SmokeTests
```

The runner covers:

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
