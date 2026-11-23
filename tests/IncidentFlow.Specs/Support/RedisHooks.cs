using Reqnroll;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace IncidentFlow.Specs.Support;

/// <summary>
/// One throwaway Redis per feature tagged @redis (needs Docker), the same way
/// IntegrationTests/Detection starts it. Scenarios share it, so each scenario uses its own keys.
/// </summary>
[Binding]
public sealed class RedisHooks
{
    [BeforeFeature("redis")]
    public static async Task StartRedis(FeatureContext feature)
    {
        var container = new RedisBuilder().WithImage("redis:7-alpine").Build();
        await container.StartAsync();
        var mux = await ConnectionMultiplexer.ConnectAsync(container.GetConnectionString());
        feature.FeatureContainer.RegisterInstanceAs(container);
        feature.FeatureContainer.RegisterInstanceAs<IConnectionMultiplexer>(mux);
    }

    [AfterFeature("redis")]
    public static async Task StopRedis(FeatureContext feature)
    {
        if (feature.FeatureContainer.IsRegistered<IConnectionMultiplexer>())
            await feature.FeatureContainer.Resolve<IConnectionMultiplexer>().DisposeAsync();
        if (feature.FeatureContainer.IsRegistered<RedisContainer>())
            await feature.FeatureContainer.Resolve<RedisContainer>().DisposeAsync();
    }
}
