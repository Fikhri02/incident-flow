using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Notification.Worker.Consumers;
using IncidentFlow.Services.Notification.Worker.Notifications;
using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;

// A web host only for /health/live and /health/ready; the work is done by the consumers.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
// Remembers which messages were already posted (dedupe by MessageId: 90 s in-flight claim, then 24 h "sent").
builder.AddRedisClient("redis");

builder.Services.Configure<SlackOptions>(builder.Configuration.GetSection(SlackOptions.SectionName));
// Typed client: gets the standard resilience handler from AddServiceDefaults.
builder.Services.AddHttpClient<IIncidentNotifier, SlackNotifier>();

builder.Services.AddMassTransit(x =>
{
    x.AddNotificationMessaging();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.UseDefaultRetry();
        cfg.UsePayloadCorrelationId();
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

app.MapDefaultEndpoints();

app.Run();
