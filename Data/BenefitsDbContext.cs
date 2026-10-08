using BenefitCardsPortal.Models;
using BenefitCardsPortal.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using BenefitCardsPortal.Services;

namespace BenefitCardsPortal.Data;

public class BenefitsDbContext(DbContextOptions<BenefitsDbContext> options) : IdentityDbContext<PortalUser>(options)
{
    public DbSet<BenefitBatch> BenefitBatches => Set<BenefitBatch>();
    public DbSet<BenefitCardRecord> BenefitCardRecords => Set<BenefitCardRecord>();
    public DbSet<EmployeeAuditEvent> EmployeeAuditEvents => Set<EmployeeAuditEvent>();
    public DbSet<PortalSetting> PortalSettings => Set<PortalSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<BenefitBatch>()
            .HasMany(batch => batch.Records).WithOne(record => record.BenefitBatch)
            .HasForeignKey(record => record.BenefitBatchId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<BenefitCardRecord>().HasIndex(record => record.NationalNumber);
        modelBuilder.Entity<BenefitCardRecord>().HasIndex(record => record.BeneficiaryNameNormalized);
        modelBuilder.Entity<EmployeeAuditEvent>().HasIndex(entry => entry.OccurredAtUtc);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        NormalizeBeneficiaryNames();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        NormalizeBeneficiaryNames();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void NormalizeBeneficiaryNames()
    {
        foreach (var entry in ChangeTracker.Entries<BenefitCardRecord>().Where(entry =>
                     entry.State == EntityState.Added || entry.Property(record => record.BeneficiaryName).IsModified))
            entry.Entity.BeneficiaryNameNormalized = ArabicNameNormalizer.Normalize(entry.Entity.BeneficiaryName);
    }
}
