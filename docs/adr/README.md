# Architecture decision records

The main choices behind IncidentFlow's services, written in [MADR](https://adr.github.io/madr/) form:
context, the decision, the consequences (good and bad), and how the decision is checked.

The reasons come from what was recorded when each choice was made: the master plan, the project timeline,
code comments, commit messages and the README. Where no reason was recorded, the record says so rather
than supplying one after the fact.

| ADR | Decision | Status |
|---|---|---|
| [0001](0001-database-per-service.md) | One database per service (four PostgreSQL databases in one local server) | Accepted |
| [0002](0002-rabbitmq-via-masstransit.md) | RabbitMQ through MassTransit, one queue per service and consumer, retry before `_error` | Accepted |
| [0003](0003-redis-for-transient-windows-only.md) | Redis only for transient time windows, never as a source of truth | Accepted |
| [0004](0004-yarp-gateway.md) | A YARP reverse proxy as the single HTTP entry | Accepted |
| [0005](0005-ddd-aggregate-for-incident-lifecycle.md) | The incident lifecycle lives in a DDD aggregate | Accepted |
| [0006](0006-transactional-outbox-and-inbox.md) | Transactional outbox and inbox through MassTransit's EF Core support | Accepted |
| [0007](0007-slack-through-standard-resilience-handler.md) | Slack is called through the standard HTTP resilience handler | Accepted |
| [0008](0008-reporting-as-a-projection-of-the-timeline.md) | Reporting is a rebuildable projection of the Timeline event log | Accepted |

## Adding a record

Copy the shape of an existing record, take the next number, and add a row above. A record is not
rewritten when the decision changes: write a new one and mark the old one "Superseded by NNNN".
