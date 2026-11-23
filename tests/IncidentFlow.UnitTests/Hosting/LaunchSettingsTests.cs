using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace IncidentFlow.UnitTests.Hosting;

// The workers are web hosts (for /health). Without a launch profile each one binds Kestrel's default
// port 5000, so the second worker to start fails with "address already in use". Aspire 8 also creates a
// project's endpoints from its launch profile, so the profile is where each worker gets its own port.
// Reporting is an API, but the gateway reaches it through the endpoint Aspire builds from the same
// profile, so it is held to the same rule.
public sealed class LaunchSettingsTests
{
    private static readonly string[] Workers =
    [
        "src/Services/IncidentFlow.Services.Detection.Worker",
        "src/Services/IncidentFlow.Services.Correlation.Worker",
        "src/Services/IncidentFlow.Services.Notification.Worker",
        "src/Services/IncidentFlow.Services.Reporting.Api"
    ];

    // Every project the AppHost starts, plus the AppHost itself. Aspire 8 gives a project endpoints only
    // from its launch profile, and the AppHost will not start without its dashboard URLs, so a host without
    // a profile is unreachable (the gateway could not route to it) or, for the AppHost, never starts.
    private static readonly string[] Hosts =
    [
        "src/IncidentFlow.AppHost",
        "src/IncidentFlow.ApiGateway",
        "src/Services/IncidentFlow.Services.Ingestion.Api",
        "src/Services/IncidentFlow.Services.Incident.Api",
        "src/Services/IncidentFlow.Services.Timeline.Api",
        "src/Services/IncidentFlow.Services.Detection.Worker",
        "src/Services/IncidentFlow.Services.Correlation.Worker",
        "src/Services/IncidentFlow.Services.Notification.Worker",
        "src/Services/IncidentFlow.Services.Reporting.Api"
    ];

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "IncidentFlow.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("IncidentFlow.sln not found above the test output.");
    }

    /// <summary>Every port named by an applicationUrl in the file, per profile.</summary>
    private static List<Uri> ApplicationUrls(string launchSettingsPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(launchSettingsPath));
        var urls = new List<Uri>();
        foreach (var profile in doc.RootElement.GetProperty("profiles").EnumerateObject())
        {
            if (!profile.Value.TryGetProperty("applicationUrl", out var value)) continue;
            urls.AddRange(value.GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(u => new Uri(u)));
        }
        return urls;
    }

    [Fact]
    public void EachWorker_HasAnHttpApplicationUrl()
    {
        var root = RepoRoot();
        foreach (var worker in Workers)
        {
            var path = Path.Combine(root, worker, "Properties", "launchSettings.json");
            File.Exists(path).Should().BeTrue($"{worker} needs a launch profile with its own port");
            ApplicationUrls(path).Should().Contain(u => u.Scheme == "http", $"{worker} needs an http endpoint");
        }
    }

    [Fact]
    public void WorkerPorts_AreDistinct_FromEachOther_AndFromEveryOtherProject()
    {
        var root = RepoRoot();
        var workerFiles = Workers.Select(w => Path.GetFullPath(Path.Combine(root, w, "Properties", "launchSettings.json"))).ToList();
        workerFiles.Should().OnlyContain(f => File.Exists(f));

        var workerPorts = workerFiles.SelectMany(f => ApplicationUrls(f).Select(u => u.Port)).ToList();
        workerPorts.Should().OnlyHaveUniqueItems();
        workerPorts.Should().NotContain(5000, "5000 is Kestrel's default, which any host without a profile binds");

        var otherPorts = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "launchSettings.json", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(Path.GetFullPath)
            .Except(workerFiles)
            .SelectMany(f => ApplicationUrls(f).Select(u => u.Port))
            .ToList();
        workerPorts.Should().NotIntersectWith(otherPorts);
    }

    [Fact]
    public void EveryHost_HasAnHttpLaunchProfile_OnADistinctPort()
    {
        var root = RepoRoot();
        var ports = new List<int>();
        foreach (var host in Hosts)
        {
            var path = Path.Combine(root, host, "Properties", "launchSettings.json");
            File.Exists(path).Should().BeTrue($"{host} needs a launch profile, or Aspire gives it no endpoint");
            var urls = ApplicationUrls(path);
            urls.Should().Contain(u => u.Scheme == "http", $"{host} needs an http endpoint");
            ports.AddRange(urls.Select(u => u.Port));
        }
        ports.Should().OnlyHaveUniqueItems();
        ports.Should().NotContain(5000);
    }

    [Fact]
    public void AppHost_Profile_SetsTheDashboardUrls()
    {
        var path = Path.Combine(RepoRoot(), "src/IncidentFlow.AppHost", "Properties", "launchSettings.json");
        File.Exists(path).Should().BeTrue("the AppHost throws at startup without ASPNETCORE_URLS and an OTLP endpoint");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var env = doc.RootElement.GetProperty("profiles").GetProperty("http").GetProperty("environmentVariables");
        env.TryGetProperty("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", out _).Should().BeTrue();
    }
}
