# Roadmap

What a Malaysian or Singaporean SME accounting product needs before a business can run on
it, what exists today, and the order I would build the rest in.

Every requirement below is a domain requirement, verifiable independently: e-invoicing is
mandated by LHDN, payment file formats are published by each bank, capital allowances are in
the Income Tax Act, and SST return forms are published by the Royal Malaysian Customs
Department. None of it is speculation about what any particular product does.

---

## Where the line is today

Built, tested, and deployed:

| | |
|---|---|
| General ledger, immutable postings, corrections, multi-currency | ✅ |
| Trial balance, profit and loss, balance sheet | ✅ |
| Sales invoices, receipts, allocation, receivables ageing, statements | ✅ |
| Bills, payments, allocation, payables ageing, statements | ✅ |
| Credit notes, both sides | ✅ |
| Stock with FIFO cost layers and retroactive cost correction | ✅ |
| Tax as effective-dated regimes and codes | ✅ |
| Consolidation, elimination, IAS 21 translation | ✅ |
| Authentication, tenant isolation, row level security | ✅ |

That is a correct double-entry core with both trading sides. It is not yet a product a
business can run on, and the gap is wider than the list above is long.

---

## What is missing, in the order I would build it

### 1. Period close and year-end

**Why first:** every other item on this list is easier once periods can be locked, and the
balance sheet currently derives retained earnings rather than reading it from an account
precisely because there is no close. A business cannot file anything without being able to
say "this year is finished."

Needs: period state transitions with an audit trail (the model is already there), the
year-end entry that transfers profit to retained earnings, and locking that survives a
reopen request.

### 2. Bank reconciliation

**Why so early:** it is the control that catches everything else. An accountant reconciles
the bank before believing any other figure, and a system that cannot reconcile is a system
whose numbers nobody trusts. It is also the single most-used screen in a small business.

Needs: statement import, matching against posted receipts and payments, an unreconciled
listing, and a reconciliation as at a date that ties to the bank's own closing balance.

### 3. e-Invoice submission (LHDN MyInvois, and Peppol)

**Why:** mandatory in Malaysia, phased by turnover, and by now covering businesses of the
size this product targets. A Malaysian accounting product that cannot submit an e-invoice is
not sellable at any price, regardless of how correct its ledger is.

Needs: the MyInvois submission API, document validation before submission, status tracking
and rejection handling, TIN validation, consolidated invoices for exempt cases, and the
Peppol path for cross-border. This is the largest single item on this list and the least
negotiable.

### 4. Tax returns

**Why:** the tax data is already carried on every posting, by design. What is missing is the
return itself — the form a business actually files.

Needs: SST-02 and SST-02A for Malaysia, and the box-by-box mapping from tax codes to return
lines. Bad debt relief has its own rules and its own claim window, and is a real requirement
rather than an edge case. Singapore's GST F5 is the same shape with a different form, which
is the test of whether the tax abstraction actually works.

### 5. The document chain

**Why:** quotation → sales order → delivery order → invoice, and requisition → purchase
order → goods received → bill. Nothing in the current model is wrong, but every document
starts from nothing, which is not how anyone works. A business quotes, converts, delivers
part of an order, and invoices what was delivered.

Needs: document conversion carrying lines forward, partial fulfilment, and outstanding-order
reporting. The immutability rules make this more interesting than usual: a converted
document must record what it became without being editable afterwards.

### 6. Payment files for local banks

**Why:** deeply unglamorous and enormously sticky. A business that pays forty suppliers a
month does not want to type forty transfers into a banking portal, and the file format is
specific to each bank — layout, header, padding, checksum. There are roughly twenty banks
that matter across Malaysia and Singapore, and each publishes its own specification.

This is worth naming as a moat rather than a chore. It is exactly the kind of work that is
too boring for a new entrant to do and too valuable for a customer to give up.

### 7. Fixed assets

**Why:** every business that owns anything needs depreciation, and Malaysian capital
allowance rules differ from straight accounting depreciation, so it cannot be approximated
with a manual journal for long.

Needs: an asset register, depreciation schedules and their monthly posting, disposal with
gain or loss, capital allowance computation, and the asset movement report an auditor asks
for.

### 8. Stock depth

The current stock module is costing-correct and operationally thin.

Needs: stock take with a variance worksheet and its adjustment posting, multiple locations
and transfers between them, batch and expiry tracking, serial number tracking, reorder
advice, and an assembly or bill-of-materials build. Costing is the hard part and it is
already done; this is breadth on top of it.

### 9. Users and permissions

**Why:** one role today. A real business needs a clerk who can enter but not post, and an
accountant who can close a period.

Needs: roles, per-module rights, and — given this product's central claim — an access log
that is itself append-only.

### 10. Printable documents

**Why:** an invoice a customer cannot receive is not an invoice. Currently there is no way to
produce one.

Needs: templates per document type, company branding, PDF generation, and email delivery.

### 11. Migration importer

**Why last, despite being commercially decisive:** it needs everything above to exist first.
An importer can only bring across what the target can represent, and until stock takes,
fixed assets and the document chain exist, an import would silently drop them.

Two prerequisites are worth recording now, because both affect the model:

- **Document numbers must be importable.** A migrated invoice keeps the number its customer
  knows. Posting currently always allocates from a series, and an import path has to accept a
  supplied number without corrupting the counter that new documents draw from.
- **Import into a fresh tenant, reconcile, then commit.** Everything here is immutable by
  design, so a bad import cannot be edited away — it can only be discarded. That makes
  tenant-level disposability part of the importer's design rather than an afterthought.

I would also scope the first version to **opening balances only**: one entry as at go-live,
plus open receivables and payables detail, stock quantities with costs, and the master data.
Full history means every historical period has to reconcile, and there is no period close yet
to lock the ones that do.

---

## Also missing, and deliberately not prioritised

Project and departmental costing, budgets, multiple units of measure, landed cost, POS
integration, credit-bureau checking, loyalty points, mobile access, and the long tail of
analytical reports. All real, none of them blocking a first customer.

---

## The honest summary

The hard, differentiating part is built: books that cannot be altered without evidence,
enforced below the application. Items 1 through 4 are what stand between that and something a
Malaysian business could legally run on. Items 5 through 10 are what stand between it and
something they would choose over what they already have.
