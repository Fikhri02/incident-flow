using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace IncidentFlow.Contracts.Tests;

/// <summary>
/// Every service publishes and consumes these records as JSON, so a renamed, removed, added or
/// retyped field breaks the services on the other side of the broker. These tests make that a
/// visible, reviewed change: the shape must match Snapshots/&lt;Contract&gt;.json.
/// </summary>
public sealed class ContractSnapshotTests
{
    public static TheoryData<string> Contracts()
    {
        var data = new TheoryData<string>();
        foreach (var contract in ContractShape.MessageContracts) data.Add(contract.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void Json_shape_matches_the_checked_in_snapshot(string contract)
    {
        var actual = ContractShape.Describe(ContractShape.Find(contract));

        if (Snapshots.Updating)
        {
            Snapshots.Write(contract, actual);
            return;
        }

        var snapshot = Snapshots.Read(contract);
        snapshot.Should().NotBeNull(
            $"every contract needs Snapshots/{contract}.json. Current shape:\n{Format(actual)}\n" +
            $"Run with {Snapshots.UpdateVariable}=1 to write it, then review it");

        snapshot!.Contract.Should().Be(contract);
        var differences = Diff(snapshot.Properties, actual);
        differences.Should().BeEmpty(
            $"{contract}'s JSON shape changed. If that is intended, version the contract or update the " +
            $"snapshot ({Snapshots.UpdateVariable}=1) and tell every consumer");
    }

    [Fact]
    public void Every_snapshot_belongs_to_a_contract_that_still_exists()
    {
        var contracts = ContractShape.MessageContracts.Select(t => t.Name);
        Snapshots.CheckedInNames().Should().BeSubsetOf(contracts,
            "a removed or renamed contract must also remove its snapshot, deliberately");
    }

    [Fact]
    public void The_known_contracts_are_all_covered()
    {
        // A floor, not the full list: a contract that silently drops out of the namespace fails here.
        ContractShape.MessageContracts.Select(t => t.Name).Should().Contain(
        [
            "ErrorSignalReceived", "LogSignalReceived", "MetricSignalReceived", "DeploymentSignalReceived",
            "HealthCheckSignalReceived", "AnomalyDetected", "IncidentCreated", "IncidentAcknowledged",
            "IncidentResolved", "IncidentClosed", "CorrelationFound"
        ]);
        ContractShape.MessageContracts.Select(t => t.Name).Should().NotContain("IntegrationEvent");
    }

    [Theory]
    [MemberData(nameof(Contracts))]
    public void A_consumer_reads_back_what_a_publisher_writes(string contract)
    {
        var type = ContractShape.Find(contract);
        var json = JsonSerializer.Serialize(ContractShape.Sample(type), type, ContractShape.Wire);

        var readBack = JsonSerializer.Deserialize(json, type, ContractShape.Wire);

        JsonSerializer.Serialize(readBack, type, ContractShape.Wire).Should().Be(json);
    }

    private static List<string> Diff(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual)
    {
        var lines = new List<string>();
        foreach (var (name, type) in expected)
        {
            if (!actual.TryGetValue(name, out var now)) lines.Add($"removed: {name} ({type})");
            else if (now != type) lines.Add($"changed: {name} {type} -> {now}");
        }

        foreach (var (name, type) in actual)
            if (!expected.ContainsKey(name)) lines.Add($"added: {name} ({type})");

        return lines;
    }

    private static string Format(IReadOnlyDictionary<string, string> shape) =>
        string.Join("\n", shape.Select(p => $"  {p.Key}: {p.Value}"));
}
