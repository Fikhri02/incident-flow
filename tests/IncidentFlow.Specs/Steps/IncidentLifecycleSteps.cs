using FluentAssertions;
using IncidentFlow.Services.Incident.Api.Domain;
using IncidentFlow.Services.Incident.Api.Domain.Events;
using Reqnroll;

namespace IncidentFlow.Specs.Steps;

/// <summary>Drives the real <see cref="Incident"/> aggregate from Incident.Api/Domain.</summary>
[Binding]
[Scope(Feature = "Incident lifecycle")]
public sealed class IncidentLifecycleSteps
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    private Incident _incident = null!;
    private readonly List<IIncidentDomainEvent> _events = [];
    private int _eventsBeforeAttempt;
    private Exception? _refusal;
    private int _minute;

    [Given("an incident opened for {string} in {string} with fingerprint {string}")]
    public void GivenAnIncidentOpened(string service, string environment, string fingerprint)
    {
        var details = new IncidentDetails(
            Region: null, DeploymentId: null, Severity: "high", RuleName: "ErrorSpike",
            Summary: $"5 errors in 5 min for {service} [{fingerprint}]",
            DetectedAt: Start, CorrelationId: "corr-spec", SignalIds: [Guid.NewGuid()]);

        _incident = Incident.Open(new IncidentKey(service, environment, fingerprint), details, Start);
        CollectEvents();
    }

    [Given("the incident is {string}")]
    public void GivenTheIncidentIs(string status)
    {
        // Reach the status through the transitions the aggregate allows.
        switch (status)
        {
            case "open":
                break;
            case "acknowledged":
                Acknowledge("dana");
                break;
            case "resolved":
                Acknowledge("dana");
                Resolve("dana");
                break;
            case "closed":
                Acknowledge("dana");
                Resolve("dana");
                Close();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown status in the feature file.");
        }

        _incident.Status.ToWire().Should().Be(status);
    }

    [When("{string} acknowledges the incident")]
    public void WhenAcknowledges(string person) => Acknowledge(person);

    [When("{string} resolves the incident")]
    public void WhenResolves(string person) => Resolve(person);

    [When("the incident is closed")]
    public void WhenClosed() => Close();

    [When("someone tries to {word} the incident")]
    public void WhenSomeoneTriesTo(string change)
    {
        _eventsBeforeAttempt = _events.Count;
        _refusal = Attempt(() =>
        {
            switch (change)
            {
                case "acknowledge": Acknowledge("dana"); break;
                case "resolve": Resolve("dana"); break;
                case "close": Close(); break;
                default: throw new ArgumentOutOfRangeException(nameof(change), change, "Unknown change in the feature file.");
            }
        });
    }

    [When("someone acknowledges the incident without giving a name")]
    public void WhenAcknowledgedWithoutAName()
    {
        _eventsBeforeAttempt = _events.Count;
        _refusal = Attempt(() => Acknowledge("  "));
    }

    [Then("the incident is {string}")]
    [Then("the incident is still {string}")]
    public void ThenTheIncidentIs(string status) =>
        _incident.Status.ToWire().Should().Be(status);

    [Then("it was acknowledged and resolved by {string}")]
    public void ThenAcknowledgedAndResolvedBy(string person)
    {
        _incident.AcknowledgedBy.Should().Be(person);
        _incident.ResolvedBy.Should().Be(person);
        _incident.AcknowledgedAt.Should().NotBeNull();
        _incident.ResolvedAt.Should().BeAfter(_incident.AcknowledgedAt!.Value);
        _incident.ClosedAt.Should().BeAfter(_incident.ResolvedAt!.Value);
    }

    [Then("the incident has recorded these events:")]
    public void ThenTheIncidentHasRecordedTheseEvents(DataTable table)
    {
        var expected = table.Rows.Select(row => row["event"]).ToList();
        _events.Select(e => e.GetType().Name).Should().Equal(expected);
        _events.Should().OnlyContain(e => e.IncidentId == _incident.IncidentId);
    }

    [Then("the change is refused with {string}")]
    public void ThenTheChangeIsRefusedWith(string reason)
    {
        // IncidentTransitionException is what Incident.Api maps to 409 Conflict.
        _refusal.Should().BeOfType<IncidentTransitionException>()
            .Which.Message.Should().Be(reason);
    }

    [Then("the request is rejected as invalid")]
    public void ThenRejectedAsInvalid()
    {
        // ArgumentException is what Incident.Api maps to 400 Bad Request.
        _refusal.Should().BeOfType<ArgumentException>();
        _events.Should().HaveCount(_eventsBeforeAttempt);
    }

    [Then("no new event is recorded")]
    public void ThenNoNewEventIsRecorded()
    {
        CollectEvents();
        _events.Should().HaveCount(_eventsBeforeAttempt);
    }

    private void Acknowledge(string person)
    {
        _incident.Acknowledge(person, Next());
        CollectEvents();
    }

    private void Resolve(string person)
    {
        _incident.Resolve(person, Next());
        CollectEvents();
    }

    private void Close()
    {
        _incident.Close(Next());
        CollectEvents();
    }

    private static Exception? Attempt(Action change)
    {
        try
        {
            change();
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private DateTimeOffset Next() => Start.AddMinutes(++_minute);

    // The service dequeues the events and publishes them through its outbox; the spec only collects them.
    private void CollectEvents() => _events.AddRange(_incident.DequeueDomainEvents());
}
