# CLAUDE.md â€” backend

Read the root `CLAUDE.md` first. This covers the C# side only.

**If you have also worked in `erp-api`/`IqiCore`:** four rules here are the *opposite* of
that codebase's â€” lazy loading, EF migrations, the repository layer, and error handling. Do
not carry those habits over.

**Architecture:** `Controllers â†’ Services â†’ DbContext â†’ PostgreSQL`. There is **no
repository layer**; services use `AccountingDbContext` directly.

---

## The ledger is immutable, and PostgreSQL enforces it

Do not weaken any of this. It is the product.

- `UPDATE` and `DELETE` are **revoked** from `clearwise_app` on `journal_entries`,
  `postings` and `period_events`.
- A **deferred** constraint trigger asserts debits = credits in functional currency at
  `COMMIT`, so a multi-row insert stays legal while in progress.
- Correction links point **backwards only** â€” `reverses_entry_id` and
  `supersedes_entry_id` sit on the *new* row. A forward pointer would require updating the
  original, which the revoke forbids.
- A posting to a control account **must** carry its dimension: receivables needs a
  customer, payables a supplier, stock an item.
- Nothing posts into a closed period, and the entry date must fall inside the period it
  names.
- Documents are `Draft` (mutable) or `Posted` (frozen by trigger). Posting is one-way, and
  a document's lines are frozen through their parent.

There is **no** `UpdatedAt`, `UpdateCount` or `IsCancelled` on a ledger table. Their absence
is the design: a column recording mutation implies mutation is possible.

---

## Two database roles

| Role | Used by | Holds |
|---|---|---|
| `clearwise_owner` | EF migrations, via `AccountingDbContextFactory` | DDL |
| `clearwise_app` | The running application | DML only, minus the revokes above |

**The application must never own the ledger tables** â€” an owner can grant itself back what
the design revokes. Keep the two connection strings separate.

Tenant isolation is **row level security**, not query discipline. Every tenant-scoped table
has a policy on `tenant_id` and is `FORCE`d. Write queries **without** a tenant predicate:
the database applies it, so a forgotten filter cannot leak. An unset tenant matches nothing
and returns no rows, which is the correct way to fail.

---

## EF Core

- **Code-first migrations are the source of truth.** Never hand-edit the schema.
  `dotnet ef migrations add <Name> --project backend/Accounting.Api/Accounting.Api.csproj`
- **Lazy loading is OFF.** `.Include()`/`.ThenInclude()` is required, not banned â€” a
  used-but-not-included navigation property is `null`, not a silent extra query.
- **Read every generated migration before applying it**, especially `defaultValue` on new
  non-nullable columns. EF emits the CLR default, which is often wrong for existing rows.
  This has already bitten once: a string-converted enum was given `defaultValue: ""` when
  the correct value was `"None"`, which would have broken every read of that table.
- Released migrations are immutable. Fix mistakes with a new migration.
- **A migration must never rewrite a posted row.** If a change alters what a stored value
  *means*, that is a restatement and needs before-and-after trial balances that reconcile.
- `snake_case` naming via `EFCore.NamingConventions`, already configured.
- Versions are pinned centrally in `Directory.Packages.props`, with transitive pinning on.

---

## Services

- **One service per feature or entity.** A service handling a second entity's lifecycle
  should be split.
- **Validate before persisting.** The database guarantees correctness but its messages are
  terse; the service exists to reject bad input early with something a person can act on.
  Where the two disagree, **the database is right**.
- **Guard clauses first.** Immutable request DTOs (records). No God classes.
- **Constructor injection only**, registered in `Program.cs`. Never `new` up a service or a
  `DbContext` inside another.
- Posting rules are **code, not configuration** â€” a pure function from document to posting
  set, run once at posting time, its output then a frozen fact. **Never re-derive postings.**
- **Allocate document numbers inside the transaction that writes the document.** A gapless
  series is gapless only because a rolled-back write rolls back the counter too.
- A service that needs to be atomic with a posting should open the transaction and let
  `PostingService` join it rather than open its own.

---

## Controllers

- **Thin.** Route, call one service, return. No business logic and **no `DbContext`**.
- **Never catch an exception.** `GlobalExceptionHandler` maps type â†’ status in one place:
  `NotFoundException` â†’ 404, `PostingValidationException` â†’ 400,
  `LedgerIntegrityException` â†’ 409, anything else â†’ 502 `ProblemDetails`.
- 400/404/409 bodies are a **raw JSON string** so the frontend reads the message directly.
  Don't change that shape without updating `frontend/src/api/client.ts`.
- Routes are `kebab-case`: `/api/sales-invoices`, `/api/journal-entries`.
- Use `CreatedAtRoute` with an explicit route name, never `CreatedAtAction(nameof(...))` â€”
  ASP.NET strips the `Async` suffix and the mismatch throws *after* the entity is committed,
  turning a success into a misleading error.
- Simple field-required checks stay as inline guard clauses returning `BadRequest`.
- **Always paginate list endpoints.** Never return an unbounded result set.

---

## Code Style

- Braces always, even single-line bodies.
- Naming: `{Entity}Controller`, `{Entity}Service`, `_camelCase` private fields, `Async`
  suffix on async methods.
- All I/O async. Never `.Result`, `.Wait()` or `Thread.Sleep`. Thread `CancellationToken`
  through the whole call stack.
- Structured log templates, never interpolation. Never log secrets or PII:
  `logger.LogInformation("Posted {EntryNo} for {Entity}", entryNo, code)`
- `IOptions<T>` in services, not raw `IConfiguration`.
- Money is `decimal` with explicit precision â€” `numeric(19,4)` amounts,
  `numeric(19,10)` rates.
- Filter at the query level, not in memory.

---

## Testing

- **Tests run against a real PostgreSQL**, never an in-memory provider. RLS, deferred
  triggers and revoked privileges are the things under test, and no in-memory provider
  implements any of them â€” a green suite against one would be actively misleading.
- Each test builds its own tenant via `LedgerFixture`, so tests never see each other's rows.
- Method naming: `MethodName_Scenario_ExpectedResult`.
- **Don't assert the exception type for a deferred constraint** â€” it surfaces from `COMMIT`
  unwrapped, not inside `DbUpdateException`. Assert the message instead.
- Add tests alongside the code, not in a later pass.

---

## Never Do

- Put business logic or a `DbContext` in a controller
- Catch an exception in a controller to map a status â€” throw a typed exception
- Weaken the ledger revokes, the balance trigger, or a document freeze trigger
- Add `UpdatedAt`/`IsCancelled` to a ledger table
- Re-derive postings for a document that is already posted
- Add a tenant predicate as a substitute for RLS, or bypass RLS
- Apply a migration without reading its `defaultValue`s
- Allocate a document number outside the document's transaction
- Avoid `.Include()` out of lazy-loading habit â€” lazy loading is **OFF** here
- `new` up a service or `DbContext` inside another
