# VIS (Visual Inventory System) — Handoff State

**Supersedes the July 2026 handoff (stopped at Pass 4) and the Pass 9 handoff below it.**
Current as of the 2026-08-11 Claude Code session (Passes 10–17). I went live against
my real copied-in database during this window, then followed up the same stretch with
data cleanup, an access-control pass, the Branches/Lines redesign, a round of UI/data
polish, mandatory-Line registration + Line-scoped log visibility, an Unclaimed-filter
fix, a bulk Line reconciliation, whole-Branch assignment at user creation, User.Team's
move to many-to-many, and most recently serial/TC capture at registration and intake
plus a real fix to the Bulk Intake "hold for approval" path, which turned out to have
never actually worked — all below. The Pass 13/14 db (with all its data work) has now
actually been copied to the host and is what I'm testing
against going forward.

I'm the owner (Rheem). Built on ASP.NET Core MVC (`net10.0`), EF Core + SQLite, Razor,
Bootstrap 5 dark theme. Session-based name-only "identify" (no password) plus a numeric
`AccessLevel` (1 Viewer – 5 Admin) via `[RequireLevel(x)]`.

**Delivery model changed starting with Pass 10.** Every pass before that shipped as a
folder-structured zip I copy-merged into my own VS project and compiled/migrated/tested
myself — Claude never ran the app. From Pass 10 on I've been running Claude Code (a CLI
agent with real Bash/PowerShell, a `dotnet` toolchain, and a browser-automation tool)
directly in my repo:
- Edits files in place (no zip).
- Runs `dotnet build` after every real chunk of work, not just at the end.
- Generates every migration with `dotnet ef migrations add` — the old "no dotnet SDK in
  sandbox, hand-author every migration" constraint no longer applies in this environment.
- Starts the actual app and drives it through a real browser tab — clicking, filling
  forms, reading the DOM, checking console for errors, reading the server's live SQL
  log — against my real, copied-in `inventory.db`, not a fixture.
- Commits/pushes only once I've explicitly said to.
See "What was actually verified live" near the end of this file before trusting any
claim above that a feature "works" — some things were confirmed this way, some were
only confirmed by static reading and a clean build, and the difference is spelled out.

---

## Deployed state

```
Migrations       35   (latest: 20260812023317_AddIntakeRowMultiSerialAndTc)
Items           487   (compressor ownership reconciled against real claim sheets;
                 leftover test-fixture rows removed; one exact duplicate merged;
                 63 Residential OD compressors re-minted with an RCR- prefix and
                 Group flipped to Residential -- see Pass 14)
Compressor units count evolving -- On Hand roster is directly editable, not just
                 a pickup-time log
Motor units      TC only, seeded from real pickups
Locations        18   Parent/Major/Sub, managed
Map zones         4   seeded, editable
Teams             8   Samurai, Ninja, Falcon, Polaris, Hurricane, T-Rex, Spartan,
                 Sustaining (all 7 real names now exist as rows, plus Sustaining)
Branches          3   Residential Air, Commercial Air, Sustaining -- now managed
                 in Settings, not hardcoded (see Pass 13)
Lines            10   managed alongside Branches, same place
Users            51   (9 added in Pass 14 -- see Pass log)
```

Publishes **self-contained** (`-r win-x64 --self-contained`) to `C:\VIS_Publish` on the
host laptop, started with `--urls "http://0.0.0.0:5000"`. Database lives at
`C:\VIS_Inventory\inventory.db` — an absolute path in `appsettings.json`, so it is
**not** carried by publish and must be moved deliberately.

**Migrations apply automatically on startup** (`Program.cs` calls `Migrate()`) — no
manual step needed. All migrations through Pass 13 (`AddOrderItemStatus`,
`RenameLineVocabulary`, `AddTeamLine`, `AddMotorUnits`, `AddUserBranch`,
`AddTransactionLogItemName`, `AddBranchesAndLines`) were confirmed to apply cleanly
against my actual copied-in database, with no `PendingModelChangesWarning`.

---

## Working conventions

- Read every file fully before editing. No blind edits.
- **Claude Code sessions (Pass 10 on):** edit in place, `dotnet build` after every real
  chunk, `dotnet ef migrations add` for schema changes, test live against the real app
  and real db when possible, `git commit`/`push` only when I explicitly say to.
- **Older chat-based sessions (Pass 9 and earlier):** delivered as folder-structured
  zip (path-preserving), listed changed/new files one line each, hand-authored EF
  migrations by mirroring existing migration file structure and regenerating
  `AppDbContextModelSnapshot.cs`/`*.Designer.cs` by hand. If a session ever loses shell
  access, that constraint is back — use this pattern.
- Verify brace/paren balance and diff against prior delivered state before packaging,
  if hand-authoring.
- Caution before schema migrations / breaking changes; proceed on routine work.
- **"Spiterate before building"** — my own term for how I want a genuinely new mechanic
  handled: write back the complete scope in prose (every field, every gate, every edge
  case) and wait for my explicit confirmation before opening a file. Used successfully
  for the short-pull flow, Team.Line, MotorUnit, the Sustaining branch, Delete Item,
  and the Branches/Lines redesign.
- Tone: direct, low-formatting, no unneeded explanation.

---

## Architecture — load-bearing facts

**`InventoryItem` = family identity. `ItemVariant` = per-location physical stock.**
`InventoryItem.Quantity` and the location fields are `[NotMapped]` pass-throughs over
variants — **never usable inside raw EF LINQ queries**, only after materialization.
18 models legitimately have two variants (stocked at both New Test Cells and the Lean-To).

**Visibility is Line, not Team.** Signed-in user sees items where
`item.Line == "" || item.Line == user.Line` (case-insensitive). **Level 5 bypasses
entirely.** A blank *item* Line fails OPEN for everyone. A blank *user* Line is a
separate, stronger case: it skips the filter entirely and that user sees the whole org,
not just blank-Line items — worth knowing before assuming "unassigned" means "scoped to
nothing." (Pass 13 added a middle option — see `User.Branch` below.)
`InventoryService.ApplyLineVisibility()` filters `GetAll`/`Search`/`ExportToCsv` only;
`GetById`/`FindByRheemPart`/quantity math stay unfiltered on purpose so org-wide dedup
and cross-Line reassignment keep working.

**`OrgStructure.cs` is managed now, not hardcoded (Pass 13).** Branches and Lines live
in `Branches`/`OrgLines` tables, editable from Settings the same way Teams and Locations
already were. `OrgStructure` itself stays a static class every other file reads from —
it's a DB-backed snapshot refreshed at startup (`Program.cs`) and after any Settings
Branch/Line edit (`SettingsController.RefreshOrgStructure()`), same pattern
`LocationCodec` already used for Locations. `BranchFor`/`GroupFor`/`IsValidLine`/
`AllLines`/`BranchLines` are all unchanged as a public surface — nothing that reads them
had to change. Branch is still **derived**, never stored on an item — see Group below.
(While rebuilding this I found `LocationCodec` itself never actually got a startup
refresh despite the comment claiming it did — added a real one for both.)

**Group is derived and frozen at creation.** `OrgStructure.GroupFor(user.Line)` maps a
Branch to its Group word by stripping a trailing `" Air"` (`Commercial Air → Commercial`,
`Residential Air → Residential`); a Branch that doesn't end in `" Air"` (like
`Sustaining`) passes through as-is. Blank/unrecognized Line falls back to `Commercial`.
It exists only to mint the `ItemId` prefix, and records *who registered the item*. Not a
form field, not editable afterwards — changing it would put an item's Group at odds with
its own id.

**`LocationCodec.Encode()` is the only thing that turns a name into a code** — 1st, 3rd,
5th and last alphanumeric. Codes are **derived, never stored**. `Decode` reads a volatile
map pushed in by `LocationCodec.Refresh()` at startup (`Program.cs`) and after any
Settings edit.

**`CompressorUnit` is a roster, not a pickup log** (changed in 6A). Holds On Hand stock as
well as units that have left. **Quantity remains authoritative; units are a partial
overlay** — most units don't have serials, so unit rows must never drive counts. Unique
index is `(ItemId, SerialNumber)`, blanks exempt: LG reuses serials *across* models, so
serial alone can't be unique.

**Superuser gate** is separate from `AccessLevel`: session name matches the configured
username **and** the passcode from `appsettings.json`. "Master key not stored in any
lockbox it opens." Admin (5) alone does **not** unlock Settings — the two are
deliberately independent, since Settings is what edits AccessLevel itself.

**Pickup:** `OrderService.PickUpOrder()` (not "FulfillOrder"). Pulls from the
**lowest-numbered variant first**, which has physical consequences — an order can send
someone to two buildings.

**A short pull refuses, it doesn't silently under-fulfill (Pass 10).** `PickUpOrder`
checks real shelf stock per line before pulling anything; a line that can't be honored
gets `OrderItem.Status = "Cancelled"` and is returned to the caller as a `ShortPullLine`
instead of pulling what's there. `ReportShortPull` is the correction: the picker reports
the true count per location, stock gets corrected (logged as `"Stock Adjustment"`, not a
pickup), and a fresh order is issued for the corrected qty and picked up in the same
action.

**`CompressorUnit` and `MotorUnit` are two entrances now, not one (Pass 11–12).** Both
used to be created only at pickup. Both modals now also let a human log/correct a unit
directly — `InventoryService.LogCompressorUnits`/`LogMotorUnits` — for stock that's on
the shelf and was simply never recorded. `MotorUnit` is `CompressorUnit`'s sibling, not a
generalization of it (deliberately kept separate): no `SerialNumber`, just an optional
`LabNumber`, and — unlike `CompressorUnit` — a Picked-Up `MotorUnit` row stays editable,
since there's no serial to fall back on for identity.

**`Team.Line` and `User.Branch` are pure metadata, never a visibility gate (Pass 11,
13).** A team can carry a home Branch/Line; a user can carry either a specific `Line` or
an entire `Branch` (new in Pass 13, for someone who needs broader-than-one-Line
visibility without the full Admin bypass — e.g. a director who oversees a whole Branch).
Both only *suggest* — Line on the item/user is what actually governs visibility, and
neither of these fields touches that.

**`ModifyStock`'s Adjustment action can reclassify existing stock as TC (Pass 12)** —
"take my current N units, mark X of those as thermocoupled," independent of whatever
quantity delta the same adjustment makes. Distinct from Add's "these NEW units include X
TC."

**Delete Item is the app's first true hard-delete (Pass 13).** Available only once an
item's total quantity is 0 and nothing is still outstanding — no unit still `Picked Up`
on a `CompressorUnit`/`MotorUnit`, no line on a `Pending` order. Removes the
`InventoryItem` and its `ItemVariants`. Deliberately does **not** touch `TransactionLogs`
or already-`Returned`/`Scrapped` unit history rows — those keep reading under the
now-defunct `ItemId`, same soft-hide philosophy as Team/User, just applied to a real
delete for the first time. Re-registering the same model later mints a fresh `ItemId`;
numbers are never reused. Gated Admin (5). Pass 23 added a second, narrower hard-delete
alongside this one — see `DeleteVariant` below — for the case Delete Item doesn't cover:
one empty stack on an item that still carries stock elsewhere.

**`TransactionLog.ItemName` is a point-in-time snapshot (Pass 13).** The Activity Feed's
primary lookup is still a live join against `InventoryItems` (so a rename shows up
retroactively), but that join returns nothing for a deleted item. This column is the
fallback for exactly that case — populated at every item-transaction log site going
forward; logs written before this existed just fall back further, to "Unknown item."

**The Activity Feed reads a blank `TransactionLog.ItemId` as the real discriminator for
"this isn't about an inventory item" (Pass 14).** Every Settings/admin action (users,
teams, branches, lines, locations, zones, notifications — 20 log sites) is logged with
`ItemId = ""` on purpose, since none of them are about one. The feed used to assume
every log row was an item action and tried to look one up regardless, which rendered as
`": Unknown item"` for all of these. Fixed by branching on whether `ItemId` is blank:
item actions keep the existing item-name-lookup path, non-item actions just show their
own `ActionType` as the title and `Details` as the subtitle — the same thing View Logs
already did correctly, just brought over. This is deliberately **not** a per-ActionType
ternary — a brand new Settings action reads correctly here automatically the moment it's
added, with nothing else to update.

**A user's effective Branch, for UI purposes, is: their `Line`'s Branch if set, else
their `Branch` field directly, else blank (Pass 14).** `ViewBag.MyBranch` in
`HomeController.Index` computes this once per request. It currently only drives graying
out the two Quick Filter branch buttons a user isn't on (Admin sees all three, same as
Luis/Derek/me) — it is **not** a visibility mechanism, `ApplyLineVisibility` is still the
only thing that actually gates data.

**Line is mandatory on new registrations as of Pass 15 — but only new ones.** Both
`CreateItem` and `SubmitIntake`/Intake now reject a blank `Line` server-side (and the
form fields carry `required` client-side too). This does **not** retroactively touch the
~180+ items already sitting blank across Motors/EEVs/Coils/etc. — the "blank Line fails
open" default from Pass 3 is still exactly as true for everything already in the system.
This just closes the door going forward.

**`InventoryService.ApplyLogVisibility()` extends the same Line rule to `TransactionLog`
rows (Pass 15).** View Logs and the dashboard Activity Feed used to show every log entry
to every signed-in user regardless of Line — a strictly wider audience than could even
see those items in Search. Built on top of the existing (private) `ApplyLineVisibility`
rather than duplicating its logic: a log row is visible if it isn't about an item at all
(blank `ItemId` — every Settings/admin action), if the item it names has since been
deleted (nothing left to check a Line against — same "keeps reading sensibly" treatment
Delete Item already gives these), or if the item it names is one the viewer can currently
see. Verified live: Admin (me) saw 578 rows on View Logs, a user scoped to a single Line
(Cedric Martis, Residential Coils/AH) saw 349. **Deliberately does not touch direct item
actions** (Modify Stock, Delete Item, Ownership, etc.) — those still use the unfiltered
`GetById`, unchanged, so someone who knows an `ItemId` can still act on it even if they
can no longer see it in Search or Logs.

**"Unclaimed" on the Compressor/Motor modals means missing Team OR missing Line, not
AND (Pass 16).** The original definition required both blank simultaneously, which
after Pass 13's reconciliation could never be true again — every compressor item came
out of that pass with at least one of the two set (117 Team-blank/Line-set, 40
Team-set/Line-blank, zero fully blank), so the checkbox silently showed "0 of 245."
Motors were worse: all 124 motor-type items have Team set and 100% have Line blank
(never touched by Line reconciliation at all), so they were *entirely* invisible under
the old AND logic despite being the biggest gap in the system. Fixed at both call
sites (`Views/Home/Index.cshtml`'s `isUnclaimed`/`isUnclaimedMotor`).

**All 242 non-compressor items reconciled onto `Line = "Commercial Packaged/Splits"`
(Pass 16, data-only).** Every Motor/EEV/Coil/Control/etc. item was already Team-tagged
Samurai or Ninja and Group-tagged Commercial with a blank Line — this is genuinely all
Commercial Packaged/Splits stock (Samurai and Ninja's own home Line), not a guess.
Direct SQL against the live db, same pattern as the Pass 13/14 reconciliations.
Compressors were explicitly excluded (`WHERE Type <> 'Compressor'`) and confirmed
untouched.

**Add User now supports whole-Branch assignment at creation, not just after (Pass
16).** The `__WHOLE_BRANCH__` sentinel and Branch/Line cascade already existed for the
per-row `UpdateLine` picker (how Karthig got scoped to all of Commercial Air); the Add
User form's Branch/Line `<select>`s were already wired to the same JS cascade
(`wireLinePicker(addUserForm)`) but the Branch `<select>` had no `name="branch"` so it
never posted, and `AddUser()` didn't accept the parameter at all. Both fixed to mirror
`UpdateLine` exactly. Verified live end-to-end: a test user picked as "Commercial Air /
— Entire Branch —" landed with `Branch = "Commercial Air"`, `Line = NULL` in the real
db, same shape as Karthig's row.

**`User.Team` is now many-to-many via a new `UserTeams` table, not a single string
(Pass 16).** A user can belong to several teams at once. Managed *team-centric*, not
user-centric, per how it's actually used: Settings' "Team Membership" picker (in the
Teams & Projects card) shows a dropdown of active teams, picking one lists every user
with a checkbox, Save diffs and applies adds/removes in one action, logged as
`"Team Membership Changed"`. `UserTeams.TeamName` is a plain string, not a
`Team.Id` foreign key — same vocabulary-table convention as every other Team/Line/
Branch reference in this app (`InventoryItem.Team`, `User.Line`, `Team.Line`), so
hiding or (hypothetically) renaming a team behaves the same way it already does
everywhere else. The Add User form's free-text "Team (optional)" field is gone — it
was the one place in the app where a Team value could actually be typed instead of
picked from the managed list, and team assignment now happens through the
team-centric picker after a user exists, not at creation. `User.Team` was confirmed
dead everywhere except display (Settings Users table) and one real consumer,
`SetDefaultThreshold` (bulk `AlertThreshold` setter, scoped to "my team's items"),
which was generalized to "my teams' items" (a list, `Contains` instead of `==`) rather
than left broken. Migration (`AddUserTeams`) carries forward every existing
single-Team value before dropping the column — verified live against the real db:
Conner Walworth (Samurai) and James Masters (Ninja), the only two users who had a
Team set, both landed correctly in `UserTeams` with zero data loss. Add/remove tested
live through the actual Settings UI (add Kason to Samurai, confirm, remove, confirm
back to original state) with `TransactionLog` entries checked after each.

---

## Pass log

**Passes 1–4** — see git history. Superuser Settings, add/remove users,
`NotificationSubscription`, Rheem PN `N/A` as first-class, Branch/Line org structure,
compressor serial capture at pickup.

**Pass 5 — the three stacked bugs.** Migrations had silently failed three times.
Root cause was `PendingModelChangesWarning`: `CompressorUnit.ItemId`'s index was in the
snapshot but not configured in `OnModelCreating`, so `Migrate()` aborted before running
any SQL. Behind it, `AllowNARheemPartNumber` folded blanks to `N/A` before dropping the
old unique index — `UNIQUE constraint failed` on the second row. Both were swallowed by
`Program.cs`. Fixed the model drift, reordered the migration, and normalised five
placeholder PNs that only existed as workarounds for the index that never got relaxed.

**Compressor rebuild (SQL, not a code pass).** Wiped 75 compressors / 84 variants / 76
logs / Order 3. Loaded 85 models / 189 units at New Test Cells, then 177 models / 636
units at the Lean-To. Reconciliation of five notebooks plus an intern's PDF used
max-on-overlap within the bottom-row group rather than summing — my own "the two halves
should be even" test put the halves at 320 vs 313.

**Pass 6A — CompressorUnit roster.** `+ItemVariantId`, `+Status`, `+RecordedAt/By`;
`OrderId`/`PickedUpAt`/`PickedUpBy` nullable. Unique `(ItemId, SerialNumber)`.
`PickUpOrder` became match-or-create — mandatory, not optional.

**Pass 6B — Done Using.** Compressors became loanable. `LoanableQuantity` was dead code
duplicated inline in `PickUpOrder`. `LoanOutstanding` is reused for compressors meaning
"not yet dispositioned" — deliberate naming debt, one counter that can't drift beats two
that must be kept in sync. Reason field added to Return and Scrap for all loanable types.

**Pass 7A — managed Teams.** `Teams` table, CRUD in Settings, Team optional with `N/A`.
Killed a ternary that gave every future team Samurai's project code. Group removed from
the registration form.

**Pass 7B — Transfer means Line.** Deleted a flattened, wrong copy of `OrgStructure` that
lived in the "New Group" picker. Branch/Line moved out of Edit Details into Transfer,
where ownership belongs. Edit Details is identity only again.

**Pass 7C — managed Locations.** The vocabulary lived in four hand-kept copies and had
drifted. One `Locations` table now feeds all of them.

**Pass 8 — map zones.** `LocationZones` table with normalised 0–1 coordinates. Drag-to-draw
editor in Settings.

**Pass 9 — bulk intake.** `Intake` screen, Standard+. `IntakeBatches`/`IntakeRows` hold
batches whose location isn't recognised. `CreateItem` and the five ordering actions
moved Engineer → Standard so interns can register and order.

**Pass 10 — short-pull flow, compressor filter, ItemId branch fix.** See "A short pull
refuses" above. Compressor modal got its first filter bar. Fixed `ItemId`'s Branch
letter defaulting to Commercial regardless of what was picked — it was deriving from the
registrant's own account Line rather than the Branch/Line actually selected on the form.

**Pass 11 — compressor on-hand logging, Team.Line, Line vocabulary rename, shared
cascade helper.** See "Two entrances now" and "`Team.Line`" above. Extracted a
Branch→Line cascade that had been hand-copied three times into one shared helper set.
Fixed a real bug I caught myself before testing found it: moving the Ownership pane's
cascade wiring to run at page load instead of lazily inside an event handler put it
ahead of `const orgStructure`'s own declaration further down the same script — a JS
temporal-dead-zone error that silently killed every initializer after it.

**Pass 12 — TC motor tracking, Adjustment TC reclassification, filter/UX follow-ups.**
See "Two entrances now" and "Adjustment action" above. Motors modal gets the same filter
bar as Compressors; New Item Registry gained a live, non-gating name-match dropdown;
Order Details gained a Brand column.

**Pass 13 (2026-08-06/07) — go-live data reconciliation, access-control pass,
Delete Item, Branches/Lines redesign.** The big one. In order:

- **Compressor Team/Line reconciliation against real claim sheets.** Cross-referenced
  the live db against an export wizard file (Residential OD claims by name) and a
  prefix/suffix reference sheet (Ninja vs. Samurai model families) covering 249
  compressor items. Assigned 63 to Residential OD, 8 to Ninja by exact model match, 53
  confirmed Samurai by base-model-prefix match, and 55 more that share the Ninja-shaped
  prefix pattern but aren't on the exact reference list — those got the correct Line
  with Team deliberately left unassigned rather than guessed, since some may belong to
  a third team (Spartan) that also sits on the same Line. ~70 items in genuinely
  different brand families were left untouched entirely — no guesses where the data
  didn't support one.
- **Data cleanup.** Removed 4 leftover test-fixture items (`TESTCOMP-A/B/C`, "screen
  test coil") along with their full dependent chain — variants, compressor units,
  transaction logs, and the now-empty test orders that existed only for them. Found and
  merged the app's one exact duplicate registration (`YRM083TAA`, registered twice by
  accident, both halves zeroed out mid-fix) back into a single record with the correct
  combined count.
- **Sustaining branch added.** A third Branch beyond the original fixed two, with 4
  Lines (each carrying its own project code in the name, since Lines have no separate
  code field) and 4 matching Teams.
- **Access-control audit and fixes.** Catalogued what every AccessLevel tier actually
  unlocks, end to end, including inline checks that don't match their route attribute.
  Found and fixed a real bug: `CreateItem`/`AddToCart`/`StartOrder` were server-gated at
  Standard (intentionally, since Pass 9) but the UI hid their buttons until Engineer —
  interns were authorized but had no way to actually use it. Renamed the confusingly-
  named `SystemReset` action to `ClearCart` and dropped its gate from Admin to Standard
  to match what it's always actually done (wipe the calling user's own session cart,
  nothing system-wide).
- **`User.Branch` added.** A user can now be scoped to an entire Branch instead of one
  Line, for people who need broader visibility than a single Line but shouldn't have
  full Admin bypass.
- **Delete Item shipped.** See Architecture above.
- **`TransactionLog.ItemName` added.** See Architecture above — this was found and
  fixed as a direct consequence of Delete Item breaking the Activity Feed's live-lookup
  assumption.
- **Branches and Lines are managed vocabulary now, not hardcoded.** See Architecture
  above. Verified live: added a real test Branch and Line through the running Settings
  UI, confirmed it appeared in every picker across the app immediately with no restart,
  then removed it.

**Pass 14 (2026-08-07) — RCR rename, Quick Filter/access polish, Activity Feed fix,
roster growth.**

- **63 Residential OD compressors re-minted `CCR-` → `RCR-0001`...`RCR-0063`,
  `Group` flipped `Commercial` → `Residential`.** These were the compressors reconciled
  onto Residential OD in Pass 13 but still carrying a `CCR-` id and `Group="Commercial"`
  frozen from their original (wrong) registration — a real rename, not a delete/re-add:
  cascaded to the 63 `TransactionLogs` rows and 3 `CompressorUnits` rows that referenced
  the old id, zero stale references left anywhere afterward. This is the one deliberate
  exception to "Group is frozen forever" in this project's history — done because these
  63 were never actually Commercial to begin with, not because a later reassignment
  should retroactively rewrite history in general.
- **Sustaining added as a third Quick Filter button**, alongside the existing
  Commercial/Residential (still Omni-Search-based, matching the existing mechanism —
  worth knowing this reads `Group`/other text fields, not `Line`, same caveat as those
  two already had).
- **Quick Filter branch buttons gray out for the two Branches a user isn't on**, via
  `ViewBag.MyBranch` — see Architecture above. Admin bypasses.
- **"Pick Up Orders" dashboard widget renamed to "Available Tasks."** Flagged, not
  renamed, for a later pass: the `PickupQueue` action/route/view filename, the
  `pickup-box` CSS class, and — worth resolving — that the destination page's own
  heading already reads "Tasks Available" (reverse word order from the widget's new
  "Available Tasks").
- **Activity Feed now formats every Settings/admin action correctly.** See
  Architecture above.
- **9 users added:** Cedric Martis (Engineer, Residential Coils/AH); Nathan Gibson
  (Standard), Jacob Moffett, Chase Binz, Mohamed Elrifae, Ethan Phan, Luis Fragoso,
  Marco Balcazar (Engineer), Travis Gregory (Management) — all eight to International
  (Commercial Air).

**Pass 15 (2026-08-07) — mandatory Line at registration, Line-scoped log visibility.**

- **Line is now required to register an item**, both single-item (`CreateItem`) and
  bulk (`Intake`/`SubmitIntake`) — server-side rejection plus a client-side `required`
  field on both forms. See Architecture above for exactly what this does and doesn't
  cover (new registrations only, nothing retroactive).
- **View Logs and the Activity Feed are now Line-scoped**, via the new
  `InventoryService.ApplyLogVisibility()`. See Architecture above for the full rule.
  Verified live with a real before/after comparison across an Admin and a Line-scoped
  user, not just a clean build.

**Pass 16 (2026-08-10) — Unclaimed filter fix, bulk Line reconciliation, whole-Branch
at creation, User.Team → many-to-many.**

- **Compressor/Motor "Unclaimed only" fixed from AND to OR.** Was showing "0 of 245"
  for compressors and hiding all 124 motor items outright. See Architecture above.
- **242 non-compressor items bulk-set to `Line = "Commercial Packaged/Splits"`**, direct
  SQL against the live db, compressors explicitly excluded and confirmed untouched. See
  Architecture above.
- **Add User supports whole-Branch assignment at creation**, mirroring the per-row
  `UpdateLine` picker's existing `__WHOLE_BRANCH__` sentinel. See Architecture above.
- **`User.Team` rebuilt as many-to-many (`UserTeams` table) with a team-centric
  membership picker in Settings**, replacing the single string field (which had no UI
  path to set it at all) and the free-text "Team (optional)" field on Add User. See
  Architecture above for the full mechanism, migration behavior, and what was verified
  live.
- New migration: `AddUserTeams` (34th). Applied cleanly against the real db, no
  `PendingModelChangesWarning`.
- **Dropped the "Who can see this item. Required as of Pass 15." subtext** under New
  Item Registry's Line field — the red asterisk already carries that meaning; the note
  was redundant clutter a full pass after Line actually became mandatory.
- **Location Transfer's cascade fixed to source from the `Locations` table, not
  existing stock.** Modify Stock's location picker was built by walking `allItems`'
  already-stored Parent/Major/Sub codes (`Views/Home/Index.cshtml`'s `locHierarchy`),
  so a Sub added in Settings with zero stock in it could never be picked as a transfer
  destination — New Item Registry/Intake never had this problem because their cascade
  (`BuildLocationTree()`) already reads straight from `Locations`. New
  `HomeController.BuildLocationHierarchyCoded()` mirrors `BuildLocationTree()` exactly,
  just code-keyed instead of name-keyed (since `ItemVariant.Parent/Major/Sub` store
  codes). This is the ninth instance of the "one more hardcoded/independently-sourced
  copy of the location vocabulary" pattern this project keeps finding. Verified live:
  added a real zero-stock test Sub, confirmed it appeared in the transfer cascade data
  immediately, removed it.
- **5 real compressors registered** (Samurai: YGH137W ×4, YGH137T ×2, YGH137R ×4;
  Hitachi DC80PHDG-D1Y2 ×2 with no Team, Line = International), all at Plant Test
  Cells/Lean-To. One registration (YGH182W) turned out to already exist as `CCR-0003`
  — caught via a post-registration duplicate-ItemName check (the pre-check had only
  covered Rheem PN, which is "N/A" on this whole family), fixed by moving the 2 units
  onto `CCR-0003` as its second variant (same "stocked at two locations" shape 18 other
  models already use) and deleting the erroneous duplicate registration, with a
  corrected `TransactionLog` entry in its place.
- **New Item Intake Excel template** built for other teams to submit stock lists —
  dropdowns for Line/Team/Type/Location sourced live from the real Teams/OrgLines/
  Locations tables at generation time, required fields (Item Name, Quantity, Line,
  Rheem PN) visually marked. Standalone file, not wired into the app — intended to be
  bulk-loaded by hand (same as the compressor registrations) or typed through Bulk
  Intake once filled in.

**Pass 17 (2026-08-11) — serial/TC capture at registration and intake, Bulk Intake's
hold-for-approval path fixed (it never actually worked).**

- **Compressor serial capture and motor TC-count declaration now live on both New
  Item Registry and Bulk Intake, not just the post-hoc Log Units entrance.** Type =
  Compressor grows one optional serial box per unit of Quantity; Type ending in
  "Motor" shows an optional "Are any of these thermocoupled?" checkbox that reveals a
  capped "how many" count. Neither is required — both are opportunities the form
  surfaces automatically, same "nudge, don't block" rule as everything else touching
  Line/RPN this project has shipped. Backend-wise this is almost entirely reuse: the
  serial path generalizes the `LogIntakeSerial` helper Bulk Intake already had (one
  serial → a list, looped) into `LogIntakeSerials`, shared by `CreateItem` and both
  `CommitIntake` branches; the TC path is new (`LogIntakeThermocoupled`) but mirrors
  `PickUpOrder`'s existing pattern for untracked TC motor stock exactly — sets
  `ItemVariant.ThermocoupledQty` and mints that many blank (no lab yet) `MotorUnit`
  rows, so a later trip to the Motor modal's Log Units entrance has real rows to
  attach a lab number to instead of nothing. **Lab # is deliberately not asked for at
  either entrance, for either type** — that stays a "later in its life" question,
  answered through the roster that already exists for it.
- **`IntakeRow` (the held-batch table) widened to match** — `SerialNumber` (single
  string) replaced with `Serials` (comma-joined, multiple) and a new
  `ThermocoupledQty` column, so a batch that ends up held because its location isn't
  recognized yet keeps everything typed, not just the first serial. New migration:
  `AddIntakeRowMultiSerialAndTc` (35th). `IntakeRows` was empty at migration time, so
  no data-preservation step was needed.
- **Real bug found and fixed: Bulk Intake's "hold for an unrecognized location" path
  had never actually worked.** The Parent `<select>`'s "Not listed..." option posts
  the literal string `"__NEW__"`, not blank — but the server-side hold check only
  tested for blank. Since the "what do you call this place?" box only becomes
  typeable once `"__NEW__"` is selected in the first place, the real-world condition
  (`requestedLocation` filled in, `parentCode` blank) could never actually occur
  through the UI — every attempt fell straight through to a direct commit with the
  literal string `"__NEW__"` stored as the item's location (`Parent`, `FdaString`,
  visible as `"_NEW_"` in Modify Stock's Current Location and the variant's location
  tag). Fixed by recognizing `"__NEW__"` as unresolved alongside blank, plus a new
  guard that refuses outright (rather than falling through) if "Not listed..." was
  picked but nothing was typed for it. Pre-existing, not introduced by this session —
  first actually exercised, and caught, while testing the serial/TC feature above.
  One real item (`CCR-0255`) got created with the corrupted location during testing;
  deleted along with its log entry.
- **Small nudge added to `ApproveIntake`:** approving a held batch by creating a
  brand-new **Parent**-level location (not Major/Sub — those don't get their own map
  zone, only Parents do) now appends a reminder to the success toast that the new
  location has no map zone yet and stays unclickable on the facility map until one's
  drawn. Not required, matches the same optional-nudge shape as everything else this
  pass.

**Pass 18 (2026-08-12) — cancelled-order pickup race closed.** Worktree session
(`claude/sad-shtern-1b1145`), single-file fix in `Services/OrderService.cs`.

- **`PickUpOrder` now rejects any non-Pending order, not just Completed.**
  Previously the only guard was `Status == "Completed"`, and `CancelPersistedOrder`
  leaves an order's lines at `"Pending"` — so a runner holding a stale Pickup Queue
  page could still post `PickUpOrderConfirmed` after an Engineer cancelled: real
  stock left the shelf for every still-Pending line, and the order's status was
  overwritten `Cancelled` → `Completed`, erasing the cancellation. New guard:
  `order.Status != "Pending"` throws "Order #N was cancelled and can no longer be
  picked up," after the existing "Already completed." check.
- **`CancelPersistedOrder` deliberately still leaves its lines `"Pending"`.**
  Considered marking them `"Cancelled"` for consistency and rejected it: line-level
  `"Cancelled"` specifically means "came up short at pickup" — it's
  `ReportShortPull`'s eligibility guard (a stock-adjusting correction + reissue) and
  `OrderDetails`' "Short — reissued separately" badge. An all-lines-short pickup
  already legitimately produces `Order.Status = "Cancelled"` with `"Cancelled"`
  lines that ReportShortPull then corrects; marking an engineer-cancelled order's
  lines the same way would make the two cases indistinguishable and open cancelled
  orders to short-pull corrections. Nothing else reads line status on a cancelled
  order — the pending-allocation math (`GetAvailableQuantity`) and Delete Item's
  outstanding-order gate both key off `Order.Status`, so a cancelled order already
  releases its allocation regardless of line status.

**Pass 19 (2026-08-12) — three more worktree-session fixes, merged same batch as
Pass 18.** All four Pass 18/19 branches were spawned from the same round of chip
suggestions and merged into master together (`04de9e9`); this is the writeup for
the three that weren't Pass 18 itself.

- **`ReportShortPull`'s reissue was dropping TC count and requested location.**
  The fresh `OrderItem` it creates for the corrected quantity copied only `ItemId`
  and `Quantity` from the original line — not `ThermocoupledCount` or
  `RequestedVariantId`. For a short-pulled TC motor line this meant the immediate
  re-pickup ran with TC = 0: no TC stock deliberately drawn down, no `MotorUnit`
  rows flipped to Picked Up, and — since `LoanableQuantity(motor, qty, 0)` is 0 —
  **no loan created at all**, so the TC motors left the shelf with nothing
  expecting them back. Fixed by copying both fields onto the reissue, TC clamped
  to the corrected quantity (which can be lower than what was originally ordered).
- **`ReturnLoan` was storing `ItemVariantId = 0`** on units returned to a
  freshly-minted variant (the "no target location picked, mint a new one" path).
  `dest` was added to `inv.Variants` but never saved before `cu.ItemVariantId =
  dest.Id` / `mu.ItemVariantId = dest.Id` ran, so `dest.Id` was still 0 at that
  point — and since neither `CompressorUnit.ItemVariantId` nor
  `MotorUnit.ItemVariantId` carries an FK/navigation (see Architecture above), EF
  did no fixup and silently wrote the dangling 0. Any unit returned to a
  brand-new location lost its "which shelf is this exact unit on" link. Fixed
  with one `_db.SaveChanges()` right after `dest` is added, same pattern
  `CreateItem` already uses for the identical need.
- **Three misleading user-facing messages, all in `Services/InventoryService.cs`
  / the two controllers, none touching real correctness — just what got said
  about it:**
  - `ModifyStock`'s Scrap branch logged (and toasted) the *requested* scrap
    quantity even when clamped down to what was actually on the shelf —
    `ExportToCsv`'s `ScrappedQty` column sums these logs, so exports overstated
    too. Now logs the clamped amount, with a note when a clamp happened.
  - An unrecognized Line on an Ownership move used to return the success-shaped
    tuple with nothing changed/logged/saved — the controller toasted "applied"
    over a silent no-op. Now throws, which the controller's existing `catch`
    turns into a real error toast.
  - A partial Bulk Intake failure (`CommitIntake` saves per row, so
    non-erroring rows land even when others fail) said "Nothing was imported"
    at both call sites (`SubmitIntake`, `ApproveIntake`). Now distinguishes
    total failure from partial success and says how many rows/units actually
    landed.

**Pass 20 (2026-08-12/13) — map cleanup, Intake Team autofill, Registry
name-match jumps into Modify Stock with serial capture on Add.**

- **Facility map: removed the glowing red tracker dot** (element, CSS, the
  9-waypoint path, the 2.5s `setInterval` loop) **and made zone row-counts
  hover-only.** The dot was decorative and unrelated to real data; the "N Rows"
  label under each pin now shows only on `:hover` (`.map-zone:hover
  .marker-count`) so the map reads cleaner at rest — the pin alone still marks
  every zone.
- **Bulk Intake preseeds Branch/Line from the signed-in user, and Team overwrites
  it on pick.** `HomeController.Intake()` now passes `TeamLinesJson` (same
  Team→home-Line map Registration already used) plus the session user's
  effective Branch/Line. On page load a user with a specific Line lands with
  Branch derived and Line preselected; a whole-Branch user lands with Branch
  preset and Line left for them to pick; an unassigned/Admin session presets
  nothing. Picking a Team with a home Line overwrites Branch/Line to match
  (still editable after) — a team with no home Line, or clearing back to N/A,
  touches nothing. Verified live: Cedric Martis (Residential Coils/AH) landed
  preset correctly on page load; picking Samurai flipped Branch/Line to
  Commercial Air / Commercial Packaged/Splits.
- **New Item Registry's name-match dropdown now jumps into Modify Stock instead
  of doing nothing.** Previously `selectable = false` on that binding meant a
  click only hid the list. `bindAutocomplete()` gained a `jumpToStock` mode:
  closes the New Item modal (chained off `hidden.bs.modal`, same pattern
  `jumpToHandleStock` already used elsewhere) and opens Modify Stock preloaded
  with the matched ItemId — whatever was typed into the registry form is
  discarded, since clicking a match means "this already exists." Verified
  live: clicking a match for CCR-0118 closed New Item Registry and opened
  Modify Stock with that ItemId loaded.
- **Modify Stock's Add action gained compressor serial capture — Pass 17
  parity for this new jump-in path.** Optional per-unit serial boxes
  (`serial_N`, same convention `CreateItem` reads), shown only for Add +
  Type = Compressor, sized to the typed quantity and rebuilt live as it
  changes. Server: `HomeController.ModifyStock` reads `serial_N` the same way
  `CreateItem` does; `InventoryService.ModifyStock` gained a `serials`
  parameter, captures whichever variant actually received the stock (existing
  stack or a freshly-minted NEW-location one) as `addedVariant`, and — after
  the existing `SaveChanges()` so a NEW variant's `Id` is real — calls the same
  `LogIntakeSerials` helper Bulk Intake/`CreateItem` already share. Verified
  live: 3 serial boxes rendered for qty 3 on a compressor Add; confirmed
  absent for Adjustment on the same item and for Add on a non-compressor item.

**Pass 21 (2026-08-13) — Bulk Intake splits already-registered rows into a
batch Modify Stock review instead of silently quick-adding them.**

- **A row whose typed Model name exact-matches a known item no longer imports.**
  Previously (since Pass 9) a name match just added a variant at the intake
  location straight through `CommitIntake`, silent and immediate. Now that row
  is pulled out of the grid entirely into a separate "Already registered" list
  — excluded from Import — carrying over whatever was typed (qty, serials, TC)
  so nothing has to be retyped. Two rows naming the same known item both land
  there and **merge into one section** when the review modal opens (summed
  qty, concatenated serials, summed TC), rather than racing each other as two
  separate actions. The held-batch approval path (`SettingsController.
  ApproveIntake`) is **unchanged** — it still runs `CommitIntake` exactly as
  before; this split only applies to the interactive Intake page, since
  there's a human present to review sections there and not in an approval
  batch.
- **"Modify stock counts / other" opens an Intake-local review modal**, one
  section per merged ItemId: current on-hand count, an Action select
  (**Add or Adjustment only** — this is a stock-count correction from the
  intake flow, not an ownership move), Quantity, and — gated on the item's
  Type exactly like Registry/Add already are — serial boxes for a compressor
  or a TC count for a motor. Each section has its own acknowledge checkbox;
  **Apply All stays disabled until every section is checked.** Posts to a new
  `SubmitIntakeStockUpdates` action via a second `<form>` (a form can't nest
  inside the Import form), field names stamped with a per-section index right
  before submit — same flat `stockItemId_N`/`stockAction_N`/`stockQty_N`/
  `stockTc_N`/`stockSerial_N_slot` convention every other multi-row post in
  this app already uses, not typed ASP.NET model binding.
- **`InventoryService.CommitIntakeStockBatch` applies the whole batch in ONE
  transaction** — same pattern `OrderService.Submit()`/`PickUpOrder()` use for
  their own multi-step commits: N calls to `ModifyStock` itself (reusing its
  existing-stack-vs-NEW-location resolution, TC clamps, serial logging,
  `TransactionLog` writes — nothing about Add/Adjustment was re-derived), with
  `tx.Rollback()` on the first update that can't proceed so a batch never
  applies half of itself. Two failure modes deliberately throw rather than
  silently misapply: an item deleted between the modal opening and Apply All,
  and — **the one genuinely new piece of logic** — an **Adjustment against a
  location the item doesn't already have a variant at**. `ModifyStock`'s
  Adjustment branch has no "NEW location" concept (only Add does); letting an
  Adjustment through with `targetVariant = "NEW"` would silently fall back to
  adjusting the item's PRIMARY variant instead of the location the human was
  actually looking at, so `CommitIntakeStockBatch` refuses that combination
  outright with a clear message instead. The controller fires the existing
  edge-triggered low-stock email per item after a successful batch, same as
  `ModifyStock`'s normal caller does.
- **Verified live end-to-end against the real db, not just build+DOM.** Two
  rows for the same real item (YRH104RA / CCR-0001, qty 3 + qty 2) confirmed
  merging into one qty-5 section with 5 serial boxes; a mixed compressor+motor
  batch confirmed Apply All requires **both** sections' checkboxes, not just
  one, and confirmed the motor section rendered a TC input while the
  compressor section rendered serial boxes. Then a real single-item Add (qty
  1, real location `NWES` — New Test Cells) was applied for real: CCR-0001's
  actual variant (id 328) went 10 → 11, logged as a normal `"Add"`
  `TransactionLog` row identical in shape to a hand-entered one, confirming
  the batch targets the item's real existing variant rather than minting a
  spurious duplicate. Reversed immediately after with a real Scrap of 1
  through Modify Stock (a separately, already-verified path) to restore
  10 — net effect on real data is zero, audit trail of the verification
  itself is left in place (this was a real item, not a synthetic test
  fixture, so nothing needed scrubbing the way a fake test Location/user
  would). Separately confirmed the Adjustment-without-existing-variant guard:
  picked a location (`RLB` — RD Lab) CCR-0001 has no stock at, chose
  Adjustment, submitted — refused with "no existing stock at this location to
  adjust," CCR-0001's quantity confirmed unchanged at 10 afterward.

**Pass 22 (2026-08-13) — Pass 21's batch review rebuilt as live inline
sections (feedback: the modal "felt like a dangling process"), plus the
same dropdown styling everywhere a text-datalist field still looked different.**

- **The batch review modal is gone.** A name match on Intake no longer
  requires a button click to "open" anything — it creates a live Modify
  Stock-style section directly on the page, in a scrollable
  `#stock-review-sections` container, the instant the match happens. A
  second row for the SAME item **grows that section in place** (bumps
  Quantity, extends serial boxes / bumps the TC count) instead of opening a
  duplicate — and grow reads the section's CURRENT DOM values first, so a
  hand-edit already made to a serial box (or anything else in the section)
  survives a later merge instead of being silently overwritten by a full
  re-derive. Verified live: typed two serials by hand into a 3-serial
  section, merged a second row (qty 3→5) for the same item, confirmed both
  hand-typed values were untouched and the new row's serial landed in a
  fresh slot.
- **No more acknowledge checkboxes.** Sections are fully visible and
  editable on the page now, not hidden behind a modal-open gesture, so the
  checkbox gate was compensating for something that no longer exists.
  **Apply All is just enabled whenever at least one section exists** —
  confirmed live (disabled at zero sections, enabled the instant one
  appears, correctly re-disabled after removing the only section via its
  own `×` button). The server endpoint (`SubmitIntakeStockUpdates` /
  `CommitIntakeStockBatch`) is **unchanged** — same transaction, same
  Adjustment-without-existing-variant guard, same field-stamping convention
  on submit. Re-verified live end to end through the new markup: a real
  qty-1 Add on CCR-0001 at New Test Cells (10 → 11), reversed with a real
  Scrap of 1 back to 10.
- **Styled dropdown parity, three more fields.** The list-group
  suggestion-panel look (dark rows, `list-group-item-action`, positioned
  under the input) used by Registry's model-name field and Modify Stock's
  search box now also covers: Intake's per-row **Type** field (was a native
  `<datalist>`, one dropdown built per row via a new generic
  `bindValueAutocomplete(inputEl, listEl, values)` helper — plain-string
  suggestions, no id/qty payload, dispatches a real `input` event on pick so
  `syncUnitCapture` and everything else already listening keeps working, with
  a one-shot suppress flag so that synthetic event can't reopen the same
  list); **New Item Registry's Type field** (`reg-type` had no dropdown at
  all before — plain text with a placeholder — now sourced from distinct
  Types already in `itemsList`, no server round trip); and the **sign-in
  name field** (was a native `<datalist>` showing `UserName`/`DisplayName`
  inconsistently across browsers — now matches against `DisplayName`, since
  people know their own name, and sets the input to `UserName` on pick,
  since that's what the server parses). Each is its own small duplicated
  copy per file (`bindValueAutocomplete` in Intake.cshtml/Index.cshtml, a
  bespoke display-name version in Identify.cshtml) — same accepted-debt
  pattern as `encodeLoc()` already being triplicated across this project
  rather than centralized in `site.js`.
- **Fixed a real pre-existing bug while touching Identify.cshtml**: the
  name field's `pattern="^[A-Za-z'-]+\.[A-Za-z'-]+$"` threw "Invalid
  regular expression... Invalid character in character class" in the
  browser console on every page load (Chrome's newer v-mode character-class
  parsing doesn't like an unescaped trailing `-` there) — client-side
  validation was silently not runnable this whole time, though the server's
  own regex check in `HomeController.Identify()` was unaffected and kept
  enforcing the format regardless. Fixed by escaping the hyphen
  (`[A-Za-z'\-]`). Confirmed live: zero console errors on the sign-in page
  after the fix, versus the error firing before it on every prior pass that
  touched this page.

**Pass 23 (2026-08-13) — Delete Stack (variant-level hard delete), Rack/Row
cascading suggestions everywhere they appear.**

- **`InventoryService.DeleteVariant(itemId, variantId)` fills the real gap Delete
  Item leaves.** Delete Item requires the item's TOTAL quantity to be 0; a single
  variant that drains to 0 while OTHER variants still carry stock (the exact real
  scenario that surfaced this: a compressor got 2 units added to a brand-new pile,
  both scrapped, leaving an empty second stack with no way to remove it) had no
  path to go away at all before this. Same guard shape as Delete Item: refuses if
  the variant's own quantity isn't 0, refuses if it's the item's ONLY active
  variant (points at Delete Item instead — deleting a lone empty stack is really
  "delete the item"), refuses if an On-Hand `CompressorUnit`/`MotorUnit` still
  references it, refuses if a Pending order's `RequestedVariantId` points at it.
  On success removes the `ItemVariant` row and logs `"Stack Deleted"` — same
  "TransactionLogs keep reading under the old FdaString, nothing here touches
  them" philosophy as Delete Item. **Gated Admin (5)**, same tier as Delete Item
  — this removes a row outright, not just a quantity.
- **Surfaced on Modify Stock, not the search-result card.** Delete Item lives on
  the card because it acts on the whole item, independent of any in-progress
  action; Delete Stack needs to know WHICH variant, which only exists once the
  Location/Variant selector is showing. A small trash-icon button
  (`stock-delete-variant-btn`, Admin-only, server-rendered) sits next to that
  selector, shown/hidden by `refreshDeleteVariantBtn()` whenever the currently
  chosen variant's quantity is 0 AND the item has more than one active variant
  (a single-variant zero-qty item still correctly funnels to Delete Item, not
  this). Clicking it opens a small confirm modal (`deleteVariantModal`, mirrors
  `deleteItemModal`'s shape exactly) posting to a new `DeleteVariant` action.
  **Verified live against the real db**, on the actual item that surfaced the
  gap (CCR-0013): the button appeared only once its empty Variant 2 was
  selected, the confirm modal populated the right label, and the real delete
  removed exactly that variant — Variant 1's 3 units and the item's identity
  untouched, `TransactionLog` shows `"Stack Deleted ... Variant 2 (ETRD.TALA.
  TAL1.FLOOR.0) deleted"`. This was a real fix to the user's actual stuck
  item, not a synthetic test case.
- **Rack/Row now cascade-suggest from real stock data, on all four surfaces
  that have them** (New Item Registry, Bulk Intake, Export Wizard's filter —
  new fields there, didn't exist before — and Modify Stock's Location
  Transfer/Add-to-new-location pane). Deliberately **not** a managed
  vocabulary table like Parent/Major/Sub — `ItemVariant.cs` already documents
  Rack/Row as "free-form, team-assigned," and the backlog's older "Rack/Row as
  managed levels" idea is a bigger, separate thing this doesn't attempt.
  Instead: `HomeController.BuildRackRowMap()` groups active `ItemVariant`s by
  `(Parent, Major, Sub)` **code** tuple and returns the distinct Rack/Row
  values already typed in under each — a suggestion, never a gate, so a
  brand new value always types in fine. New generic `bindValueAutocomplete`
  capability (already built in Pass 22 for Type fields) now also accepts a
  **getter function**, not just a fixed array, since Rack/Row's valid
  suggestion set changes live as Parent/Major/Sub changes — re-evaluated on
  every keystroke rather than rebound on every cascade change. Two of the
  four surfaces (Registry, Export) have NAME-valued Parent/Major/Sub selects
  and need `encodeLoc()` before the lookup; the other two (Modify Stock,
  Intake) already carry CODE-valued selects directly. **Verified live on all
  four**: picking External Yard → Connex Area → Connex Box 6 correctly
  narrowed Rack suggestions to `[FLOOR, RACK 1, RACK 2]` on Registry, Modify
  Stock, and Export Wizard, and to `[FLOOR]` on Intake (a different real Sub
  with less data); clicking a suggestion set the field and fed the FDA
  preview correctly; typing an unlisted value was accepted with the list
  correctly staying closed.

**Pass 24 (2026-08-13) — the app's first responsive pass. Zero `@@media`
queries existed anywhere before this, on any page.**

- **The dashboard is a fixed 6-column CSS grid** (`.overlay-grid` —
  `grid-template-columns: 380px repeat(4, 1fr) 380px`, `position: fixed`)
  with every widget placed via `!important` `r*`/`c*`/`row-span-*`/
  `col-span-*` utility classes. Below roughly 1400px combined width it
  overflowed horizontally with **no way to scroll to reach the clipped
  columns** — a phone or a real tablet couldn't use the dashboard at all
  before this pass, not just uncomfortably. Fixed with three breakpoints,
  all pure CSS, **zero DOM/markup changes**: ≤1400px narrows the two
  sidebar tracks to 300px; ≤1100px switches `.overlay-grid` to
  `position: static; display: flex; flex-direction: column` and
  neutralizes every placement utility (same selectors, `!important`,
  positioned later in the cascade so they win), so every widget just
  stacks in **document order** — Advanced Filters, Omni Search, Export
  Wizard, Modify Stock button, Stock Alerts, Pickup box, the left button
  column, the map, then the right Activity Feed panel; ≤900px collapses
  `.holo-viewer` (search results/cart) from its 2-column grid to a single
  stacked column.
- **Two real overflow bugs found only by live-testing at actual mobile
  width** (DOM/CSS review alone didn't catch either):
  - `.map-stats-bar` sits inside `.map-container`, which has
    `overflow: visible` **on purpose** (so the zone click-menus can pop out
    past the map's edge). Its 4-across stat row (Total Items / Active
    Locations / Low Stock / Out of Stock) couldn't shrink below its
    natural content width without `flex-wrap`, so on a narrow map it
    silently bled past the container — unclipped, since nothing upstream
    of it clips overflow either — and widened the **entire page's layout
    viewport**, not just looking cramped. Fixed with `flex-wrap: wrap` on
    the bar (zero visual effect wherever 4 already fit on one line).
  - The top nav's `navbar-expand-sm`/`d-sm-inline-flex` (576px+) meant a
    real tablet (768px) rendered the full inline nav — links + notification
    bell + user badge need ~620px alone — and overflowed. Bumped both to
    `-lg`/`d-lg-inline-flex` (992px) so only genuinely desktop-width
    screens get the inline layout; phones and tablets both get the
    hamburger collapse.
- **Every `<table>` in the app wrapped in `table-responsive`** (Bootstrap's
  own horizontal-scroll container) where it wasn't already, with a small
  `min-width` on the table itself so columns don't crush to unreadable
  widths — the wrapper is what makes that safe, since overflow now scrolls
  inside its own box instead of forcing the page wider. Covers: Intake's
  row-entry grid, AllItems, both Logs tabs, OrderDetails, Orders, both
  PickupQueue tables, all 7 Settings tables (map zones, locations, teams,
  branches, lines, users, the held-intake-batch preview), and Index.cshtml's
  6 modal tables (Stock Alerts/Out-of-Stock previews, both compressor Log
  Units tables, motor Log Units, PN backlog).
- **Verified live at three real viewport sizes** (375px mobile / 768px
  tablet / 1280px desktop, not just DOM/CSS review) across the dashboard,
  Intake, AllItems, Logs, and two dashboard modals: zero horizontal
  overflow (`document.documentElement.scrollWidth === clientWidth`) at
  mobile and tablet on every page checked; desktop confirmed
  byte-for-byte unregressed (same fixed grid, same `position:fixed`, same
  expanded nav) — this pass changes nothing for the primary desktop
  workflow, only adds a working fallback below it. Zero console errors
  throughout.
- **Not done this pass, worth knowing:** `Views/Settings/Index.cshtml`'s
  table wraps rode on the same proven mechanical pattern but were **not**
  separately live-verified — this session had no superuser passcode to
  unlock Settings against the real app. `MyOrders.cshtml`'s `.mo-table`
  is already `width:100%` (no overflow-forcing risk, unlike the others
  found), but wasn't given a content-density pass — many columns on a
  narrow phone will be readable but tight, not broken. Individual modal
  dialog widths weren't audited for hardcoded pixel values beyond
  Bootstrap's own size classes (`modal-lg` etc. already scale down
  reasonably by default, but a few modals in this app set explicit
  `width`/`max-width` in inline styles that weren't part of this sweep).

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
location list were found across four passes because each stored something different.
The same lesson applied to Branches/Lines in Pass 13, though that one only had the one
hardcoded dictionary to replace.

**`site.js` loads after `@RenderBody()`**, so a view's inline script runs before it. Moving
shared JS there breaks any IIFE that calls it at parse time.

**A `<script>` block's top-level data `const`s must load before ANY code that might
reference them synchronously** — not just before code that merely defines a function
using them (Pass 11). If a page-load bug takes out a seemingly unrelated set of buttons,
check whether something new is being called unconditionally at top level rather than
deferred into a handler.

**A copied scratchpad/backup `.db` file needs its `-wal`/`-shm` sidecars copied as one
atomic set, or not at all (Pass 13).** A stale `-wal`/`-shm` pair left over from an
earlier backup, sitting next to a freshly-copied newer `.db`, produces
`database disk image is malformed` — even in strict read-only mode, since the corruption
is in the file bytes, not a locking issue. If that error shows up on a copied db, check
whether the wal/shm actually belong to that exact copy of the main file before assuming
real corruption; the fix is usually just re-copying as a clean set (or deleting the
stale sidecars if the source is known to be a standalone file). Also: force-killing a
running app instance leaves the same kind of stray `-wal`/`-shm` behind — checkpoint
(`PRAGMA wal_checkpoint(TRUNCATE)`) and switch to `DELETE` journal mode before handing a
test copy back off.

---

## Known issues — accepted as-is

- **`AlertThreshold` is 0 on all compressors** — no low-stock warning ever fires.
  `SetDefaultThreshold(team, threshold)` exists to bulk-fix it.
- **Superuser passcode is plaintext in `appsettings.json`**, tracked in git and now on the
  host laptop. Git history keeps the old value even after a change, so rotating matters more
  than removing the line.
- **`encodeLoc()` is duplicated** in `Index.cshtml` and `Intake.cshtml`, plus the C# original.
- **`Type` is free text** — no vocabulary. `IsControlType` matches only items literally typed
  `Control`; EEV, VFD and Valve are uncovered.
- **`newGroup`** is a dead parameter on `ModifyStock`, accepted and ignored.
- Build warnings: `NU1903` (SQLitePCLRaw advisory), `CS0114` (`SignOut` hides base member),
  a few `CS8602`. Unchanged count/shape through Pass 13 — nothing this session introduced
  a new one.
- **`<select>` imbalance in `Index.cshtml` is a false positive** — `<select>` inside a JS
  comment. Pre-existing, harmless, don't chase it.
- **27 skipped negative-quantity Ninja rows from the original go-live import** — accepted
  as data debt, not being chased. I asked the original team about it; the honest answer
  was "we'll count better going forward," and that's fine — imperfect starting data that
  improves through normal use beats spending more time reconstructing numbers nobody
  can verify anymore.
- **Motor loan return/scrap auto-selects the oldest outstanding units for that order
  line** (no unit-picker checkboxes, unlike compressors — nothing for a human to
  distinguish between since motors have no serial). Confirmed live for pickup; a real
  Return or Scrap against a TC motor loan specifically hasn't been separately verified.
- **~70 compressor items from the Pass 13 reconciliation are still unclaimed** — genuinely
  different brand families (Highly, GMCC's odd tonnages, the Copeland `ZP` scroll line,
  a few small LG models) that don't match either team's reference sheet. Left untouched
  on purpose rather than guessed.
- **The letter-family hypothesis from Pass 13 is unconfirmed.** The pattern that seemed
  to separate Samurai's compressor family (`YA`/`YRH`/`YGH`-prefix) from Ninja's
  (`YAS`/`YAT`/`YRM`-prefix, plus `YRH067` specifically) held for everything checkable,
  but whether it generalizes across the board is still an open question I'm running down
  separately.

---

## Backlog

- **8C — location requests.** Deferred: the Pass 9 pending queue covers the case that
  actually mattered.
- **9D — CSV import** for volume intake. In-app entry covers small lists; I handle large
  ones by SQL.
- **Rack/Row as managed levels.** The `Locations` table already supports arbitrary depth
  via `ParentId`. Pass 23 covered the narrower, more likely want — cascading
  suggestions sourced from real stock data, still free text — deliberately without
  building this. Revisit only if free-form Rack/Row actually turns out to need
  Settings-level governance (rename/hide/audit) the way Parent/Major/Sub do.
- **Locations tree view** with add-in-place, replacing the flat table + level picker.
- **6C — pickup selects a serial** from a dropdown instead of typing. Optional now that
  match-or-create puts correctness in the service.
- **Team / Location rename** — not offered anywhere; items store names as plain strings, so
  a rename orphans them unless it cascades. Add-and-hide is the workaround. Same is now
  true of Branch/Line rename.
- Notification categories: table is multi-category, only `PickupRequested` exists.
- No backup story — `inventory.db` is hand-copied.
- **New Item Registry's duplicate check only covers Rheem PN, not Item Name** —
  surfaced in Pass 16 when a Samurai compressor (YGH182W) got registered a second
  time under a new ItemId, because the whole `YGH*` family carries PN `N/A` so the
  PN check had nothing to catch. Caught and fixed by hand that time (merged into the
  original `CCR-0003` as a second variant); worth a real fix — a name-collision
  heads-up alongside the existing PN check — before the next family-wide-`N/A` model
  gets registered twice for real.
- **Motors: only the TC subset is tracked (deliberate).**
- **Compressor/Motor filter: Team→Branch/Line is one-way on purpose.**
- **`MyOrders.cshtml` was not extended for motor-unit selection.**

- **Unit lifecycle / event history — scoped in Pass 16, not started.** The eventual
  goal (Kason's framing): full lifecycle tracking on a serialized unit — pick a model
  in the Compressor/TC-Motor modal, find a specific serial, see everything that's ever
  happened to it. Immediate use is compressors and TC motors; **the real target is
  broader** — VIS eventually tracking whole air conditioner units and other serialized
  asset types the same way, not just compressor/motor components. Whatever gets built
  should be designed as a general "unit lifecycle" concept from the start, not
  compressor-specific, or it gets rebuilt when that broader scope lands.

  **Current state:** neither `CompressorUnit` nor `MotorUnit` retains history — both
  are current-state rows, not event logs. `ReturnLoan` explicitly nulls
  `OrderId`/`OrderItemId`/`PickedUpAt`/`PickedUpBy` back to blank the moment a unit
  returns to the shelf (`Services/OrderService.cs`), so a unit picked up twice has
  already lost the trace of its first cycle in the unit table itself. The only place a
  real trail exists is `TransactionLog`'s free-text `Details` (pickup/return/scrap/
  "Unit Logged" all embed the serial when one's on record), but there's no structured
  link back to a specific unit or serial — just a string that happens to be in there.

  **Two sizes of fix, not yet decided between:**
  1. *Small, no migration* — a serial search box on the modal that string-matches
     against `TransactionLogs.Details` for that ItemId. Quick, but a best-effort
     reconstruction off free text, not a real audit trail — fragile if a log
     entry's phrasing ever drifts, and can't show more than "log lines that happen
     to mention this string."
  2. *Real feature, needs a migration* — turn unit tracking from a current-state row
     into an append-only event log: a new table (e.g. `UnitEvents` — EventType,
     Timestamp, By, OrderId, VariantId/location) written alongside the existing row
     instead of overwriting it, on every write path that currently mutates a unit
     (`PickUpOrder`, `LogCompressorUnits`, `ReturnLoan`, `ScrapLoan`). This is a
     genuinely new mechanic — full "spiterate before building" scoping (every field,
     every event type, how it covers both Compressor and Motor units, how a future
     "full AC unit" asset type would plug into the same shape) needed before opening
     a file, not decided here.

---

## Carried-forward items from prior review sessions

Nine items were flagged across earlier threads and never confirmed closed. Each was
re-checked against the deployed code.

### DONE — Pass 10

**1 · Short-pull logs the ordered quantity, not the pulled quantity.** Fixed in Pass 10,
and taken further than the original ask — a short pull now refuses outright instead of
silently under-fulfilling.

### STILL PRESENT — layout, cosmetic

**2 · `.map-stats-bar` / `.map-live-bar` are `position: absolute`.** Wanted in normal
flex-column flow. Unchanged.

### CLOSED on inspection — no action

**3 · "Fresh database seeds every user as Viewer."** Does not reproduce. `Program.cs`
seeds users with explicit levels, me as Admin (5). A fresh database is administrable
immediately.

**4 · "Leftover test scaffolding comments in `Program.cs`."** None present.

**5 · "`syncHeaderOffset()` not executing."** Wired correctly.

**6 · "`.holo-viewer` collapsing without explicit width."** Both now carry one.

### UNVERIFIED — cannot be checked from code alone

**7 · TC counts not reserved across concurrent pending orders.** No reservation logic
exists, so the behaviour is plausible; confirming needs two concurrent orders against
the same TC stock.

**8 · Disposed-transaction rollback in a `catch`.** Practically unreachable. Lowest
priority.

**9 · Role-aware gating from an old artefact I no longer have visibility into.** The
Pass 13 access-control audit covered what each level actually unlocks end to end
(catalogued in full — ask if a copy of that catalog is useful), which should supersede
whatever the original concern was, but I haven't gone back to reconcile the two
line-by-line.

---

## What was actually verified live (Pass 10 onward)

Read this before trusting any "works" claim above. Everything here was confirmed by
actually running the app against my real, copied-in database — not a fixture — checking
the DOM, the browser console, and the server's live SQL/exception log after each action.

**Passes 10–12:**
- Migrations apply cleanly, no `PendingModelChangesWarning`.
- Handle Stock, Add to Cart, and the search boxes all confirmed working after the Pass
  11 TDZ fix.
- Ownership pane, New Item Registry, Compressor/Motor filters, Compressor On Hand
  roster, and `PickUpOrder`'s MotorUnit creation — confirmed by me personally on a real
  order (Order 10, CMR-0078).
- Order Details Brand column confirmed rendering a real brand.

**Pass 13:**
- All migrations through `AddBranchesAndLines` applied cleanly against the real db.
- The full compressor reconciliation batch applied and spot-checked against specific
  items discussed by name.
- Test-fixture removal and the duplicate merge both verified with before/after queries
  and a post-write integrity check.
- The three UI/access-level fixes (VIEW CART, Add to Cart, New Item Registry now
  showing at Standard) confirmed rendering correctly for the signed-in test session.
- Delete Item: confirmed the button only appears at quantity 0 and Admin level, and the
  confirmation modal renders with the exact intended wording, against a real zero-stock
  item — stopped short of completing the actual delete so I could test that step myself.
- Branches & Lines: added a real test Branch and Line through the live Settings UI,
  confirmed it appeared in every picker across the app immediately, then removed it.

**Pass 14:**
- The Sustaining Quick Filter, the "Available Tasks" text, and the Activity Feed fix
  were all confirmed live in the running app in a single pass — signed in, checked the
  dashboard, toggled a couple of real Settings actions, and watched them render
  correctly in the feed for the first time.
- The RCR rename was verified with before/after queries and a post-write integrity
  check, plus a stale-reference sweep across `TransactionLogs`/`CompressorUnits`
  confirming zero old `CCR-` ids left anywhere.
- The branch-graying logic was verified for the Admin case (all three buttons enabled
  for my own session) by direct inspection of the rendered button classes; the non-Admin
  graying path was verified by code review, not by actually signing in as a non-Admin
  user.

**Pass 15:**
- `ApplyLogVisibility` verified with a real before/after comparison: signed in as
  Admin (me) and counted 578 rows on View Logs, then signed in as Cedric Martis
  (Engineer, scoped to Residential Coils/AH) and counted 349 on the same page. Real
  reduction confirmed, not just "should filter."
- The mandatory-Line client-side `required` attribute confirmed present and correctly
  labeled on both the New Item Registry and Intake forms by direct DOM inspection. The
  server-side rejection path was confirmed by code/build, not by actually submitting a
  blank-Line registration and watching it bounce.

**Not verified live:** `ReturnLoan`/`ScrapLoan`'s auto-select logic for `MotorUnit`
specifically (pickup was confirmed on Order 10, but a Return or Scrap on that same loan
wasn't separately exercised). The non-Admin case of the Pass 14 branch-button graying.
The actual server-side rejection of a blank-Line submission (Pass 15) end to end.

**Pass 16:**
- Unclaimed filter fix confirmed by direct sqlite query against the live db, not just
  code review: 0 compressor items had both Team and Line blank (the old AND condition),
  vs. 157 with at least one blank (117 Team-blank/Line-set, 40 Team-set/Line-blank) —
  the exact set the OR fix now surfaces. Clean build after.
- The 242-item bulk Line update verified with before/after row counts: 242 updated
  (exact match to the non-compressor item count), compressors confirmed untouched (the
  142 compressors already on that Line predate this update, from Pass 13), total item
  count unchanged at 487.
- Add User whole-Branch: tested through the actual live Settings UI end-to-end — picked
  "Commercial Air / — Entire Branch —", submitted, confirmed the resulting row in the
  real db had `Branch = "Commercial Air"`, `Line = NULL`, then deleted the test user and
  its audit log entry.
- `UserTeams` migration: verified the pre-migration snapshot (2 users had a Team:
  Conner Walworth/Samurai, James Masters/Ninja) matched exactly post-migration.
  Team-centric picker tested live: added Kason to Samurai, confirmed the `UserTeams` row
  and `TransactionLog` entry, removed him, confirmed back to the original 1-row state.
  Users table's joined-team display spot-checked for three users (Conner → "Samurai",
  James → "Ninja", Kason → "All").
- `SetDefaultThreshold` (touched only because removing `User.Team` broke its build,
  not because it was in scope) exercised live with a true no-op value (0 → 0, since all
  487 items already sit at threshold 0) — submitted without error, dashboard reloaded
  clean.
- Location Transfer fix: added a real Sub location with zero stock (`ZZ_FixVerify_
  DeleteMe`, under Connex Area) through the live Settings UI, confirmed it appeared in
  the transfer cascade's actual JS data (`locHierarchyData['ETRD']['CNEA']`) immediately
  alongside the pre-existing Connex Box 6, then deleted the test location and its
  log entry. No new migration — pure data-source change, no schema touched.
- 5 real compressor registrations (4 Samurai + 1 Hitachi) done through the live New
  Item Registry form exactly as a real user would, not raw SQL — confirmed each one's
  full row (Type/Brand/Team/Line/ProjectCode/variant/FdaString) after every submission.
  The one duplicate (YGH182W landing on top of pre-existing `CCR-0003`) was caught by
  a full-catalog `fetchall()` re-check afterward, not before — the pre-check only
  covered Rheem PN, which didn't apply to this family (`N/A` on every one of them).

**Worth knowing:** the `vis-dev` launch profile points at the same
`C:\VIS_Inventory\inventory.db` as production — there's no separate test database, so
"verified live" in this doc means the real data, with test rows/log entries cleaned up
immediately after.

**Pass 17:** verified live by me (Kason) directly this time, not Claude — I tested
against my own pre-release backup copy. Five scenarios run through the real Bulk
Intake UI: mixed compressor/motor/plain rows in one batch, deleting a mid-batch row
before submit, a compressor row with Qty > 1 and multiple serials, and TC-count
capture on both Registry and Intake all passed. The fifth (an unrecognized location,
meant to hold for approval) surfaced the `"__NEW__"` bug above — real data corruption
(`CCR-0255`), not a UI glitch, caught because I actually pushed the held-batch path
for the first time rather than trusting it worked. Claude traced the exact mismatch,
fixed it, and cleaned up the corrupted test item afterward. The
approve-a-new-Parent-location reminder was built in response but not yet exercised
live — next real "not listed" approval through Settings should confirm it fires.

**Pass 18:** verified live end-to-end by Claude against a scratchpad **copy** of the
real db (not the live file — no test rows were written to `C:\VIS_Inventory\
inventory.db` this session). The exact race was reproduced through the running app
with two browser tabs: order placed (Order #8, 2 × CCR-0003), Pickup Queue opened in
one tab, order cancelled from the Orders page in a second tab, then Pick Up clicked
on the first tab's stale render. The post was rejected with the new toast ("Order #8
was cancelled and can no longer be picked up."), and a direct db check confirmed the
order stayed `Cancelled` with no `FulfilledBy`, both CCR-0003 variants untouched
(6 + 2), zero pickup log rows, and zero `CompressorUnit` rows. Before the fix this
same sequence would have pulled 2 real units and flipped the order to `Completed`.

**Pass 19:** the `ReportShortPull`/`ReturnLoan` fixes were verified by code review and
a clean build within their own worktree sessions, not by reproducing either scenario
live (a short-pulled TC motor line, and a loan return to a brand-new location, are
both narrow enough paths that neither has been separately exercised end-to-end since
they were built). The three message-accuracy fixes are lower-risk (wording/logging
only, no state-machine change) and were build-verified only.

**Pass 20:** verified live against the real running app and real db (read-only —
nothing was actually submitted through any of these paths, so no test rows/log
entries needed cleanup). Confirmed via signed-in sessions as Cedric Martis
(Residential Coils/AH) and Kason (Admin): Intake's Branch/Line autofill on page load
and on Team change, the Registry-to-Modify-Stock jump end to end, and the serial-box
count/visibility rules (shown for Add+Compressor, hidden for every other
action/type combination tried). The actual server-side serial write (submitting an
Add with real serials filled in) was not separately exercised — that path reuses
`LogIntakeSerials`, already verified live in Pass 17, but not through this specific
new entrance.

---

## Current state

Went live for real starting Pass 10. Pass 13 closed out the data-quality and
access-control gaps found along the way and added Branches/Lines as managed vocabulary;
Pass 14 followed up with the RCR rename, a round of dashboard/access polish, and 9 more
real users; Pass 15 made Line mandatory going forward and closed the log-visibility gap;
Pass 16 fixed the Unclaimed filter, reconciled all non-compressor items onto their real
Line, closed the whole-Branch-at-creation gap, rebuilt Team as many-to-many, fixed the
Location Transfer cascade's location-vocabulary source, and added 5 real compressors
(the RheemPN duplicate-check gap this surfaced — model-name collisions on a family
that's uniformly `N/A` for PN — is worth a real fix later, not just something I caught
by hand this time). Pass 17 added serial/TC capture at registration and intake, and
fixed a real pre-existing bug in Bulk Intake's held-batch path that meant it had never
actually worked through the live UI. Pass 18 closed a stale-queue race that let a
cancelled order still be picked up; Pass 19, merged in the same batch, fixed
`ReportShortPull` dropping TC/location on reissue and `ReturnLoan` writing a dangling
`ItemVariantId = 0`, plus three user-facing message-accuracy fixes. Pass 20 removed
the map's decorative red tracker dot, made zone row-counts hover-only, gave Bulk
Intake the same Team/Branch/Line autofill Registration already had, and made New Item
Registry's name-match dropdown jump straight into Modify Stock (with serial capture
now on Add, for that path). Pass 21 changed Bulk Intake's behavior for already-known
models: instead of a silent quick-add, a name match now routes through a batch Modify
Stock action, applied in one transaction. Pass 22 rebuilt that batch review as live
inline sections instead of a modal (feedback that the modal felt disconnected), and
added the same styled-dropdown treatment to three more text fields that were still
using native `<datalist>`s or nothing at all. Pass 23 added Delete Stack (a
variant-level hard delete for the gap Delete Item doesn't cover) and cascading,
still-free-text Rack/Row suggestions across Registry/Intake/Export/Modify Stock.
Pass 24 was the app's first responsive pass — zero `@@media` queries existed
anywhere before it. The dashboard's fixed 6-column grid genuinely didn't work
below ~1400px (no scroll escape, not just cramped); it now reflows to a single
stacked column on phones/tablets with no markup changes, and every table in
the app scrolls inside itself instead of forcing the page wider.
**The Pass 13/14 database has now been copied to the host** — this is no longer just
sitting in a scratchpad copy, it's what I'm actually testing against. Passes 15–17
were all tested directly against that same live db through the running app (Passes 16
and 17 both made real data changes to it — the 242-item Line reconciliation, 5 new
compressors, plus Pass 17's discovery-and-cleanup of the `"__NEW__"` corruption —
beyond their schema migrations), so nothing further needs copying for any of them.
Pass 18 was tested against a scratchpad copy, not the live file. Pass 20 was tested
read-only against the live app/db (nothing was actually submitted, so nothing needed
cleanup). Passes 21 and 22 each made one small real write against the live db (the
same +1/-1 round trip on CCR-0001, once against each version of the batch write path)
and left it net zero both times, audit trail intact. Pass 23 made one real write that
was NOT reversed afterward — it was the actual fix requested: deleted CCR-0013's
genuinely-empty Variant 2, the real stuck stack that surfaced the whole feature.
The morning's Pass 16 release (everything through the `AddUserTeams` migration) is
already published and running; everything from the Location Transfer fix through
Pass 24 (no new migrations since `AddIntakeRowMultiSerialAndTc`, Pass 17's) is
committed and pushed but **not yet in a published release** — still need a fresh
`dotnet publish` to reach the host.
Remaining before I'd call it fully settled: do one real Return or Scrap on a TC motor
loan, resolve the letter-family hypothesis for the still-unclaimed compressor items,
sign in as a non-Admin user to confirm the branch-button graying looks right in
practice, actually submit a blank-Line registration to watch Pass 15's rejection fire
for real, exercise the new map-zone-creation reminder live (built in Pass 17, not yet
triggered through a real approval), submit a real serial through either Pass 20's
Modify Stock Add entrance or the batch review (both share `LogIntakeSerials`,
verified live in Pass 17 through Registry/Intake, but not separately through either
of these two newer entrances — both live batch tests so far used a bare quantity,
deliberately, to keep the round-trip trivial to reverse), and give Pass 24's
responsive work a real-device pass with an actual phone/tablet (browser-emulated
viewport resize is a good proxy but not a substitute) plus a live check of
Settings on mobile once the superuser passcode is available.
Everything else on the scaling list (SQL Server/Azure SQL move, SSO, backup story) is
expansion work, not a blocker.
