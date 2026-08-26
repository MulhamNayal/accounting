using Accounting.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Data;

public class AccountingDbContext(DbContextOptions<AccountingDbContext> options) : DbContext(options)
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
    public DbSet<TaxRegime> TaxRegimes => Set<TaxRegime>();
    public DbSet<TaxCode> TaxCodes => Set<TaxCode>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<StockMove> StockMoves => Set<StockMove>();
    public DbSet<CostLayer> CostLayers => Set<CostLayer>();
    public DbSet<CostConsumption> CostConsumptions => Set<CostConsumption>();
    public DbSet<ExchangeRate> ExchangeRates => Set<ExchangeRate>();
    public DbSet<ConsolidationRun> ConsolidationRuns => Set<ConsolidationRun>();
    public DbSet<ConsolidationPosting> ConsolidationPostings => Set<ConsolidationPosting>();

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
        ConfigureTax(builder);
        ConfigureStock(builder);
        ConfigureConsolidation(builder);
    }

    private static void ConfigureConsolidation(ModelBuilder builder)
    {
        builder.Entity<ExchangeRate>(e =>
        {
            e.Property(x => x.FromCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.ToCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.ClosingRate).HasPrecision(19, 10);
            e.Property(x => x.AverageRate).HasPrecision(19, 10);
            e.Property(x => x.Source).HasMaxLength(120);

            e.HasIndex(x => new { x.TenantId, x.FromCurrency, x.ToCurrency, x.RateDate }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint(
                "ck_exchange_rate_positive",
                "closing_rate > 0 AND (average_rate IS NULL OR average_rate > 0)"));

            // A currency's rate against itself is always one and recording it invites a
            // contradictory row.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_exchange_rate_distinct_currencies", "from_currency <> to_currency"));
        });

        builder.Entity<ConsolidationRun>(e =>
        {
            e.Property(x => x.PresentationCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.Note).HasMaxLength(500);

            e.HasIndex(x => new { x.TenantId, x.AsOf });
        });

        builder.Entity<ConsolidationPosting>(e =>
        {
            e.Property(x => x.Direction).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.FunctionalAmount).HasPrecision(19, 4);
            e.Property(x => x.PresentationAmount).HasPrecision(19, 4);
            e.Property(x => x.RateUsed).HasPrecision(19, 10);

            e.HasIndex(x => x.ConsolidationRunId);

            e.HasOne(x => x.ConsolidationRun).WithMany(r => r.Postings)
                .HasForeignKey(x => x.ConsolidationRunId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Account).WithMany()
                .HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.LegalEntity).WithMany()
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);

            // Only a translation line belongs to no entity; an entity balance or an
            // elimination is always attributable to one.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_consolidation_line_entity",
                "legal_entity_id IS NOT NULL OR kind = 'Translation'"));
        });
    }

    private static void ConfigureStock(ModelBuilder builder)
    {
        builder.Entity<StockMove>().Property(m => m.Direction).HasConversion<string>().HasMaxLength(10);

        builder.Entity<Item>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(40).IsRequired();
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.BaseUom).HasMaxLength(20).IsRequired();

            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            e.HasOne(x => x.InventoryAccount).WithMany()
                .HasForeignKey(x => x.InventoryAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.CostOfSalesAccount).WithMany()
                .HasForeignKey(x => x.CostOfSalesAccountId).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<StockMove>(e =>
        {
            e.Property(x => x.Quantity).HasPrecision(19, 4);
            e.Property(x => x.SourceDocumentType).HasMaxLength(60).IsRequired();
            e.Property(x => x.Description).HasMaxLength(500);

            e.HasIndex(x => new { x.LegalEntityId, x.ItemId, x.MovedOn });

            e.HasOne(x => x.Item).WithMany()
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.JournalEntry).WithMany()
                .HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);

            // Direction carries the sign, never the quantity. A negative quantity would make
            // every on-hand sum ambiguous.
            e.ToTable(t => t.HasCheckConstraint("ck_stock_move_quantity", "quantity > 0"));
        });

        builder.Entity<CostLayer>(e =>
        {
            e.Property(x => x.QuantityReceived).HasPrecision(19, 4);
            e.Property(x => x.UnitCost).HasPrecision(19, 4);

            // Consumption walks this in order, so it must be unique per item.
            e.HasIndex(x => new { x.ItemId, x.Sequence }).IsUnique();
            e.HasIndex(x => new { x.LegalEntityId, x.ItemId });

            e.HasOne(x => x.Item).WithMany()
                .HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.SourceMove).WithMany()
                .HasForeignKey(x => x.SourceMoveId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AdjustsLayer).WithMany()
                .HasForeignKey(x => x.AdjustsLayerId).OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint("ck_cost_layer_cost", "unit_cost > 0"));

            // A receipt brings quantity; an adjustment revises cost and brings none. Anything
            // else would either invent stock or create a layer nothing can consume.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_cost_layer_quantity_matches_kind",
                "(adjusts_layer_id IS NULL AND quantity_received > 0) "
                + "OR (adjusts_layer_id IS NOT NULL AND quantity_received = 0)"));
        });

        builder.Entity<CostConsumption>(e =>
        {
            e.Property(x => x.Quantity).HasPrecision(19, 4);
            e.Property(x => x.UnitCost).HasPrecision(19, 4);
            e.Property(x => x.Amount).HasPrecision(19, 4);

            e.HasIndex(x => x.CostLayerId);
            e.HasIndex(x => x.OutMoveId);

            e.HasOne(x => x.CostLayer).WithMany(l => l.Consumptions)
                .HasForeignKey(x => x.CostLayerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OutMove).WithMany()
                .HasForeignKey(x => x.OutMoveId).OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint("ck_cost_consumption_quantity", "quantity > 0"));
        });
    }

    private static void ConfigureTax(ModelBuilder builder)
    {
        builder.Entity<TaxRegime>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.CountryCode).HasMaxLength(2).IsFixedLength().IsRequired();

            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint(
                "ck_tax_regime_dates", "effective_to IS NULL OR effective_to >= effective_from"));
        });

        builder.Entity<TaxCode>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(20).IsRequired();
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Rate).HasPrecision(9, 4);

            e.HasIndex(x => new { x.TaxRegimeId, x.Code }).IsUnique();

            e.HasOne(x => x.TaxRegime).WithMany(r => r.Codes)
                .HasForeignKey(x => x.TaxRegimeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OutputAccount).WithMany()
                .HasForeignKey(x => x.OutputAccountId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InputAccount).WithMany()
                .HasForeignKey(x => x.InputAccountId).OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t => t.HasCheckConstraint("ck_tax_code_rate", "rate >= 0 AND rate <= 100"));
            e.ToTable(t => t.HasCheckConstraint(
                "ck_tax_code_dates", "effective_to IS NULL OR effective_to >= effective_from"));

            // A code that charges tax must say where it goes. A zero-rated or exempt code
            // needs no account, which is why this is conditional rather than NOT NULL.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_tax_code_has_output_account",
                "rate = 0 OR output_account_id IS NOT NULL"));
        });

        builder.Entity<SalesInvoiceLine>(e =>
        {
            e.Property(x => x.TaxRate).HasPrecision(9, 4);
            e.HasOne(x => x.TaxCode).WithMany()
                .HasForeignKey(x => x.TaxCodeId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.TaxAmount);
            e.Ignore(x => x.LineTotalWithTax);

            // A line with no code charges no tax; a line with a code carries whatever rate
            // was in force. Mismatching the two makes the stored rate meaningless.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_invoice_line_tax_rate_matches_code",
                "(tax_code_id IS NULL AND tax_rate = 0) OR tax_code_id IS NOT NULL"));
        });
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
            e.Ignore(x => x.TaxTotal);
            e.Ignore(x => x.TotalWithTax);

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
    /// The posting core. Nothing here is ever updated or deleted â€” the application role's
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
            // legal_entity_id carried no foreign key until now, so a posting could name an
            // entity that does not exist. Every existing row references a real one, so this
            // constrains what was already true rather than changing anything.
            e.HasOne(x => x.LegalEntity).WithMany()
                .HasForeignKey(x => x.LegalEntityId).OnDelete(DeleteBehavior.Restrict);
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
