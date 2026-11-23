using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace IncidentFlow.UnitTests.Hosting;

// The workers are web hosts (for /health). Without a launch profile each one binds Kestrel's default
// port 5000, so the second worker to start fails with "address already in use". Aspire 8 also creates a
// project's endpoints from its launch profile, so the profile is where each worker gets its own port.
public sealed class LaunchSettingsTests
{
    private static readonly string[] Workers =
    [
        "src/Services/IncidentFlow.Services.Detection.Worker",
        "src/Services/IncidentFlow.Services.Correlation.Worker",
        "src/Services/IncidentFlow.Services.Notification.Worker"
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
}
