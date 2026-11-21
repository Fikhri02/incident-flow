using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Notification.Worker.Consumers;
using IncidentFlow.Services.Notification.Worker.Notifications;
using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.Configure<SlackOptions>(builder.Configuration.GetSection(SlackOptions.SectionName));
// Typed client: gets the standard resilience handler from AddServiceDefaults.
builder.Services.AddHttpClient<IIncidentNotifier, SlackNotifier>();

builder.Services.AddMassTransit(x =>
{
    x.UseServiceEndpointNames("notification");
    x.AddConsumer<IncidentCreatedConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.UseDefaultRetry();
        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
