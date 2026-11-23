using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Detection.Worker.Consumers;
using IncidentFlow.Services.Detection.Worker.Detection;
using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;

// A web host only for /health/live and /health/ready; the work is done by the consumers.
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddRedisClient("redis");

builder.Services.AddSingleton<SpikeDetector>();

builder.Services.AddMassTransit(x =>
{
    x.AddDetectionMessaging();

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
