using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace IncidentFlow.IntegrationTests.Shared;

public static class MassTransitTestServices
{
    /// <summary>
    /// Removes MassTransit's hosted services so the API starts without RabbitMQ.
    /// MassTransit 8 registers them through a factory, so ImplementationType alone is null;
    /// check the factory's declaring type as well.
    /// </summary>
    public static void RemoveMassTransitHostedServices(this IServiceCollection services)
    {
        static bool FromMassTransit(Type? type) =>
            type?.Assembly.GetName().Name?.StartsWith("MassTransit", StringComparison.Ordinal) == true;

        var hosted = services
            .Where(d => d.ServiceType == typeof(IHostedService) &&
                        (FromMassTransit(d.ImplementationType) ||
                         FromMassTransit(d.ImplementationInstance?.GetType()) ||
                         FromMassTransit(d.ImplementationFactory?.Method.DeclaringType)))
            .ToList();

        foreach (var d in hosted) services.Remove(d);
    }
}
