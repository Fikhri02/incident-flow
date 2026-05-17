using IncidentFlow.Services.Ingestion.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Ingestion.Api.Data;

public sealed class IngestionDbContext(DbContextOptions<IngestionDbContext> options) : DbContext(options)
{
    public DbSet<SignalEntity> Signals => Set<SignalEntity>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SignalEntity>(e =>
        {
            e.HasKey(s => s.SignalId);
            e.Property(s => s.SignalType).HasMaxLength(50).IsRequired();
            e.Property(s => s.SourceSystem).HasMaxLength(100).IsRequired();
            e.Property(s => s.ServiceName).HasMaxLength(100).IsRequired();
            e.Property(s => s.Environment).HasMaxLength(50).IsRequired();
            e.Property(s => s.Region).HasMaxLength(50);
            e.Property(s => s.Fingerprint).HasMaxLength(256);
            e.Property(s => s.TraceId).HasMaxLength(128);
            e.Property(s => s.DeploymentId).HasMaxLength(128);
            e.Property(s => s.Summary).HasMaxLength(1000);
            e.Property(s => s.IdempotencyKey).HasMaxLength(256);
            e.Property(s => s.CorrelationId).HasMaxLength(64).IsRequired();
            e.HasIndex(s => new { s.ServiceName, s.Environment, s.OccurredAt });
            e.HasIndex(s => s.IdempotencyKey).HasFilter("\"IdempotencyKey\" IS NOT NULL");
        });
    }
}
