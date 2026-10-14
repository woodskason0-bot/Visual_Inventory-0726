# Unit Tracker — Build Spec

Scoped 2026-10-13. To be built in passes; Kason tests each pass before the next
one starts. Schema change (one new table), so `dotnet ef migrations add` and the
usual migration care apply.

> **BLOCKED: §4 (the unit tag) waits on Kason's code list.** Do not start Pass 1
> until §4 reads DECIDED. Every log row, the Unit Card's history and the unique
> index all hang off the tag, so nothing ships with a placeholder.

---

## 0. Read first

- `docs/VIS_Handoff_State.md`: the OPEN section (live database state) and the
  Traps section. `docs/vis-addendum-consolidated.md` for working method.
- This spec decides unit work. Where it disagrees with the code, the code is the
  truth: stop and ask Kason rather than picking one.
- Grep the whole repo for every name changed or reused, and ignore
  `.claude/worktrees/` (stale copies).
- Test against `C:\VIS_Inventory\inventory.dev.db` (wired by
  `appsettings.Development.json`), never `C:\VIS_Inventory\inventory.db`. Back the
  dev db up before the migration runs.

## 1. Source and decisions

**Conner Walworth (Samurai), Teams, 2026-10-13:** count the G-cab units on hand
and track them in VIS, to see what's available for work orders going into the
model shop. Fields he asked for: unit model, Sample # (their tracking number, tied
to the test tracker), voltage, Ready / Not Built, Tested / Not Tested,
AHRI / Reliability / Field Trial. The biggest questions are how many units are
on hand and at what tonnage. Many units are rebuilt, so the model is usually the
short form, not a full model number. The short form is R (Rheem) + AC / GE / HP +
cabinet C / D / G + a 3-digit size + a voltage letter, e.g. `RACG180D`. Model is free
text with a hint showing the pattern; some new products have no model number.
Sample # runs 1, 1a, 1b … up to 10. Tonnage can be written `078` or `6.5T`.

**Kason, 2026-10-13:**
- One row per physical unit, in its own table. No inventory item family, no
  stacks, no quantities. Units are information only: they cannot be ordered.
- Its own page, **Unit Tracker**, with its own **Unit Card** (not the Item Card).
- Findable from Search Center: Omni Search matches units, and the Type filter gets
  a "Unit" choice that shows unit-specific filters.
- Condition: a **Damaged** flag plus a note. No photos.
- Voltage: store and show the **letter only** (C / D / Y). No meaning attached
  until Conner confirms what the letters stand for.
- Tag: target shape `C-RACG180D-001`, but **not used yet** (§4).
- The code tables (product type, cabinet, voltage) live in one file, so Kason's
  list is a single edit.

## 2. Out of scope

Ordering, cart, pickup, loans, Request Transfer · photos · voltage meanings ·
Command Center KPIs and map counts (units are not counted there) · the Export
Wizard (items only) · a WO # field (question for Conner, §17) · Settings-managed
vocabulary for the dropdowns · loading the SharePoint sheet
(`Unit_Inventory_Tracking_Samurai.xlsm`). The fresh G-cab count replaces it.

## 3. Data model

### `Models/TrackedUnit.cs` → table `TrackedUnits`

Named `TrackedUnit`, not `Unit`: VIS already uses "unit" for `CompressorUnit` /
`MotorUnit` / `UnitStatus` rows. Non-nullable strings default to `""`, matching
`InventoryItem`, so queries need no null checks.

| Property | Type | Max | Default | Rule |
|---|---|---|---|---|
| `Id` | int | | | PK |
| `Tag` | string | 30 | `""` | Minted at creation, unique, never reissued. **§4.** |
| `ModelNumber` | string | 60 | `""` | As typed, whitespace removed, uppercased. Optional. |
| `ProductType` | string | 2 | `""` | `AC` / `GE` / `HP` / blank |
| `Cabinet` | string | 1 | `""` | `C` / `D` / `G` / blank |
| `NominalMbh` | int? | | null | The 3-digit size code (180 = 15 T). Shown as tonnage. |
| `Voltage` | string | 1 | `""` | `C` / `D` / `Y` / blank. Letter only. |
| `SampleNumber` | string | 3 | `""` | Canonical `1`–`10` + optional lowercase letter (§5.3) |
| `BuildStatus` | string | 20 | `""` | `Not Built` / `Ready` / blank (= not recorded) |
| `TestStatus` | string | 20 | `""` | `Not Tested` / `Tested` / blank |
| `Designation` | string | 30 | `""` | `AHRI` / `Reliability` / `Field Trial` / blank (= unassigned) |
| `Parent`, `Major`, `Sub` | string | 50 each | `""` | Location **codes**, same as `ItemVariant`. Parent required. No Rack/Row. |
| `Line` | string | 100 | `""` | Required on Add (`OrgStructure.IsValidLine`) |
| `Team` | string | 100 | `""` | Optional, one of the active Teams |
| `SerialNumber` | string | 50 | `""` | Optional nameplate serial (catches double counting) |
| `IsDamaged` | bool | | false | |
| `DamageNote` | string | 500 | `""` | |
| `Notes` | string | 1000 | `""` | |
| `Status` | string | 20 | `On Hand` | `TrackedUnitStatus`: `On Hand` / `Transferred Out` / `Scrapped` |
| `DispositionNote` | string | 200 | `""` | Where it went / why. Required when leaving. |
| `RecordedAt` | DateTime | | | UTC |
| `RecordedBy` | string | 100 | `""` | `_currentUser.Name` |
| `UpdatedAt` | DateTime? | | null | UTC |
| `UpdatedBy` | string | 100 | `""` | |

`TrackedUnitStatus` is a static class in the same file, shaped like
`UnitStatus` in `CompressorUnit.cs` (`OnHand`, `TransferredOut`, `Scrapped`,
`All`). Don't reuse `UnitStatus`: its `Picked Up` doesn't apply.

### `AppDbContext`
- `public DbSet<TrackedUnit> TrackedUnits { get; set; } = null!;`
- In `OnModelCreating`: unique index on `Tag`, plain index on `Status`. Declare
  them here **before** running `migrations add`. An index the snapshot has but
  the model lacks raises `PendingModelChangesWarning`, and `Migrate()` aborts
  before any SQL runs (Pass 5).

### Migration
`dotnet ef migrations add AddTrackedUnits` (the 40th). It creates one empty table
and changes nothing that exists. The host picks it up automatically at startup
(`Program.cs` → `Migrate()`).

### `Services/UnitNomenclature.cs` (static, DB-free)
The one place the code tables live, so Kason's list is a single edit:

```
ProductTypes   = { "AC", "GE", "HP" }
Cabinets       = { "C", "D", "G" }
Voltages       = { "C", "D", "Y" }
BuildStatuses  = { "Not Built", "Ready" }
TestStatuses   = { "Not Tested", "Tested" }
Designations   = { "AHRI", "Reliability", "Field Trial" }
```
Also holds `Decode`, `ParseTonnage`, `FormatTonnage`, `ParseSample` and
`ShortCode` (§5). Dropdowns render from these arrays; there are no hand-typed
`<option>` lists anywhere.

## 4. Tag — PENDING (Kason's code list)

**Status: NOT DECIDED. Do not implement minting.**

**The shape Kason chose: `<G>-<CODE>-<NNN>`**, e.g. `C-RACG180D-001`.
- `<G>` is the first letter of `OrgStructure.GroupFor(unit.Line)`, taken at
  creation: C, R, S, or L for Lab Operations.
- `<CODE>` is `UnitNomenclature.ShortCode(unit)`, built from the **stored fields**
  (R + ProductType + Cabinet + NominalMbh as 3 digits + Voltage), not from the
  first 8 characters of the model. A full model like `RACGYB240ACG800BA` starts
  `RACGYB24`, which isn't the code.
- `<NNN>` is the next number for that `<G>-<CODE>-` prefix, 3 digits, spilling to
  4 or more past 999.

**These hold whatever rule is chosen:**
1. **Unique:** enforced by the unique index on `Tag`.
2. **Never reissued.** Take the next number from the highest seen across
   `TrackedUnits.Tag` **and** `TransactionLogs.ItemId` for that prefix. Unit
   history is read from logs by tag (§10), so a reissued tag would inherit a
   deleted unit's history. Item IDs already have this flaw; see the debug log.
3. **Can never equal an item ID.** Both live in `TransactionLog.ItemId`, and
   `ApplyLogVisibility` and the Command Center feed resolve by that string. Item
   IDs look like `^[A-Z]{3}-\d{4,}$`; a 1-letter first segment can't match.
4. **Fits in 30 characters.**

**Open, to settle with Kason's list:**
- A unit with no model, or only part of one decoded (no cabinet or no size): what
  goes in `<CODE>`?
- No voltage letter: a 7-character code (`C-RACG180-001`), or something else?
- Model corrected after the tag is minted: does the tag stay frozen (VIS
  freezes an item's group letter for the same reason), or get re-minted (which
  means renaming log rows, like the Pass 14 RCR rename)? **Recommendation:
  frozen.**
- Does the count run per `<G>-<CODE>` (as written) or per code across groups?
- What Kason's list adds: more product types, cabinets, voltages, or more
  positions in the code.

## 5. Model decoding, tonnage, Sample #

All in `UnitNomenclature`, all pure functions, used both by the server on save and
by the live read-out (§7.3). **No JavaScript copy of these rules.** The page asks
the server.

### 5.1 `Decode(raw)`
1. `norm` = `raw` with all whitespace removed, uppercased. This is what
   `ModelNumber` stores. More than 60 characters is refused ("Model is at most
   60 characters").
2. `s` = `norm` with only ASCII `A–Z` and `0–9` kept. Use explicit ranges, not
   `char.IsLetterOrDigit`, which also keeps non-ASCII letters and digits.
3. If `s` is shorter than 4, or `s[0] != 'R'`, or `s.Substring(1, 2)` isn't in
   `ProductTypes`: nothing decodes. All parts are blank and there is no error.
4. `Cabinet` = `s[3]` if it's in `Cabinets`, else blank.
5. Size: the **first** match of `[A-Z]([0-9]{3})(?![0-9])` in `s`. This is the
   same rule as `Airflow_Tool-1012/src/airflow_core.py` `TONNAGE_RE`, written with
   `[0-9]` because .NET's `\d` also matches non-ASCII digits. A match of 000
   counts as no size.
6. `Voltage` = the character right after the 3 digits (`s[match.Index + 4]`),
   only if it exists and is in `Voltages`; else blank.

Decoding **fills** the form fields; the person's own edits win after that, and
what's posted is what's stored. On save the server validates each posted field
against its table (blank always allowed) and does **not** re-derive it from the
model. If the model's decode disagrees with a stored field, the card shows a
hint ("model says HP, unit says AC"). It never blocks.

| Typed | Stored model | Type | Cab | Size | Volt |
|---|---|---|---|---|---|
| `RACG180D` | `RACG180D` | AC | G | 180 | D |
| `R AC G 180 D` | `RACG180D` | AC | G | 180 | D |
| `rhpg180` | `RHPG180` | HP | G | 180 | — |
| `RACD090` | `RACD090` | AC | D | 090 | — |
| `RACGYB240ACG800BA` | same | AC | G | 240 | — (`A` follows) |
| `RGEGYB300ADF40BBA` | same | GE | G | 300 | — |
| `RAC180D` | same | AC | — (`1`) | 180 | D |
| `XYZ123` | same | — | — | — | — |
| blank | blank | — | — | — | — |

### 5.2 Tonnage (`ParseTonnage` / `FormatTonnage`)
The form's **Tonnage** box takes either form Conner writes:
- Remove all spaces and uppercase first.
- A `TONS`, `TON` or `T` suffix means **tons**: `6.5T` → 78, `15 T` → 180. Strip
  the longest one first.
- No suffix and exactly 3 digits (`^[0-9]{3}$`) means the **code**: `078` → 78,
  `180` → 180.
- Any other number with no suffix means **tons**: `6.5` → 78, `15` → 180.
- Tons are parsed with
  `decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out …)`.
  **Not `NumberStyles.Number`**: it accepts thousands separators, so `6,5` would
  read as 65 tons and save as 780 instead of being refused.
- Tons × 12, rounded with `MidpointRounding.AwayFromZero`. The result must be
  1–999, else refused: "Tonnage is a 3-digit code (180) or tons (15, 6.5T)."
  Blank = unknown (null).
- Display: `FormatTonnage(78)` = `6.5 T (078)`, `180` → `15 T (180)`,
  `100` → `8.33 T (100)` (tons to at most 2 decimals, code padded to 3).

### 5.3 Sample # (`ParseSample`)
Blank (only whitespace) is allowed and stores `""`. Otherwise: strip spaces and
`#`, lowercase, then remove a leading `sample` or, failing that, a leading `s`.
What's left must match `^(10|[1-9])[a-z]?$`. Anything else is refused,
**including nothing left after the prefix**: "Sample # is 1 to 10 with an
optional letter: 1, 1a, 10b." Stored canonical (`1a`), shown as `Sample 1a`.
Conner's ceiling is 10, so `11` is refused.

| Typed | Stored |
|---|---|
| `Sample 1A` | `1a` |
| `S1a`, `sample #1a` | `1a` |
| `#3` | `3` |
| `10b` | `10b` |
| (blank) | `""` |
| `11`, `0`, `1ab`, `a`, `Sample`, `s` | refused |

### 5.4 `ShortCode(unit)`
`R` + ProductType + Cabinet + `NominalMbh:D3` + Voltage, only when ProductType,
Cabinet and NominalMbh are all set; else `""`. Used by the summary label and by
the tag (§4).

## 6. Line visibility

Units follow the item rule: Admin sees all, a blank unit Line is visible to
everyone, a viewer with a Line sees that Line, a viewer with only a Branch sees
that Branch's Lines, and a viewer with neither sees everything. **Units are
Line-scoped everywhere, including Search Center.** Items in search show other
Lines only so people can request a transfer, and units have no transfer.

**One definition, two thin appliers.** In `InventoryService`:
1. Extract the decision out of `ApplyLineVisibility` into
   `private List<string>? VisibleLinesLower()`.
   - Return `null` for no filter: Admin; a viewer with no Line and no Branch; or
     a viewer with a Branch that isn't in `OrgStructure.BranchLines`, which today
     falls through to `return query`.
   - A viewer with a Line gets `[myLine.Trim().ToLower()]`.
   - A viewer with only a Branch gets that Branch's Lines, lowercased.
2. `ApplyLineVisibility` becomes
   `lines == null ? query : query.Where(i => i.Line == "" || lines.Contains(i.Line.ToLower()))`.
   This is the same SQL meaning as today: single-Line equality becomes a
   one-element `IN`.
3. New `public IQueryable<TrackedUnit> ApplyUnitLineVisibility(IQueryable<TrackedUnit> q)`
   uses the same helper with the same predicate on `u.Line`.
4. `ApplyLogVisibility`: also load `allUnitTags` and `visibleUnitTags` (through
   `ApplyUnitLineVisibility`), and change the predicate to:
   ```
   log.ItemId == ""
   || (!allItemIds.Contains(log.ItemId) && !allUnitTags.Contains(log.ItemId))
   || visibleItemIds.Contains(log.ItemId)
   || visibleUnitTags.Contains(log.ItemId)
   ```
   Without this, a unit tag matches no item and reads as a "deleted item", which
   is visible to everyone. A deleted unit's logs stay visible to all, the same as
   a deleted item's.

`IsOwnLine` (in-memory) is unchanged.

## 7. Unit Tracker page

`Controllers/UnitTrackerController.cs`, so `HomeController` (2,400+ lines)
doesn't grow. Its constructor takes `UnitTrackerService`, `InventoryService`,
`AppDbContext` and `CurrentUserService`. The global `RequireNameFilter` already
covers a new controller. Add
`builder.Services.AddScoped<UnitTrackerService>();` in `Program.cs`.

`Services/UnitTrackerService.cs` holds every query and write: `Search(UnitFilter)`,
`Summary()`, `Add`, `Update`, `ChangeStatus`, `Delete`, tag minting (§4), log
writes (§10) and the duplicate checks. The controller holds no LINQ of its own,
and Search Center calls the same `Search`, so there's one filter definition.

### 7.1 Route and sidebar
- `GET /UnitTracker` → `UnitTrackerController.Index` → `Views/UnitTracker/Index.cshtml`.
- Sidebar link right after Search Center, label **Unit Tracker**, icon `bi-boxes`.
- **Active-link fix (required):** in `_Layout.cshtml`, `isActiveAction` compares
  the action name only (`currentAction`, read from
  `RouteData.Values["action"]`). A new controller's `Index` would light up
  Command Center too. The Settings page already does this today: both Command
  Center and Settings show active there.
  - Add `string currentController = ViewContext.RouteData.Values["controller"]?.ToString() ?? "Home";`.
  - Make `isActiveAction` require `currentController == "Home"`.
  - Add `isUnitTrackerActive` alongside `isSettingsActive`.
  - Leave `isIdentifyPage` as it is.

### 7.2 Layout, top to bottom
1. **Summary**, Conner's first question. One row per (ProductType, Cabinet,
   NominalMbh) over **On Hand** units the viewer can see, ignoring the list
   filters. Rows are sorted by type, cabinet, size; units with no size come last.
   - Columns: label (e.g. `HP · G · 15 T (180)`, blanks shown as `—`), **On
     Hand**, **Ready**, **Tested**, **Unassigned** (blank Designation),
     **Damaged**.
   - A totals row at the bottom.
   - Clicking a row applies those three filters to the list.
2. **Filters** (a GET form, so links can be shared):
   - The dropdown filters: ProductType, Cabinet, Tonnage (sizes that exist),
     Voltage, Build, Test, Designation. Build, Test and Designation each also
     offer "Not recorded" (value `__NONE__` matches blank).
   - Status: defaults to On Hand, with an "All" option.
   - Damaged only.
   - Location Parent / Major / Sub, the same markup and narrowing as Search
     Center's location filter.
   - A text box searching tag, model, sample, serial and notes.
   - Query parameter names match Search Center's (§9).
3. **Add Unit** button (Standard and up).
4. **List**, wrapped in `table-responsive`: Tag, Model, Type, Cab, Tonnage, V,
   Sample, Build, Test, Designation, Location (`LocationCodec.FriendlyPath` with
   blank Rack/Row/FDA), a Damaged icon, Status (when not On Hand), Updated.
   Clicking a row or pressing Enter opens the Unit Card.

### 7.3 Add Unit (modal, `modal-fullscreen-sm-down`)
Built for counting in the yard on a phone.
- **Model** box. Placeholder: `e.g. RACG180D: R · AC/GE/HP · cabinet C/D/G · size · voltage`.
  - As the person types (debounced about 250 ms), it calls
    `GET /UnitTracker/Decode?model=…&tonnage=…`, which returns JSON `{ model,
    productType, cabinet, mbh, voltage, tonnageText, error }`.
  - The response fills Type, Cabinet, Tonnage and Voltage, but **only fields the
    person hasn't touched in this form**.
  - It also shows a read-out line, e.g. `AC · G cab · 15 T (180) · V: D`.
  - If the session has expired, `RequireNameFilter` redirects the fetch to the
    sign-in page, so the response isn't JSON. Treat any non-JSON response as "no
    read-out" and stay silent; the save shows the real problem.
- **Type, Cabinet, Voltage, Build, Test, Designation:** `<select>`s from
  `UnitNomenclature`, each with a blank option.
- **Tonnage:** a text box, re-read through `Decode` on blur.
- **Sample #, Serial #:** text boxes.
- **Location:** three selects (Parent required) rendered server-side with
  `data-parent` / `data-major`. The rows come from the same Locations projection
  Search Center's filter uses (§13).
- **Line:** one `<select>` with an `<optgroup>` per Branch
  (`OrgStructure.BranchLines`), preset to the signed-in user's Line. A
  Branch-only or unassigned user gets no preset. Required.
- **Team:** active Teams plus a None option.
- **Damaged** checkbox, which reveals the damage note. **Notes** box.
- **How many like this:** 1–50, default 1. It creates that many rows, each with
  its own tag, in one transaction, with one log row each. If it's above 1 and
  Sample # or Serial # is filled in, the add is **refused**: "Sample # and Serial
  # belong to one unit. Add those one at a time, or leave them blank and set them
  on each card."
- **Buttons:**
  - **Save.**
  - **Save & add another:** after saving, the page reopens Add with Model,
    Type, Cabinet, Tonnage, Voltage, Build, Test, Designation, Location, Line and
    Team kept. Sample #, Serial #, Damaged and Notes are cleared. Carry the kept
    values through `TempData` (JSON); the page reopens the modal on load when
    it's present.
- The success toast names the tags minted, plus any duplicate warnings (§12).

## 8. Unit Card

`GET /UnitTracker/Card?id=` returns `PartialView("_UnitCardPartial")`, injected
into a `#unitCardModal` shell.
- **One shared partial for the shell and its script:**
  `Views/Shared/_UnitCardHost.cshtml`, included by both `UnitTracker/Index` and
  `Home/SearchCenter`. It holds `#unitCardModal` (markup like `#itemCardModal`:
  `modal-lg modal-dialog-scrollable modal-fullscreen-sm-down`), `openUnitCard(id)`
  (the `openItemCard` pattern: spinner, `fetch` with `X-Requested-With`, an error
  body on failure), and every handler the card needs.
- **Card HTML is injected with `innerHTML`, so `<script>` tags inside it never
  run.** Every behavior inside the card must come from **delegated** listeners in
  the host partial: the Edit toggle, location narrowing, the model read-out,
  status forms and the delete confirm. The Item Card works the same way.
- A unit the viewer can't see returns **404** (looked up through
  `ApplyUnitLineVisibility`). This differs from `ItemCard`, which is unfiltered so
  another Line's item can be transfer-requested; units have no such action.

### 8.1 Contents
- **Header:** the tag (large), the model, the decoded read-out, a Status badge
  (when not On Hand), a Damaged badge, and a mismatch hint (§5.1).
- **Fields:** Type, Cabinet, Tonnage, Voltage, Sample, Build, Test, Designation,
  Location, Line (+ its Branch), Team, Serial, Damage note, Notes, Disposition
  note (when it has left), Recorded at/by, Updated at/by.
- **History:** every log row whose `ItemId` is this tag, newest first, through
  `ApplyLogVisibility`.

### 8.2 Actions (shown by level; the server enforces each one separately, §11)
- **Edit** (Standard and up): reveals the same fields as Add, minus How many.
  Posts to `POST /UnitTracker/Update`.
  - Line and Team are read-only below Engineer.
  - If the unit's current Line, Team or location code is no longer active, keep
    it as an extra selected option labelled "(current)". `IsValidLine` and the
    pickers only know active values, so without this, saving an untouched form
    would fail or silently change the field. **Only validate a field the person
    actually changed.**
- **Transfer Out / Scrap** (Engineer and up, On Hand units only): a required
  note. Posts to `POST /UnitTracker/ChangeStatus`.
- **Return to On Hand** (Engineer and up, units that have left): a location is
  required, preset to the last one; the note is optional.
- **Delete** (Admin): an inline two-step confirm inside the card ("Delete" →
  "Click again to delete C-…"), so no second modal stacks.
  - Posts to `POST /UnitTracker/Delete`.
  - Removes the row and writes `Tracked Unit Deleted`; earlier log rows stay.
  - The tag is never reissued (§4).
- Every POST has `[ValidateAntiForgeryToken]`; the partial renders
  `@Html.AntiForgeryToken()` in each form.
- After a POST, redirect back to the Referer with a `TempData["Success"]` or
  `TempData["Error"]` toast. `_Layout` renders both on every page.
  `HomeController.SmartRedirect` is private, so give `UnitTrackerController` its
  own 3-line copy, falling back to `/UnitTracker`.

## 9. Search Center

`HomeController.SearchCenter` and `Views/Home/SearchCenter.cshtml`. Inject
`UnitTrackerService` into `HomeController`.

1. **The "Units" quick button already exists.** It sits in "Filter by type",
   linking `?mode=Filter&filterType=Unit`, and has been there since the first
   commit. Today it runs an item search on Type contains "Unit", which matches
   nothing. It becomes the way into unit results; it doesn't need adding.
2. **The Type filter `<select>`** gets a fixed `Unit` option right after "Any
   type".
   - It is `selected` when `ViewBag.FilterType` equals "Unit" (ignoring case),
     the same check the other options use.
   - `FilterTypeOptions` comes from item Types: drop any that equal "Unit"
     (ignoring case) so it can't appear twice.
   - When `filterType == "Unit"` (ignoring case), a second filter row appears.
     The show/hide runs client-side, in a `DOMContentLoaded` handler like the
     location filter's; the server ignores these fields unless Type is Unit:
     `filterUnitType`, `filterUnitCabinet`, `filterUnitMbh`,
     `filterUnitVoltage`, `filterUnitBuild`, `filterUnitTest`,
     `filterUnitDesignation`, `filterUnitStatus` (default On Hand),
     `filterUnitDamaged`.
3. **Which results come back.** `isFilterActive` means the same as in
   `InventoryService.Search`; Omni only runs when no filter is set.

   | Search | Items | Units |
   |---|---|---|
   | Nothing set (a plain visit) | none (as today) | none |
   | `filterType == "Unit"` | none (`Search()` is not called) | `UnitTrackerService.Search` with the unit filters + Branch + Location |
   | Any other Type, or Rheem PN or Brand set | as today | none |
   | Only Branch and/or Location (Type "Any") | as today | units matching Branch (`unit.Line` in the Branch's Lines) and Location (columns); **On Hand only** |
   | Omni | as today | Tag, ModelNumber, SampleNumber, SerialNumber, Designation, Notes, DamageNote, DispositionNote (lowercase `Contains`); **any status**, with a badge |

   Units are always Line-scoped (§6).
4. **`SearchResult`:** add `public List<TrackedUnit> Units { get; set; } = new();`.
   `HasResults` becomes `Items.Any() || Units.Any()`.
5. **Mode (required fix).** **The Advanced Filters form posts no `mode` field**,
   only the quick-filter links do. Today the action sets
   `Mode = mode ?? "None"`, then `"Filter"` only if
   `foundItems.Any() || mode == "Filter"`.
   - A Type=Unit search from the form finds no items by design, so Mode would
     stay `None`. `showHolo` would then be false, and the unit results would
     never appear.
   - Make the condition
     `foundItems.Any() || units.Any() || mode == "Filter" || isUnitType`, so a
     Type=Unit search with zero units still shows "No results found".
   - Omni is unaffected: it already sets `"Omni"` whenever `omniSearch` is
     non-blank.
6. **Holo viewer:** after the item rows, a "Units (N)" heading, then one row per
   unit with `data-unit-id` (and **no** `data-item-id`). Each row shows the tag,
   model and read-out, Sample, Build/Test/Designation badges, location, Line,
   Status and Damaged badges, and an **Open** button calling `openUnitCard(id)`.
7. **Row click and Enter (required change).** The delegated `.holo-row` click
   and keydown handlers near the end of `SearchCenter.cshtml` call
   `openItemCard(row.dataset.itemId)` for every `.holo-row`. They also write
   `row.dataset.itemId` into `#holo-add-itemId`. If unit rows reuse `.holo-row`,
   both handlers must check `row.dataset.unitId` **first**, call
   `openUnitCard`, and return.
8. Include `_UnitCardHost` on the page. The Omni box's "Items: N" line stays as
   it is.
9. **New Item Registry steering.** The Type box's placeholder in
   `_NewItemRegistryPartial.cshtml` reads `e.g. Motor, Unit...`, which invites
   registering a unit as an item. Change it to `e.g. Motor, EEV...`.
   `CreateItem` and `SubmitIntake` refuse Type "Unit" (ignoring case): "Whole
   units go in Unit Tracker, not the item registry." That keeps the filter's
   "Unit" value unambiguous.
   - Those two are the only ways an item gets a Type: Edit Details doesn't
     touch it, and held Intake batches only carry rows that passed
     `SubmitIntake`.

## 10. Logging

One `TransactionLog` row per unit per action:
- `ItemId` = the unit's tag.
- `ItemName` = the model, or `(no model)`.
- `User` = `_currentUser.Name`.
- `Timestamp` = `DateTime.UtcNow`.

| ActionType | QuantityChange | Details |
|---|---|---|
| `Tracked Unit Added` | +1 | `RACG180D · AC G 15 T (180) V:D · Sample 1a · Not Built / Not Tested / AHRI · at External Yard › Connex Area · Commercial Packaged/Splits` |
| `Tracked Unit Updated` | 0 | One `Field: old → new` per changed field, `; `-joined, blank shown as `—` |
| `Tracked Unit Transferred Out` | −1 | `→ <note>` |
| `Tracked Unit Scrapped` | −1 | `→ <note>` |
| `Tracked Unit Returned` | +1 | `→ <location>; <note>` |
| `Tracked Unit Deleted` | −1 if it was On Hand, else 0 | the tag and model, so the history explains itself |

- An edit that changes nothing saves nothing and logs nothing; the toast says
  "Nothing changed."
- **Don't reuse item ActionTypes** (`Scrap`, `Add`, `Item Deleted` and so on).
  The Command Center feed renames those as item events, and `ExportToCsv` sums
  `Scrap`. `Tracked Unit …` names fall through to the feed's default, which shows
  the ActionType as-is.
- **Why "Tracked Unit", not "Unit":** `Unit Logged` already exists. It's the
  compressor/motor roster's ActionType (`LogCompressorUnits` / `LogMotorUnits`).
  `Unit Added` next to it in View Logs would read as the same family.
- In the feed, a unit row reads `C-RACG180D-001: RACG180D` (from the `ItemName`
  snapshot). It isn't clickable, because `GetById(tag)` returns null.

## 11. Access levels

| Action | Level | Server check |
|---|---|---|
| Page, Card, Decode, Search Center unit results | any signed-in user | global `RequireNameFilter`; Line-scoped (§6) |
| Add | Standard (2) | `[RequireLevel(AccessLevels.Standard)]` |
| Update (all fields except Line/Team) | Standard (2) | same |
| Change Line or Team on an existing unit | Engineer (3) | inside `Update`: refuse the whole update if Line or Team changed and level < Engineer |
| Transfer Out / Scrap / Return | Engineer (3) | `[RequireLevel(AccessLevels.Engineer)]` |
| Delete | Admin (5) | `[RequireLevel(AccessLevels.Admin)]` |

A Viewer sees a read-only card and no Add button. Hidden buttons are cosmetic;
every rule above is enforced on the POST. `RequireLevel` answers AJAX with a 403
and redirects form posts to Command Center with the AuthError banner.

The split follows VIS's existing rule: adding is Standard, and changing ownership
or disposing of something is Engineer. Editing the count fields (build, test,
designation, sample, location) stays at Standard on purpose, because the people
doing counts are the ones updating them.

## 12. Edge cases

- **No model number:** allowed. Type, Cabinet, Tonnage and Voltage are entered by
  hand. A blank Tonnage gives a warning on save, not a refusal.
- **Duplicate warnings** never block. The save goes through and the toast names
  the other tag(s).
  - Same `SampleNumber` + `ModelNumber` + `Designation` on another On Hand unit.
    Skipped when the model or sample is blank.
  - Same `SerialNumber` (ignoring case, any status) on another unit.
  - Case-insensitive matching happens after materializing; EF can't translate
    `string.Equals(..., StringComparison)` (Pass 29 trap).
- **A unit leaves:** it drops out of On Hand counts and the summary. Its row and
  history stay. Its last location stays stored, and the list shows the Status.
- **A unit comes back:** Return (Engineer and up), with the location picked
  again.
- **Line changed** to one the editor can't see: the toast says so ("moved to X;
  it won't show on your Line").
- **Location or Line hidden in Settings later:** the stored code or string stays.
  `FriendlyPath` shows a hidden location's raw code, the same as stock does. The
  edit form keeps it selectable (§8.2).
- **Two people adding at once** could get the same tag; the unique index refuses
  the second. Catch it and show "Tag collision, try again" instead of a 500.
- **Deleting the highest-numbered unit** does not free its number (§4.2).
- **Rule edits:** a `UnitNomenclature` change (Kason's list) never re-decodes
  stored rows. Stored fields are what the person confirmed.
- **Phone:** the Add modal and the card go full-screen below 576 px with no
  horizontal scroll. Kason checks on a real phone: the in-app browser only proves
  layout, not real-device behavior (Pass 26).
- **Both themes:** contrast is measured through a real `SetTheme` POST and a
  re-fetch, not by toggling `data-theme` in the page (Pass 35).

## 13. Files

**New:**
- `Models/TrackedUnit.cs` (with `TrackedUnitStatus`)
- `Models/ViewModels/UnitCardViewModel.cs`
- `Services/UnitNomenclature.cs`
- `Services/UnitTrackerService.cs` (with `UnitFilter`)
- `Controllers/UnitTrackerController.cs`
- `Views/UnitTracker/Index.cshtml`
- `Views/UnitTracker/_UnitCardPartial.cshtml`
- `Views/Shared/_UnitCardHost.cshtml`
- `Views/Shared/_UnitFormFields.cshtml`: one field set for Add and the card's
  Edit, so the two can't drift.
- The migration.

**Changed:**
- `Data/AppDbContext.cs`: DbSet and indexes.
- `Program.cs`: register `UnitTrackerService`.
- `Services/InventoryService.cs`:
  - `VisibleLinesLower`, `ApplyUnitLineVisibility` and the `ApplyLogVisibility`
    change (§6).
  - `GetLocationFilterRows()`: make the body of `HomeController.BuildLocationFilterRows`
    public here; the HomeController method calls it, so both controllers share
    one projection.
- `Controllers/HomeController.cs`:
  - Inject `UnitTrackerService`.
  - `SearchCenter` integration (§9).
  - The Type "Unit" refusal in `CreateItem` and `SubmitIntake`.
- `Models/SearchResult.cs`: `Units`, `HasResults`.
- `Views/Home/SearchCenter.cshtml`: Type option, unit filter row, unit rows, the
  row-handler branch, the host include.
- `Views/Home/_NewItemRegistryPartial.cshtml`: placeholder.
- `Views/Shared/_Layout.cshtml`: sidebar link and the `isActiveAction` fix.

## 14. Passes

- **Pass 1** (after §4 is DECIDED): §3 through §13 in full.
- **Pass 2:**
  - **Bulk assign:** checkboxes on the list; set Designation, Build, Test,
    Location or Team (Line and Team need Engineer and up) across the selection.
    One transaction, one `Tracked Unit Updated` log per unit. Sample # is not
    bulk-settable.
  - **CSV export** of the filtered list (the list's columns, plus Line, Team,
    Serial, Notes and Status).

## 15. Verification (done means all of this, on the dev db)

1. `dotnet build`: **14 warnings**, the baseline. Anything else is new and gets
   explained.
2. The migration applies on startup against the backed-up dev db.
   `__EFMigrationsHistory` has 40 rows, the console has no
   `PendingModelChangesWarning`, and `PRAGMA integrity_check` is ok.
3. **Rule cases:** the decode table (§5.1), tonnage and sample table (§5.3) give
   exactly those results through `/UnitTracker/Decode` and through a real save.
   - Tonnage accepted: `078`, `6.5T`, `6.5`, `15`, `15 T`, `180`, `100`,
     `12.5`, `999`.
   - Tonnage refused: `120T`, `0`, `000`, `6,5`, `1000`, `abc`.
4. **Add:**
   - A single unit, then How many = 3: three distinct tags and three log rows.
   - How many = 3 with a Sample # is refused, and nothing is saved.
   - "Save & add another" carries the right fields.
5. **Edit:** the log lists exactly the changed fields, and a no-change save logs
   nothing.
6. **Levels, by raw POST as well as the UI:**
   - A Standard user changing Line is refused; Transfer Out is refused.
   - A Viewer gets a read-only card and a refused Add.
   - Engineer can change status; Admin can delete.
   - After deleting the highest tag, the next Add does **not** reuse it.
7. **Visibility:** a user on another Line (e.g. Colton Reather, Residential OD)
   can't see a Commercial unit on the page, in Search Center, in View Logs, in
   the Command Center feed, or by Card id (404).
8. **Refactor check:** View Logs row counts for an Admin and a Line-scoped user
   are identical before and after the §6 refactor, counting only rows that
   existed before. Search Center item results for the same queries are
   unchanged.
9. **Search Center:**
   - The Units button lists On Hand units.
   - Type=Unit filters narrow correctly.
   - Type=Unit submitted **from the Advanced Filters form** (no `mode` field)
     opens the viewer: units are listed, or "No results found" shows when there
     are none (§9.5).
   - Omni finds a unit by sample, serial and tag.
   - A unit row (click and Enter) opens the **Unit Card**, and an item row still
     opens the **Item Card**.
   - Rheem PN or Brand set → no units.
10. **Sidebar:** only Unit Tracker is highlighted on its page, and Settings no
    longer highlights Command Center.
11. **Both themes,** 375 px width, zero console errors, and zero server `fail`
    lines.
12. Put the dev db back the way it was (or document what stays), as earlier
    passes did.

## 16. Traps that apply here (from the handoff)

- `@` inside `<script>` or a JS comment in `.cshtml` is a Razor transition.
- A top-level `const` must be declared in a `<script>` tag that renders before
  any code that uses it. This matters for the host partial's include position.
- Never write `String(null)` into an `<option>`. Filter nulls server-side and
  guard on the client.
- `Program.cs` swallows migration failures. If the table "doesn't exist", read
  the console first.
- Modals: one at a time, all at z-index 2200. The unit card never opens a second
  modal (its delete confirm is inline), so it needs no chaining. If a
  modal-to-modal hop is ever added: `afterModalHides` is declared **only inside
  `SearchCenter.cshtml`**, not in `site.js`. Move it to `site.js` and delete the
  page copy; don't declare a second one. Pass 39 found a page-local copy of a
  helper silently replacing the `site.js` one.
- Keep the build at the 14-warning baseline. `SearchCenter.cshtml` line 417
  (`result.Mode`) is already one of the CS8602s, so new code around the holo
  viewer is where a 15th tends to appear.
- A fixed-colour surface (badges, the read-out box) must be checked in both
  themes. Prefer theme tokens (`--vis-card`, `--vis-text`, `--vis-text-muted`).

## 17. Open questions

**For Kason (blocks Pass 1):** everything under §4, Open.

**For Conner (on site the week of 2026-10-19; none block Pass 1):**
- Does "assign from here" need a **WO #** field?
- Are there in-between statuses (In Build, Testing) beyond the two pairs?
- Should units already given to the gas team and ATI be entered as Transferred
  Out history, or only what's left?
- Are there more Designation values beyond AHRI, Reliability and Field Trial?
- What do voltage letters C, D and Y mean? Until then, VIS shows the letter only.

---

## Debug log — spec checked against the code and the dev db (2026-10-13/14)

Every existing name this spec points at was grepped in the repo (outside
`.claude/worktrees/`) and found. The facts below were checked directly; the
fixes are already folded into the sections above.

**Verified true:**
- Build: `dotnet build --no-incremental` → 0 errors, **14 warnings**, the
  baseline the handoff states (NU1903 ×2, CS0114, CS8602 ×3, CS8714 ×3,
  CA1416 ×5).
- Dev db (`inventory.dev.db`, read-only): **39** migrations, so this is the
  40th. No `TrackedUnits` table yet. No item Type contains "unit". The longest
  Line name is 44 characters (fits 100). `Unit Storage` (code `UISE`) and
  `External Yard › Connex Area` (`ETRD` / `CNEA`) exist. `Plant Parking` is an
  inactive Parent, a real case for the "(current)" option rule in §8.2.
- Branches → group letter via `OrgStructure.GroupFor`: Commercial Air → C,
  Residential Air → R, Sustaining → S, Lab Operations → L; a blank or unknown
  Line → C.
- Test users exist as written: Colton Reather (Standard, Residential OD), Conner
  Walworth (Management, Commercial Packaged/Splits), Kason Woods (Admin,
  Commercial Packaged/Splits).
- §5's rules were run as a script against every case in the decode, tonnage and
  sample tables, plus the full model numbers on Conner's SharePoint sheet
  (`RACGYB180ACH7220A`, `RACGYB150ACC000BA`, `RGEGYB180ACF322BA`,
  `RGEGYB240AD402CA`, `RACGYB180AC000BA`). Each sheet model decodes to its type,
  cabinet G and its size, with no voltage. The sheet models were read off a
  low-res screenshot, so treat their exact characters as approximate.
- `RequireNameFilter` is global (`Program.cs`), so a new controller is covered.
  `RequireLevel` answers AJAX with a 403 and redirects form posts to Command
  Center.
- `bi-boxes` exists in Bootstrap Icons 1.11.1, the version `_Layout` loads.
- Only `CreateItem` and `SubmitIntake` set an item's Type. Edit Details
  doesn't.

**Found and fixed in this spec:**
1. **The Advanced Filters form posts no `mode`.** A Type=Unit search from it
   would never open the results viewer. Fixed in §9.5.
2. **`.holo-row` click and Enter handlers call `openItemCard` for every row.**
   Unit rows would open a broken Item Card. Unit rows are now handled first
   (§9.7).
3. **A "Units" quick button already exists** (since the first commit), linking
   `filterType=Unit` and matching nothing today. The spec now reuses it instead
   of adding one (§9.1). The Registry Type placeholder `e.g. Motor, Unit...`
   invites the collision, so it changes and Type "Unit" is refused (§9.9).
4. **The sidebar's active link checks the action name only**, so a new
   `Index` would light up Command Center. This already happens on Settings.
   Fixed in §7.1 (it fixes Settings too).
5. **`Unit Logged` is an existing ActionType** (the compressor/motor roster).
   Tracked-unit actions are renamed `Tracked Unit …` (§10).
6. **C# number parsing:** `NumberStyles.Number` reads `6,5` as 65 tons.
   `AllowDecimalPoint` plus the invariant culture is now required (§5.2).
   `\d` and `IsLetterOrDigit` match non-ASCII digits and letters, so explicit
   ASCII ranges are required (§5.1).
7. **Sample #:** `Sample` or `s` alone saved silently as blank. Now refused
   (§5.3).
8. **Card HTML is injected with `innerHTML`, so its scripts never run.** All
   card behavior moves to delegated handlers in the host partial (§8).
9. **`afterModalHides` and `SmartRedirect` are page- or controller-private.**
   The spec no longer assumes they're reachable from new code (§8.2, §16).
10. **Editing a unit whose Line, Team or location was hidden later** would fail
    `IsValidLine` or silently change the field. Fixed with the "(current)"
    option, validating only changed fields (§8.2).
11. **Tag reuse.** Minting from the highest *existing* tag would hand a
    deleted unit's number, and its log history, to a new unit. Minting now
    reads `TransactionLogs` too (§4.2).

**Found in existing code, outside this spec's scope:**
- **Item IDs can be reissued.** `InventoryService.GenerateItemId` took the
  highest *existing* `ItemId` for a prefix, so deleting the top-numbered item
  freed its number. The handoff said "numbers are never reused", which was true
  only when the deleted item wasn't the highest.
  - At risk in the dev db (and on the host per the handoff): `CCO-0001` was
    deleted 9/10 and still has log rows; no `CCO-` item exists. The next item
    minted with a `CCO-` prefix would have been `CCO-0001` again, showing the
    deleted item's Scrap of 10.
  - The other orphaned log IDs (`CCL-0001`, `CCL-0034`, `CMR-0028`, `CMR-0040`)
    sit below their prefix's current maximum and are safe.
  - **Correction (2026-10-14): the orphan scan above missed a reissue that had
    already happened.** A reused ID isn't an orphan, because it's live again.
    CCR-0255 (YRH083WAA, 8/27) took the ID of a test item deleted 8/19 and
    shows that item's two log rows as its own.
  - **Fixed separately as VIS-15 (2026-10-14).** `GenerateItemId` now counts
    every number ever used, across the tables a delete leaves behind. See
    `VIS_Debugging_Log.md`. §4.2's rule for unit tags is the same one.
