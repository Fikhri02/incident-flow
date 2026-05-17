using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace IncidentFlow.Services.Timeline.Api.Data;

public sealed class TimelineDbContextFactory : IDesignTimeDbContextFactory<TimelineDbContext>
{
    public TimelineDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TimelineDbContext>()
            .UseNpgsql("Host=localhost;Database=timeline;Username=postgres;Password=postgres")
            .Options;
        return new TimelineDbContext(options);
    }
}
