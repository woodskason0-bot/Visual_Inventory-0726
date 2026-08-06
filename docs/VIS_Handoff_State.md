# VIS (Visual Inventory System) — Handoff State

**Supersedes the July 2026 handoff (stopped at Pass 4) and the Pass 9 handoff below it.**
Current as of the 2026-08-05/06 Claude Code session (Passes 10–12), about to go live
against Kason's real copied-in database.

Owner: Kason (Rheem). ASP.NET Core MVC (`net10.0`), EF Core + SQLite, Razor, Bootstrap 5
dark theme. Session-based name-only "identify" (no password) + numeric `AccessLevel`
(1 Viewer – 5 Admin) via `[RequireLevel(x)]`.

**Delivery model changed this session.** Every pass before this one shipped as a
folder-structured zip Kason copy-merged into his own VS project and compiled/migrated/
tested himself — Claude never ran the app. This session ran in **Claude Code** (a CLI
agent with real Bash/PowerShell, a `dotnet` toolchain, and a browser-automation tool),
directly in Kason's repo:
- Edited files in place (no zip).
- Ran `dotnet build` after every real chunk of work, not just at the end.
- `dotnet tool install --global dotnet-ef` once, then generated every migration with
  `dotnet ef migrations add` — the old "no dotnet SDK in sandbox, hand-author every
  migration" constraint **no longer applies** in this environment.
- Started the actual app (`dotnet run` via the browser tool's dev-server launcher),
  signed in, and drove it through a real Chromium tab — clicking, filling forms,
  reading the DOM, checking `console` for errors, reading the server's live SQL log —
  against Kason's **real, copied-in `inventory.db`**, not a fixture.
- Ran `git add`/`commit`/`push` directly once Kason approved each checkpoint.
See "What was actually verified live" near the end of this file before trusting any
claim above that a feature "works" — some things were confirmed this way, some were
only confirmed by static reading + a clean build, and the difference is spelled out.

---

## Deployed state

```
Migrations       31   (latest: 20260806140648_AddMotorUnits)
Items           492   (249 compressor models, 37 TC-tracked motor models)
Compressor units count evolving -- On Hand roster is now directly editable
                 (see Pass 11), not just a pickup-time log
Motor units      new table, TC only, seeded from real pickups starting tonight
Locations        18   Parent/Major/Sub, managed
Map zones         4   seeded, editable
Teams             2 confirmed active in the real DB (Samurai, Ninja) -- see
                 "Open item: 5 teams may not exist as rows yet" below
Users            29
```

Published **self-contained** (`-r win-x64 --self-contained`) to `C:\VIS_Publish` on the
host laptop, started with `--urls "http://0.0.0.0:5000"`. Database at
`C:\VIS_Inventory\inventory.db` — an absolute path in `appsettings.json`, so it is
**not** carried by publish and must be moved deliberately.

**Migrations apply automatically on startup** (`Program.cs` calls `Migrate()`) — no
manual step needed when this repo next runs against the real db. All 5 migrations
added tonight (`AddOrderItemStatus`, `RenameLineVocabulary`, `AddTeamLine`,
`AddMotorUnits`, plus the earlier ones already applied) were confirmed to apply
cleanly against Kason's actual copied-in database during this session, with no
`PendingModelChangesWarning`.

---

## Working conventions

- Read every file fully before editing. No blind edits.
- **Claude Code session (this one on):** edit in place, `dotnet build` after every real
  chunk, `dotnet ef migrations add` for schema changes (works fine in this environment
  — see delivery-model note above), test live against the real app + real db when
  possible, `git commit`/`push` only when Kason explicitly says to.
- **Older chat-based sessions (Pass 9 and earlier):** delivered as folder-structured
  zip (path-preserving), listed changed/new files one line each, hand-authored EF
  migrations by mirroring existing migration file structure and regenerating
  `AppDbContextModelSnapshot.cs`/`*.Designer.cs` by hand. If you're a chat session
  without shell access, that constraint is back — use this pattern.
- Verify brace/paren balance and diff against prior delivered state before packaging,
  if hand-authoring.
- Caution before schema migrations / breaking changes; proceed on routine work.
- **"Spiterate before building"** (Kason's term): for a genuinely new mechanic, write
  back the complete scope in prose — every field, every gate, every edge case — and
  wait for explicit confirmation before opening a file. Used successfully all session
  for the short-pull flow, Team.Line, and MotorUnit.
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

**A short pull refuses, it doesn't silently under-fulfill (Pass 10).** `PickUpOrder`
checks real shelf stock per line before pulling anything; a line that can't be honored
gets `OrderItem.Status = "Cancelled"` (new column) and is returned to the caller as a
`ShortPullLine` instead of pulling what's there. `ReportShortPull` is the correction:
the picker reports the true count per location, stock gets corrected (logged as
`"Stock Adjustment"`, not a pickup), and a fresh order is issued for the corrected
qty and picked up **in the same action** — the whole reason for widening this to
Standard-level. Sibling lines on the same order complete normally regardless of one
short line; that's what `OrderItem.Status` exists for.

**`CompressorUnit` and `MotorUnit` are two entrances now, not one (Pass 11–12).**
Both used to be created ONLY at pickup (`PickUpOrder`'s match-or-create). Both modals
now also let a human log/correct a unit directly — `InventoryService.
LogCompressorUnits`/`LogMotorUnits` — for stock that's on the shelf and was simply
never recorded. `MotorUnit` is `CompressorUnit`'s sibling, not a generalization of it
(deliberately kept separate — Kason picked this over one shared table): no
`SerialNumber`, just an optional `LabNumber`, and — unlike `CompressorUnit` — a
Picked-Up (out-on-loan) `MotorUnit` row **stays editable**, not just On Hand ones,
since there's no serial to fall back on for identity and a lab number is worth
capturing whenever someone notices it. `ReturnLoan`/`ScrapLoan` auto-select the
oldest outstanding `MotorUnit` rows for that order line (no unit-picker checkboxes
needed for motors, unlike compressors — nothing for a human to distinguish between).

**`Team.Line` is pure metadata, never a visibility gate (Pass 11).** A team can carry
a home Branch/Line, but it only *suggests* Branch/Line on the registration form, the
Ownership pane, and the compressor/motor filters — always overridable, never
enforced. Visibility is still Line on the item/user, untouched by this.

**`OrgStructure.BranchLines`' three Line names were corrected (Pass 11)**, with a data
migration renaming already-stored rows too: `Commercial Package/Splits` →
`Commercial Packaged/Splits`, `Residential Package` → `Residential Packaged`,
`Residential Gas Furnace` → `Residential Gas Furnaces`. Line is free text, not FK'd,
so fixing the constant without fixing stored rows would have silently orphaned
anything already on the old spelling from Branch resolution.

**`ModifyStock`'s Adjustment action can now reclassify existing stock as TC (Pass
12)** — "take my current N units, mark X of those as thermocoupled," independent of
whatever quantity delta the same adjustment makes. Distinct from Add's "these NEW
units include X TC." The `thermocoupledQty` parameter was already threaded through
the whole method for Add/Scrap/Location-Transfer; the Adjustment branch just never
read it before tonight.

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

**Pass 10 — short-pull flow, compressor filter, ItemId branch fix.** See "A short pull
refuses" above for the mechanics. Compressor modal got its first filter bar (Unclaimed
/ Team / Brand) — initially shipped with a real bug (inline `onchange="..."` attributes
can't see a function defined inside the file's enclosing IIFE; the filter silently only
ran once, on modal open) — fixed by switching to `addEventListener`. Also fixed:
`ItemId`'s Branch letter was defaulting to Commercial regardless of what was picked,
because it was deriving from the *registrant's own account* Line rather than the
Branch/Line actually selected on the registration form.

**Pass 11 — compressor on-hand logging, Team.Line, Line vocabulary rename, shared
cascade helper.** See "Two entrances now" and "`Team.Line` is pure metadata" above.
The Branch→Line cascade (populate Branch options, narrow Line options on Branch
change) had been hand-copied three times across this file (registration form,
Ownership pane, and the new compressor filter) — extracted into one shared
`wireLineCascade`/`setLineCascade`/`branchForLine` set. `Settings/Index.cshtml`
turned out to already have its *own* separate, well-built version of this same
pattern (`wireLinePicker`, built for Users' Line) that nobody had connected to
Teams — reused as-is rather than adding a fifth copy of the same idea project-wide.
**A real bug shipped and was caught by Kason, not by testing:** moving the Ownership
pane's cascade wiring to run immediately at page load (instead of lazily inside an
event handler, as it had before) put it ahead of `const orgStructure`'s own
declaration further down the same `<script>` block. JS temporal-dead-zone
`ReferenceError`, silently aborting every script initializer after it — Handle
Stock, Add to Cart, and every search box broke, while Omni Search (wired earlier in
the file) kept working. Fixed by moving the page's top-level data consts
(`itemsList`/`orgStructure`/`teamLines`) to the very top of the script, before
anything could reference them synchronously. See the Traps section.

**Pass 12 — TC motor tracking, Adjustment TC reclassification, filter/UX
follow-ups.** See "Two entrances now" and "Adjustment action" above for the
mechanics. Also: Motors modal gets the same filter bar as Compressors (Unclaimed /
Branch / Line / Team / Brand); New Item Registry gained a live, **non-gating**
name-match dropdown (mirrors the existing Rheem PN live-check, but registering
something new is the whole point of that form, so it never forces picking an
existing match — `bindAutocomplete()` gained a `selectable` flag for this); Order
Details gained a Brand column (`----` when blank).

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

**A `<script>` block's top-level data `const`s must load before ANY code that might
reference them synchronously — not just before code that merely *defines* a function
using them (Pass 11).** `Index.cshtml`'s huge single `<script>` block declares
`itemsList`/`orgStructure`/`teamLines` partway down the file. As long as everything
referencing them only ran inside event handlers (deferred until after the whole
script had parsed), order didn't matter. The moment something calls a function that
reads one of them **immediately, at top level** — as an Ownership-pane cascade call
did during Pass 11 — JS's temporal dead zone throws `ReferenceError: Cannot access
'X' before initialization`, and **the entire rest of the script silently never
runs**. Nothing after the crash point gets wired up. If a page-load bug takes out a
seemingly unrelated set of buttons, check whether something new is being called
unconditionally at top level rather than deferred into a handler, and whether it's
reading something declared later in the file. Fix applied: `itemsList`/
`orgStructure`/`teamLines` moved to the very top of the script.

---

## Known issues — accepted as-is

- **`AlertThreshold` is 0 on all compressors** — no low-stock warning ever fires.
  `SetDefaultThreshold(team, threshold)` exists to bulk-fix it.
- **Superuser passcode is plaintext in `appsettings.json`**, tracked in git and now on the
  host laptop. Git history keeps the old value even after a change, so rotating matters more
  than removing the line.
- **`encodeLoc()` is duplicated** in `Index.cshtml` and `Intake.cshtml`, plus the C# original.
  Two client copies is avoidable; see the `site.js` trap above.
- **`Type` is free text** — no vocabulary. `IsControlType` matches only items literally typed
  `Control`; EEV, VFD and Valve are uncovered. `IsMotorType`/`IsCompressorType` have the same
  shape of limitation but are confirmed correct for what exists today (suffix-match on
  "...Motor" covers ID/OD/bare Motor; exact-match on "Compressor").
- **`newGroup`** is a dead parameter on `ModifyStock`, accepted and ignored.
- Build warnings: `NU1903` (SQLitePCLRaw advisory), `CS0114` (`SignOut` hides base member),
  a few `CS8602`. Unchanged count/shape as of Pass 12 — none of tonight's work introduced
  a new one; check `dotnet build` output against this list if that ever seems to drift.
- **`<select>` imbalance in `Index.cshtml` is a false positive** — `<select>` inside a JS
  comment. Pre-existing, harmless, don't chase it.
- **Open item — 5 of the 7 real team names Kason gave may not exist as `Team` rows yet.**
  The `AddTeamLine` migration seeds Falcon/Polaris/Hurricane/T-Rex/Spartan's home Line by
  `UPDATE ... WHERE Name = '<name>'` — a no-op for any name that doesn't exist yet rather
  than an error. Only **Samurai** and **Ninja** were confirmed present in `ActiveTeams`
  during live testing tonight. If the other five aren't in Settings → Teams yet, their Line
  assignment silently did nothing — add them there and the migration's seeded Line will
  already be sitting on the row once it exists... actually it won't, since the migration
  only runs once. **Check Settings → Teams for all 7 before relying on Team.Line auto-fill
  for anyone but Samurai/Ninja; add missing ones and set their Line by hand via the new
  picker if the migration already ran.**
- **Motor loan return/scrap auto-selects the oldest outstanding units for that order line**
  (no unit-picker checkboxes, unlike compressors — nothing for a human to distinguish
  between since motors have no serial). This was **not separately verified live** — Order
  10 confirmed pickup + the order reaching "Completed," but whether a Return or Scrap
  action was also exercised against that TC motor loan wasn't confirmed in this session.
  Worth one real test before trusting it in front of the team.

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
- **Motors: only the TC subset is tracked (deliberate).** A plain non-TC motor has no
  per-unit tracking of any kind and isn't in scope — confirmed with Kason, TC only
  comes from the manufacturer and is the only subset worth per-unit identity.
- **Compressor/Motor filter: Team→Branch/Line is one-way on purpose.** Picking a Team
  suggests Branch/Line; the reverse (picking a Line auto-selecting a Team) was
  explicitly not built, since more than one team can share a Line (Spartan/Ninja/
  Samurai all sit on Commercial Packaged/Splits) — no single right answer.
- **`MyOrders.cshtml` was not extended for motor-unit selection.** Compressors let a
  human tick which specific serial is being returned/scrapped; motors skip that UI
  entirely in favor of auto-selecting the oldest outstanding units, since there's no
  serial to distinguish between. If that ever stops being true (motors get serials
  somehow), this auto-select assumption needs revisiting.

---

## Carried-forward items from prior review sessions

Nine items were flagged across earlier threads and never confirmed closed. Each was
re-checked against the deployed code. **Four resolved on inspection — do not
re-investigate them.**

### DONE — Pass 10

**1 · Short-pull logs the ordered quantity, not the pulled quantity.** ~~Was: logs
`-it.Quantity` (ordered) instead of `pulledQty` (actual), overstating short pulls in
exports; `order.Status` closed unconditionally even on a short pull.~~ **Fixed in
Pass 10**, and taken further than the original ask: a short pull now *refuses*
outright instead of silently under-fulfilling — see "A short pull refuses" in
Architecture above. Do not re-investigate; this is the current, deliberate behavior.

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

## What was actually verified live tonight (2026-08-05/06)

Read this before trusting any "works" claim above. Everything here was confirmed by
actually running the app (via Claude Code's browser tool) against Kason's real,
copied-in database — not a fixture — checking the DOM, the browser console, and the
server's live SQL/exception log after each action:

- Migrations apply cleanly against the real db, no `PendingModelChangesWarning`.
- Handle Stock, Add to Cart, and the search boxes all confirmed working (these were
  the exact things the Pass 11 TDZ bug broke — confirmed fixed, not just "should be").
- Ownership pane: Branch/Line cascade populates correctly with the corrected Line
  spelling; Team auto-fill confirmed (picked Ninja, Branch/Line snapped to Commercial
  Air / Commercial Packaged/Splits).
- New Item Registry: Team auto-fill confirmed; `ItemId` preview showed `R...` for
  Residential Air and `C...` for Commercial Air, confirming the branch-letter fix end
  to end; live name-match dropdown found an existing item and did NOT overwrite the
  typed text on click.
- Compressor filter and Motor filter: both confirmed actually narrowing results
  (not just displaying), Team-picks confirmed feeding Branch/Line, a couple of
  "0 of N shown" results double-checked against raw `data-*` attributes and confirmed
  to be real data facts, not filter bugs.
- Compressor On Hand roster: confirmed rendering the right row count, right location
  grouping, right form field names, real POST reaching the server correctly (tested
  the not-found path against a fake item id rather than writing real test data into
  Kason's roster).
- Motors modal: confirmed finding and grouping 37 real TC motor models correctly.
- **`PickUpOrder`'s MotorUnit creation: confirmed by Kason himself, not by me** —
  Order 10, CMR-0078, real pickup by Gavin.Grant. This is better evidence than
  anything I could have produced, since I was explicitly avoiding writing test data
  into the real db.
- Order Details Brand column: confirmed rendering a real brand ("Rheem") for Order 10.

**Not verified live, flagged above too:** `ReturnLoan`/`ScrapLoan`'s auto-select
logic for `MotorUnit` (whether Order 10's loan was actually returned or scrapped
afterward, exercising that specific code path, wasn't confirmed this session), and
whether the 5 not-yet-confirmed teams (Falcon/Polaris/Hurricane/T-Rex/Spartan) exist
as `Team` rows for their seeded `Line` to have actually landed on.

---

## Current state per Kason

About to go live for real: Kason is copying his live database in and deploying this
tonight/tomorrow for the team to use at work. Other groups arriving soon; intake path
is Settings → Locations (add their areas) → Intake (their stock) → Branch/Line set at
entry.

**Before calling it fully verified:** confirm the 5 possibly-missing teams in
Settings → Teams, and do one real Return or Scrap on a TC motor loan to close the
one gap in Pass 12's live testing. Everything else in "What was actually verified
live tonight" is solid. Everything else on the scaling list (SQL Server/Azure SQL
move, SSO, backup story) is expansion work per the addendum, not a blocker for
tonight.
