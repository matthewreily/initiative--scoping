using InitiativeScoping.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<BusinessUnit> BusinessUnits => Set<BusinessUnit>();
    public DbSet<ResourceType> ResourceTypes => Set<ResourceType>();
    public DbSet<RateCard> RateCards => Set<RateCard>();
    public DbSet<RateCardEntry> RateCardEntries => Set<RateCardEntry>();
    public DbSet<SizingConversion> SizingConversions => Set<SizingConversion>();
    public DbSet<AllocationTemplate> AllocationTemplates => Set<AllocationTemplate>();
    public DbSet<AllocationTemplateLine> AllocationTemplateLines => Set<AllocationTemplateLine>();
    public DbSet<Initiative> Initiatives => Set<Initiative>();
    public DbSet<InitiativeMember> InitiativeMembers => Set<InitiativeMember>();
    public DbSet<Phase> Phases => Set<Phase>();
    public DbSet<PhaseDateHistory> PhaseDateHistories => Set<PhaseDateHistory>();
    public DbSet<InitiativeAllocation> InitiativeAllocations => Set<InitiativeAllocation>();
    public DbSet<ForecastBaseline> ForecastBaselines => Set<ForecastBaseline>();
    public DbSet<ForecastBaselineLine> ForecastBaselineLines => Set<ForecastBaselineLine>();
    public DbSet<RebaselineRequest> RebaselineRequests => Set<RebaselineRequest>();
    public DbSet<Person> People => Set<Person>();
    public DbSet<InitiativeSourceMapping> InitiativeSourceMappings => Set<InitiativeSourceMapping>();
    public DbSet<ActualsImport> ActualsImports => Set<ActualsImport>();
    public DbSet<ActualEntry> ActualEntries => Set<ActualEntry>();
    public DbSet<ActualAdjustment> ActualAdjustments => Set<ActualAdjustment>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    private const string CaseInsensitive = "case_insensitive";

    protected override void OnModelCreating(ModelBuilder b)
    {
        // Business keys compare case-insensitively on every provider (SQL Server's default);
        // PostgreSQL needs an explicit non-deterministic ICU collation, SQLite has NOCASE.
        var ciCollation = Database.IsSqlite() ? "NOCASE" : CaseInsensitive;
        if (Database.IsNpgsql())
        {
            b.HasCollation(CaseInsensitive, locale: "und-u-ks-level2", provider: "icu", deterministic: false);
        }

        b.Entity<BusinessUnit>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).UseCollation(ciCollation);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<ResourceType>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).UseCollation(ciCollation);
            e.Property(x => x.Discipline).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<RateCard>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.Status, x.EffectiveStart });
            e.HasMany(x => x.Entries).WithOne(x => x.RateCard).HasForeignKey(x => x.RateCardId);
        });

        b.Entity<RateCardEntry>(e =>
        {
            e.Property(x => x.Location).HasMaxLength(100).UseCollation(ciCollation);
            e.Property(x => x.HourlyRate).HasPrecision(18, 2);
            e.HasIndex(x => new { x.RateCardId, x.ResourceTypeId, x.BusinessUnitId, x.Seniority, x.Location, x.ResourcingClass })
                .IsUnique();
            e.HasOne(x => x.ResourceType).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BusinessUnit).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SizingConversion>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(50);
            e.Property(x => x.Hours).HasPrecision(18, 2);
            e.HasIndex(x => new { x.Method, x.Key }).IsUnique();
        });

        b.Entity<AllocationTemplate>(e =>
        {
            e.Property(x => x.SizeKey).HasMaxLength(50);
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasIndex(x => new { x.Method, x.SizeKey }).IsUnique();
            e.HasMany(x => x.Lines).WithOne(x => x.AllocationTemplate).HasForeignKey(x => x.AllocationTemplateId);
        });

        b.Entity<AllocationTemplateLine>(e =>
        {
            e.Property(x => x.PhaseName).HasMaxLength(200);
            e.Property(x => x.Percent).HasPrecision(5, 2);
            e.HasOne(x => x.ResourceType).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Initiative>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(300);
            e.Property(x => x.SizeKey).HasMaxLength(50);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
            e.Property(x => x.VarianceThresholdPct).HasPrecision(5, 2);
            e.HasIndex(x => x.Status);
            e.HasOne(x => x.BusinessUnit).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Phases).WithOne(x => x.Initiative).HasForeignKey(x => x.InitiativeId);
            e.HasMany(x => x.Allocations).WithOne(x => x.Initiative).HasForeignKey(x => x.InitiativeId);
            e.HasMany(x => x.Members).WithOne(x => x.Initiative).HasForeignKey(x => x.InitiativeId);
            e.HasMany(x => x.Baselines).WithOne(x => x.Initiative).HasForeignKey(x => x.InitiativeId);
        });

        b.Entity<InitiativeMember>(e =>
        {
            e.HasKey(x => new { x.InitiativeId, x.UserId });
            e.Property(x => x.UserId).HasMaxLength(200);
        });

        b.Entity<Phase>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.HasMany(x => x.DateHistory).WithOne(x => x.Phase).HasForeignKey(x => x.PhaseId);
        });

        b.Entity<PhaseDateHistory>(e => e.Property(x => x.ChangedBy).HasMaxLength(200));

        b.Entity<InitiativeAllocation>(e =>
        {
            e.Property(x => x.Location).HasMaxLength(100);
            e.Property(x => x.EstimatedHours).HasPrecision(18, 2);
            e.HasOne(x => x.Phase).WithMany().HasForeignKey(x => x.PhaseId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ResourceType).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ForecastBaseline>(e =>
        {
            e.Property(x => x.SnapshotBy).HasMaxLength(200);
            e.Property(x => x.TotalHours).HasPrecision(18, 2);
            e.Property(x => x.TotalCost).HasPrecision(18, 2);
            e.HasIndex(x => new { x.InitiativeId, x.Version }).IsUnique();
            e.HasIndex(x => new { x.InitiativeId, x.IsCurrent });
            e.HasMany(x => x.Lines).WithOne(x => x.ForecastBaseline).HasForeignKey(x => x.ForecastBaselineId);
        });

        b.Entity<RebaselineRequest>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.RequestedBy).HasMaxLength(200);
            e.Property(x => x.DecidedBy).HasMaxLength(200);
            e.Property(x => x.DecisionNote).HasMaxLength(1000);
            e.HasIndex(x => new { x.InitiativeId, x.Status });
            e.HasOne(x => x.Initiative).WithMany(i => i.RebaselineRequests).HasForeignKey(x => x.InitiativeId);
            e.HasOne(x => x.ResultingBaseline).WithMany().HasForeignKey(x => x.ResultingBaselineId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ForecastBaselineLine>(e =>
        {
            e.Property(x => x.Location).HasMaxLength(100);
            e.Property(x => x.Hours).HasPrecision(18, 2);
            e.Property(x => x.HourlyRate).HasPrecision(18, 2);
            e.Property(x => x.Cost).HasPrecision(18, 2);
        });

        b.Entity<Person>(e =>
        {
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.ExternalIds).HasMaxLength(1000);
            e.Property(x => x.Location).HasMaxLength(100);
            e.HasOne(x => x.ResourceType).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BusinessUnit).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<InitiativeSourceMapping>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.ExternalProjectId).HasMaxLength(200);
            e.HasIndex(x => new { x.Source, x.ExternalProjectId }).IsUnique();
            e.HasOne(x => x.Initiative).WithMany(i => i.SourceMappings).HasForeignKey(x => x.InitiativeId);
        });

        b.Entity<ActualsImport>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(50);
            e.Property(x => x.Status).HasMaxLength(50);
            e.Property(x => x.StartedBy).HasMaxLength(200);
            e.Property(x => x.FileName).HasMaxLength(260);
            e.HasMany(x => x.Entries).WithOne(x => x.ActualsImport).HasForeignKey(x => x.ActualsImportId);
        });

        b.Entity<ActualEntry>(e =>
        {
            e.Property(x => x.SourceReference).HasMaxLength(200).UseCollation(ciCollation);
            e.Property(x => x.ExternalProjectId).HasMaxLength(200);
            e.Property(x => x.ExternalPersonId).HasMaxLength(200);
            e.Property(x => x.Hours).HasPrecision(18, 2);
            e.Property(x => x.SourcedCost).HasPrecision(18, 2);
            e.Property(x => x.CalculatedCost).HasPrecision(18, 2);
            e.HasIndex(x => x.SourceReference);
            e.HasIndex(x => new { x.InitiativeId, x.IsUnmapped, x.WorkDate });
            e.HasIndex(x => x.IsUnmapped);
            e.HasOne(x => x.Initiative).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Person).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ActualAdjustment>(e =>
        {
            e.Property(x => x.Hours).HasPrecision(18, 2);
            e.Property(x => x.Cost).HasPrecision(18, 2);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.CreatedBy).HasMaxLength(200);
        });

        b.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Entity).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Action).HasMaxLength(50);
            e.Property(x => x.UserId).HasMaxLength(200);
            e.HasIndex(x => new { x.Entity, x.EntityId });
            e.HasIndex(x => x.At);
            e.HasIndex(x => x.Action);
        });
    }
}
