# CLAUDE.md — frontend

Read the root `CLAUDE.md` first. This covers the React/TypeScript side only.

**Stack:** React + TypeScript on Vite, **Fluent UI React** (`@fluentui/react-components`),
`react-router-dom`. Vite proxies `/api` to `http://localhost:5100`, so the app only ever
uses relative URLs — as it will in production behind one origin.

```bash
npm run dev      # :5173
npm run build    # tsc -b && vite build — run before calling any change done
```

---

## Use the real Fluent 2 components

Accounting should read as a Windows 11 application, because the people it is for spend their
day in Windows desktop accounting software and Office. That means Fluent's *patterns*, not
just its components. Getting this wrong once already cost a rewrite.

| Need | Use | Not |
|---|---|---|
| App navigation | `NavDrawer`, `NavDrawerBody`, `AppItem`, `NavSectionHeader`, `NavItem`, `Hamburger` | `TabList` — tabs switch views *within* a page |
| Search | `SearchBox` | `Input` with a magnifying-glass icon |
| Content container | `Card` | a hand-rolled bordered `div` |
| Errors, warnings, notices | `MessageBar` + `MessageBarBody` | coloured `Body1` |
| Action or filter rows | `Toolbar`, `ToolbarDivider` | a flex `div` |
| Sortable tables | `DataGrid` | raw `Table` where sorting is wanted |
| Hierarchies | `Tree`, `TreeItem`, `TreeItemLayout` | indentation faked with padding |

Wrap icons in **`bundleIcon(Filled, Regular)`** so they fill when selected. That is the
Fluent convention and its absence is immediately visible.

Colour, spacing, radius and type come from **Fluent tokens** — never hard-coded hex or px.
Both `webLightTheme` and `webDarkTheme` must look right; a hard-coded colour breaks one of
them.

---

## Conventions

- **`verbatimModuleSyntax` is on** — always `import type { X }` for type-only imports.
  `tsc -b` fails otherwise.
- **`api/{resource}.ts`**, one file per backend resource, with interfaces mirroring the DTO
  field-for-field (camelCase, matching `System.Text.Json`'s default). When a backend record
  changes, update the matching interface in the same commit.
- **Shared HTTP lives in `api/client.ts`** — `getJson`, `postJson`, and the error
  unwrapping. Don't duplicate any of it into a resource module.
- **`theme.ts` is the single source for cross-cutting visual conventions.** Don't re-solve a
  global look with page-local `sx`/`makeStyles` overrides.
- **`components/` must stay reusable** — no knowledge of *what* it is displaying.
  Page-specific composition belongs in `pages/`.
- Keep the key on the **fragment** when a `.map()` renders more than one row per item.
  Keying an inner row breaks reconciliation, and TypeScript will not catch it.

---

## Show the server's message

The API returns a raw JSON string for 400/404/409 and `ProblemDetails` for 502; both are
unwrapped in `api/client.ts`. **Surface that message verbatim.**

It names the rule that was broken and usually why it exists — *"Account 1210 is a control
account, so the line must name a customer. Without it the balance is invisible to the
subledger while still counting toward the control account."* No client-side string is going
to improve on that.

Validate in the client only for **fast feedback** — a live debit/credit total, a disabled
submit button. The server still decides.

---

## Reflect the ledger's honesty in the UI

- **Never offer an Edit or Delete action on a posted document.** There is no endpoint for
  either, and a disabled button implies the operation exists somewhere.
- Say plainly what a draft is: not in the books, no number taken, nothing posted.
- Show corrections as what they are — a reversal pair, both visible.
- Label what isn't built yet with a `MessageBar` rather than hiding the page. The shape of
  the product should be visible; what doesn't work should say so.

---

## Never Do

- Import `@fluentui/react-icons` glyphs without `bundleIcon` for navigation
- Hard-code a colour, spacing or radius instead of using a token
- Duplicate HTTP or error-unwrapping logic outside `api/client.ts`
- Use a bare `import { X }` for a type
- Offer an action the API has no endpoint for
- Replace a server error message with a generic one
