using IncidentFlow.Services.Timeline.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Timeline.Api.Data;

public sealed class TimelineDbContext(DbContextOptions<TimelineDbContext> options) : DbContext(options)
{
    public DbSet<TimelineEntryEntity> Entries => Set<TimelineEntryEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<TimelineEntryEntity>(e =>
        {
            e.HasKey(t => t.EntryId);
            e.Property(t => t.EventType).HasMaxLength(100).IsRequired();
            e.Property(t => t.ServiceName).HasMaxLength(100).IsRequired();
            e.Property(t => t.Environment).HasMaxLength(50).IsRequired();
            e.Property(t => t.Summary).HasMaxLength(1000).IsRequired();
            e.Property(t => t.CorrelationId).HasMaxLength(64).IsRequired();
            e.Property(t => t.Actor).HasMaxLength(200);
            e.HasIndex(t => t.IncidentId);
            e.HasIndex(t => new { t.CorrelationId, t.OccurredAt });
        });
    }
}
