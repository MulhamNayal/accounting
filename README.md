# ClearWise

Open-source double-entry accounting, inventory and compliance platform for small and
medium businesses.

> **Status: pre-alpha.** Nothing is built yet. This repository currently holds scope
> decisions only. Do not treat anything here as working software.

## Why

Small businesses in Malaysia and Singapore run on desktop accounting software that is
fast and locally compliant but single-machine, hard to integrate with, and rigid about
reporting. ClearWise aims at the same job — books that balance, stock that reconciles,
statutory filings that submit — as a web-first system.

## Scope

The core, in rough dependency order:

- **General ledger** — chart of accounts, journals, period closing, immutable audit trail
- **AR / AP** — customers, suppliers, ageing, payment allocation and knock-off
- **Sales & purchase cycles** — quotation → order → delivery → invoice → credit note,
  including partial fulfilment and cancellation
- **Inventory** — multi-UOM, batch and serial tracking, FIFO / weighted-average costing,
  stock take, multi-location
- **Tax & compliance** — SST, withholding tax, and e-Invoice submission with validation
  status tracking
- **Reporting** — trial balance, P&L, balance sheet, stock cards, statutory formats
- **Migration** — an importer so a business can move off its existing system with its
  history intact

Migration is treated as a first-class feature, not an afterthought: without it, correct
books are not enough to make anyone switch.

## Non-negotiables

These are the properties that make an accounting system trustworthy, and they are
easier to design in than to retrofit:

- The ledger is **provably balanced** at all times, including after edits and
  back-dated entries.
- Posted documents are **immutable**; corrections are reversing entries, never
  in-place edits.
- Inventory cost is **recomputable** — changing a historical purchase price correctly
  cascades.
- Document numbering is **gap-free and unique** under concurrency.
- Every mutation is **attributable** to a user, a time, and a reason.

## Provenance

ClearWise is a clean-room implementation. It is designed by studying commercial
accounting software as a user and from public documentation. No source code, database
schema, or proprietary API contract from any commercial or internally-licensed system
is copied into this repository. The migration importer works from data the customer
owns and exports.

## Stack

Backend and database are intended, not yet committed: ASP.NET Core / C# and SQL Server,
chosen for continuity with existing work rather than because the domain demands it.
Revisit before the first line of the ledger is written.

The frontend is decided: **React + TypeScript with Fluent UI React**
(`@fluentui/react-components`), Microsoft's official implementation of the Fluent 2
design language. ClearWise should look and feel like a Windows 11 application, because
the people it is for spend their working day in Windows desktop accounting software and
Office. Familiarity is a feature, not a vanity choice.

Deliberately web rather than native WinUI 3: a single-machine, Windows-only application
would reproduce the exact limitation this project exists to remove. A desktop shell
(WebView2 or Tauri) over the same codebase remains open if offline operation turns out
to be a hard requirement.

## Licence

AGPL-3.0. See [LICENSE](LICENSE).
