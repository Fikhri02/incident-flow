using IncidentFlow.BuildingBlocks.EventBus;
using IncidentFlow.IntegrationTests.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace IncidentFlow.IntegrationTests.Ingestion;

public class IngestionApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();

    /// <summary>Signals each source may send per minute; the real default is 1,000.</summary>
    protected virtual int RateLimitPerMinute => 1000;

    /// <summary>False keeps the service's own MassTransit event bus (and its outbox) instead of the null bus.</summary>
    protected virtual bool ReplaceEventBus => true;

    public string ConnectionString => _postgres.GetConnectionString();

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
        builder.UseSetting("ConnectionStrings:ingestion-db", _postgres.GetConnectionString());
        builder.UseSetting("Ingestion:RateLimit:PermitsPerMinute", RateLimitPerMinute.ToString());
        // The bus is built (health checks resolve it) but never started: its hosted services are removed.
        builder.UseSetting("ConnectionStrings:rabbitmq", "amqp://guest:guest@localhost:5672");

        builder.ConfigureServices(services =>
        {
            // Replace IEventBus so tests don't need RabbitMQ
            if (ReplaceEventBus)
            {
                var busDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IEventBus));
                if (busDescriptor is not null) services.Remove(busDescriptor);
                services.AddScoped<IEventBus, NullEventBus>();
            }

            // Remove MassTransit hosted services so no RabbitMQ connection is attempted
            services.RemoveMassTransitHostedServices();
        });
    }
}
