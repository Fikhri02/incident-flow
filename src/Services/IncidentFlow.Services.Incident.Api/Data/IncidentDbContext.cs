using IncidentFlow.Services.Incident.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Incident.Api.Data;

public sealed class IncidentDbContext(DbContextOptions<IncidentDbContext> options) : DbContext(options)
{
    public DbSet<Domain.Incident> Incidents => Set<Domain.Incident>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Domain.Incident>(e =>
        {
            e.ToTable("Incidents");
            e.HasKey(i => i.IncidentId);
            e.Property(i => i.ServiceName).HasMaxLength(100).IsRequired();
            e.Property(i => i.Environment).HasMaxLength(50).IsRequired();
            e.Property(i => i.Region).HasMaxLength(50);
            e.Property(i => i.Fingerprint).HasMaxLength(256);
            e.Property(i => i.DeploymentId).HasMaxLength(128);
            e.Property(i => i.Severity).HasMaxLength(20).IsRequired();
            e.Property(i => i.RuleName).HasMaxLength(100).IsRequired();
            e.Property(i => i.Summary).HasMaxLength(1000).IsRequired();
            // Stored as the same lowercase text as before ("open", "acknowledged", ...): no migration needed.
            e.Property(i => i.Status)
                .HasConversion(s => s.ToWire(), v => IncidentStatusExtensions.FromWire(v))
                .HasMaxLength(20)
                .IsRequired();
            e.Property(i => i.AcknowledgedBy).HasMaxLength(200);
            e.Property(i => i.ResolvedBy).HasMaxLength(200);
            e.Property(i => i.CorrelationId).HasMaxLength(64).IsRequired();
            e.Ignore(i => i.Key);
            e.Ignore(i => i.SignalIds);
            e.Ignore(i => i.DomainEvents);
            e.HasIndex(i => new { i.ServiceName, i.Environment, i.Status });
            e.HasIndex(i => new { i.Fingerprint, i.Status });
        });
    }
}
