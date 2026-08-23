# VIS — Pass Audit

Every numbered pass, one entry each, sized to how much actually happened in it —
not padded to match a template. Pulled from `docs/VIS_Handoff_State.md`'s own
retrospective "Pass log," `docs/Commit_History.md`'s per-commit changelog, and
`git log` for the commit hashes neither doc quotes directly. Where the two source
docs disagree slightly on grouping (the pre-Pass-13 era was renumbered more than
once in hindsight), I followed the handoff doc's own Pass log — it's my own later
reconciliation of what actually happened, not the commit messages typed in the
moment.

Passes 1–9 predate the point where I started numbering consistently in real time —
detail there is whatever survives in commit messages and my own later retrospective
notes, not a full live-verification record like everything from Pass 13 on has.
Sub-phases (2a, 2b, …) are nested under their parent pass, not counted separately.

---

## Passes 1–4 — foundation (2026-07-13 → 2026-07-23)

No individual write-up exists for these — the handoff doc itself just says "see
git history." What shipped, in commit order:

- `e1f3bd2` (07-13) / `bae87b7` (07-14) — moved the database to an absolute path
  outside the publish folder, so republishing could never overwrite live data
  again (the "first-publish no Users table" incident this fixed is in the
  addendum, not here).
- `0a02b40` (07-20) — renamed the project from its original dev-scaffold name
  (`InventoryDevTwo`) to `Visual_Inventory_System`; added Rheem Part Number as a
  primary identifier with nagging validation. 75 files touched.
- `7b79eae` (07-23) — the Superuser gate: a second, independent lock on Settings
  (session name + passcode), deliberately separate from `AccessLevel` since
  Settings is what edits `AccessLevel` itself. First version of the Settings page.
- `2f0987a` (07-23) — per-user notification subscriptions (`NotificationSubscription`),
  starting with `PickupRequested`.
- `8bbcde8` (07-23) — folded blank Rheem PNs to a shared `N/A` sentinel; new
  `AllItems.cshtml` view.
- `43482c7` — the original Branch/Line org structure (`OrgStructure.cs`) and
  `User.Line`/`InventoryItem.Line` — the commit that first made visibility
  Line-based instead of Team-based, the rule everything since has built on.
- `57752da` — `CompressorUnit` table, first version (a pickup-time log, not yet
  the on-hand roster Pass 6 turns it into).

---

## Pass 5 — the three stacked bugs

Migrations had silently failed three times in a row. Root cause:
`PendingModelChangesWarning` — `CompressorUnit.ItemId`'s index was in the EF
snapshot but never configured in `OnModelCreating`, so `Migrate()` aborted before
running any SQL, and `Program.cs` swallowed the warning to console instead of
crashing loud. Behind that: `AllowNARheemPartNumber` folded blanks to `N/A` before
dropping the old unique index, so the second blank row hit `UNIQUE constraint
failed`. Fixed the model drift, reordered the migration, normalized five
placeholder PNs that only existed as workarounds for the index that was never
actually relaxed. Landed alongside Pass 6 in `bcb48fb`.

**Compressor rebuild (real-data SQL, not a code pass), same window:** wiped 75
compressors / 84 variants / 76 logs / Order 3, reloaded from five notebooks plus an
intern's PDF — 85 models / 189 units at New Test Cells, 177 models / 636 units at
the Lean-To. Reconciliation used max-on-overlap within the bottom-row group rather
than summing; my own "the two halves should be even" sanity check put them at 320
vs. 313.

---

## Pass 6A — CompressorUnit roster

`+ItemVariantId`, `+Status`, `+RecordedAt/By`; `OrderId`/`PickedUpAt`/`PickedUpBy`
made nullable; unique `(ItemId, SerialNumber)`. `PickUpOrder` became match-or-create
— mandatory, not an optional path. Commit `bcb48fb`.

## Pass 6B — Done Using

Compressors became loanable. Found `LoanableQuantity` was dead code, duplicated
inline in `PickUpOrder` — editing the real helper changed nothing until the call
site was pointed at it. `LoanOutstanding` reused for compressors to mean "not yet
dispositioned" — deliberate naming debt, one counter that can't drift beats two
that have to be kept in sync by hand. Reason field added to Return and Scrap for
every loanable type. Commit `bcb48fb`.

---

## Pass 7A — managed Teams

`Teams` table, CRUD in Settings, Team made optional with `N/A`. Killed a ternary
that gave every future team Samurai's project code by accident. Group removed from
the registration form — became derived, not entered. Commit `a3e20d9`.

## Pass 7B — Transfer means Line

Deleted a flattened, wrong copy of `OrgStructure` that lived in the "New Group"
picker. Branch/Line moved out of Edit Details into Transfer, where ownership
actually belongs — Edit Details went back to being identity-only.

## Pass 7C — managed Locations

The location vocabulary lived in four hand-kept copies and had drifted — 34 variant
rows carried Sub codes no copy knew about, rendering as raw codes on screen. One
`Locations` table now feeds all four. Commit `814a324`.

---

## Pass 8 — map zones

`LocationZones` table with normalized 0–1 coordinates (was raw pixels before), plus
a drag-to-draw editor in Settings. Commit `e2e8401`.

## Pass 9 — bulk intake

New `Intake` screen, Standard+. `IntakeBatches`/`IntakeRows` hold rows whose
location isn't recognized until Settings maps or creates it. `CreateItem` and the
five ordering actions dropped Engineer → Standard so interns could register and
order. Killed three more hardcoded copies of the location vocabulary (6th–8th found
across these passes). Commit `e2e8401` (same commit as Pass 8).

---

## Pass 10 — short-pull flow, compressor filter, ItemId branch fix (2026-08-05)

A short pull now refuses outright instead of silently under-fulfilling —
`PickUpOrder` checks real shelf stock first; `ReportShortPull` lets a Standard-level
picker correct the count and re-pick up in one action. Compressor modal got its
first filter bar (Unclaimed/Team/Brand) — shipped with a real bug (an inline
`onchange` couldn't see a function defined inside the enclosing IIFE), fixed same
session by switching to `addEventListener`. Fixed `ItemId`'s Branch letter
defaulting to Commercial regardless of what was picked on the form — it had been
deriving from the registrant's own account Line instead of the Branch/Line actually
selected. Commit `aa9f9e8`.

## Pass 11 — compressor on-hand logging, Team.Line, Line rename (2026-08-05)

`CompressorUnit`/`MotorUnit` gained a second entrance — logging/correcting a unit
directly from the modal, not just at pickup. Teams gained a home Branch/Line
(metadata only, auto-fill, never gates visibility). Fixed three Line-name typos
project-wide with a data migration that renamed already-stored rows too, so nothing
orphaned. Extracted a Branch→Line cascade that had been hand-copied three times
into one shared helper. Caught a real bug myself before testing found it: moving
the Ownership pane's cascade wiring to run at page load instead of inside an event
handler put it ahead of `const orgStructure`'s own declaration later in the same
script — a JS temporal-dead-zone error that silently killed every initializer after
it. Commit `e150162`.

## Pass 12 — TC motor tracking (2026-08-06)

New `MotorUnit` table, `CompressorUnit`'s sibling for TC-tracked motors — no
serial, optional lab number. `ModifyStock`'s Adjustment action can now reclassify
existing stock as thermocoupled without changing the quantity. Motors modal got the
same filter bar Compressors already had. New Item Registry gained a live,
non-gating name-match dropdown. Order Details gained a Brand column. Commit
`c706d27`.

---

## Pass 13 — go-live reconciliation, access-control audit, Delete Item (2026-08-06/07)

The big one. In order:

- **Compressor Team/Line reconciliation against real claim sheets.** Cross-referenced
  the live db against an export-wizard file and a prefix/suffix reference sheet
  covering 249 compressor items: 63 → Residential OD, 8 → Ninja by exact match, 53
  confirmed Samurai by prefix, 55 more Ninja-shaped-but-unlisted got the Line
  corrected with Team deliberately left unassigned (a third team, Spartan, sits on
  the same Line — no guessing). ~70 items in genuinely different brand families
  left untouched.
- **Data cleanup.** Removed 4 leftover test-fixture items and their full dependent
  chain (variants, units, logs, now-empty orders). Found and merged the app's one
  exact duplicate registration (`YRM083TAA`).
- **Sustaining added as a third Branch**, 4 Lines, 4 matching Teams.
- **Access-control audit.** Found `CreateItem`/`AddToCart`/`StartOrder` were
  server-gated at Standard since Pass 9 but the UI hid their buttons until
  Engineer — interns were authorized but couldn't reach the buttons. Renamed
  `SystemReset` → `ClearCart`, regated Admin → Standard to match what it actually
  does (wipe the calling user's own cart, nothing system-wide).
- **`User.Branch` added** for whole-Branch visibility short of full Admin.
- **Delete Item shipped** — the app's first true hard delete.
- **`TransactionLog.ItemName` snapshot added** — found and fixed as a direct
  consequence of Delete Item breaking the Activity Feed's live-lookup assumption.
- **Branches and Lines became managed vocabulary**, not a hardcoded dictionary —
  new `Branches`/`OrgLines` tables, `OrgStructure.cs` rebuilt as a DB-backed static
  snapshot mirroring `LocationCodec`'s pattern (and fixed a real gap in that
  original pattern along the way — `LocationCodec` never actually got a startup
  refresh despite documentation claiming it did). Verified live: added a real test
  Branch/Line through Settings, confirmed it appeared everywhere with no restart,
  removed it.

Commit `a019e52`.

## Pass 14 — RCR rename, Quick Filter/access polish (2026-08-07)

63 Residential OD compressors re-minted `CCR-` → `RCR-0001`...`RCR-0063` (a real
rename cascaded across 63 `TransactionLogs` and 3 `CompressorUnits` rows, not a
delete/re-add) — the one deliberate exception to "Group is frozen forever" in this
project's history, because these 63 were never actually Commercial to begin with.
Sustaining added as a third Quick Filter button (still Omni-Search-based at the
time — the same caveat that button carried until this week's fix). Quick Filter
branch buttons gray out for Branches a user isn't scoped to. "Pick Up Orders"
widget renamed "Available Tasks." Activity Feed fixed to stop showing every
Settings/admin action as "Stock Adjusted: Unknown item." 9 users added. Commit
`41f3982`.

## Pass 15 — mandatory Line, Line-scoped logs (2026-08-07)

Line is now required to register an item, single or bulk — server-side rejection
plus client-side `required`. View Logs and the Activity Feed became Line-scoped via
new `ApplyLogVisibility()`, built on top of the existing filter rather than
duplicating it. Verified live with a real before/after across an Admin and a
Line-scoped user. Commit `729c8dc`.

---

## Pass 16 — Unclaimed filter fix, UserTeams (2026-08-10)

Compressor/Motor "Unclaimed only" fixed from AND to OR (was showing "0 of 245" and
hiding all 124 motors). 242 non-compressor items bulk-reconciled to a real Line via
direct SQL. Add User gained whole-Branch assignment at creation. `User.Team`
rebuilt as many-to-many (`UserTeams` table) with a team-centric picker in Settings,
replacing a single string field with no UI path to set it — migration carried
forward both existing values with zero data loss, verified live through the real
Settings UI with `TransactionLog` entries checked after each add/remove. Location
Transfer's cascade fixed to source from the `Locations` table instead of existing
stock (the ninth instance of the "one more hardcoded location copy" pattern). 5
real compressors registered, one duplicate caught and merged. New Item Intake Excel
template built for other teams. Commit `fb2a2a0`.

## Pass 17 — serial/TC capture at registration, Bulk Intake hold-path fix (2026-08-11)

Compressor serial capture and motor TC-count declaration added to both New Item
Registry and Bulk Intake, not just the post-hoc Log Units entrance — "nudge, don't
block," same rule as everything else touching Line/RPN. Mostly reuse: the serial
path generalizes an existing helper into `LogIntakeSerials`; the TC path
(`LogIntakeThermocoupled`) mirrors `PickUpOrder`'s existing pattern. `IntakeRow`
widened to carry multiple serials + a TC count (migration `AddIntakeRowMultiSerialAndTc`,
35th). Also found and fixed a pre-existing bug: Bulk Intake's "hold for
unrecognized location" path had never actually worked — the `"__NEW__"` sentinel
fell through the blank-only hold check and got stored as a real location. Verified
live by me directly, not Claude. Commit `72d89fd`.

---

## Pass 18 — reject pickup of cancelled orders (2026-08-12)

`PickUpOrder` only guarded against `Completed`; `CancelPersistedOrder` leaves an
order's lines `Pending` — so a runner holding a stale Pickup Queue tab could still
post a pickup after an Engineer cancelled: real stock left the shelf and the
order's status got overwritten `Cancelled` → `Completed`. New guard rejects any
non-Pending order with a distinct message. Verified live by reproducing the exact
two-tab race against a scratchpad copy of the real db. Commit `c553384`.

## Pass 19 — three worktree fixes, merged same batch as Pass 18 (2026-08-12)

Built in isolated worktrees, merged together:
- `ReturnLoan` storing `ItemVariantId=0` on units returned to a newly-minted
  variant — `dest.Id` was referenced before `SaveChanges()` gave it a real id, and
  since neither unit table's `ItemVariantId` is a real FK, EF did no fixup and
  silently wrote a dangling 0. Fixed with a `SaveChanges()` right after `dest` is
  added. Commit `908673d`.
- `ReportShortPull` dropping TC count and pull location on reissue — the fresh
  `OrderItem` only copied `ItemId`/`Quantity`, so a short-pulled TC motor line
  reissued with TC = 0 (no loan, no `MotorUnit` rows flipped). Commit `ec667ae`.
- Three misleading user-facing messages: Scrap overstated the logged quantity past
  what was actually on the shelf; an unrecognized Line on Ownership silently
  no-op'd instead of throwing; a partial Bulk Intake failure claimed "nothing
  imported" when rows without errors do land. Commit `ae888d8`.

## Pass 20 — map cleanup, Intake Team autofill, Registry→Modify Stock jump (2026-08-13)

Removed the facility map's decorative red tracker dot entirely; zone row-count
labels made hover-only. Bulk Intake preseeds Branch/Line from the signed-in user
and overwrites it when a Team with a home Line is picked. New Item Registry's
name-match dropdown now jumps into Modify Stock instead of doing nothing when
clicked; Modify Stock's Add action gained optional compressor serial capture to
match (Pass 17 parity for this entrance). Verified live, read-only, against the
real app/db. Commit `f67eb5c`.

## Pass 21 — Bulk Intake batch review modal (2026-08-13)

A row whose typed Model name exact-matches a known item routes to an "Already
registered" batch-review list instead of quick-adding — merged by item id (two
rows for the same item combine into one section), each section Add/Adjustment
only, needs its own acknowledge. New `CommitIntakeStockBatch` applies the whole
batch in one transaction, refusing an Adjustment against a location the item has
no existing variant at. Verified live: merge-by-id, the acknowledge gate, a real
+1/-1 round trip on CCR-0001. Commit `b2d87f1`.

## Pass 22 — batch review as live inline sections (2026-08-13)

Direct feedback on Pass 21 ("felt like a dangling process"): rebuilt the modal as
live inline sections directly on the Intake page — a match creates a section
immediately, a second match grows it in place, reading current DOM values first so
hand-edits survive. Server side unchanged. Added a styled autocomplete dropdown
(new generic `bindValueAutocomplete()`) to Intake's Type field, Registry's Type
field, and the sign-in name field — fixed a real pre-existing broken regex on the
sign-in field's `pattern` attribute found while touching that file (silently
throwing a console error on every load). Commit `6318433`.

## Pass 23 — Delete Stack, Rack/Row cascading suggestions (2026-08-13)

New `DeleteVariant` closes the gap Delete Item leaves — a single empty variant on
an item that still carries stock elsewhere. Same guard shape as Delete Item, gated
Admin. Rack/Row fields across Registry/Intake/Export Wizard/Modify Stock now
cascade-suggest from real stock data via `BuildRackRowMap()`, still free text.
Verified live: Delete Stack used for real on the actual stuck item (CCR-0013) that
surfaced the gap. Commit `08cd9bc`.

## Pass 24 — the app's first responsive pass, 3 checkpoints (2026-08-13)

Zero media queries existed anywhere before this. **Checkpoint 1** (`f644898`):
breakpoints reflowing the fixed 6-column dashboard grid into a stacked column below
1100px, narrower sidebars below 1400px, holo-viewer single-column below 900px.
Found and fixed two real overflow bugs live: the map stats bar bled past its
container, and the top nav tried to render full-inline on a real 768px tablet.
**Checkpoint 2** (`da4a558`): same table-responsive wrapping applied across
AllItems, both Logs tabs, OrderDetails, Orders, both PickupQueue tables, all 7
Settings tables. **Checkpoint 3** (`d21d799`): the last 6 unwrapped tables in
Index's own modals. Verified live at 375px/768px; desktop confirmed unregressed.

---

## Pass 25 — Delivery intake/claim workflow (2026-08-14)

New `Delivery` model: required photo (saved to `C:\VIS_Image-Uploads`, outside the
publish folder, resized via `System.Drawing.Common`), optional tracking#/order#/
brand-of-shipping/brand-of-item with the Rheem-PN-style N/A toggle, routed to a
specific Management+ person or a shared "Unknown Delivery" bucket, `VisTask`-style
Open/Claimed/Done claim lifecycle on a new Deliveries board. Verified live against
the real db end-to-end. Commit `bb1b1ce`.

## Pass 26 — real-device mobile fixes (2026-08-14)

Two bugs that slipped past Pass 24's "verified live" claim because that
verification ran through Claude Code's Browser-pane tool, which isn't a
visually-compositing real renderer and can't catch this class of bug — found only
on a real phone: `background-attachment: fixed` on the dot-matrix background
desynced from foreground scroll on mobile Safari/Chrome (page wouldn't scroll, only
the background moved), dropped to `scroll` below 1100px. The navbar toggler never
had `navbar-dark`, defaulting to a dark-on-dark variant invisible against this
app's permanently-dark navbar. Scroll fix confirmed on a real phone; navbar fix
confirmed via computed styles only at commit time. Commit `c35c963`.

## Pass 27 phase 1 — left sidebar shell (2026-08-16)

`_Layout.cshtml` rebuilt around `.app-shell` (sidebar + main-area) instead of the
old `<header><nav>`, per a real design mockup. Every nav item, gate, theme toggle,
notification bell, and identity/sign-out relocated with no functional changes —
pure structural move. New Branch→Line→tier identity breadcrumb. Desktop collapses
to icon-only; mobile (<992px) gets an off-canvas drawer, deliberately without a
body-scroll-lock given how close Pass 26's scroll bug was to this same code path.
Sign-in page now renders with no shell at all (real-phone feedback, same session).
Commit `5ac6288`.

**Regression fixes, same arc** (`385f625`, shipped from a remote session with no
compiler available): `syncHeaderOffset()` still queried the `<header>` element
Phase 1 removed, silently no-op'ing; the Add-to-Cart order-mode sweep still
targeted the pre-rename `.rheem-navbar` class. `jQuery`/`Bootstrap`/`site.js` load
order moved to right after `<body>` opens — a prerequisite for Pass 28's shared-
partial extraction. A follow-up commit (`75d1949`) fixed three more real bugs from
live use (light-mode text legibility, sidebar-collapse layout) but its own "fix"
for the collapse behavior turned out not to actually run — see Pass 28 (2a) below,
which found why.

---

## Pass 28 — Command Center rebuild + Search Center extraction (2026-08-16 → 2026-08-21)

```
Pass 28
├── 2a    Extract shared partials (Modify Stock / New Item Registry / Alert Rules)
│         └── verification pass: 3 live regressions found + fixed
├── 2b    Search Center extracted to its own route
├── 2c    Command Center built standalone, styled to match sidebar
├── 2d    The swap — "/" now renders Command Center
├── 2d.1  Light/dark legibility pass; Alert Rules found unreachable
└── 2d.2  Closed out 2d.1's two open items
```

**Why bigger than "build Command Center":** `Index.cshtml` was simultaneously the
dashboard AND the search/browse page. Command Center's KPI strip and cards needed
somewhere real to link to, but Search Center didn't exist yet — so this pass did a
mechanical, non-visual extraction first, deliberately leaving the visual match to
phase 4.

**2a** (`3691cb9` → `15822b4`) — Modify Stock, New Item Registry, and Alert Rules
pulled into shared partials; generic JS promoted to `site.js`. Shipped from a
remote session with no compiler, "verified" via line-count/`node --check` only.
The next session's real click-through found three live-only regressions: page-level
data consts (`itemsList`/`orgStructure`/etc.) still declared *after* the partials
that referenced them at script top level, killing the rest of each partial's script
silently; a sidebar-collapse IIFE querying the DOM before that markup existed post
Pass-27's load-order change, silently no-op'ing collapse entirely (and explaining
why an earlier "fix" for it never actually ran); map zone pins only resyncing on
`window.resize`, not on a sidebar-collapse-driven container resize. All three fixed
and confirmed live with zero console errors. Off-repo, same session: deleted one
test item that had leaked into the live db, and pulled a 58-item compressor
catalog cross-reference for Sean, handed off as xlsx.

**2b** (`543c7a8`) — Advanced Filters/Omni Search/results grid/Export Wizard/
Compressor-Motor Registry moved to `/Home/SearchCenter`, `/Home/Index` untouched
throughout. Found every cart/stock action hardcoded `RedirectToAction("Index")` —
fixed with the existing `SmartRedirect()` pattern before it could strand Search
Center users. Found and fixed a real pre-existing `locMap` scope bug (declared
inside an IIFE, invisible to `site.js`'s cascade helper) — reproduced live on the
old dashboard first to confirm it predated this pass.

**2c** (`6054b51`, plus `1cc26d9` same-day fix) — Command Center built standalone:
KPI strip, map + synced Location List, two donuts, Quick Actions, bottom row.
Picked up the sidebar's visual language immediately rather than waiting for
phase 4 — a judgment call once the two looks sat side by side. Found
`ViewBag.InventoryService` was never set, silently zeroing every map/list count
despite a correct KPI number from a separate variable; found the Need Serial
donut's original definition was true but misleading (near-zero by construction,
only 184 of 849 on-hand units ever logged a serial at all) and redefined it to
match Need PN's framing same day.

**2d** (`3fa3c79`) — the swap. `Index()` now returns Command Center directly; the
separate `/Home/CommandCenter` route retired (404s on purpose). `Index.cshtml`
(2551 lines) deleted outright. Nav relabeled, real Search Center link added.
Verified live end-to-end: real numbers on "/", nav highlighting, deep-links, a
full sign-out/sign-in cycle confirming non-stale data.

**2d.1** (`c7cec21`) — went through both pages in both themes with a real contrast
checker, not eyeballing. Fixed four real contrast bugs (`.text-light-gray` had no
base color at all; `.modal-content label` had no light-mode override, missed
entirely on Command Center; `text-info`/`text-warning` and the outline-button
variants read 1.6–2:1 on white sitewide). Found Alert Rules had been completely
unreachable since Index was retired — Command Center never got a trigger for it;
added one.

**2d.2** (`2a1278f`, `a8903aa`) — re-verified Alert Rules end-to-end against real
data, per-item and bulk, snapshotting and restoring the real db state around each
test. Confirmed a dark-mode contrast anomaly from 2d.1 couldn't be reproduced
across two independent sessions — downgraded from open defect to "closed on
inspection." Identity breadcrumb simplified to `Line | LX`. Side observation, not
fixed: alert-threshold writes don't log a `TransactionLog` entry like every other
admin action does.

---

## Same-day follow-up, not its own pass (2026-08-21/22, commit `6014f74`)

Quick Filters' Commercial/Residential/Sustaining buttons were still doing a plain
Omni-Search text match for the literal word — reliable only by accident, and not
even searching the `Line` field it was implicitly trying to represent. Rewired to
a real `Line`-membership filter via `OrgStructure.BranchLines`, verified live
(388 vs. 63 items returned for Commercial vs. Residential — a real, differing
subset, not the same near-everything result both times). Extended the existing
`startOrderModal` light-mode contrast fix to `compressorRegistryModal` and
`motorRegistryModal`, which shared the identical white-card structure and bug but
had never been covered by it.
