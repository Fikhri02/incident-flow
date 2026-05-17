using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Detection.Worker.Consumers;
using IncidentFlow.Services.Detection.Worker.Detection;
using MassTransit;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddSingleton<SpikeDetector>();

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<ErrorSignalConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.ConfigureEndpoints(ctx);
    });
});

var host = builder.Build();
host.Run();
