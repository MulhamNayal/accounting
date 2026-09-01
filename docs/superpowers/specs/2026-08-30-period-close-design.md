# Period close and year-end — design

Roadmap item 1. The period state machine has existed since Layer 0 and nothing has ever
driven it: no service, no endpoint, and not one `period_events` row ever written. This closes
that gap, and fixes two things the existing code gets wrong once a close exists.

---

## What is already built

Worth stating, because it makes this smaller than it looks.

| | Where |
|---|---|
| `fiscal_years`, `periods`, `period_events`, all under RLS | `Layer0_TenancyEntitiesAccountsPeriods` |
| `UPDATE`/`DELETE` revoked on `period_events` | `Layer0_RowLevelSecurity` |
| `PeriodState` on both period and fiscal year | `Models/Enums.cs` |
| Posting into a non-Open period refused **by the database** | `Layer1_LedgerInvariants`, trigger `journal_entries_period_open` |
| Entry date must fall inside the period it names | same trigger |
| A friendly version of the same check | `PostingService.ResolvePeriodAsync` |
| `AccountSystemRole.RetainedEarnings`, account `3020` seeded | `Models/Enums.cs`, `DevDataSeeder` |
| Reversals dated today, never back into the original period | `PostingService.ReverseAsync` |
| A balance sheet whose arithmetic already survives a close | `FinancialStatementsContracts.cs` |

The last two matter. Correcting a closed period already works, and needs no change:
`ReverseAsync` posts the reversal into the current open period on purpose, so a closed month
is never silently restated. And `RetainedEarningsBroughtForward` is computed from profit and
loss account balances, so the moment a close zeroes those accounts the retained earnings
account picks the figure up and the two cannot double count. That was written in advance.

---

## Four problems

**1. Nothing creates a fiscal year outside the dev seeder.** `DevDataSeeder` is the only code
that has ever inserted a `FiscalYear` or a period. On a real tenant there is no path to a
period at all, so every posting fails with *"No accounting period covers…"*. Provisioning is
a prerequisite of this milestone, not an addition to it.

**2. The profit and loss account will read zero for a closed year.**
`GetProfitAndLossAsync` filters on date range alone. The closing entry is dated the last day
of the year and debits every income account, so closing FY2026 makes the FY2026 P&L report
nothing. The balance sheet is safe; this is not.

The fix is to make the closing entry identifiable and exclude it. A nullable
`closes_fiscal_year_id` FK on `journal_entries` rather than a magic `SourceDocumentType`
string: it points backwards, like `reverses_entry_id` and `supersedes_entry_id`, and it gives
year → closing entry for free.

Note what stays unchanged. The trial balance is `asOf`-dated and therefore *should* show the
income and expense accounts at zero on the last day of a closed year — that is the
post-closing trial balance, and it is correct.

**3. `periods.state` is updatable with nothing forcing a `period_event`.** The trail table is
append-only, but no rule requires a row to be written when the state changes, and nothing at
the database level prevents `HardClosed → Open`. The design spec's defence is *"not a
permission check that could be granted, an absent code path"* — which is application
discipline, the single thing this project refuses to rely on anywhere else. The trail **is**
the feature; it belongs in the database:

- a deferred constraint trigger on `periods` requiring a matching `period_events` row, with
  matching `from_state`/`to_state`, in the same transaction — the same mechanism as the
  balance trigger
- a trigger rejecting any transition out of `HardClosed`

`period_events` already carries a `from_state <> to_state` check constraint, so a no-op
update is not recordable and the trigger only has real transitions to judge.

**4. A close strands drafts.** Six tables carry `DocumentState` — sales invoices, customer
receipts, bills, supplier payments, and credit notes on both sides. A `Draft` dated inside a
period being closed can never be posted afterwards. Close needs a readiness check that says
so up front, rather than leaving it to be discovered later.

---

## Decisions

**Year-end close is two steps.** "Post closing entry" leaves the year soft-closed and the
entry reversible. A separate "finalise year" hard-closes every period. This is the spec's own
phrasing — the entry is *"visible and reversible until the year is hard-closed"* — and it
gives the accountant the window in which late adjustments actually arrive. Given that
`HardClosed` is terminal by design, collapsing the two steps would make a mistake
unrecoverable.

**Close is sequential; reopen is free.** Only the earliest open period may be soft-closed, so
the year cannot end up as Swiss cheese. Any soft-closed period in a year that is not
hard-closed may be reopened, with a mandatory reason.

Free reopen is safe *here specifically* because every balance is derived from postings. There
is no stored opening balance for a reopened January to invalidate, and no recompute to
trigger — the same property that lets the stock module correct a cost retroactively.

**Ordering inside year-end.** The closing entry must be posted while the final period is
still Open, then the hard close runs. `ResolvePeriodAsync` and the
`journal_entries_period_open` trigger will both reject it otherwise.

---

## Shape

### Migration

`closes_fiscal_year_id` on `journal_entries`, nullable, FK to `fiscal_years`. Plus the two
triggers from problem 3. Read the generated `defaultValue`s before applying — a
string-converted enum has already been given `""` once in this repo when `"None"` was meant.

### Services

**`FiscalYearService`** — create a year and generate its periods. Monthly by default, with an
explicit period count so a short first year or a 52/53-week year is expressible. Validates
that the year does not overlap an existing one for the entity.

**`PeriodService`** — `SoftCloseAsync`, `ReopenAsync`, `GetReadinessAsync`. Every transition
writes its `period_events` row inside the same transaction as the state change, which is what
the new trigger requires. `SoftCloseAsync` refuses if an earlier period is still open.

**`YearEndCloseService`** — builds the closing entry from income and expense balances at the
year end, transferring the net to the `RetainedEarnings` account, and posts it through
`PostingService` joining its transaction. `FinaliseAsync` hard-closes every period in the year
and sets the year's own state, writing a `period_event` per period so there is one trail
rather than two.

The closing entry refuses to post if the year has no `RetainedEarnings` account, the same way
consolidation refuses without a translation reserve.

### API

`PeriodsController` — `/api/fiscal-years` (list, create), `/api/periods` (list by entity and
year), `/api/periods/{id}/readiness`, `/api/periods/{id}/close`, `/api/periods/{id}/reopen`,
`/api/fiscal-years/{id}/closing-entry`, `/api/fiscal-years/{id}/finalise`. Thin, no
`DbContext`, kebab-case, paginated where it lists.

### Reports

Exclude entries with `closes_fiscal_year_id` set from `GetProfitAndLossAsync`. Nothing else
changes.

### Frontend

A Periods page under the entity: the period grid with state, close and reopen with a
mandatory reason, the readiness panel, the two year-end actions, and the event history — the
last of these being the point of the whole feature, so it should be visible rather than
buried.

### Tests

- posting into a soft-closed period is refused, and into a hard-closed one
- a state change without a matching event is refused **by the database**
- `HardClosed → Open` is refused by the database, not only absent from the service
- closing out of order is refused; reopening out of order is allowed
- the closing entry zeroes every income and expense account as at year end
- the P&L for a closed year still reports its income and expenses
- the balance sheet is identical before and after the close, and stays balanced
- readiness lists a draft in each of the six document tables

Assert the message, not the exception type, for anything the deferred triggers catch.

---

## Not in this milestone

Authorisation. The spec says a period may be reopened "by an authorised role" and there is
exactly one role today — roadmap item 9 is where that lands. Until then reopen is available to
any authenticated user, and the `period_events` trail is what makes that tolerable.
