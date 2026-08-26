# Deployment

Push to `main` deploys. `.github/workflows/deploy.yml` builds the API and the frontend,
generates a migration script from the `Migrations/` folder, uploads a bundle to S3, and runs
`scripts/deploy-bootstrap.ps1` on the target Windows server over WinRM.

The deployed product is **one IIS Application**. The API serves the built frontend out of its
own `wwwroot`, so there is a single origin and CORS is never configured in production.

Migrations are applied **before** the app pool stops, using the owner role. A failed
migration therefore aborts the deploy with the previous build still serving, rather than
shipping code that asks for columns which do not exist. Nothing migrates at startup.

---

## One-time server setup

Everything below is done once, by hand, on the target server.

### 1. PostgreSQL 17

Install the server (which also provides `psql.exe`, required by
`scripts/apply-migration.ps1`). Confirm it is running as a Windows service so it survives a
reboot.

### 2. Roles and database

Two roles, because the guarantee that the application cannot alter the ledger is *a
privilege boundary*. If one role did both DDL and DML there would be nothing to enforce.

Roles are created by hand rather than by a migration on purpose: a migration that created its
own login roles would be granting itself the privileges it is supposed to be constrained by.

```sql
-- as the postgres superuser
CREATE ROLE accounting_owner LOGIN PASSWORD '<owner password>';
CREATE ROLE accounting_app   LOGIN PASSWORD '<app password>';
CREATE DATABASE accounting OWNER accounting_owner;
```

| Role | Used by | Holds |
|---|---|---|
| `accounting_owner` | migrations only, via the deploy | DDL |
| `accounting_app` | the running application | DML, minus `UPDATE`/`DELETE` on the ledger |

### 3. ASP.NET Core Hosting Bundle for .NET 10

Required by IIS to host the app. Installs side by side with older runtimes and does not
disturb anything already on the box.

### 4. IIS site

`deploy-bootstrap.ps1` creates the app pool and the `accounting` Application itself, but the
**parent site must already exist** — the script provisions an Application under a site, not
the site. Set `IIS_SITE_NAME` to that site's name.

The app lands at `/accounting`.

---

## Repository secrets

All of these are required. `deploy-bootstrap.ps1` fails before touching the server if any is
missing.

| Secret | Value |
|---|---|
| `AWS_ACCESS_KEY_ID` | |
| `AWS_SECRET_ACCESS_KEY` | |
| `AWS_REGION` | |
| `S3_BUCKET` | bundle staging |
| `EC2_HOST` | target server |
| `EC2_USER` | |
| `EC2_PASS` | |
| `IIS_SITE_NAME` | the existing parent site |
| `DB_CONNECTION_STRING` | `Host=…;Database=accounting;Username=accounting_app;Password=…` |
| `DB_MIGRATION_CONNECTION_STRING` | same, as `accounting_owner` |
| `JWT_SIGNING_KEY` | at least 32 bytes of random |
| `SEED_DEMO_PASSWORD` | see below |

Only `DB_CONNECTION_STRING` is persisted on the server, as an IIS app-pool environment
variable. The owner connection is used to apply migrations and then discarded.

### About `SEED_DEMO_PASSWORD`

A fresh database has no users, and therefore no way to sign in. `DevDataSeeder` creates the
demonstration tenant and a `demo@accounting.test` account using this password.

It is **required, not optional**. `appsettings.Development.json` carries a local convenience
value, and this repository is public — so a forgotten secret would silently make a published
string the sign-in credential of the deployed instance. The bootstrap script refuses to run
without it.

Set `Seed:DemoPassword` to nothing at all and no seeding happens. That is the correct setting
for any instance meant to hold real books.

---

## Notes

- **`ASPNETCORE_ENVIRONMENT` is set to `Development`** on the box, which is what it is. That
  is what exposes the OpenAPI document. Change it to `Production` if the server ever becomes
  one.
- **Never trigger this workflow from `pull_request`.** The repository is public; a
  fork-triggered run would be handed every secret above.
- **The frontend's base path is baked in at build time** (`VITE_BASE` in the workflow). If the
  IIS Application name changes, that has to change with it or every asset URL breaks.
- **Local dev and the server are fully independent** — separate databases, separate roles,
  separate JWT signing keys. Neither can affect the other.
