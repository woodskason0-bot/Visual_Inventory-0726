# VIS (Visual Inventory System) — Handoff State

**Supersedes the July 2026 handoff, which stopped at Pass 4.** Current as of Pass 9,
live on the host laptop.

Owner: Kason (Rheem). ASP.NET Core MVC (`net10.0`), EF Core + SQLite, Razor, Bootstrap 5
dark theme. Session-based name-only "identify" (no password) + numeric `AccessLevel`
(1 Viewer – 5 Admin) via `[RequireLevel(x)]`. Delivered as folder-structured zips Kason
copy-merges into his own VS project; **he compiles/migrates/tests himself, not me.**

---

## Deployed state

```
Migrations       26   (latest: 20260801000000_AddIntakeBatches)
Items           487   (244 compressors)
Compressor units 825  on hand · 184 with a serial on the roster
Locations        18   Parent/Major/Sub, managed
Map zones         4   seeded, editable
Teams             2   Samurai (7166) · Ninja (7165), managed
Users            29
```

Published **self-contained** (`-r win-x64 --self-contained`) to `C:\VIS_Publish` on the
host laptop, started with `--urls "http://0.0.0.0:5000"`. Database at
`C:\VIS_Inventory\inventory.db` — an absolute path in `appsettings.json`, so it is
**not** carried by publish and must be moved deliberately.

---

## Working conventions

- Read every file fully before editing. No blind edits.
- Deliver as folder-structured zip (path-preserving), list changed/new files one line each.
- Hand-author EF migrations (no dotnet SDK in sandbox): mirror existing migration file
  structure exactly; regenerate `AppDbContextModelSnapshot.cs` and the matching
  `*.Designer.cs` by transforming the snapshot's `BuildModel` into `BuildTargetModel`.
- Verify brace/paren balance and diff against prior delivered state before packaging.
- Caution before schema migrations / breaking changes; proceed on routine work.
- Tone: direct, low-formatting, no unneeded explanation.

---

## Architecture — load-bearing facts

**`InventoryItem` = family identity. `ItemVariant` = per-location physical stock.**
`InventoryItem.Quantity` and the location fields are `[NotMapped]` pass-throughs over
variants — **never usable inside raw EF LINQ queries**, only after materialization.
18 models legitimately have two variants (stocked at both New Test Cells and the Lean-To).

**Visibility is Line, not Team.** Signed-in user sees items where
`item.Line == "" || item.Line == user.Line` (case-insensitive). **Level 5 bypasses
entirely.** Blank Line on *either* side **fails OPEN** — a deliberate rollout default.
`InventoryService.ApplyLineVisibility()` filters `GetAll`/`Search`/`ExportToCsv` only;
`GetById`/`FindByRheemPart`/quantity math stay unfiltered on purpose so org-wide dedup
and cross-Line reassignment keep working.

**`OrgStructure.cs`** is the single source for the fixed 2 Branch × 6 Line structure.
Branch is **derived**, never stored.

**Group is derived and frozen at creation.** `OrgStructure.GroupFor(user.Line)` maps
`Commercial Air → Commercial → C`, `Residential Air → Residential → R`, blank →
`Commercial`. It exists only to mint the `ItemId` prefix, and records *who registered
the item*. Not a form field, not editable afterwards — changing it would put an item's
Group at odds with its own id.

**`LocationCodec.Encode()` is the only thing that turns a name into a code** — 1st, 3rd,
5th and last alphanumeric. Codes are **derived, never stored**. `Decode` reads a volatile
map pushed in by `LocationCodec.Refresh()` at startup (`Program.cs`) and after any
Settings edit.

**`CompressorUnit` is a roster, not a pickup log** (changed in 6A). Holds On Hand stock as
well as units that have left. **Quantity remains authoritative; units are a partial
overlay** — 184 of 825 have serials, so unit rows must never drive counts. Unique index
is `(ItemId, SerialNumber)`, blanks exempt: LG reuses serials *across* models, so serial
alone can't be unique.

**Superuser gate** is separate from `AccessLevel`: session name == `Kason.Woods` **and**
the passcode from `appsettings.json`. "Master key not stored in any lockbox it opens."

**Pickup:** `OrderService.PickUpOrder()` (not "FulfillOrder"). Pulls from the
**lowest-numbered variant first**, which now has physical consequences — an order can send
someone to two buildings.

---

## Pass log

**Passes 1–4** — see git history. Superuser Settings, add/remove users,
`NotificationSubscription`, Rheem PN `N/A` as first-class, Branch/Line org structure,
compressor serial capture at pickup.

**Pass 5 — the three stacked bugs.** Migrations had silently failed three times.
Root cause was **`PendingModelChangesWarning`**: `CompressorUnit.ItemId`'s index was in the
snapshot but not configured in `OnModelCreating`, so `Migrate()` aborted *before running
any SQL*. Behind it, `20260725000000_AllowNARheemPartNumber` folded blanks to `N/A`
*before* dropping the old unique index — `UNIQUE constraint failed` on the second row.
Both were swallowed by `Program.cs`. Fixed the model drift, reordered the migration, and
normalised five placeholder PNs (`N/A - 3`, `None yet`, …) that only existed as
workarounds for the index that never got relaxed.

**Compressor rebuild (SQL, not a code pass).** Wiped 75 compressors / 84 variants /
76 logs / Order 3. Loaded 85 models / 189 units at New Test Cells, then 177 models /
636 units at the Lean-To. Reconciliation of five notebooks plus an intern's PDF used
**max-on-overlap** within the bottom-row group rather than summing — Kason's own
"the two halves should be even" test put the halves at 320 vs 313.

**Pass 6A — CompressorUnit roster.** `+ItemVariantId`, `+Status`, `+RecordedAt/By`;
`OrderId`/`PickedUpAt`/`PickedUpBy` nullable. Unique `(ItemId, SerialNumber)`.
**`PickUpOrder` became match-or-create** — mandatory, not optional: without it, typing a
serial already on the roster crashes the pickup for doing the right thing.

**Pass 6B — Done Using.** Compressors became loanable. `LoanableQuantity` was **dead code**
duplicated inline in `PickUpOrder`; editing the helper changed nothing until the call site
was pointed at it. `LoanOutstanding` is reused for compressors meaning *"not yet
dispositioned"* — deliberate naming debt, one counter that can't drift beats two that must
be kept in sync. Reason field added to Return and Scrap for all loanable types.

**Pass 7A — managed Teams.** `Teams` table, CRUD in Settings, Team optional with `N/A`.
Killed `ProjectCode = newTeam == "ninja" ? "7165" : "7166"`, which gave every future team
Samurai's code. Group removed from the registration form.

**Pass 7B — Transfer means Line.** `Internal - Transfer`'s "New Group" picker offered two
Branches and one Line as peers — a flattened, wrong copy of `OrgStructure`. Deleted.
Branch/Line moved *out* of Edit Details (where Pass 3 had parked it) into Transfer, where
ownership belongs. Edit Details is identity only again.

**Pass 7C — managed Locations.** The vocabulary lived in **four** hand-kept copies and had
drifted: 34 variant rows carried Sub codes (`MZA3`–`MZA8`) no copy knew, rendering as raw
codes. One `Locations` table now feeds all of them. Also folded 2 `Major='LATO'` variants
into `Rack='Lean-To'` to match the other 177, and put Rack/Row into the loan/pickup
breadcrumb — they'd been dropped entirely, so every Lean-To variant read as plain
"Plant Test Cells".

**Pass 8 — map zones.** `LocationZones` table with **normalised 0–1** coordinates (was
pixels against a 1146×643 image). Drag-to-draw editor in Settings, inline new-Parent
creation. The four `<area>` tags were the **fifth** copy of the location vocabulary.

**Pass 9 — bulk intake.** `Intake` screen, Standard+. `IntakeBatches`/`IntakeRows` hold
batches whose location isn't recognised; the Settings queue **maps** the requested name onto
an existing location or **creates** it, then runs the *same* `CommitIntake` a known-location
batch takes. **Permissions: `CreateItem` + the five ordering actions moved Engineer →
Standard** so interns can register and order. Found copies **six, seven and eight** of the
location vocabulary — `reg-parent`, `exp-parent`, and `mapData` (that last one meant a new
Parent read `0 Rows` forever regardless of stock).

---

## Traps — read before editing

**`@` is a Razor transition inside `<script>` blocks and JS comments.** Writing
`@RenderBody()` in a JS comment invoked it for real and took the dashboard down. There is
no such thing as a safe comment in a `.cshtml`.

**`PendingModelChangesWarning` aborts `Migrate()` before any SQL runs.** If migrations
"silently don't apply," this is the first thing to check — not the migration itself.

**`Program.cs` swallows DB-init failures** to console and continues. Offered hardening
twice, declined twice; it has cost three debugging cycles. The app runs but nothing works.

**Look for hardcoded vocabulary in more shapes than you expect.** Eight copies of the
location list were found across four passes because each stored something different:
codes (`value="RLB"`), names (`value="RD Lab"`), a JS object, an image map, and a stats
dictionary keyed by name.

**`site.js` loads after `@RenderBody()`**, so a view's inline script runs before it. Moving
shared JS there breaks any IIFE that calls it at parse time.

---

## Known issues — accepted as-is

- **`AlertThreshold` is 0 on all 244 compressors** — 825 units, no low-stock warning ever.
  `SetDefaultThreshold(team, threshold)` exists to bulk-fix it.
- **Superuser passcode is plaintext in `appsettings.json`**, tracked in git and now on the
  host laptop. Git history keeps the old value even after a change, so rotating matters more
  than removing the line.
- **`encodeLoc()` is duplicated** in `Index.cshtml` and `Intake.cshtml`, plus the C# original.
  Two client copies is avoidable; see the `site.js` trap above.
- **`Type` is free text** — 14 values, no vocabulary. `IsControlType` matches only the 9 items
  literally typed `Control`; EEV, VFD and Valve are uncovered.
- **`newGroup`** is a dead parameter on `ModifyStock`, accepted and ignored.
- Build warnings: `NU1903` (SQLitePCLRaw advisory), `CS0114` (`SignOut` hides base member),
  three `CS8602`.
- **`<select>` imbalance in `Index.cshtml` is a false positive** — `<select>` inside a JS
  comment. Pre-existing, harmless, don't chase it.

---

## Backlog

- **8C — location requests.** Deferred: the Pass 9 pending queue covers the case that
  actually mattered.
- **9D — CSV import** for volume intake. In-app entry covers small lists; Kason handles
  large ones by SQL.
- **Rack/Row as managed levels.** The `Locations` table already supports arbitrary depth
  via `ParentId`. Would fix `'RACK 5'` vs `'15'` vs `'2'` drift. Racks are per-place, so
  they'd hang off their Sub.
- **Locations tree view** with add-in-place, replacing the flat table + level picker.
- **6C — pickup selects a serial** from a dropdown instead of typing. Optional now that
  match-or-create puts correctness in the service.
- **Team / Location rename** — not offered anywhere; items store names as plain strings, so
  a rename orphans them unless it cascades. Add-and-hide is the workaround.
- Notification categories: table is multi-category, only `PickupRequested` exists.
- No backup story — `inventory.db` is hand-copied.

---

## Carried-forward items from prior review sessions

Nine items were flagged across earlier threads and never confirmed closed. Each was
re-checked against the deployed code. **Four resolved on inspection — do not
re-investigate them.**

### CONFIRMED — act on this

**1 · Short-pull logs the ordered quantity, not the pulled quantity.**
`OrderService.PickUpOrder`, the "Order Picked Up" log:

```csharp
QuantityChange = -it.Quantity,          // ORDERED
Details = "...pulled {actual pulls}"    // ACTUAL
```

`pulledQty` is computed correctly two lines above (`it.Quantity - remaining`) and is
already used for `LoanOutstanding` and the `CompressorUnit` rows — the log simply
doesn't use it. The `Details` string stays honest, but `QuantityChange` is what
`ExportToCsv` sums, so a short pull **overstates in exports**.

```csharp
QuantityChange = -pulledQty,
```

`order.Status = "Completed"` is also set unconditionally at line 380 — a short pull
closes the order. Decide separately whether that's wrong or just how it works.

### STILL PRESENT — layout, cosmetic

**2 · `.map-stats-bar` / `.map-live-bar` are `position: absolute`** (Index.cshtml
lines 276 and 293). Wanted in normal flex-column flow. Unchanged.

### CLOSED on inspection — no action

**3 · "Fresh database seeds every user as Viewer."** Does not reproduce. `Program.cs`
seeds four users with explicit levels — Kason Woods as **Admin (5)**, plus three at
Management (4). A fresh database is administrable immediately. The `User.AccessLevel`
model default *is* Viewer, which is likely what that session saw, but the seeder
overrides it.

**4 · "Leftover test scaffolding comments in `Program.cs`."** None present — no
TODO/test/temp/debug anywhere in the file.

**5 · "`syncHeaderOffset()` not executing."** It is wired correctly: defined at 2711,
bound to `load` and `resize`, and invoked directly at 3010. If `--vis-header-bottom`
reads empty in DevTools the cause is elsewhere — most likely the same stale build that
misled the roles investigation, or the measured element not existing at call time.

**6 · "`.holo-viewer` collapsing without explicit width."** Both now carry one —
`.holo-viewer` has `width: 80vw !important; max-width: 1200px`, `.holo-overlay` has
`width: 100vw`. Render not confirmed, but the stated cause is gone.

### UNVERIFIED — cannot be checked from code alone

**7 · TC counts not reserved across concurrent pending orders.** Promised at order
time, not held until pickup. No reservation logic exists, so the behaviour is plausible;
confirming it needs two concurrent orders against the same TC stock.

**8 · Disposed-transaction rollback in a `catch`.** `catch { tx.Rollback(); throw; }`
in `ReturnLoan`/`ScrapLoan`. Practically unreachable — would need a throw after commit.
Lowest priority.

**9 · Role-aware gating from `roles_and_modal.zip`.** No visibility into that artefact
from this thread. The later session traced the symptoms to a stale build but never
re-confirmed all three roles gate correctly. **Needs a manual pass per role.**

---

## Current state per Kason

Live on the host laptop, serving the team. Other groups arriving soon; intake path is
Settings → Locations (add their areas) → Intake (their stock) → Branch/Line set at entry.

Remaining work before the Rheem NA rollout is the short-pull fix above, then the move off
SQLite and off a console window. Everything else on the scaling list is expansion work,
not a blocker for the current site.
