# Accounting

Open-source double-entry accounting, inventory and compliance platform for small and
medium businesses.

> **Status: pre-alpha, and honest about it.** Layers 0–6 of eight are built and tested, and
> the whole stack runs. Nobody's real books belong in it yet: there is no migration importer,
> no purchase or payables side, and no period-close mechanics. Treat it as a working
> foundation, not a product.

## Why this exists

The accounting software small businesses actually run is fast, locally compliant, and
single-machine. It is also, almost universally, built so that a posted transaction can be
edited in place — with a change log written by the application, if the application chooses to.

That last clause is the whole point. An audit trail the storage layer does not enforce records
what a cooperating program decided to record. Examining a production company file from a
widely-used desktop system showed exactly this: documents updated in place, cancellation as a
boolean flag, deletion possible, and every trace of it written by the application. The
database itself had no triggers and its factory-default password still worked.

Accounting's differentiator is not features — incumbents have more. It is that **the books
cannot be altered without evidence, and that is enforced below the application.**

## What that means concretely

Every one of these is enforced by PostgreSQL, not by application discipline:

- **`UPDATE` and `DELETE` are revoked** from the application's database role on journal
  entries, postings, allocations, stock movements and cost history. The application can
  append and read. It has no means to alter a posted row — not by bug, not by malice, not by
  a support engineer in a hurry.
- **Every entry balances**, checked by a deferred constraint trigger at commit. "The ledger
  is provably balanced" is a constraint here, not an aspiration.
- **Corrections are new entries** linked backwards to what they correct. There is no
  `UpdatedAt`, no `UpdateCount`, no `IsCancelled` on any ledger table — their absence is the
  design.
- **Tenant isolation is row level security**, so a forgotten `WHERE` clause cannot leak
  another company's books. The tenant comes from a signed token claim, never from request
  input.
- **A posting to a control account must carry its dimension** — receivables needs a customer,
  stock needs an item — so a subledger can never quietly drift from its control account.

The limit worth stating plainly: this stops the *application*. Someone with the database
owner's credentials can still act. That is true of every system; it means the guarantee is
"the app cannot alter the books", not "nobody can".

## Built so far

| Layer | | |
|---|---|---|
| 0 | Tenancy, entities, chart of accounts, periods | ✅ |
| 1 | Immutable posting core, corrections, multi-currency | ✅ |
| 2 | Number series, documents, posting rules, sales invoices | ✅ |
| 3 | Receivables, allocation, realised FX, ageing | ✅ |
| 4 | Tax as a jurisdiction abstraction | ✅ |
| 5 | Stock with FIFO cost layers and the correction cascade | ✅ |
| 6 | Consolidation, eliminations, currency translation | ✅ |
| 7 | Payables: suppliers, bills, payments, allocation, ageing | ✅ |
| 8 | Migration importer | ⬜ |

Plus authentication, the profit and loss account and balance sheet, and a React front end
covering sales invoices, receipts, receivables ageing, bills, payments, payables ageing,
stock, journals, the three statements and the chart of accounts.

**Not built, and the list is still longer than the one above:** credit and debit notes on
either side, the sales and purchase document chain (quotation, order, delivery note, goods
received), bank reconciliation, stock takes, transfers and multiple locations, period close and
year-end, tax returns, user and permission management, printable document layouts, e-Invoice
submission, the migration importer, and a UI for consolidation.

What exists is a correct, tamper-evident ledger core with both trading sides, stock and group
reporting on top. That is the hard part and the part incumbents get wrong; it is not yet a
product anyone should run a business on. `docs/superpowers/specs/` has the design;
`docs/research/` has what the incumbent-system examination found.

## Scope

- **General ledger** — chart of accounts, journals, immutable postings, dimensions, and the
  trial balance, profit and loss account and balance sheet, each computed from postings on
  every request so that no stored figure can drift from the ledger it describes
- **Receivables** — customers, invoices, receipts, allocation, ageing, statements
- **Payables** — suppliers, bills, payments, allocation, ageing, statements, a duplicate-bill
  control keyed on the supplier's own invoice number, and input tax that goes to an asset or
  into the cost depending on whether the regime allows a reclaim
- **Inventory** — FIFO cost layers, issues costed from the layers actually consumed, and
  retroactive cost corrections that adjust inventory and cost of sales without rewriting
  history
- **Tax** — effective-dated regimes and codes, so a jurisdiction is added rather than coded
  around, and a superseded regime's history is never restated
- **Group reporting** — intercompany elimination and IAS 21 translation into a presentation
  currency
- **Migration** — an importer so a business can leave its existing system with its history
  intact. Not built yet, and it is the commercially decisive piece: correct books alone do
  not make anyone switch.

## International by construction

Multi-currency is not a later addition. Every posting stores its transaction amount, the
functional amount, and the rate used. Entities keep their own functional currency and their
own financial year, and consolidate into a presentation currency. Accounts are found by role,
never by code, because a chart's numbering belongs to its owner.

The immutability and gap-free numbering are advantages abroad rather than local quirks —
France, Germany, Italy and Portugal all mandate tamper-evident books, and several require
dense document sequences.

What remains jurisdiction-shaped is tax detail (VAT input reclaim, partial exemption, reverse
charge) and e-invoice adapters. Both are extension points on the existing abstractions rather
than rewrites.

## Stack

ASP.NET Core / C# on .NET 10, React + TypeScript with Fluent UI React, PostgreSQL.

`Controllers → Services → DbContext → PostgreSQL`. No repository layer. Two database roles:
one owns the schema and runs migrations, one runs the application and deliberately cannot
alter the ledger.

## Running it

Requires .NET 10 SDK, Node, and PostgreSQL.

```bash
# databases and roles: see backend/CLAUDE.md
cd backend/Accounting.Api
dotnet user-secrets set "ConnectionStrings:AccountingDatabase" "Host=localhost;Database=accounting_dev;Username=accounting_app;Password=..."
dotnet user-secrets set "ConnectionStrings:MigrationDatabase" "Host=localhost;Database=accounting_dev;Username=accounting_owner;Password=..."
dotnet user-secrets set "Jwt:SigningKey" "<at least 32 bytes of random>"

dotnet ef database update --project backend/Accounting.Api/Accounting.Api.csproj
dotnet run --urls http://localhost:5100          # API
cd frontend && npm install && npm run dev        # :5173

dotnet test Accounting.slnx                       # needs a local PostgreSQL
```

Development seeds a demo tenant with two entities, a chart of accounts, tax regimes and a
sign-in of `demo@accounting.test` / `accounting-demo`.

Tests run against a real PostgreSQL, never an in-memory provider — row level security,
deferred triggers and revoked privileges are the things under test, and no in-memory provider
implements any of them.

## Provenance

Accounting is a clean-room implementation, designed by studying commercial accounting software
as a user and from public documentation. No source code, database schema, or proprietary API
contract from any commercial or internally-licensed system is copied into this repository.
The migration importer, when built, will work from data the customer owns and exports.

## Licence

AGPL-3.0. See [LICENSE](LICENSE).
