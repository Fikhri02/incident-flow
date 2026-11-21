using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Incident.Api.Consumers;
using IncidentFlow.Services.Incident.Api.Data;
using IncidentFlow.Services.Incident.Api.Domain;
using IncidentFlow.Services.Incident.Api.Endpoints;
using IncidentFlow.Services.Incident.Api.Messaging;
using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IncidentDbContext>("incident-db");

builder.Services.AddScoped<IEventBus, MassTransitEventBus>();
builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
builder.Services.AddScoped<IncidentEventPublisher>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddMassTransit(x =>
{
    x.UseServiceEndpointNames("incident");
    x.AddConsumer<AnomalyDetectedConsumer>();

    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.UseDefaultRetry();
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
app.MapIncidentEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IncidentDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program { }
