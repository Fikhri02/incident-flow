using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace IncidentFlow.IntegrationTests.Shared;

public sealed class RedisFixture : IAsyncLifetime
{
    private readonly RedisContainer _redis = new RedisBuilder().Build();

    public IConnectionMultiplexer Connection { get; private set; } = null!;

    public string ConnectionString => _redis.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _redis.StartAsync();
        Connection = await ConnectionMultiplexer.ConnectAsync(_redis.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await Connection.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
