using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace IncidentFlow.IntegrationTests.Shared;

/// <summary>
/// A service's real consumers and EF inbox on the in-memory transport, writing to the given database.
/// Waits by counting consumed InboxState rows, so a test knows every message it published is committed.
/// </summary>
public sealed class ConsumerHost<TContext> : IAsyncDisposable
    where TContext : DbContext
{
    private readonly ServiceProvider _provider;
    private readonly string _connectionString;

    private ConsumerHost(ServiceProvider provider, string connectionString)
    {
        _provider = provider;
        _connectionString = connectionString;
    }

    public ITestHarness Harness => _provider.GetRequiredService<ITestHarness>();

    public IServiceProvider Services => _provider;

    public static async Task<ConsumerHost<TContext>> StartAsync(
        string connectionString, Action<IBusRegistrationConfigurator> addMessaging)
    {
        var provider = new ServiceCollection()
            .AddLogging()
            .AddDbContext<TContext>(o => o.UseNpgsql(connectionString))
            .AddMassTransitTestHarness(x =>
            {
                addMessaging(x);
                x.UsingTestTransport();
            })
            .BuildServiceProvider(true);

        await using (var scope = provider.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<TContext>().Database.MigrateAsync();

        await provider.GetRequiredService<ITestHarness>().Start();
        return new ConsumerHost<TContext>(provider, connectionString);
    }

    /// <summary>Publishes each message with its own MessageId and waits until all of them are consumed.</summary>
    public async Task PublishAsync(params object[] messages)
    {
        var before = await ConsumedCountAsync();
        foreach (var message in messages)
            await Harness.Bus.Publish(message);

        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (await ConsumedCountAsync() < before + messages.Length)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException(
                    $"Only {await ConsumedCountAsync() - before} of {messages.Length} messages were consumed.");
            await Task.Delay(50);
        }
    }

    public Task<long> ConsumedCountAsync() =>
        OutboxRows.CountAsync(_connectionString, """SELECT count(*) FROM "InboxState" WHERE "Consumed" IS NOT NULL""");

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        // Each test gets its own database, so its pool would otherwise hold idle connections until the
        // container hits max_connections. Clearing closes idle connections only.
        NpgsqlConnection.ClearAllPools();
    }
}
