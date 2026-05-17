using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IncidentFlow.Services.Ingestion.Api.Data;

public sealed class IngestionDbContextFactory : IDesignTimeDbContextFactory<IngestionDbContext>
{
    public IngestionDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IngestionDbContext>()
            .UseNpgsql("Host=localhost;Database=ingestion;Username=postgres;Password=postgres")
            .Options;
        return new IngestionDbContext(options);
    }
}
