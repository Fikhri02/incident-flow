using FluentAssertions;
using IncidentFlow.Services.Incident.Api.Domain;
using IncidentFlow.Services.Incident.Api.Domain.Events;
using Xunit;

namespace IncidentFlow.UnitTests.Incidents;

// Pure domain tests: no database, broker or Docker.
public sealed class IncidentAggregateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);

    private static Incident OpenIncident()
    {
        var key = new IncidentKey("payment-api", "prod", "payment-timeout");
        var details = new IncidentDetails(
            "ap-southeast-1", "deploy-42", "high", "ErrorSpike", "5 errors in 5 minutes",
            T0, "corr-1", [Guid.NewGuid(), Guid.NewGuid()]);
        return Incident.Open(key, details, T0);
    }

    private static Incident InStatus(IncidentStatus status)
    {
        var incident = OpenIncident();
        if (status == IncidentStatus.Acknowledged) incident.Acknowledge("aina", T0);
        if (status == IncidentStatus.Resolved) incident.Resolve("aina", T0);
        if (status == IncidentStatus.Closed) incident.Close(T0);
        incident.DequeueDomainEvents();
        return incident;
    }

    [Fact]
    public void Open_StartsOpen_AndRecordsOpenedEvent()
    {
        var incident = OpenIncident();

        incident.Status.Should().Be(IncidentStatus.Open);
        incident.Key.Should().Be(new IncidentKey("payment-api", "prod", "payment-timeout"));
        incident.SignalIds.Should().HaveCount(2);
        incident.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<IncidentWasOpened>()
            .Which.IncidentId.Should().Be(incident.IncidentId);
    }

    [Fact]
    public void Key_RequiresServiceAndEnvironment()
    {
        var noService = () => new IncidentKey(" ", "prod", "fp");
        var noEnvironment = () => new IncidentKey("payment-api", "", "fp");

        noService.Should().Throw<ArgumentException>();
        noEnvironment.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Key_IsEqualByValue()
    {
        new IncidentKey("a", "prod", null).Should().Be(new IncidentKey("a", "prod", null));
        new IncidentKey("a", "prod", "x").Should().NotBe(new IncidentKey("a", "prod", "y"));
    }

    [Fact]
    public void Acknowledge_FromOpen_SetsWhoAndWhen_AndRecordsEvent()
    {
        var incident = InStatus(IncidentStatus.Open);
        var at = T0.AddMinutes(3);

        incident.Acknowledge("aina", at);

        incident.Status.Should().Be(IncidentStatus.Acknowledged);
        incident.AcknowledgedBy.Should().Be("aina");
        incident.AcknowledgedAt.Should().Be(at);
        incident.DomainEvents.Should().ContainSingle()
            .Which.Should().Be(new IncidentWasAcknowledged(incident.IncidentId, "aina", at));
    }

    [Theory]
    [InlineData(IncidentStatus.Acknowledged, "acknowledged")]
    [InlineData(IncidentStatus.Resolved, "resolved")]
    [InlineData(IncidentStatus.Closed, "closed")]
    public void Acknowledge_OnlyFromOpen(IncidentStatus from, string wire)
    {
        var incident = InStatus(from);

        var act = () => incident.Acknowledge("aina", T0);

        act.Should().Throw<IncidentTransitionException>()
            .WithMessage($"Cannot acknowledge incident in status '{wire}'.");
        incident.Status.Should().Be(from);
        incident.DomainEvents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(IncidentStatus.Open)]
    [InlineData(IncidentStatus.Acknowledged)]
    public void Resolve_FromOpenOrAcknowledged(IncidentStatus from)
    {
        var incident = InStatus(from);

        incident.Resolve("ravi", T0.AddMinutes(10));

        incident.Status.Should().Be(IncidentStatus.Resolved);
        incident.ResolvedBy.Should().Be("ravi");
        incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentWasResolved>();
    }

    [Theory]
    [InlineData(IncidentStatus.Resolved, "resolved")]
    [InlineData(IncidentStatus.Closed, "closed")]
    public void Resolve_RejectedWhenAlreadyFinished(IncidentStatus from, string wire)
    {
        var incident = InStatus(from);

        var act = () => incident.Resolve("ravi", T0);

        act.Should().Throw<IncidentTransitionException>().WithMessage($"Incident is already '{wire}'.");
    }

    [Theory]
    [InlineData(IncidentStatus.Open)]
    [InlineData(IncidentStatus.Acknowledged)]
    [InlineData(IncidentStatus.Resolved)]
    public void Close_FromAnyStatusExceptClosed(IncidentStatus from)
    {
        var incident = InStatus(from);

        incident.Close(T0.AddHours(1));

        incident.Status.Should().Be(IncidentStatus.Closed);
        incident.ClosedAt.Should().Be(T0.AddHours(1));
        incident.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<IncidentWasClosed>();
    }

    [Fact]
    public void Close_Twice_IsRejected()
    {
        var incident = InStatus(IncidentStatus.Closed);

        var act = () => incident.Close(T0);

        act.Should().Throw<IncidentTransitionException>().WithMessage("Incident is already closed.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Acknowledge_And_Resolve_NeedAPerson(string? who)
    {
        var acknowledge = () => InStatus(IncidentStatus.Open).Acknowledge(who!, T0);
        var resolve = () => InStatus(IncidentStatus.Open).Resolve(who!, T0);

        acknowledge.Should().Throw<ArgumentException>().WithMessage("Acknowledged by is required.");
        resolve.Should().Throw<ArgumentException>().WithMessage("Resolved by is required.");
    }

    [Fact]
    public void DequeueDomainEvents_ReturnsThemOnce()
    {
        var incident = OpenIncident();
        incident.Acknowledge("aina", T0);

        incident.DequeueDomainEvents().Should().HaveCount(2);
        incident.DomainEvents.Should().BeEmpty();
        incident.DequeueDomainEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData(IncidentStatus.Open, true)]
    [InlineData(IncidentStatus.Acknowledged, true)]
    [InlineData(IncidentStatus.Resolved, false)]
    [InlineData(IncidentStatus.Closed, false)]
    public void ActiveMeansStillBeingWorked(IncidentStatus status, bool active)
    {
        status.IsActive().Should().Be(active);
        Incident.IsActive.Compile()(InStatus(status)).Should().Be(active);
    }

    [Theory]
    [InlineData(IncidentStatus.Open, "open")]
    [InlineData(IncidentStatus.Acknowledged, "acknowledged")]
    [InlineData(IncidentStatus.Resolved, "resolved")]
    [InlineData(IncidentStatus.Closed, "closed")]
    public void Status_RoundTripsThroughItsStoredText(IncidentStatus status, string wire)
    {
        status.ToWire().Should().Be(wire);
        IncidentStatusExtensions.FromWire(wire).Should().Be(status);
    }
}
