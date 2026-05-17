using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IncidentFlow.Services.Incident.Api.Data;

public sealed class IncidentDbContextFactory : IDesignTimeDbContextFactory<IncidentDbContext>
{
    public IncidentDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<IncidentDbContext>()
            .UseNpgsql("Host=localhost;Database=incident;Username=postgres;Password=postgres")
            .Options;
        return new IncidentDbContext(options);
    }
}
