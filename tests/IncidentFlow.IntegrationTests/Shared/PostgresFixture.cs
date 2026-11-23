using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IncidentFlow.IntegrationTests.Shared;

/// <summary>One PostgreSQL container per test class; each test asks for its own database.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    /// <summary>Creates a database no other test uses and returns its connection string.</summary>
    public string NewDatabase()
    {
        var name = $"t_{Guid.NewGuid():N}";
        using (var conn = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            conn.Open();
            using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            cmd.ExecuteNonQuery();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }
}
