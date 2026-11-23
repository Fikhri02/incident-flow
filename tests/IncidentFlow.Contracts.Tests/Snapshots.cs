using System.Text.Encodings.Web;
using System.Text.Json;

namespace IncidentFlow.Contracts.Tests;

/// <summary>
/// Reads the checked-in snapshots (copied next to the test assembly) and, only when
/// UPDATE_CONTRACT_SNAPSHOTS=1, writes accepted shapes back into the project's Snapshots folder.
/// </summary>
public static class Snapshots
{
    public const string UpdateVariable = "UPDATE_CONTRACT_SNAPSHOTS";

    // Web defaults (camelCase keys) and no escaping of < > so the files read cleanly in a diff.
    private static readonly JsonSerializerOptions FileFormat = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool Updating => Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    public static string OutputFolder => Path.Combine(AppContext.BaseDirectory, "Snapshots");

    public static IReadOnlyList<string> CheckedInNames() =>
        Directory.Exists(OutputFolder)
            ? Directory.GetFiles(OutputFolder, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().ToList()
            : [];

    public static Snapshot? Read(string contract)
    {
        var path = Path.Combine(OutputFolder, contract + ".json");
        return File.Exists(path) ? JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(path), FileFormat) : null;
    }

    public static void Write(string contract, IReadOnlyDictionary<string, string> shape)
    {
        var snapshot = new Snapshot(contract, new Dictionary<string, string>(shape));
        var path = Path.Combine(ProjectSnapshotsFolder(), contract + ".json");
        File.WriteAllText(path, JsonSerializer.Serialize(snapshot, FileFormat) + Environment.NewLine);
    }

    private static string ProjectSnapshotsFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "IncidentFlow.Contracts.Tests.csproj")))
                return Directory.CreateDirectory(Path.Combine(dir.FullName, "Snapshots")).FullName;
        }

        throw new DirectoryNotFoundException("Could not find the IncidentFlow.Contracts.Tests project folder.");
    }
}

/// <summary>One contract's pinned JSON shape: property name to type descriptor.</summary>
public sealed record Snapshot(string Contract, Dictionary<string, string> Properties);
