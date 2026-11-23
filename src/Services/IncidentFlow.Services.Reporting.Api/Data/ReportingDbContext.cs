using IncidentFlow.Services.Reporting.Api.Models;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace IncidentFlow.Services.Reporting.Api.Data;

public sealed class ReportingDbContext(DbContextOptions<ReportingDbContext> options) : DbContext(options)
{
    public DbSet<IncidentReportRow> Incidents => Set<IncidentReportRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<IncidentReportRow>(e =>
        {
            e.ToTable("IncidentReports");
            e.HasKey(r => r.IncidentId);
            e.Property(r => r.IncidentId).ValueGeneratedNever();
            e.Property(r => r.ServiceName).HasMaxLength(100);
            e.Property(r => r.Environment).HasMaxLength(50);
            e.Property(r => r.Severity).HasMaxLength(50);
            e.HasIndex(r => new { r.ServiceName, r.Environment });
        });

        // MassTransit inbox: one InboxState row per (MessageId, consumer) marks a message as consumed,
        // so a redelivery is skipped. The outbox tables come with it; Reporting publishes nothing.
        model.AddInboxStateEntity();
        model.AddOutboxMessageEntity();
        model.AddOutboxStateEntity();
    }
}
