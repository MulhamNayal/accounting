# CLAUDE.md

Guidance for Claude Code in this repository. Read it fully before making changes.

These are project rules, not suggestions — follow them even where they differ from generic
defaults, because they usually encode a real constraint. If a rule appears to cause a
concrete bug or contradicts the code, stop and flag it rather than working around it.

**Further rules live closer to the code.** Read `backend/CLAUDE.md` before touching C#, and
`frontend/CLAUDE.md` before touching TypeScript. This file holds only what applies to both.

---

## Project Overview

- **Purpose:** open-source double-entry accounting, inventory and compliance platform for
  SMEs, positioned against desktop incumbents.
- **Stack:** ASP.NET Core / C# on .NET 10, React + TypeScript + Fluent UI React, PostgreSQL.
- **Layout:** `backend/Accounting.Api`, `backend/Accounting.Tests`, `frontend/`, `docs/`.
- **Design spec:** `docs/superpowers/specs/2026-08-25-ledger-model-design.md`. Read it
  before touching the ledger. The build order is layered, 0 through 7.

### The one idea everything serves

**The books cannot be altered without evidence, and that is enforced below the
application.** Corrections are new entries; nothing is ever edited or deleted. The
differentiator is not features — the incumbent has more — it is that its audit trail exists
only because its application chooses to write one, and this one does not depend on that.

Anything that would let a posted figure change quietly is wrong, however convenient.

---

## Build & Test

```bash
dotnet build Accounting.slnx -v minimal
dotnet test Accounting.slnx                    # needs local Postgres running
cd frontend && npm run build                  # tsc -b && vite build
cd frontend && npm run dev                    # :5173, proxies /api to :5100
cd backend/Accounting.Api && dotnet run --urls http://localhost:5100
```

**Always build and run the tests after changes. Never leave either broken.**

---

## This machine

- **Stop the running API before building** — it locks its own exe and the build fails with
  MSB3027: `Get-Process Accounting.Api | Stop-Process -Force`
- **Postgres is not a Windows service** and does not survive a reboot:
  `& "C:\Program Files\PostgreSQL\17\bin\pg_ctl.exe" -D "C:\Program Files\PostgreSQL\17\data" -l "$env:TEMP\pg.log" start`
- **`git` is not on PATH.** Use
  `C:\Program Files\Microsoft Visual Studio\18\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd`
- Local dev credentials are in user-secrets, never in `appsettings.json`.
- **Never bulk-rewrite files with `Get-Content -Raw` + `WriteAllText`.** Windows PowerShell
  5.1 reads a BOM-less UTF-8 file as Windows-1252, so every em dash and ellipsis in the
  source comes back as mojibake and gets written back doubly encoded. Use
  `[System.IO.File]::ReadAllText($path, (New-Object System.Text.UTF8Encoding($false)))`.
  This has already cost one repair pass across 65 files.

---

## Deployment

**Push to `main` deploys.** No branch or PR workflow exists, so confirm with Mulham before
pushing unless he has already asked for it in the current exchange.

Full setup, secrets and rationale: `docs/deployment.md`. The parts worth knowing before
touching anything:

- **The deploy applies migrations itself**, from a script generated out of `Migrations/` on
  every run, using the owner role, *before* the app pool stops. Don't hand-write a migration
  script into `scripts/sql/` — adding the migration is enough, and a committed script is a
  second source of truth that will drift.
- **One IIS Application.** The API serves the built frontend from its own `wwwroot`, so the
  deployed product is a single origin. `frontend/src/api/client.ts` resolves against
  `BASE_URL` for exactly this reason.
- **Never add `pull_request` to `deploy.yml`.** This repository is public; a fork-triggered
  run would be handed every secret.

---

## Commits

Lowercase, imperative, prefixed by area: `backend: ...`, `frontend: ...`, `fix: ...`,
`docs: ...`. Explain *why* in the body when it is not obvious.

**Use `git commit -F <file>`, never `-m`,** for anything multi-line or containing quotes —
PowerShell mangles the argument and git treats the fragments as pathspecs.

**No AI attribution, no `Co-Authored-By`.** Commits should read like Mulham wrote them.

---

## Workflow

- Read the relevant service, controller or page before writing. Mirror existing patterns.
- Minimal, surgical diffs. Don't reformat or tidy untouched code.
- Ask before changing anything that affects how a figure is calculated or stored.
- Verify with build **and** tests before saying a change is done.

---

## Never Do

- Weaken any guarantee that makes a posted figure immutable
- Store a balance, total or outstanding amount that postings can derive
- Hand-edit the database schema outside a migration
- Use `double` for money — `decimal` with explicit precision, always
- Commit secrets, connection strings or API keys
- Commit source-system data files (`.fdb`, `.fbk`, exports) — **this repo is public**
- Commit with AI attribution
- Push to `main` without confirming — it deploys immediately
