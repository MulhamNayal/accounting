# How the incumbent desktop system handles corrections â€” findings

Date: 2026-08-24

## Purpose and provenance

Accounting needs to decide how a posted transaction is corrected. Rather than assume, this
was measured against a real production company file from the incumbent desktop accounting
system, examined read-only for **interoperability purposes** â€” understanding what a
migration importer must accept, and what behaviour customers will arrive expecting.

Method notes:

- The file was **copied first**; the original was never opened for write. Attaching a
  Firebird database mutates its header and transaction counters, so the original is
  untouched.
- Only **schema metadata and aggregates** were read. No customer records, no amounts, no
  document contents.
- **Absolute volumetrics are deliberately omitted from this document.** Invoice and
  transaction counts describe a real business's trading volume, and this repository is
  public. Rates and ratios are recorded; magnitudes are not.
- Nothing here is copied into Accounting's own ledger design. This file documents the
  *source system's behaviour*, which is an input to the importer and to product
  expectations â€” not a template for our schema.

## Platform

| Property | Value |
|---|---|
| Engine | Firebird 3.0, ODS 12.0 |
| Page size | 8192 |
| Default charset | `NONE` |
| Tables | 730 |
| Views | 0 |
| Stored procedures | 0 |
| User triggers | **0** |
| Generators (sequences) | 102 |

Two things matter here.

**`charset NONE`.** The database declares no character set, so byte sequences are stored
without declared encoding. The importer cannot assume UTF-8 and must treat text decoding
as an explicit, configurable step â€” with a fallback for mis-encoded legacy rows. This is a
known source of silent corruption in migrations and needs handling by design, not by luck.

**Zero triggers, zero procedures, zero views.** All business logic lives in the
application. The database is a passive store that enforces almost nothing. The consequence
for the audit trail is decisive and is covered below.

## Schema shape

Roughly 210 business tables under clear module prefixes â€” `GL` (general ledger), `AR`,
`AP`, `SL` (sales), `PH` (purchase), `ST` (stock, 39 tables), `SY` (system), `FA` (fixed
assets), `GST` (13 tables) and `SST` (5), plus `MYINVOIS_TRANS` for e-Invoice.

The remaining **520 of 730 tables are transient**, named `T_01_` followed by a random
token â€” per-session working tables that are created and never reclaimed. They are 71% of
the schema and contribute meaningfully to file size.

Implication for Accounting: transient query workspaces must be namespaced and reclaimed on
a schedule, or the same accretion happens. It also means the importer should ignore
anything matching the transient pattern rather than trying to interpret 520 unknown
tables.

The presence of both `GST_*` and `SST_*` tables reflects Malaysia's 2018 GST-to-SST
transition. A migration importer must handle historical documents carrying a tax regime
that no longer exists â€” history cannot be re-stated under current rules.

## The answer: corrections are in-place edits, audited by the application

Two tables implement the audit trail:

**`AUDIT`** â€” one row per audited operation: `DOCKEY`, `USERNAME`, `UPDATEKIND`,
`MODULE`, `DOCDATETIME`, `REF`, `REFERENCE`, `DELETED`.

**`AUDITDTL`** â€” one row per changed field: `TABLENAME`, `FIELDNAME`, **`OLD`**,
**`NEW`**, `UPDATEKIND`.

`UPDATEKIND` uses three values, `I`, `E` and `D` â€” insert, edit, delete. All three occur
in the production data. Deletes are rare but **non-zero**: posted documents are not merely
cancellable, they are removable.

Document tables (`AR_IV`, `AR_CN`, `GL_JE`, `AP_PI`) each carry `UPDATECOUNT`,
`LASTMODIFIED`, `CANCELLED`, `STATUS`, `APPROVESTATE` and `POSTDATE`.

The evidence, in order of how conclusive it is:

1. **An `OLD`/`NEW` field-level diff log only makes sense if rows are `UPDATE`d in
   place.** A reversal-based system has no old value to record â€” it has two documents.
2. **`UPDATECOUNT` is a mutation counter on the document itself.** It is null until the
   first edit, then increments. Roughly **3% of sales invoices in this file had been
   edited after posting**, a small number of them repeatedly. So in-place editing of
   posted documents is not theoretical â€” it is ordinary practice.
3. **`CANCELLED` is a boolean flag on the document, not a reversing entry.** Cancelling
   sets a flag; it does not generate a compensating posting.
4. **`UPDATEKIND = 'D'` exists in the data.** Documents get deleted outright.

So the incumbent's model is: **mutable documents, period locking as the control, and a
field-level change log as the evidence.**

### The weakness worth naming

Because there are **zero database triggers**, the audit trail is written entirely by the
application. Nothing in the database compels it. Any process that writes to the file
directly â€” a script, a report tool, a support engineer with the `SYSDBA` password, which
is the Firebird factory default â€” mutates the books and leaves no trace.

An audit trail that the storage layer does not enforce is not tamper-evident. It records
what a cooperating application chose to record. That is the substantive difference between
this design and a ledger where correction is structurally impossible to hide.

A caveat on volume, for honesty: `AUDITDTL` is very large, but most of its rows come from
`UPDATEKIND = 'I'` â€” the log captures every field on insert as well as on edit. The
row count is therefore **not** evidence of heavy editing. The evidence for editing is
`UPDATECOUNT` and the `E` operation count, both cited above.

### e-Invoice: schema present, unused

`MYINVOIS_TRANS` exists with `STATUS`, `STATUSREASON`, `CANCEL_UTC`, `TYPEVERSIONNAME` and
`CREATEDBYUSERID`, and is **empty** in this file. So this company had not begun submitting
e-invoices as at the file date, and nothing here evidences the incumbent's *runtime*
e-Invoice behaviour.

The schema shape is still informative. `MYINVOIS_TRANS` is a **separate table from the
invoice**, carrying its own status and its own cancellation timestamp. The submitted
e-invoice therefore has a lifecycle running alongside a document that remains
independently editable, with no structural link forcing the two to agree.

That is a divergence risk by construction: an invoice can be edited after the
corresponding e-invoice has been validated by the tax authority, and only convention keeps
the two consistent.

## What this means for Accounting's decision

This is input to the open decision, not the decision itself.

**Arguing for matching the incumbent (mutable + audit log):** it is what the entire
addressable market already does daily. Roughly 3% of invoices get edited after posting â€”
low enough that reversal friction would be occasional, high enough that it would be felt.
Users who cannot fix a typo the way they always have will say the system is worse.

**Arguing for an immutable ledger:** two independent reasons.

1. *The audit trail here is only as good as the application's cooperation.* An
   append-only ledger makes correction visible structurally rather than by convention.
2. *e-Invoice removes the choice for sales invoices anyway.* Under the MyInvois regime,
   once an invoice is validated it cannot be edited, and after the cancellation window it
   cannot be cancelled either â€” corrections must be credit/debit/refund notes referencing
   the original. **This should be re-verified against current LHDN guidance before it is
   relied on**; it is not evidenced by this file, which has no e-invoice activity.

If the second point holds, then a mutable model produces two different correction
behaviours in one product â€” editable for most documents, reversal-only for e-invoiced
sales invoices. The `MYINVOIS_TRANS` design above is what that compromise looks like when
it is bolted on rather than designed in.

The recommendation on the table remains an append-only ledger with an Edit action that
reverses and reposts underneath: the incumbent's convenience, without its dependence on
the application choosing to tell the truth.

## Open questions

- Verify the current MyInvois cancellation window and correction rules against LHDN's
  latest published guidance.
- Determine what the incumbent writes to `AUDIT`/`AUDITDTL` when a document is *deleted*
  rather than edited â€” whether the deleted content is recoverable from `OLD` values.
- Confirm the encoding actually used for text under `charset NONE` before designing the
  importer's decode step.
