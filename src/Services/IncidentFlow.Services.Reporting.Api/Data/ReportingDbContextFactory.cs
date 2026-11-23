using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IncidentFlow.Services.Reporting.Api.Data;

public sealed class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ReportingDbContext>()
            .UseNpgsql("Host=localhost;Database=reporting;Username=postgres;Password=postgres")
            .Options;
        return new ReportingDbContext(options);
    }
}
