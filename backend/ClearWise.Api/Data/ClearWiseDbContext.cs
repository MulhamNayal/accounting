using ClearWise.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ClearWise.Api.Data;

public class ClearWiseDbContext(DbContextOptions<ClearWiseDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<LegalEntity> LegalEntities => Set<LegalEntity>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<EntityAccount> EntityAccounts => Set<EntityAccount>();
    public DbSet<FiscalYear> FiscalYears => Set<FiscalYear>();
    public DbSet<AccountingPeriod> Periods => Set<AccountingPeriod>();
    public DbSet<PeriodEvent> PeriodEvents => Set<PeriodEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Enums are stored as text rather than integers. This database will be inspected
        // directly by people reconciling figures, and 'HardClosed' reads better than 3.
        builder.Entity<Account>().Property(a => a.AccountType).HasConversion<string>().HasMaxLength(20);
        builder.Entity<Account>().Property(a => a.ControlType).HasConversion<string>().HasMaxLength(20);
        builder.Entity<FiscalYear>().Property(f => f.State).HasConversion<string>().HasMaxLength(20);
        builder.Entity<AccountingPeriod>().Property(p => p.State).HasConversion<string>().HasMaxLength(20);
        builder.Entity<PeriodEvent>().Property(e => e.FromState).HasConversion<string>().HasMaxLength(20);
        builder.Entity<PeriodEvent>().Property(e => e.ToState).HasConversion<string>().HasMaxLength(20);

        builder.Entity<Tenant>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
        });

        builder.Entity<AppUser>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.ExternalAuthId).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<LegalEntity>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.RegistrationNo).HasMaxLength(50);
            e.Property(x => x.TaxId).HasMaxLength(50);
            e.Property(x => x.FunctionalCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany(t => t.Entities).HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_legal_entity_fy_start_month",
                "financial_year_start_month BETWEEN 1 AND 12"));
        });

        builder.Entity<Account>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasOne(x => x.Tenant).WithMany().HasForeignKey(x => x.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Parent).WithMany(x => x.Children).HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.NormalBalance);
        });

        builder.Entity<EntityAccount>(e =>
        {
            e.Property(x => x.LocalName).HasMaxLength(200);
            e.HasIndex(x => new { x.LegalEntityId, x.AccountId }).IsUnique();
            e.HasOne(x => x.LegalEntity).WithMany(x => x.EntityAccounts)
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Account).WithMany(x => x.EntityAccounts)
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<FiscalYear>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.HasIndex(x => new { x.LegalEntityId, x.Code }).IsUnique();
            e.HasOne(x => x.LegalEntity).WithMany(x => x.FiscalYears)
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_fiscal_year_dates", "end_date > start_date"));
        });

        builder.Entity<AccountingPeriod>(e =>
        {
            e.HasIndex(x => new { x.FiscalYearId, x.Sequence }).IsUnique();
            e.HasOne(x => x.FiscalYear).WithMany(x => x.Periods)
                .HasForeignKey(x => x.FiscalYearId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LegalEntity).WithMany()
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
            e.ToTable(t => t.HasCheckConstraint(
                "ck_period_dates", "end_date >= start_date"));
        });

        builder.Entity<PeriodEvent>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasIndex(x => new { x.PeriodId, x.AtUtc });
            e.HasOne(x => x.Period).WithMany(x => x.Events)
                .HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ByUser).WithMany()
                .HasForeignKey(x => x.ByUserId).OnDelete(DeleteBehavior.Restrict);
            // A transition that changes nothing is not an event worth recording, and is
            // more likely a bug than an intent.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_period_event_state_changed", "from_state <> to_state"));
        });
    }
}
