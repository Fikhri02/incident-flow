using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Correlation.Worker.Consumers;
using IncidentFlow.Services.Correlation.Worker.Correlation;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient("redis");

builder.Services.AddSingleton<CorrelationEngine>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<IncidentCreatedConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
