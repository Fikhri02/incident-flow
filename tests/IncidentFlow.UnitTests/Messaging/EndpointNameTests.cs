using FluentAssertions;
using IncidentFlow.BuildingBlocks.Messaging;
using IncidentFlow.Services.Notification.Worker.Consumers;
using Xunit;

namespace IncidentFlow.UnitTests.Messaging;

public sealed class EndpointNameTests
{
    [Fact]
    public void Queue_names_start_with_the_service_name()
    {
        MessagingDefaults.EndpointNames("notification").Consumer<IncidentCreatedConsumer>()
            .Should().Be("notification-incident-created");
    }

    [Fact]
    public void Same_consumer_class_in_three_services_gets_three_queues()
    {
        // Notification, Correlation and Timeline each have an IncidentCreatedConsumer. With the
        // default formatter all three map to one queue and compete; each service must get its own.
        var names = new[] { "notification", "correlation", "timeline" }
            .Select(s => MessagingDefaults.EndpointNames(s).Consumer<IncidentCreatedConsumer>())
            .ToList();

        names.Should().OnlyHaveUniqueItems().And.HaveCount(3);
    }
}
