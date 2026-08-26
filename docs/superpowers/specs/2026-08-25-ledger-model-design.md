# Accounting Ledger Model â€” Design

Date: 2026-08-25
Status: proposed, awaiting review

## Overview

This specifies the foundation of Accounting: how a financial fact is stored, corrected,
constrained and reported. Everything else in the product â€” sales, purchasing, stock, tax,
consolidation, the migration importer â€” is a consumer of what is defined here.

It is one document rather than several because these decisions interlock. Specifying the
posting model apart from inventory costing produces a posting primitive that cannot express
a cost adjustment; specifying corrections apart from tax produces two incompatible
correction behaviours in one product. The document is one thing; the **build order is
layered** (see *Build order*) so implementation can proceed in sequence.

### Decisions this design rests on

| Decision | Choice |
|---|---|
| Tenancy | Multi-tenant SaaS â€” one deployment, many customers |
| Entities | Multi-entity per tenant, with consolidation |
| Currency | Full multi-currency, including revaluation and translation |
| Corrections | Immutable append-only ledger; edit = reverse + repost |
| Chart of accounts | Shared master at tenant level, per-entity activation |
| Inventory costing | FIFO cost layers |
| Periods | Soft monthly close, hard year-end close |
| Numbering | Gap-free for tax documents; unique-but-gappy elsewhere |
| Ledger architecture | One universal posting table; subledgers derived |
| Database | PostgreSQL |

## The constraint that decides the design

A measured examination of a production company file from the incumbent desktop system
(recorded in `docs/research/2026-08-24-source-system-correction-behaviour.md`) found:

- Posted documents are `UPDATE`d in place, tracked by an `UPDATECOUNT` column. Around 3%
  of sales invoices had been edited after posting.
- Cancellation is a **boolean flag** on the document, not a reversing entry.
- Documents are sometimes **deleted outright**.
- The audit trail is a field-level `OLD`/`NEW` change log â€” and it is written **entirely by
  the application**. The database has zero triggers.
- Period control is a single mutable table of permitted posting-date windows. There is no
  fiscal period entity and no concept of a closed year.

The last two together are the constraint. An audit trail that the storage layer does not
enforce records only what a cooperating application chose to record. During research, the
database was opened with the vendor's factory-default `SYSDBA` password and read freely; a
write would have left no trace whatsoever.

**So Accounting's differentiator is not features. It is that the books cannot be altered
without evidence, and that this is enforced below the application.** Every choice below
follows from that, and it is why immutability is structural rather than conventional.

A second, external constraint points the same way. Under Malaysia's e-Invoice regime, once
an invoice is validated by LHDN it cannot be edited, and after the cancellation window it
cannot be cancelled â€” corrections must be credit, debit or refund notes referencing the
original. A mutable document model therefore needs *two* correction behaviours; an
immutable one needs only the one it already has.

> **To verify before implementation:** the current MyInvois cancellation window and
> correction rules against LHDN's latest published guidance. This has changed repeatedly
> and is not evidenced by the research file, which contains no e-invoice activity.

## Layer 0 â€” Tenancy, entities, accounts

```
tenant           id, name, created_at_utc
app_user         id, tenant_id, email, display_name, ...
entity           id, tenant_id, code, name, registration_no, tax_id,
                 functional_currency, fy_start_month, is_active
```

Every business table below carries `tenant_id`. Isolation is enforced by **PostgreSQL Row
Level Security**, with a policy on `tenant_id` matching a session variable set per request.
The application role is granted no `BYPASSRLS`. A forgotten predicate in a query therefore
cannot return another tenant's rows â€” isolation does not depend on the correctness of every
query.

`tax_id` is per entity because e-Invoice identity is per TIN, and each entity files
separately. This is a hard requirement, not a convenience.

### Chart of accounts

```
account          id, tenant_id, code, name, account_type, parent_id,
                 is_postable, control_type, is_active
entity_account   entity_id, account_id, is_active, local_name
```

`account_type` is one of `Asset`, `Liability`, `Equity`, `Income`, `Expense`. Normal balance
is derived from type, never stored: assets and expenses are debit-normal, the rest
credit-normal. Storing it invites disagreement with the type.

`control_type` marks accounts whose balance is *composed of* subledger detail â€” `AR`, `AP`,
`Stock`, `Tax`, `Bank`. Postings to a control account **must** carry the corresponding
dimension (a receivables posting must name a customer). This is what makes derived
subledgers possible, and it is enforced as a check at post time.

Only leaf accounts are postable; parents exist for rollup. The chart lives at tenant level;
`entity_account` activates accounts per entity and allows a local label. Because both
entities share account codes, consolidation needs no mapping table.

## Layer 1 â€” The posting core

This is the heart of the system, and the only part that must be perfect.

```
journal_entry
  id, tenant_id, entity_id
  entry_no                    -- from a number series
  entry_date                  -- accounting date; drives period
  period_id
  source_doc_type             -- 'SalesInvoice', 'Payment', 'StockIssue', 'Manual', ...
  source_doc_id
  posted_at_utc, posted_by_user_id
  reverses_entry_id           -- null unless this entry reverses another
  supersedes_entry_id         -- null unless this entry replaces a reversed one
  reason_code, memo

posting
  id, tenant_id, entity_id, journal_entry_id, line_no
  account_id
  direction                   -- 'D' | 'C'
  amount            numeric(19,4)    -- transaction currency
  currency_code
  functional_amount numeric(19,4)    -- entity functional currency
  fx_rate           numeric(19,10)
  -- dimensions, all nullable
  customer_id, supplier_id, item_id, location_id,
  project_id, agent_id, area_id,
  intercompany_entity_id,
  tax_code_id
```

### Why one table

A sales invoice for RM1,000 produces exactly two rows â€” a debit to receivables and a credit
to income â€” both tagged with the customer. "What does this customer owe" and "what is the
receivables balance" are then two queries over *the same rows*, and cannot disagree.

The alternative, a separate AR ledger posting summaries into the GL, stores the same fact
twice and makes subledger-to-control-account drift a permanent operational burden. That
drift is one of the most common and most expensive defect classes in accounting software.
Here it is not unlikely; it is unrepresentable.

Dimensions carry the reporting axes the market demands â€” the incumbent's invoice table
carries area, agent and project, so agent commission and project profitability are expected
features. Deriving them from postings means they always reconcile to the ledger.

### The three invariants, enforced by the database

**1. Every entry balances.** A deferred constraint trigger, checked at commit, asserts that
for each `journal_entry`, the sum of debit `functional_amount` equals the sum of credit
`functional_amount`. Deferred rather than immediate, so a multi-row insert is legal
mid-transaction. This makes "the ledger is provably balanced" a constraint rather than an
aspiration.

Note it is enforced in *functional* currency only. Transaction-currency amounts need not
balance across a multi-currency entry, because they are different units.

**2. Postings are append-only.** `REVOKE UPDATE, DELETE ON posting, journal_entry FROM
<app_role>`. The application can insert and select; it is structurally incapable of altering
or removing a posted row. This is the specific weakness found in the incumbent, closed at
the layer where it can actually be closed.

**3. Control accounts carry their dimension.** A check constraint: a posting to an account
whose `control_type` is `AR` requires `customer_id`; `AP` requires `supplier_id`; `Stock`
requires `item_id`. Without this, a derived subledger silently loses rows.

### There is no `updated_at`

No `update_count`, no `is_cancelled`, no change-log table. The absence is deliberate and is
the design. A column that records mutation implies mutation is possible.

## Layer 1a â€” Corrections

A posted entry is never touched. Two operations exist.

**Reverse.** Insert a new `journal_entry` with `reverses_entry_id` pointing at the original
and postings mirroring it with directions flipped. Same amounts, same dimensions, same
functional amounts â€” so the pair sums to zero and neither row moved.

**Repost.** Insert a further new entry with `supersedes_entry_id` pointing at the reversed
original, carrying the corrected figures.

Both pointers face **backwards**, from the new row to the old. This matters: a forward
pointer (`replaced_by_entry_id` on the original) would require updating the original, which
the immutability grant forbids. Nothing about a posted entry ever changes, including its
links.

The current state of a document is therefore *derived*: the entry in its chain with no
successor. For query speed a `document_current_entry` projection may cache this â€” clearly
labelled a cache, rebuildable by replay, never a source of truth.

The user-facing Edit action performs reverse-and-repost in one transaction. Users get the
convenience they have today; the ledger keeps the history. Reversal pairs are hidden from
the default day-book view and shown under an explicit "include corrections" toggle, so the
convenience does not come at the cost of a day book that looks like noise.

`reason_code` is mandatory on any entry carrying `reverses_entry_id`. A correction without a
stated reason is the thing an auditor asks about.

## Layer 1b â€” Currency

Each entity has a functional currency. Every posting stores the transaction amount and
currency, the functional amount, and the rate used. The rate is stored, not looked up later,
because a historical posting must always reproduce the same functional figure.

```
fx_rate   tenant_id, from_currency, to_currency, rate_date, rate, source
```

Three distinct mechanics:

**Realised FX** arises on settlement. A USD invoice recorded at one rate, paid at another,
produces a functional-currency difference. That difference posts to an FX gain/loss account
as part of the allocation entry (Layer 3).

**Unrealised FX** arises at period end on open foreign-currency balances. A revaluation run
posts a normal journal entry restating them at the closing rate. It is an ordinary entry,
reversible and auditable like any other.

**Translation** arises on consolidation when entities have different functional currencies:
balance-sheet items at closing rate, income-statement items at period-average rate, the
difference to a currency translation reserve. Covered in Layer 6.

## Layer 1c â€” Periods and closing

```
fiscal_year     id, tenant_id, entity_id, code, start_date, end_date, state
period          id, tenant_id, entity_id, fiscal_year_id, seq,
                start_date, end_date, state
period_event    id, period_id, from_state, to_state, at_utc, by_user_id, reason
```

`state` is `Open`, `SoftClosed`, or `HardClosed`.

- Posting is permitted only into an `Open` period. The period is resolved from
  `entry_date`, not from the wall clock â€” back-dating into an open period is normal and
  allowed.
- `SoftClosed` blocks posting but may be reopened by an authorised role. Every transition
  writes a `period_event` row, including who and why. `period_event` is append-only.
- `HardClosed` is terminal. **No transition out of it exists in the model** â€” not a
  permission check that could be granted, an absent code path.
- Year-end close posts a closing entry moving income and expense balances to retained
  earnings, then hard-closes every period in the year. It is itself a journal entry, so it
  is visible and reversible until the year is hard-closed.

This is deliberately stricter than the incumbent, which has no close at all. An immutable
ledger whose periods reopen indefinitely is not meaningfully immutable â€” the guarantee has
to terminate somewhere.

## Layer 2 â€” Document numbering

```
number_series    id, tenant_id, entity_id, doc_type, format,
                 reset_policy, is_gapless, is_active
number_counter   series_id, period_key, next_number
```

`format` is a template (`IV-{0:D5}`, `INV/{yyyy}/{0:D4}`). Multiple active series per
document type are supported â€” the research file shows two concurrent invoice series in one
company, so this is a real requirement, not flexibility for its own sake.

**Gapless series** (sales invoices, credit notes, debit notes) allocate inside the same
transaction that commits the document, taking a row lock on `number_counter`. If the
transaction rolls back, so does the increment, so no number is burned. This serialises
inserts within a single `(entity, doc_type, period)` â€” an accepted cost, paid only where a
tax authority actually examines the sequence.

**Gappy series** (journals, receipts, stock movements) draw from a Postgres sequence.
Fast, concurrent, and may skip on rollback.

Reversals **consume a number** from the same series. A voided invoice therefore appears as a
visible pair rather than a hole â€” which is the whole point of a dense sequence.

Measured on the research file, the incumbent's invoice sequence had **24,419 missing
numbers** across its span, a second series whose suffixes were not numeric at all, and one
document numbered `#NA`. Accounting's own numbering is strict; the **importer must not assume
incoming numbers are parseable, unique in format, or dense** (Layer 7).

## Layer 2a â€” Documents and posting rules

Documents are stored per type, because a sales invoice, a payment and a stock issue have
genuinely different fields:

```
sales_invoice       id, tenant_id, entity_id, doc_no, doc_date, customer_id,
                    currency_code, fx_rate, terms, due_date, state, ...
sales_invoice_line  id, sales_invoice_id, line_no, item_id, description,
                    qty, uom, unit_price, tax_code_id, project_id, ...
```

Documents hold **no balances**. An invoice does not store "amount outstanding"; that is
derived from its postings and allocations. This is what prevents a document from
contradicting the ledger.

A **posting rule** per document type translates a document into its posting set. Rules are
code, not configuration â€” configurable posting rules are a large feature with no first
customer, and YAGNI applies. Each rule is a pure function from document to posting set,
which makes it directly unit-testable against expected debits and credits.

Critically, a rule runs **once**, at post time, and its output is frozen. Postings are never
re-derived. If a rule is later found wrong, the fix is a correcting entry for affected
documents â€” never a recomputation, which would silently restate reported history.

Document state is `Draft` or `Posted`. Drafts are freely editable and have no postings.
Posting is the one-way door.

## Layer 3 â€” Receivables, payables and allocation

There are no AR or AP ledger tables. A customer's balance is the sum of postings to
receivables control accounts carrying that `customer_id`. Ageing buckets by the source
document's due date.

What cannot be derived is **which invoice a payment settles** â€” that is a decision, not a
calculation:

```
allocation   id, tenant_id, entity_id,
             from_doc_type, from_doc_id,      -- the payment / credit note
             to_doc_type, to_doc_id,          -- the invoice being settled
             amount, currency_code,
             functional_amount,
             fx_gain_loss_functional,
             journal_entry_id,
             allocated_at_utc, allocated_by_user_id,
             reverses_allocation_id
```

Append-only, like everything else. Un-allocating inserts a reversing allocation row; it does
not delete. Realised FX on settlement is computed here and posted through
`journal_entry_id`.

Outstanding on an invoice = its receivable posting total less the sum of allocations against
it. Derived, therefore always consistent.

## Layer 4 â€” Tax and e-Invoice

```
tax_code   id, tenant_id, code, name, tax_type, rate,
           effective_from, effective_to, output_account_id, input_account_id, is_active
```

Postings store `tax_code_id`, not a rate. Because postings are immutable, a document posted
under a superseded regime keeps its original code permanently â€” which is how the GST-to-SST
transition is handled. Historical documents are **not** restated under current rules;
`effective_from`/`effective_to` make the old codes inactive for new posting while leaving
history intact. The research file carries both `GST_*` and `SST_*` structures, confirming
migrated data will contain both.

### e-Invoice

```
einvoice_submission  id, tenant_id, entity_id, document_type, document_id,
                     lhdn_uuid, lhdn_long_id, payload_hash, current_status
einvoice_event       id, submission_id, status, at_utc, response_payload
```

`einvoice_event` is the append-only truth; `current_status` on the submission is a cache of
the latest event.

The rule that ties this to Layer 1a: **once a submission reaches validated, the underlying
document may not be superseded.** The Edit action is refused, and the UI offers a credit or
debit note instead. This is the external constraint made structural, and it is the reason
the immutable model is simpler here than a mutable one â€” there is no second correction
behaviour to build.

`payload_hash` records exactly what was submitted, so a later dispute can be settled against
what the authority received rather than what the document says now.

## Layer 5 â€” Inventory and FIFO costing

```
stock_move      id, tenant_id, entity_id, item_id, location_id,
                direction, qty, uom, doc_type, doc_id,
                journal_entry_id, moved_at, posted_at_utc

cost_layer      id, tenant_id, entity_id, item_id, location_id,
                source_move_id, qty_received, unit_cost, currency_code,
                functional_unit_cost, received_at, seq

cost_consumption id, tenant_id, cost_layer_id, out_move_id,
                 qty, functional_unit_cost, functional_amount
```

A receipt creates a `cost_layer`. An issue creates `cost_consumption` rows against the
oldest layers with quantity remaining, and COGS is the sum of what was actually consumed.

**`cost_layer` has no `qty_remaining` column.** Remaining quantity is `qty_received` minus
the sum of consumptions against that layer â€” derived, because a stored remainder would be a
mutable field on an append-only table.

For query speed this is cached in a **separate** `cost_layer_remaining` projection, not as a
column on `cost_layer` itself. The distinction matters: the projection is rebuildable from
`cost_layer` and `cost_consumption` at any time, and a discrepancy between the two is a
detectable bug rather than a silent corruption of the cost basis.

### The recomputation cascade

This is the hardest thing in the system. Suppose a purchase recorded at RM14/unit is later
corrected to RM15, and some of that stock has already been sold.

Under a mutable design you would edit the layer and recompute. Here you cannot, and the
correct accounting is better anyway:

1. The correction is a **document** â€” a supplier debit note or purchase price adjustment â€”
   not an edit.
2. Its posting rule splits the difference by where the stock now is:
   - quantity **still on hand** â†’ adjust the inventory asset account (the stock is genuinely
     worth more)
   - quantity **already sold** â†’ adjust cost of goods sold
3. Those adjustments post into the **current open period**, dated there, never into a closed
   one.
4. A **new** `cost_layer` row, linked to the original by `adjusts_layer_id`, records the
   revised cost basis for the quantity still on hand, so future issues consume at the
   corrected cost. The original layer is never modified â€” its consumptions already posted
   at the cost that was true when they happened.

History is never rewritten. Prior-period figures stand as reported, and the correction is
visible as a correction â€” which is what an auditor needs and what the incumbent's silent
recompute cannot provide.

> Out of scope for this spec: weighted-average and standard costing, serial-number tracking,
> bills of material, and manufacturing. FIFO layers can derive an average later; the reverse
> is not true, which is why layers are the foundation.

## Layer 6 â€” Consolidation

Entities in one tenant share a chart of accounts, so consolidation is a sum â€” with two
adjustments.

**Intercompany elimination.** A posting arising from a transaction with a sister entity
carries `intercompany_entity_id`. Eliminations pair matching postings across the two
entities and reverse them at group level. A consolidation run's eliminations are stored, not
computed on the fly, so a published consolidated statement is reproducible.

**Translation.** Where entities differ in functional currency, balance-sheet accounts
translate at the closing rate, income-statement accounts at the period average, and the
residual posts to a currency translation reserve.

```
consolidation_run     id, tenant_id, period_id, presentation_currency,
                      created_at_utc, created_by_user_id
consolidation_posting id, run_id, entity_id, account_id, direction,
                      functional_amount, presentation_amount, kind
```

`kind` distinguishes `Entity`, `Elimination` and `Translation` contributions, so a
consolidated figure can always be traced to its parts.

Group-level consolidation postings are held separately from entity postings. They are not
entity books and must never appear in a statutory filing for a single entity.

## Layer 7 â€” Migration importer

The commercially decisive feature, and the one with the strictest correctness bar: it runs
once per customer, against books they already rely on.

**Source.** Customer-owned exports, and optionally a direct read of the source database
performed by a connector the customer runs. Working from documented exports is preferred â€”
an undocumented internal schema changes between vendor versions with no notice.

**The central rule: imported data goes through the normal posting path.** The importer
constructs documents and posts them via the same posting rules as live data. It never
inserts postings directly. Otherwise the importer becomes a second, unverified route into
the ledger, and every invariant above becomes optional.

Consequences, each grounded in what the research file actually contained:

| Finding | Importer requirement |
|---|---|
| Source declares `charset NONE` | Text decoding is explicit and configurable, with a per-run report of undecodable values. Never assume UTF-8. |
| Two incompatible invoice number formats, plus `#NA` | Original numbers are preserved as `external_doc_no`; Accounting assigns its own. Incoming numbers are treated as opaque text. |
| 24,419 gaps in one sequence | Gaps are not errors and must not be reconstructed or back-filled. |
| Both GST and SST regimes present | Historical tax codes are imported as inactive codes with their original effective dates. |
| ~71% of source tables are transient working tables | The importer ignores anything matching the transient pattern rather than attempting interpretation. |
| Source documents carry edit history in a change log | Only the current state of each document is imported. The source's audit log is imported, if at all, as read-only reference data â€” never as Accounting postings. |

**Opening balances.** Rather than importing years of detail by default, the standard path is
a single opening journal entry per entity at the migration cut-off date, plus open AR/AP
items in full detail so ageing and allocation work. Full historical detail is an option, not
the default.

**Acceptance test.** The importer produces a reconciliation report comparing the source
trial balance to the imported trial balance, per account, at the cut-off date. **A run that
does not reconcile to zero fails and imports nothing.** This is the single most important
requirement in this section: a migration that half-succeeds is worse than one that refuses.

## Testing strategy

The invariants are the specification, so they are tested as properties rather than examples.

| Test | Asserts |
|---|---|
| Balance property | For any generated document set, every `journal_entry` has debits = credits in functional currency |
| Subledger identity | Sum of receivables postings per customer equals the receivables control account balance, always |
| Immutability | `UPDATE`/`DELETE` on `posting` as the application role is **refused by the database** |
| Replay | Balances cache rebuilt from postings equals the incrementally-maintained cache |
| Reversal | An entry and its reversal sum to zero on every account and every dimension |
| Hard close | No code path transitions a period out of `HardClosed` |
| Gapless numbering | Concurrent inserts with induced rollbacks leave the sequence dense |
| FIFO | Layer consumption produces known COGS; a retroactive cost correction splits correctly between inventory and COGS |
| Tenant isolation | With RLS active, a query lacking a tenant predicate returns only the session tenant's rows |
| Import reconciliation | A deliberately corrupted source fails the run rather than importing partially |

Backend tests use xUnit against a **real PostgreSQL instance**, never an in-memory provider.
RLS, deferred constraint triggers and revoked privileges are the things under test, and an
in-memory provider implements none of them â€” a green suite against one would be actively
misleading.

Locally that is a natively-installed PostgreSQL with a dedicated test database, created and
dropped per run, and a distinct low-privilege role matching the application's production
grants so the immutability tests are meaningful. Because the instance is shared across a
run, test classes that mutate schema-level state are serialised rather than parallelised.

On CI, the same suite runs against a service container. Connection details come from
configuration, so neither environment is special-cased in test code.

## Schema change practice

1. EF Core code-first migrations are the only source of truth. The schema is never
   hand-edited.
2. Every generated migration is read before it is applied, with particular attention to
   `defaultValue` on new non-nullable columns â€” EF emits the CLR default, which rewrites
   existing rows.
3. Released migrations are immutable. Mistakes are corrected by a new migration.
4. Deploys apply an idempotent script generated at build time, before the application
   starts, so a migration failure aborts the deploy with the previous build still serving.
5. **A migration must never rewrite a posted row.** If a change alters what a stored value
   means, that is a restatement, not a migration: it requires a documented, reversible data
   migration with before-and-after trial balances that reconcile.
6. `EFCore.NamingConventions` with snake_case, configured before the first migration.

## Build order

One document, seven shippable layers. Each is independently testable and leaves the system
in a working state.

| Layer | Contents | Done when |
|---|---|---|
| 0 | Tenant, entity, chart of accounts, RLS, periods | A tenant with two entities and a chart exists; RLS proven |
| 1 | Posting core, invariants, revoked privileges, corrections, currency | A manual journal posts, balances, reverses; `UPDATE` is refused |
| 2 | Number series, documents, posting rules | A sales invoice posts through a rule with a gapless number |
| 3 | Derived AR/AP, allocation, realised FX | A payment settles an invoice; ageing ties to the control account |
| 4 | Tax codes, e-Invoice submission and events | A validated invoice refuses Edit and offers a credit note |
| 5 | Stock moves, FIFO layers, cost adjustment cascade | A retroactive cost change splits correctly between stock and COGS |
| 6 | Consolidation, eliminations, translation | Holdings + Realty consolidate with intercompany removed |
| 7 | Migration importer | A source trial balance reconciles to zero, or the run fails |

Layers 0â€“3 are the minimum coherent accounting system. Layer 7 is the commercial unlock and
depends on everything before it.

## Explicitly out of scope

Payroll, POS, bank feeds and reconciliation, fixed-asset depreciation schedules,
manufacturing and bills of material, budgeting, weighted-average and standard costing,
configurable posting rules, and approval workflows beyond a document's Draft/Posted state.

Each is a consumer of this ledger, not a change to it. None should require revisiting the
posting model â€” and if one does, that is a defect in this design worth fixing now.

## Open questions

1. Verify current MyInvois cancellation and correction rules against LHDN's published
   guidance before implementing Layer 4.
2. Decide whether Accounting's own reporting layer reuses `open-reporting-platform`. If so,
   it needs a PostgreSQL provider, which its `IDataSourceProvider` abstraction is designed
   to accommodate.
3. Confirm the text encoding actually used by source databases declaring `charset NONE`,
   before designing the importer's decode step.
4. Determine whether a customer's own SST registration can change mid-year, and if so
   whether `tax_code` effective dating is sufficient or entity-level tax registration
   history is required.
