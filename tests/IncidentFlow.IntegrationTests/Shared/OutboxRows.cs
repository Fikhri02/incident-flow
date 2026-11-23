using Npgsql;

namespace IncidentFlow.IntegrationTests.Shared;

public sealed record OutboxRow(string MessageType, Guid? CorrelationId, string Body);

/// <summary>Reads MassTransit's OutboxMessage table straight from PostgreSQL.</summary>
public static class OutboxRows
{
    public static async Task<IReadOnlyList<OutboxRow>> WhereBodyContainsAsync(string connectionString, string text)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            """SELECT "MessageType", "CorrelationId", "Body" FROM "OutboxMessage" WHERE "Body" LIKE @text""", conn);
        cmd.Parameters.AddWithValue("text", $"%{text}%");

        var rows = new List<OutboxRow>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            rows.Add(new OutboxRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetGuid(1),
                reader.GetString(2)));
        return rows;
    }

    public static async Task<long> CountAsync(string connectionString, string sql, params (string Name, object Value)[] args)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }
}
