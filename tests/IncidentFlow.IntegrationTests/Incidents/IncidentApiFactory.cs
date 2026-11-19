extern alias incidentapi;

using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.IntegrationTests.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using IncidentProgram = incidentapi::Program;

namespace IncidentFlow.IntegrationTests.Incidents;

public sealed class IncidentApiFactory : WebApplicationFactory<IncidentProgram>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();

    public RecordingEventBus Bus { get; } = new();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // UseSetting, not ConfigureAppConfiguration: Aspire reads the connection string while
        // Program registers services, before late configuration sources are applied.
        builder.UseSetting("ConnectionStrings:incident-db", _postgres.GetConnectionString());
        // The bus is built (health checks resolve it) but never started: its hosted services are removed.
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");

        builder.ConfigureServices(services =>
        {
            var busDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEventBus));
            if (busDescriptor is not null) services.Remove(busDescriptor);
            services.AddSingleton<IEventBus>(Bus);

            // Remove MassTransit hosted services so no RabbitMQ connection is attempted
            services.RemoveMassTransitHostedServices();
        });
    }
}
