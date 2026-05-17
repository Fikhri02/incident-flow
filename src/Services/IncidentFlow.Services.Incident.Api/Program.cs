using IncidentFlow.ServiceDefaults;
using IncidentFlow.Services.Incident.Api.Consumers;
using IncidentFlow.Services.Incident.Api.Data;
using IncidentFlow.Services.Incident.Api.Endpoints;
using MassTransit;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDbContext<IncidentDbContext>("incident-db");

builder.Services.AddMassTransit(x =>
{
    x.AddConsumer<AnomalyDetectedConsumer>();

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
app.MapIncidentEndpoints();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IncidentDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();
