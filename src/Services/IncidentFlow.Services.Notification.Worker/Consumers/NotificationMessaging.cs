using IncidentFlow.BuildingBlocks.Messaging;
using IncidentFlow.Services.Notification.Worker.Notifications;
using MassTransit;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IncidentFlow.Services.Notification.Worker.Consumers;

/// <summary>Notification's consumers and endpoint policy, shared by Program and the tests.</summary>
public static class NotificationMessaging
{
    /// <summary>Needs an <c>IConnectionMultiplexer</c> (Redis) and an <see cref="IIncidentNotifier"/>.</summary>
    public static void AddNotificationMessaging(this IBusRegistrationConfigurator x)
    {
        x.UseServiceEndpointNames("notification");
        x.TryAddSingleton<ISentNotifications, RedisSentNotifications>();
        x.AddConsumer<IncidentCreatedConsumer>();
    }
}
