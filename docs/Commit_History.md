# VIS — Commit History

Per-commit summary of the whole repo, newest first, grouped by date (the closest
reliable proxy for "session" — commits on the same day are the same working stretch).
Functionality description + which areas of the codebase each commit actually touched.
Companion to `VIS_Handoff_State.md` (architecture/current-state) and
`vis-addendum-consolidated.md` (context/stakeholders) — this file is purely "what
landed, when."

---

## 2026-08-13

### `b2d87f1` — Pass 21: Bulk Intake routes known-item matches to a batch Modify Stock review
A row whose typed Model name exact-matches a known item no longer quick-adds through
`CommitIntake` -- it moves to a separate "Already registered" list, excluded from
Import, merged by item id when the new review modal opens (two rows for the same
item combine into one section instead of racing). Each section is Add/Adjustment
only, needs its own acknowledge, and Apply All stays disabled until every section is
checked. New `InventoryService.CommitIntakeStockBatch` applies the whole batch in one
transaction by calling `ModifyStock` itself per item -- the only new rule is refusing
an Adjustment against a location the item has no existing variant at (Adjustment has
no "NEW location" concept, unlike Add). The held-batch Settings approval path is
unchanged. Verified live: merge-by-id, the multi-section acknowledge gate, and a real
+1/-1 round trip on CCR-0001 (New Test Cells) confirming the batch lands on the
item's actual existing variant; separately confirmed the Adjustment guard refuses
cleanly with no state change.
**Touched:** `Controllers/HomeController.cs`, `Services/InventoryService.cs`,
`Views/Home/Intake.cshtml`, both `docs/` files.

### `03b032c` — docs: backfill Commit_History.md through Pass 20
Docs-only. Wrote up the four worktree bug-fix merges (Pass 18/19) and Pass 20, which
hadn't been logged here yet.
**Touched:** `docs/Commit_History.md`.

### `f67eb5c` — Pass 20: map cleanup, Intake Team autofill, Registry-to-Modify-Stock jump with serial capture
Removed the facility map's decorative red tracker dot (element, CSS, waypoint path,
animation loop) and made zone row-count labels hover-only. Bulk Intake now preseeds
Branch/Line from the signed-in user on page load and overwrites it when a Team with
a home Line is picked (same Team-Line map Registration already used). New Item
Registry's name-match dropdown now jumps into Modify Stock instead of doing nothing
when clicked; Modify Stock's Add action gained optional compressor serial capture to
match (Pass 17 parity for this new entrance). Verified live, read-only, against the
real app/db.
**Touched:** `Controllers/HomeController.cs`, `Services/InventoryService.cs`,
`Views/Home/Index.cshtml`, `Views/Home/Intake.cshtml`, both `docs/` files.

---

## 2026-08-12

### `04de9e9` (merge) / `ae888d8` — Fix three misleading user-facing messages: Scrap log overstatement, Ownership silent no-op, intake "nothing imported"
Scrap now logs the actual clamped quantity instead of the requested one (the log,
and `ExportToCsv`'s `ScrappedQty` sum, used to overstate a scrap past what was on the
shelf). An unrecognized Line on an Ownership move now throws instead of returning a
success-shaped no-op that toasted "applied." A partial Bulk Intake failure (rows
without errors DO land — `CommitIntake` saves per row) now says how many rows/units
actually imported instead of claiming nothing did, at both `SubmitIntake` and
`ApproveIntake`.
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Services/InventoryService.cs`. Built in an isolated worktree, merged into master
same batch as Pass 18/19.

### `ce87625` (merge) / `908673d` — Fix ReturnLoan storing ItemVariantId=0 on units returned to a newly-minted variant
`ReturnLoan`'s "mint a new variant" path added `dest` to `inv.Variants` but referenced
`dest.Id` on the returned unit rows before saving — since neither `CompressorUnit`
nor `MotorUnit`'s `ItemVariantId` carries an FK, EF did no fixup and silently wrote a
dangling 0. Fixed with a `SaveChanges()` right after `dest` is added.
**Touched:** `Services/OrderService.cs`. Built in an isolated worktree, merged into
master same batch as Pass 18/19.

### `6452429` (merge) / `ec667ae` — Fix ReportShortPull dropping TC count and pull location on reissue
The fresh `OrderItem` `ReportShortPull` reissues for the corrected quantity copied
only `ItemId`/`Quantity` — not `ThermocoupledCount` or `RequestedVariantId`. For a
short-pulled TC motor line this meant the immediate re-pickup ran with TC = 0: no
loan created, no `MotorUnit` rows flipped. Fixed by carrying both forward (TC
clamped to the corrected quantity).
**Touched:** `Services/OrderService.cs`. Built in an isolated worktree, merged into
master same batch as Pass 18/19.

### `ce3d7f7` — docs: backfill Commit_History.md (Passes 16-18 -- it had stopped at Pass 15)
Docs-only. Wrote up the gap between this file's first commit (which stopped at Pass
15) and Pass 18 -- Passes 16 and 17 hadn't been backfilled here yet.
**Touched:** `docs/Commit_History.md`.

### `c553384` — Pass 18: reject pickup of cancelled orders, closing the stale-queue race
`PickUpOrder` only guarded against `Completed`, and `CancelPersistedOrder` leaves an
order's lines `Pending` — so a runner holding a stale Pickup Queue page could still
post the pickup after an Engineer cancelled: real stock left the shelf and the
order's status was overwritten `Cancelled` → `Completed`. New guard rejects any
non-Pending order with a distinct message. Cancelled orders' lines deliberately stay
`Pending` (line-level `Cancelled` means "came up short at pickup" — it's
`ReportShortPull`'s eligibility guard). Verified live by reproducing the exact
two-tab race against a scratchpad copy of the real db.
**Touched:** `Services/OrderService.cs`, both `docs/` files.

---

## 2026-08-11

### `72d89fd` — Pass 17: serial/TC capture at registration and intake, fix Bulk Intake's broken hold-for-approval path
Compressor serial capture and motor TC-count declaration added to both New Item
Registry and Bulk Intake (optional, "nudge don't block"); `IntakeRow` widened to
carry multiple serials + a TC count (new migration `AddIntakeRowMultiSerialAndTc`,
35th). Also found and fixed a pre-existing bug: Bulk Intake's "hold for unrecognized
location" path had never worked — the `"__NEW__"` sentinel from the Parent picker
fell through the blank-only hold check and got stored as a real location.
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Services/InventoryService.cs`, `Models/IntakeBatch.cs`, `Views/Home/Index.cshtml`,
`Views/Home/Intake.cshtml`, `Views/Settings/Index.cshtml`, new migration pair +
snapshot, both `docs/` files.

### `f1be280` — docs: log Location Transfer fix, compressor registrations, and scope unit lifecycle tracking
Docs-only follow-up to the two code commits below, plus scoping notes for the future
unit-lifecycle feature (written up in the handoff backlog, deliberately not built).
**Touched:** `docs/VIS_Handoff_State.md`.

### `097ab24` — Fix Location Transfer cascade to source from the Locations table, not existing stock
Modify Stock's transfer-destination picker was built by walking already-stored
variant location codes, so a Sub added in Settings with zero stock in it could never
be picked. New `BuildLocationHierarchyCoded()` mirrors `BuildLocationTree()`, just
code-keyed — the ninth "independently-sourced copy of the location vocabulary" this
project has found.
**Touched:** `Controllers/HomeController.cs`, `Views/Home/Index.cshtml`.

---

## 2026-08-10

### `5b07d55` — Drop the "Required as of Pass 15" subtext under New Item Registry's Line field
Cosmetic — the red asterisk already carries the meaning.
**Touched:** `Views/Home/Index.cshtml`.

### `fb2a2a0` — Pass 16: Unclaimed filter fix, bulk Line reconciliation, whole-Branch at creation, User.Team as many-to-many
Compressor/Motor "Unclaimed only" fixed from AND to OR (was showing 0 of 245 and
hiding all 124 motors); Add User gained whole-Branch assignment at creation
(mirroring `UpdateLine`'s `__WHOLE_BRANCH__` sentinel); `User.Team` rebuilt as
many-to-many via a new `UserTeams` table with a team-centric membership picker in
Settings, migration carrying forward existing values (new migration `AddUserTeams`,
34th). (The 242-item bulk Line reconciliation from the same session was data-only,
direct SQL against the live db — not in this diff.)
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Data/AppDbContext.cs`, `Models/User.cs`, new `Models/UserTeam.cs`,
`Services/InventoryService.cs`, `Views/Home/Index.cshtml`,
`Views/Settings/Index.cshtml`, `.claude/launch.json`, new migration pair + snapshot,
both `docs/` files.

### `61eeb20` — add CLAUDE.md pointing future sessions at the docs/ handoff files
**Touched:** new `CLAUDE.md`.

---

## 2026-08-09

### `c68a86d` — docs: add per-commit changelog (docs/Commit_History.md)
This file's own first commit.
**Touched:** new `docs/Commit_History.md`.

### `729c8dc` — Pass 15: mandatory Line at registration, Line-scoped log visibility
Line is now required (not just validated-if-present) to register an item, both
single-item and bulk Intake — server-side rejection plus client-side `required` on
both forms. View Logs and the dashboard Activity Feed are now filtered to the same
Line-visibility rule as Search/browsing, via a new `ApplyLogVisibility()` built on top
of the existing filter rather than duplicating it. Code only, no data changes.
**Touched:** `Controllers/HomeController.cs`, `Services/InventoryService.cs`,
`Views/Home/Index.cshtml`, `Views/Home/Intake.cshtml`, both `docs/` files.

---

## 2026-08-07

### `41f3982` — Pass 14: Quick Filter/access polish, Activity Feed fix, RCR compressor rename, roster growth
Added Sustaining as a third Quick Filter branch button; grayed out the two branches a
non-Admin user isn't scoped to; renamed the "Pick Up Orders" dashboard widget to
"Available Tasks"; fixed the Activity Feed showing every Settings/admin action as
"Stock Adjusted: Unknown item" by branching on blank `TransactionLog.ItemId` instead
of enumerating action types; added 9 real users. (The 63-item `RCR-` compressor
rename and roster growth were data-only changes made directly against the working
database, not part of this commit's diff — see the handoff doc for those.)
**Touched:** `Controllers/HomeController.cs`, `Views/Home/Index.cshtml`, both `docs/`
files.

### `a019e52` — Pass 13: go-live data reconciliation, access-control pass, Delete Item, Branches/Lines redesign
The big one. Compressor Team/Line ownership reconciled against real claim sheets (63
Residential OD, 8 Ninja, 53 Samurai, 55 Line-corrected/Team-unassigned); test-fixture
cleanup and one duplicate merge; Sustaining added as a 3rd Branch; full access-level
audit with real fixes (Standard-gated actions whose buttons were hidden until
Engineer; `SystemReset`→`ClearCart` rename+regate); new `User.Branch` for
whole-branch visibility; **Delete Item** shipped (the app's first true hard-delete);
`TransactionLog.ItemName` snapshot added; **Branches and Lines converted from a
hardcoded dictionary to managed, Settings-editable vocabulary** (new `Branches`/
`OrgLines` tables, `OrgStructure.cs` rebuilt as a DB-backed static snapshot mirroring
`LocationCodec`'s pattern — and fixed a real gap found in that original pattern along
the way, since `LocationCodec` never actually got a startup refresh despite
documentation claiming it did).
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Data/AppDbContext.cs`, `Models/Branch.cs` (new), `Models/OrgLine.cs` (new),
`Models/TransactionLog.cs`, `Models/User.cs`, `Program.cs`,
`Services/CurrentUserService.cs`, `Services/InventoryService.cs`,
`Services/OrderService.cs`, `Services/OrgStructure.cs`, `Views/Home/Index.cshtml`,
`Views/Settings/Index.cshtml`, 3 new migrations (`AddUserBranch`,
`AddTransactionLogItemName`, `AddBranchesAndLines`), both `docs/` files (full
first-person rewrite).

---

## 2026-08-06

### `f4e9b44` — update handoff doc for the next Claude Code session (Passes 10-12)
Docs-only. Wrote up Passes 10–12 (short-pull flow, compressor on-hand logging,
TC motor tracking) into the handoff doc for continuity into the next session.
**Touched:** `docs/VIS_Handoff_State.md`.

### `c706d27` — TC motor tracking, Adjustment TC reclassification, filter/UX follow-ups
New `MotorUnit` table — `CompressorUnit`'s sibling for TC-tracked motors (no serial,
optional lab number, Picked-Up rows stay editable since there's nothing else to
identify a unit by). `ModifyStock`'s Adjustment action can now reclassify existing
stock as thermocoupled without changing the quantity. Motors modal gets the same
filter bar Compressors already had. Order Details gained a Brand column.
**Touched:** `Controllers/HomeController.cs`, `Data/AppDbContext.cs`,
`Models/MotorUnit.cs` (new), `Services/InventoryService.cs`,
`Services/OrderService.cs`, `Views/Home/Index.cshtml`,
`Views/Home/OrderDetails.cshtml`, migration `AddMotorUnits`.

---

## 2026-08-05

### `e150162` — compressor on-hand serial logging, Team home Line, Line vocabulary rename
`CompressorUnit`/`MotorUnit` gained a second entrance — a human can now log/correct a
unit directly from the modal, not just at pickup. Teams gained a home Branch/Line
(pure metadata, auto-fill only, never gates visibility). Fixed three Line-name typos
project-wide with a data migration that also renamed already-stored rows so nothing
orphaned. Extracted a Branch→Line cascade that had been hand-copied three times into
one shared helper. A real bug shipped and was caught by testing that session: moving
cascade wiring to run at page load instead of inside an event handler created a JS
temporal-dead-zone error that silently killed every script initializer after it —
fixed by moving top-level data consts to the top of the file.
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Models/Team.cs`, `Models/User.cs`, `Services/InventoryService.cs`,
`Services/OrgStructure.cs`, `Views/Home/Index.cshtml`, `Views/Settings/Index.cshtml`,
migrations `RenameLineVocabulary` + `AddTeamLine`.

### `aa9f9e8` — short-pull refuse/correct/reissue flow, compressor unclaimed/team/brand filter, fix ItemId branch prefix
A short pull now refuses outright instead of silently under-fulfilling — `PickUpOrder`
checks real shelf stock first; `ReportShortPull` lets a Standard-level picker correct
the count and re-pick up in one action. Compressor modal got its first filter bar
(Unclaimed/Team/Brand) — shipped with a real bug (inline `onchange` couldn't see a
function defined inside the enclosing IIFE) fixed same session by switching to
`addEventListener`. Fixed `ItemId`'s Branch letter defaulting to Commercial regardless
of what was actually picked on the registration form.
**Touched:** `Controllers/HomeController.cs`, `Models/OrderItem.cs`,
`Services/OrderService.cs`, `Views/Home/Index.cshtml`,
`Views/Home/OrderDetails.cshtml`, `Views/Home/PickupQueue.cshtml`, migration
`AddOrderItemStatus`.

---

## 2026-08-04

### `2be60a5` — Merge remote-tracking branch 'origin/master'
Merge commit, no independent content.

### `3ba2a0d` — add VIS handoff state + consolidated context addendum to docs/
First appearance of both handoff docs in the repo — version-tracks the Pass 9 handoff
and the merged context addendum so project/architecture history survives outside chat
threads.
**Touched:** `docs/VIS_Handoff_State.md` (new), `docs/vis-addendum-consolidated.md`
(new).

---

## 2026-08-03

### `269c750` — Merge branch 'master' of https://github.com/woodskason0-bot/Visual_Inventory-0726
Merge commit, no independent content.

---

## 2026-08-02

### `0892211` — add comments on confusing class sections
One-line comment addition, no behavior change.
**Touched:** `Controllers/HomeController.cs`.

---

## 2026-07-30

### `e2e8401` — pass 8 + 9 — map zones, bulk intake, registration/ordering permission drop, more hardcoded-location kills
Map zones: `LocationZones` table with normalized 0–1 coordinates (was raw pixels), a
drag-to-draw editor in Settings. Bulk intake: new `Intake` screen for Standard+,
`IntakeBatches`/`IntakeRows` hold batches whose location isn't recognized until
Settings maps or creates it. `CreateItem` and the five ordering actions moved
Engineer → Standard so interns could register and order. Found and killed three more
hardcoded copies of the location vocabulary (the sixth, seventh, eighth found across
these passes).
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Data/AppDbContext.cs`, `Models/IntakeBatch.cs` (new), `Models/LocationZone.cs` (new),
`Services/InventoryService.cs`, `Views/Home/Index.cshtml`, `Views/Home/Intake.cshtml`
(new), `Views/Settings/Index.cshtml`, `Views/Shared/_Layout.cshtml`, migrations
`AddLocationZones` + `AddIntakeBatches` (migration 24 → 26).

---

## 2026-07-29

### `814a324` — pass 7 (managed Locations)
The location vocabulary lived in four hand-kept copies and had drifted — 34 variant
rows carried Sub codes no copy knew about, rendering as raw codes. One `Locations`
table now feeds all of them.
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Data/AppDbContext.cs`, `Models/Location.cs` (new), `Services/InventoryService.cs`,
`Services/LocationCodec.cs`, `Views/Home/Index.cshtml`, `Views/Home/MyOrders.cshtml`,
`Views/Settings/Index.cshtml`, migration `AddLocations`.

### `a3e20d9` — pass 7a (managed Teams)
`Teams` table with CRUD in Settings, Team made optional with `N/A`. Killed a ternary
that gave every future team Samurai's project code by accident. Group removed from
the registration form (became derived, not entered).
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Data/AppDbContext.cs`, `Models/Team.cs` (new), `Services/InventoryService.cs`,
`Services/OrderService.cs`, `Services/OrgStructure.cs`, `Views/Home/Index.cshtml`,
`Views/Settings/Index.cshtml`, migration `AddTeams`.

### `bcb48fb` — passes 3/4/5/6 (not before 6.2)
`CompressorUnit` became a roster rather than a pickup-time log (On Hand + departed
units, not just a log). `PickUpOrder` became match-or-create. Compressors became
loanable ("Done Using"); found `LoanableQuantity` was dead code duplicated inline —
editing the helper changed nothing until the call site was pointed at it.
**Touched:** `Controllers/HomeController.cs`, `Data/AppDbContext.cs`,
`Models/CompressorUnit.cs`, `Models/MyOrdersViewModel.cs`,
`Services/InventoryService.cs`, `Services/OrderService.cs`, `Views/Home/Index.cshtml`,
`Views/Home/MyOrders.cshtml`, migration `CompressorUnitRoster`.

---

## 2026-07-28

### `393e857` — rheempn na migration recommit due to not pass
Fixed/recommitted the `AllowNARheemPartNumber` migration after it didn't apply
cleanly the first time.
**Touched:** migration `AllowNARheemPartNumber` only.

---

## 2026-07-27

### `57752da` — adding/fixing migration history for compressor units (successful)
`CompressorUnit` table introduced — the roster's first version, before Pass 6 turned
it from a pickup log into a true on-hand roster.
**Touched:** `Controllers/HomeController.cs`, `Data/AppDbContext.cs`,
`Models/CompressorUnit.cs` (new), `Models/ViewModels/PendingOrderItemViewModel.cs`,
`Services/InventoryService.cs`, `Services/OrderService.cs`, `Views/Home/Index.cshtml`,
`Views/Home/PickupQueue.cshtml`, migration `AddCompressorUnits`.

### `43482c7` — ReSuperUser configs (Branch and Lines)
Original Branch/Line org structure introduced (`OrgStructure.cs`), plus `User.Line`/
`InventoryItem.Line` — this is the commit that first made visibility Line-based
instead of Team-based, the rule everything since has built on.
**Touched:** `Controllers/HomeController.cs`, `Controllers/SettingsController.cs`,
`Models/InventoryItem.cs`, `Models/User.cs`, `Services/CurrentUserService.cs`,
`Services/InventoryService.cs`, `Services/OrgStructure.cs` (new),
`Views/Home/Index.cshtml`, `Views/Settings/Index.cshtml`,
`Views/Shared/_Layout.cshtml`, migration `AddUserAndItemLine`.

### `938564e` — app settings pw set
Config-only change to the superuser passcode.
**Touched:** `appsettings.json`.

---

## 2026-07-23

### `8bbcde8` — n/a fix, enabled selectiol not disabling nagging / dashboard ux
Folded blank Rheem PNs to a shared `N/A` sentinel first-class value; UX fixes to the
dashboard's nagging/validation prompts. New `AllItems.cshtml` view.
**Touched:** `Controllers/HomeController.cs`, `Data/AppDbContext.cs`,
`Services/InventoryService.cs`, `Views/Home/AllItems.cshtml` (new),
`Views/Home/Index.cshtml`, migration `AllowNARheemPartNumber` (first version — see
the `393e857` recommit five days later).

### `2f0987a` — pass 5.1 — subscription-tiered services and modals
Per-user notification subscriptions (`NotificationSubscription` table), starting with
`PickupRequested`.
**Touched:** `Controllers/SettingsController.cs`, `Data/AppDbContext.cs`,
`Models/NotificationSubscription.cs` (new), `Program.cs`,
`Services/NotificationService.cs` (new), `Services/OrderService.cs`,
`Views/Settings/Index.cshtml`, migration `AddNotificationSubscriptions`.

### `7b79eae` — added "superusersettings" for myself to update access logic between other users
The Superuser gate introduced — a second, independent lock on the Settings area
(session name + passcode from `appsettings.json`), deliberately separate from
`AccessLevel` since Settings is what edits AccessLevel itself. First version of the
Settings page.
**Touched:** `Controllers/SettingsController.cs` (new), `Data/AppDbContext.cs`,
`Models/AppSetting.cs` (new), `Program.cs`,
`Services/RequireSuperuserAttribute.cs` (new),
`Services/SuperuserGateService.cs` (new), `Views/Settings/Index.cshtml` (new),
`Views/Settings/Unlock.cshtml` (new), `Views/Shared/_Layout.cshtml`,
`appsettings.json`, migration `AddAppSettings`.

---

## 2026-07-20

### `0a02b40` — Namespace changed "InventoryDevTwo" → "Visual_Inventory_System" | added Rheem PN functionality
Large housekeeping + feature commit: renamed the project namespace/csproj from the
original dev scaffold name to its real name, and added Rheem Part Number as a
primary identifier with nagging validation on the registration/edit forms.
**Touched:** project-wide namespace rename (all `Controllers/`, `Models/`,
`Services/`, `Views/`, every existing migration's namespace), plus new migration
`AddRheemPartNumber`. 75 files changed.

---

## 2026-07-14

### `bae87b7` — local host (non-publish v) change to update new all in one inventory host area
Config-only, host path update.
**Touched:** `appsettings.json`.

---

## 2026-07-13

### `e1f3bd2` — removed http-warning and copied inventory.db into published folder
Deployment lineage commit — noted in the addendum as the "first-publish no Users
table" fix, resolved by moving the database to an absolute, separate path so
republishing could never touch data again.
**Touched:** `Views/Home/Index.cshtml`, plus `.sqbpro` DB Browser sidecar files
(harmless, GUI-only artifacts).

---

## 2026-07-10

### `a20e92a` — Remove DB Browser workspace files
Cleanup — removed `.sqbpro` sidecar files from git (DB Browser session state, never
touched by the app or EF Core).
**Touched:** 3 `.sqbpro` files removed.

### `397d739` — Add project files.
**Initial commit.** Full ASP.NET Core MVC scaffold — the whole app as it existed at
project start: all Controllers/Models/Services/Views, the first 15 migrations, wwwroot
assets (Bootstrap, jQuery, jQuery Validation), solution/project files. 149 files,
~96,200 lines.
**Touched:** everything — this is the starting point every later commit above builds on.
