using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Ingestion.Api.Data;
using IncidentFlow.Services.Ingestion.Api.Endpoints;
using IncidentFlow.Services.Ingestion.Api.Messaging;
using IncidentFlow.Services.Ingestion.Api.RateLimiting;
using IncidentFlow.BuildingBlocks.Messaging;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IngestionDbContext>("ingestion-db");

builder.Services.AddMassTransit(x =>
{
    x.UseServiceEndpointNames("ingestion");
    x.UsingRabbitMq((ctx, cfg) =>
    {
        cfg.Host(builder.Configuration.GetConnectionString("rabbitmq"));
        cfg.UseDefaultRetry();
        cfg.ConfigureEndpoints(ctx);
    });
});

builder.Services.AddScoped<IEventBus, MassTransitEventBus>();
builder.Services.AddSingleton<SignalRateLimiter>();

var app = builder.Build();

app.UseExceptionHandler(errApp => errApp.Run(async ctx =>
{
    ctx.Response.StatusCode = 400;
    await ctx.Response.WriteAsJsonAsync(new { error = "Invalid request." });
}));

app.MapDefaultEndpoints();
app.MapSignalEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IngestionDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program { }
