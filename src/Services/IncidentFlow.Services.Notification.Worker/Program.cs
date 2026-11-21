using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Notification.Worker.Consumers;
using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

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
