using System.Text.Json;
using IncidentFlow.BuildingBlocks.Contracts;

namespace IncidentFlow.Services.Reporting.Api.Rebuild;

/// <summary>
/// Reads Timeline's stored events a page at a time. Its HttpClient's base address is "http://timeline":
/// ServiceDefaults adds service discovery to every HttpClient, which resolves the name from the
/// services:timeline:* endpoints Aspire injects for WithReference(timeline).
/// </summary>
public sealed class TimelineClient(HttpClient http)
{
    public const string HttpClientName = "timeline";
    public static readonly Uri BaseAddress = new("http://timeline");

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    // Timeline's event types for the events Reporting projects, and the contract each payload holds.
    private static readonly Dictionary<string, Type> Contracts = new(StringComparer.Ordinal)
    {
        ["incident.created"] = typeof(IncidentCreated),
        ["incident.acknowledged"] = typeof(IncidentAcknowledged),
        ["incident.resolved"] = typeof(IncidentResolved),
        ["incident.closed"] = typeof(IncidentClosed),
        ["correlation.found"] = typeof(CorrelationFound)
    };

    public async Task<TimelineEventPage> GetEventsAsync(long after, int take, CancellationToken ct) =>
        await http.GetFromJsonAsync<TimelineEventPage>($"/api/v1/timeline/events?after={after}&take={take}", Web, ct)
        ?? throw new HttpRequestException("Timeline returned an empty events page.");

    /// <summary>True when the event is one Reporting projects; false for other types.</summary>
    public static bool IsReportingEvent(TimelineEvent e) => Contracts.ContainsKey(e.EventType);

    /// <summary>The original message, or null when the type is not projected or the row predates payloads.</summary>
    public static object? Message(TimelineEvent e) =>
        Contracts.TryGetValue(e.EventType, out var contract) && e.Payload is { ValueKind: JsonValueKind.Object } payload
            ? payload.Deserialize(contract, Web)
            : null;
}

public sealed record TimelineEventPage(IReadOnlyList<TimelineEvent> Items, long NextCursor);

public sealed record TimelineEvent(
    long Sequence,
    string EventType,
    Guid? IncidentId,
    JsonElement? Payload,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt);
