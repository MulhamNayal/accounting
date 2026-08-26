using Accounting.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Accounting.Api.Services;

public interface ICustomerService
{
    Task<IReadOnlyList<CustomerSummary>> ListAsync(CancellationToken ct = default);
}

public sealed class CustomerService(AccountingDbContext db) : ICustomerService
{
    /// <summary>
    /// Customers in the current tenant.
    /// </summary>
    /// <remarks>
    /// Tenant-wide rather than per entity, so a group billing one client from two companies
    /// keeps a single record for them. No balance is returned here â€” what a customer owes
    /// comes from the receivables postings carrying their id, via
    /// <see cref="IReceivablesService"/>.
    /// </remarks>
    public async Task<IReadOnlyList<CustomerSummary>> ListAsync(CancellationToken ct = default)
        => await db.Customers
            .AsNoTracking()
            .OrderBy(c => c.Code)
            .Select(c => new CustomerSummary(
                c.Id, c.Code, c.Name, c.TaxId, c.CurrencyCode, c.CreditTermDays, c.IsActive))
            .ToListAsync(ct);
}
