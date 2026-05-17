using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.IntegrationTests.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Testcontainers.PostgreSql;
using Xunit;

namespace IncidentFlow.IntegrationTests.Ingestion;

public sealed class IngestionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Point the DB connection at the Testcontainer
        builder.ConfigureAppConfiguration(cfg =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ingestion-db"] = _postgres.GetConnectionString()
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace IEventBus so tests don't need RabbitMQ
            var busDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEventBus));
            if (busDescriptor is not null) services.Remove(busDescriptor);
            services.AddScoped<IEventBus, NullEventBus>();

            // Remove MassTransit hosted services so no RabbitMQ connection is attempted
            var massTransitHosted = services
                .Where(d => d.ServiceType == typeof(IHostedService) &&
                            d.ImplementationType?.Assembly.GetName().Name?.StartsWith("MassTransit") == true)
                .ToList();
            foreach (var d in massTransitHosted) services.Remove(d);
        });
    }
}
