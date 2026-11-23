using System.Net.Http.Json;
using FluentAssertions;
using IncidentFlow.IntegrationTests.Shared;
using IncidentFlow.Services.Reporting.Api.Consumers;
using IncidentFlow.Services.Reporting.Api.Data;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static IncidentFlow.IntegrationTests.Reporting.KnownIncidents;

namespace IncidentFlow.IntegrationTests.Reporting;

// RabbitMQ delivers at least once and in no promised order across queues. A redelivery must count once,
// and an event that overtakes IncidentCreated must still be in the figures once Created arrives.
public sealed class ReportingDeliveryTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task RedeliveredEvent_IsConsumedOnce_ByTheInbox()
    {
        var database = postgres.NewDatabase();
        await using var host = await ConsumerHost<ReportingDbContext>.StartAsync(database, x => x.AddReportingMessaging());
        var created = Created(Guid.NewGuid(), "payment-api", "prod", "High");
        var messageId = NewId.NextGuid();

        await host.Harness.Bus.Publish(created, ctx => ctx.MessageId = messageId);
        await host.Harness.Bus.Publish(created, ctx => ctx.MessageId = messageId);
        await host.Harness.InactivityTask;

        (await OutboxRows.CountAsync(database, """SELECT count(*) FROM "InboxState" WHERE "MessageId" = @id""",
            ("id", messageId))).Should().Be(1, "the inbox records the MessageId once and skips the redelivery");

        using var api = new ReportingApiFactory(database);
        var summary = await api.CreateClient().GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
        summary!.TotalIncidents.Should().Be(1);
    }

    [Fact]
    public async Task RedeliveredAcknowledgement_DoesNotChangeTheFigures()
    {
        var database = postgres.NewDatabase();
        await using var host = await ConsumerHost<ReportingDbContext>.StartAsync(database, x => x.AddReportingMessaging());
        var id = Guid.NewGuid();
        var ack = Acknowledged(id, "payment-api", "prod", 10);
        var messageId = NewId.NextGuid();

        await host.Harness.Bus.Publish(Created(id, "payment-api", "prod", "High"));
        await host.Harness.Bus.Publish(ack, ctx => ctx.MessageId = messageId);
        await host.Harness.Bus.Publish(ack, ctx => ctx.MessageId = messageId);
        await host.Harness.InactivityTask;

        using var api = new ReportingApiFactory(database);
        var summary = await api.CreateClient().GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
        summary!.TotalIncidents.Should().Be(1);
        summary.MeanTimeToAcknowledgeMinutes.Should().BeApproximately(10, 1e-9);
    }

    [Fact]
    public async Task LifecycleEventsBeforeCreated_AreKept_AndCountOnceCreatedArrives()
    {
        var database = postgres.NewDatabase();
        await using var host = await ConsumerHost<ReportingDbContext>.StartAsync(database, x => x.AddReportingMessaging());
        using var api = new ReportingApiFactory(database);
        var client = api.CreateClient();
        var id = Guid.NewGuid();

        await host.PublishAsync(
            Acknowledged(id, "payment-api", "prod", 15),
            Resolved(id, "payment-api", "prod", 45),
            Closed(id, "payment-api", "prod", 50),
            Correlated("deployment", "prod", id));

        var before = await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
        before!.TotalIncidents.Should().Be(0, "an incident is counted from its IncidentCreated, which has not arrived");

        await host.PublishAsync(Created(id, "payment-api", "prod", "Critical"));

        var after = await client.GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
        after!.TotalIncidents.Should().Be(1);
        after.OpenIncidents.Should().Be(0);
        after.ResolvedIncidents.Should().Be(1);
        after.MeanTimeToAcknowledgeMinutes.Should().BeApproximately(15, 1e-9);
        after.MeanTimeToResolveMinutes.Should().BeApproximately(45, 1e-9);
        after.SeverityBreakdown.Should().BeEquivalentTo(new Dictionary<string, int> { ["Critical"] = 1 });
        after.DeploymentRegressionStats.Should().BeEquivalentTo(new Dictionary<string, int> { ["payment-api"] = 1 });
    }

    [Fact]
    public async Task AllEventsPublishedTogether_GiveTheSameFigures_WhateverOrderTheyAreConsumedIn()
    {
        var database = postgres.NewDatabase();
        await using var host = await ConsumerHost<ReportingDbContext>.StartAsync(database, x => x.AddReportingMessaging());

        // Reversed: closes, resolves and acknowledgements first, IncidentCreated last.
        await host.PublishAsync(Events().Reverse().ToArray());

        using var api = new ReportingApiFactory(database);
        var summary = await api.CreateClient().GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
        summary!.TotalIncidents.Should().Be(5);
        summary.OpenIncidents.Should().Be(2);
        summary.ResolvedIncidents.Should().Be(2);
        summary.MeanTimeToAcknowledgeMinutes.Should().BeApproximately(20, 1e-9);
        summary.MeanTimeToResolveMinutes.Should().BeApproximately(90, 1e-9);
        summary.DeploymentRegressionStats.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            ["payment-api"] = 1, ["search-api"] = 1
        });
    }

    [Fact]
    public async Task IncidentCreated_WithNullTextFields_IsApplied()
    {
        var database = postgres.NewDatabase();
        await using var host = await ConsumerHost<ReportingDbContext>.StartAsync(database, x => x.AddReportingMessaging());
        var id = Guid.NewGuid();

        // The contract says non-nullable, but a publisher can still send nulls; the row must still be written.
        await host.PublishAsync(
            new IncidentFlow.BuildingBlocks.Contracts.IncidentCreated(id, null!, null!, null, null, null, null!,
                "ErrorSpike", "unknown failing", T0, "c"),
            new IncidentFlow.BuildingBlocks.Contracts.IncidentAcknowledged(id, null!, null!, "alice", T0.AddMinutes(5), "c"));

        using var api = new ReportingApiFactory(database);
        var summary = await api.CreateClient().GetFromJsonAsync<SummaryDto>("/api/v1/reports/summary");
        summary!.TotalIncidents.Should().Be(1);
        summary.MeanTimeToAcknowledgeMinutes.Should().BeApproximately(5, 1e-9);
    }
}
