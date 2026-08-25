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
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<Posting> Postings => Set<Posting>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<NumberCounter> NumberCounters => Set<NumberCounter>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<Allocation> Allocations => Set<Allocation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Enums are stored as text rather than integers. This database will be inspected
        // directly by people reconciling figures, and 'HardClosed' reads better than 3.
        builder.Entity<Account>().Property(a => a.AccountType).HasConversion<string>().HasMaxLength(20);
        builder.Entity<Account>().Property(a => a.ControlType).HasConversion<string>().HasMaxLength(20);
        builder.Entity<Account>().Property(a => a.SystemRole).HasConversion<string>().HasMaxLength(30);
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

        ConfigureLedger(builder);
        ConfigureNumbering(builder);
        ConfigureSales(builder);
        ConfigureReceivables(builder);
    }

    private static void ConfigureReceivables(ModelBuilder builder)
    {
        builder.Entity<CustomerReceipt>(e =>
        {
            e.Property(x => x.DocNo).HasMaxLength(40);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.FxRate).HasPrecision(19, 10);
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.Reference).HasMaxLength(80);
            e.Property(x => x.Memo).HasMaxLength(500);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);

            e.HasIndex(x => new { x.LegalEntityId, x.DocNo })
                .IsUnique()
                .HasFilter("doc_no IS NOT NULL");
            e.HasIndex(x => x.CustomerId);

            e.HasOne(x => x.LegalEntity).WithMany()
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany()
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.BankAccount).WithMany()
                .HasForeignKey(x => x.BankAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.JournalEntry).WithMany()
                .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint("ck_receipt_amount_positive", "amount > 0"));
            e.ToTable(t => t.HasCheckConstraint(
                "ck_receipt_posted_is_complete",
                "(state = 'Draft' AND doc_no IS NULL AND journal_entry_id IS NULL) "
                + "OR (state = 'Posted' AND doc_no IS NOT NULL AND journal_entry_id IS NOT NULL)"));
        });

        builder.Entity<Allocation>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.FunctionalAmount).HasPrecision(19, 4);
            e.Property(x => x.FxGainLossFunctional).HasPrecision(19, 4);

            e.HasIndex(x => x.CustomerReceiptId);
            e.HasIndex(x => x.SalesInvoiceId);

            // One reversal per allocation, enforced rather than merely checked in code.
            e.HasIndex(x => x.ReversesAllocationId)
                .IsUnique()
                .HasFilter("reverses_allocation_id IS NOT NULL");

            e.HasOne(x => x.CustomerReceipt).WithMany()
                .HasForeignKey(x => x.CustomerReceiptId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SalesInvoice).WithMany()
                .HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.JournalEntry).WithMany()
                .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReversesAllocation).WithMany()
                .HasForeignKey(x => x.ReversesAllocationId).OnDelete(DeleteBehavior.Restrict);

            // Zero would be a no-op recorded as if it were a decision.
            e.ToTable(t => t.HasCheckConstraint("ck_allocation_amount_nonzero", "amount <> 0"));

            // Sign carries meaning: an original allocation applies money, a reversal takes
            // it back. Mixing them would make the running total meaningless.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_allocation_sign_matches_kind",
                "(reverses_allocation_id IS NULL AND amount > 0) "
                + "OR (reverses_allocation_id IS NOT NULL AND amount < 0)"));
        });
    }

    private static void ConfigureSales(ModelBuilder builder)
    {
        builder.Entity<Customer>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.RegistrationNo).HasMaxLength(50);
            e.Property(x => x.TaxId).HasMaxLength(50);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.BillingAddress).HasMaxLength(500);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();

            // Tenant-wide, not per entity: one record for a client both companies bill.
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint(
                "ck_customer_credit_terms", "credit_term_days >= 0"));
        });

        builder.Entity<SalesInvoice>(e =>
        {
            e.Property(x => x.DocNo).HasMaxLength(40);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.FxRate).HasPrecision(19, 10);
            e.Property(x => x.Reference).HasMaxLength(80);
            e.Property(x => x.Memo).HasMaxLength(500);
            e.Property(x => x.State).HasConversion<string>().HasMaxLength(20);

            // Unique only where present: drafts have no number yet, and several may exist.
            e.HasIndex(x => new { x.LegalEntityId, x.DocNo })
                .IsUnique()
                .HasFilter("doc_no IS NOT NULL");
            e.HasIndex(x => new { x.LegalEntityId, x.DocDate });
            e.HasIndex(x => x.CustomerId);

            e.HasOne(x => x.LegalEntity).WithMany()
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Customer).WithMany()
                .HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.JournalEntry).WithMany()
                .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

            e.Ignore(x => x.Total);

            e.ToTable(t => t.HasCheckConstraint(
                "ck_sales_invoice_due_after_doc", "due_date >= doc_date"));

            // A posted invoice must have both a number and an entry; a draft neither.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_sales_invoice_posted_is_complete",
                "(state = 'Draft' AND doc_no IS NULL AND journal_entry_id IS NULL) "
                + "OR (state = 'Posted' AND doc_no IS NOT NULL AND journal_entry_id IS NOT NULL)"));
        });

        builder.Entity<SalesInvoiceLine>(e =>
        {
            e.Property(x => x.Description).HasMaxLength(500).IsRequired();
            e.Property(x => x.Quantity).HasPrecision(19, 4);
            e.Property(x => x.UnitPrice).HasPrecision(19, 4);

            e.HasIndex(x => new { x.SalesInvoiceId, x.LineNo }).IsUnique();

            e.HasOne(x => x.SalesInvoice).WithMany(x => x.Lines)
                .HasForeignKey(x => x.SalesInvoiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.RevenueAccount).WithMany()
                .HasForeignKey(x => x.RevenueAccountId).OnDelete(DeleteBehavior.Restrict);

            e.Ignore(x => x.LineTotal);

            e.ToTable(t => t.HasCheckConstraint("ck_invoice_line_quantity", "quantity > 0"));
            e.ToTable(t => t.HasCheckConstraint("ck_invoice_line_price", "unit_price > 0"));
        });
    }

    private static void ConfigureNumbering(ModelBuilder builder)
    {
        builder.Entity<NumberSeries>(e =>
        {
            e.Property(x => x.DocumentType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Format).HasMaxLength(60).IsRequired();
            e.Property(x => x.ResetPolicy).HasConversion<string>().HasMaxLength(20);

            e.HasIndex(x => new { x.LegalEntityId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.LegalEntityId, x.DocumentType, x.IsActive });

            e.HasOne(x => x.LegalEntity).WithMany()
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<NumberCounter>(e =>
        {
            e.Property(x => x.PeriodKey).HasMaxLength(10).IsRequired();

            // Without this, two callers creating the first counter for a window would each
            // insert one and the series would immediately issue duplicate numbers.
            e.HasIndex(x => new { x.NumberSeriesId, x.PeriodKey }).IsUnique();

            e.HasOne(x => x.NumberSeries).WithMany(x => x.Counters)
                .HasForeignKey(x => x.NumberSeriesId).OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint("ck_number_counter_positive", "next_number > 0"));
        });
    }

    /// <summary>
    /// The posting core. Nothing here is ever updated or deleted — the application role's
    /// privileges are revoked in the migration, so immutability is enforced below the
    /// application rather than trusted to it.
    /// </summary>
    private static void ConfigureLedger(ModelBuilder builder)
    {
        builder.Entity<Posting>().Property(p => p.Direction).HasConversion<string>().HasMaxLength(10);

        builder.Entity<JournalEntry>(e =>
        {
            e.Property(x => x.EntryNo).HasMaxLength(40).IsRequired();
            e.Property(x => x.SourceDocumentType).HasMaxLength(60).IsRequired();
            e.Property(x => x.ReasonCode).HasMaxLength(60);
            e.Property(x => x.Memo).HasMaxLength(500);

            e.HasIndex(x => new { x.LegalEntityId, x.EntryNo }).IsUnique();
            e.HasIndex(x => new { x.LegalEntityId, x.EntryDate });
            e.HasIndex(x => x.PeriodId);

            e.HasOne(x => x.LegalEntity).WithMany()
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Period).WithMany()
                .HasForeignKey(x => x.PeriodId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PostedBy).WithMany()
                .HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);

            // Both correction links point backwards, from the new entry to the old one.
            e.HasOne(x => x.Reverses).WithMany()
                .HasForeignKey(x => x.ReversesEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Supersedes).WithMany()
                .HasForeignKey(x => x.SupersedesEntryId).OnDelete(DeleteBehavior.Restrict);

            // A correction with no stated reason is what an auditor asks about first.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_journal_entry_reversal_has_reason",
                "reverses_entry_id IS NULL OR reason_code IS NOT NULL"));

            // An entry cannot reverse or supersede itself.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_journal_entry_no_self_reference",
                "(reverses_entry_id IS NULL OR reverses_entry_id <> id) "
                + "AND (supersedes_entry_id IS NULL OR supersedes_entry_id <> id)"));
        });

        builder.Entity<Posting>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(19, 4);
            e.Property(x => x.FunctionalAmount).HasPrecision(19, 4);
            e.Property(x => x.FxRate).HasPrecision(19, 10);
            e.Property(x => x.CurrencyCode).HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);

            e.HasIndex(x => new { x.JournalEntryId, x.LineNo }).IsUnique();

            // Balance queries filter by account within an entity; ageing filters by
            // customer or supplier on a control account.
            e.HasIndex(x => new { x.LegalEntityId, x.AccountId });
            e.HasIndex(x => new { x.LegalEntityId, x.CustomerId });
            e.HasIndex(x => new { x.LegalEntityId, x.SupplierId });

            e.HasOne(x => x.JournalEntry).WithMany(x => x.Postings)
                .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.IntercompanyEntity).WithMany()
                .HasForeignKey(x => x.IntercompanyEntityId).OnDelete(DeleteBehavior.Restrict);

            // Amounts are always positive; the side is Direction, not the sign. Allowing a
            // negative debit would make every balance query ambiguous.
            e.ToTable(t => t.HasCheckConstraint("ck_posting_amount_positive", "amount > 0"));
            e.ToTable(t => t.HasCheckConstraint(
                "ck_posting_functional_amount_positive", "functional_amount > 0"));
            e.ToTable(t => t.HasCheckConstraint("ck_posting_fx_rate_positive", "fx_rate > 0"));
        });
    }
}
