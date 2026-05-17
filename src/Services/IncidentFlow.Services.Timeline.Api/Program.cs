using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Timeline.Api.Consumers;
using IncidentFlow.Services.Timeline.Api.Data;
using IncidentFlow.Services.Timeline.Api.Endpoints;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<TimelineDbContext>("timeline-db");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<IncidentCreatedConsumer>();
    x.AddConsumer<IncidentAcknowledgedConsumer>();
    x.AddConsumer<IncidentResolvedConsumer>();
    x.AddConsumer<IncidentClosedConsumer>();
    x.AddConsumer<AnomalyDetectedConsumer>();
    x.AddConsumer<ErrorSignalConsumer>();
    x.AddConsumer<CorrelationFoundConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.ConfigureEndpoints(ctx);
    });
});

var app = builder.Build();

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.StatusCode = 400;
    await ctx.Response.WriteAsJsonAsync(new { error = "Invalid request." });
}));

app.MapDefaultEndpoints();
app.MapTimelineEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TimelineDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
