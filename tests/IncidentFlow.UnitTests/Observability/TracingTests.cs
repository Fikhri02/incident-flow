using System.Diagnostics;
using FluentAssertions;
using IncidentFlow.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Xunit;

namespace IncidentFlow.UnitTests.Observability;

public sealed class TracingTests
{
    [Fact]
    public void ServiceDefaults_ExportMassTransitSpans()
    {
        var exported = new List<Activity>();
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.ConfigureOpenTelemetryTracerProvider(t => t.AddInMemoryExporter(exported));
        using var host = builder.Build();
        var tracer = host.Services.GetRequiredService<TracerProvider>();

        // MassTransit 8 emits its send, publish and consume spans from this ActivitySource.
        using (var source = new ActivitySource("MassTransit"))
        using (source.StartActivity("incident-created publish")) { }
        tracer.ForceFlush();

        exported.Should().Contain(a => a.Source.Name == "MassTransit");
    }
}
