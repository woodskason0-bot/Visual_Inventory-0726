# Unit List (holdover) — Build Spec

Scoped 2026-10-14 with Kason. **Approved to build.** One pass. Schema change: one
new table, one migration.

This is the **holdover** for whole AC units: a plain list people can start filling
now, while the nomenclature charts and Conner's answers settle the full Unit Tracker
(`docs/Unit_Tracker_Spec.md`). The full tracker will **extend this same table** later
(more columns, a tag, decoding, statuses), so nothing typed here has to be moved.
Build exactly what is below and nothing from the full spec.

> **Changed after approval (2026-10-18): Package / Split is optional.** I wasn't told
> which each unit is and I don't have the nomenclature to tell, and this list is meant
> to be a long list with optional selections, so requiring it would have meant making a
> value up. It is now Package, Split or blank, and "Not set" is the default. That
> overrides every place below that calls it required: the §1 bullet, the `PackageType`
> row in §3, the Add form field in §6.3 and the validation rule in §8. A blank is stored
> as `""` (the column stays NOT NULL, so there is no new migration), the list shows a
> dash for it, the `pkg` filter in §6.2 gains "Not set", and the Added / Updated /
> Deleted log text in §7 leaves the Package / Split part out when it is blank. Everything
> else below stands as approved. The Where and Build boxes and the removed header line
> came after this spec too; they are in the handoff's Pass log, not here.

---

## 0. Before writing code

1. Read `CLAUDE.md`, `Behavior.md`, `docs/VIS_Handoff_State.md` (its OPEN section
   and Traps section at least) and `docs/vis-addendum-consolidated.md`.
2. Read every file in full before editing it. No blind edits.
3. Work against `C:\VIS_Inventory\inventory.dev.db` (wired by
   `appsettings.Development.json`), **never** `C:\VIS_Inventory\inventory.db`.
   Back the dev db up before the migration runs:
   `inventory.dev.db.pre-trackedunits-backup-<yyyyMMdd-HHmmss>`.
4. Grep the whole repo when reusing or changing a name, but ignore
   `.claude/worktrees/` (stale copies).
5. Run `dotnet build` after every real chunk. The baseline is **0 errors, 14
   warnings**. A 15th warning means this work added it; fix it or explain it.
6. Don't `git commit` or `git push` until Kason says so.

## 1. What Kason decided (2026-10-14)

- **A plain list/table. No tag yet.** Rows get the database's own `Id`, shown as
  `#12`. No minted ID of any kind. The tag rule waits on Kason's code list.
- **Model number is free text**, typed by whoever registers the unit.
- **A required Package / Split choice** next to it. *(Optional since 2026-10-18; see
  the change note at the top.)*
- **Sample # and Serial # are free text**, both optional, with no format rules.
- **Ownership down to the team:** Branch → Line → Team, **autofilled the way Bulk
  Intake does it**: preset from the signed-in user's own Line or Branch, and
  overwritten when a Team with a home Line is picked.
- **A Notes box per unit, editable by everyone.**
- **Delete: Engineer (L3) and up.**
- Units are information only. They can't be ordered, aren't counted anywhere else
  in VIS, and don't appear in Search Center yet.

## 2. Not in this pass

Do not build any of these; they belong to the full Unit Tracker:

- A tag, model decoding, tonnage, voltage, Build/Test status, Designation, WO #,
  location, Damaged, status (On Hand / Off Site / …), rebuild handling.
- Search Center / Omni integration, Command Center counts, CSV export, bulk edit.
- Line-scoped hiding of rows: **every signed-in user sees every row** (decided:
  units are visible across Lines).

## 3. Data model

### `Models/TrackedUnit.cs` → table `TrackedUnits`

Named `TrackedUnit` on purpose: the full tracker reuses this class and table. Not
`Unit`, which collides with `CompressorUnit` / `MotorUnit` / `UnitStatus` vocabulary.
Non-nullable strings default to `""`, matching `InventoryItem`, so queries need no
null checks. Use `[Required]` / `[MaxLength]` attributes the way `CompressorUnit.cs`
does.

| Property | Type | Max | Rule |
|---|---|---|---|
| `Id` | int | | PK. Shown as `#<Id>`. |
| `ModelNumber` | string | 60 | Required. Trimmed; stored as typed otherwise (case kept). |
| `PackageType` | string | 10 | ~~Required.~~ Optional since 2026-10-18: exactly `Package` or `Split`, or `""` for not recorded (see `TrackedUnitPackage`). |
| `SerialNumber` | string | 60 | Optional, trimmed. |
| `SampleNumber` | string | 30 | Optional, trimmed. No format rule. |
| `Line` | string | 100 | Required on Add; must pass `OrgStructure.IsValidLine`. |
| `Team` | string | 100 | Optional. An active `Teams.Name`, or `""`. `N/A` posts as `""`. |
| `Notes` | string | 2000 | Optional, free text, multi-line. |
| `RecordedAt` | DateTime | | UTC, set on Add, never changed. |
| `RecordedBy` | string | 100 | `_currentUser.Name` on Add. |
| `UpdatedAt` | DateTime? | | UTC, set on every saved change. |
| `UpdatedBy` | string | 100 | `""` until the first change. |

**Branch is not stored.** It's derived for display with
`OrgStructure.BranchFor(Line)`, the same way items do it. Line is what VIS stores
everywhere; storing Branch too would be a second field to keep in sync.

Put a static class in the same file, the same shape as `UnitStatus` in
`CompressorUnit.cs`:

```csharp
public static class TrackedUnitPackage
{
    public const string Package = "Package";
    public const string Split = "Split";
    public static readonly string[] All = { Package, Split };
}
```

Render the Package/Split picker from `TrackedUnitPackage.All`. No hand-typed
`<option>` list.

### `Data/AppDbContext.cs`

- `public DbSet<TrackedUnit> TrackedUnits { get; set; } = null!;`
- In `OnModelCreating`: `b.HasIndex(u => u.Line);` (the list filters on it).
  Declare it **before** running `migrations add`. If the snapshot has an index the
  model lacks, EF raises `PendingModelChangesWarning`, and `Migrate()` aborts
  before any SQL runs (handoff Pass 5 trap).

### Migration

`dotnet ef migrations add AddTrackedUnits`. This is the 40th migration; count
`Migrations/` to confirm 39 exist first. It must only **create** `TrackedUnits`.
Open the generated `Up()` and confirm it touches no other table. `Program.cs`
applies it at startup. `Program.cs` also swallows migration failures to the
console, so read the console if the table seems missing.

## 4. Who can do what

| Action | Level | Notes |
|---|---|---|
| See the page and every row | any signed-in user (L1+) | The global `RequireNameFilter` already covers a new controller. |
| Add a unit | Standard (L2) and up | Same tier as New Item Registry and Intake. |
| Edit Model / Package-Split / Serial / Sample | Standard (L2) and up | |
| Change Line or Team on an existing unit | Engineer (L3) and up | Matches item Ownership: moving ownership is a different trust than adding. |
| Edit Notes | **every signed-in user, Viewer included** | Kason: "editable by all". |
| Delete | Engineer (L3) and up | Kason: "deletable by all L3 and up". Any Line. |

**Every rule is enforced on the server.** Hidden buttons are cosmetic only.

- Use `[RequireLevel(AccessLevels.Standard)]` on Add and
  `[RequireLevel(AccessLevels.Engineer)]` on Delete.
- Update and Notes can't use the attribute alone, because Notes is open to L1.
  Use a separate `UpdateNotes` action with no level attribute. `Update` gets
  `[RequireLevel(AccessLevels.Standard)]`, and inside it refuses a changed Line or
  Team below Engineer.
- `RequireLevel` redirects a refused form post to Command Center with the
  AuthError banner, and answers an AJAX call with a 403.

## 5. Files

**New:**

- `Models/TrackedUnit.cs` (with `TrackedUnitPackage`)
- `Services/TrackedUnitService.cs`: every query and write. The controller holds
  no LINQ of its own.
- `Controllers/UnitTrackerController.cs`
- `Views/UnitTracker/Index.cshtml`
- the migration

**Changed:**

- `Data/AppDbContext.cs`: DbSet and index.
- `Program.cs`: `builder.Services.AddScoped<TrackedUnitService>();`
- `Views/Shared/_Layout.cshtml`: sidebar link and the active-link fix (§6.1).

Don't touch `HomeController` (2,400+ lines); this work needs nothing from it. The
Branch/Line/Team data is cheap to rebuild in the new controller (§6.3).

## 6. The page

### 6.1 Route and sidebar

- `GET /UnitTracker` → `UnitTrackerController.Index` → `Views/UnitTracker/Index.cshtml`.
  Title: **Unit Tracker** (the page keeps this name when the full tracker replaces
  the holdover).
- Sidebar link right after Search Center: label **Unit Tracker**, icon `bi-boxes`
  (it exists in Bootstrap Icons 1.11.1, which `_Layout` loads). Shown to every
  signed-in user.
- **Active-link fix (required).** `_Layout.cshtml` sets
  `isActiveAction = action => currentAction == action`, which checks the action
  name only. `UnitTrackerController.Index` would therefore light up the Command
  Center link too. The Settings page already has this bug: both Command Center and
  Settings show active there. Fix:
  - Add `string currentController = ViewContext.RouteData.Values["controller"]?.ToString() ?? "Home";`.
  - Make `isActiveAction` require `currentController == "Home"`.
  - Add `bool isUnitTrackerActive = currentController == "UnitTracker";` and use
    it on the new link.
  - Leave `isIdentifyPage` alone.
  - Check afterwards that Command Center, Search Center, View Logs, My Activity,
    Intake, Log Delivery and Deliveries still highlight correctly, and that
    Settings no longer lights Command Center.

### 6.2 Layout, top to bottom

Use Command Center's card language (`.grid-card`, `--vis-*` tokens), not Search
Center's dark style. It must read correctly in both themes.

1. **Header:** "Unit Tracker", plus one line of muted text: "Holdover list — model
   numbers, serials and samples while the full tracker is built."
2. **Count line:** `N units · P Package · S Split`, counted over the rows the
   current filters return.
3. **Add Unit** button (Standard and up; hidden below that).
4. **Filters:** a GET form so a filtered view can be linked. Fields:
   - `q`: text box. Lowercase `Contains` over ModelNumber, SerialNumber,
     SampleNumber, Notes, Line and Team.
   - `pkg`: Any / Package / Split.
   - `team`: Any, plus every Team that appears on at least one row.
   - `line`: Any, plus every Line that appears on at least one row.
   - Apply and Clear buttons.
5. **The table**, wrapped in `table-responsive`, newest first (`Id` descending).
   Columns:
   - `#` (Id)
   - Model
   - Pkg/Split
   - Serial
   - Sample
   - Branch › Line (Branch derived)
   - Team (`—` when blank)
   - Notes (first ~80 characters, ellipsis, full text in the `title` attribute)
   - Added (local date + by)
   - Updated (local date + by, or `—`)
   - Actions: **Edit** for everyone; **Delete** for L3+ only.
6. **Empty state:** "No units yet." Below Standard, add "Ask someone at Standard
   or above to add one."

All rendering is Razor, so text is encoded automatically. **Don't build any row
from strings with `innerHTML`**: that was VIS-7, stored XSS through exactly this
kind of free text. If JS has to build anything, use `textContent` /
`createElement` or `escapeHtml` from `site.js`.

### 6.3 Add Unit modal

Use `modal-fullscreen-sm-down`, because units get counted in the yard on a phone.
It posts to `POST /UnitTracker/Add` with `[ValidateAntiForgeryToken]` and
`@Html.AntiForgeryToken()` in the form.

Fields, in this order:

1. **Model number:** text, required, max 60. Placeholder: `e.g. RACG180D`.
2. **Package / Split:** radio buttons (`btn-check`). ~~Two, required, none preselected.~~
   Since 2026-10-18: Package, Split and a third, "Not set" (value `""`), preselected,
   and not required.
3. **Serial #:** text, optional, max 60.
4. **Sample #:** text, optional, max 30. Placeholder: `e.g. Sample 1a`.
5. **Branch:** a select with no `name` (it's never posted). It only drives the
   Line select.
6. **Line:** select, required.
7. **Team / Project:** select. First option `N/A — no team` (value `""`), then
   every active team.
8. **Notes:** textarea, 3 rows, max 2000.

**Branch → Line → Team autofill: copy Bulk Intake exactly.** The reference is
`Views/Home/Intake.cshtml`: the `in-branch` / `in-line` / `in-team` markup near
the top and the autofill IIFE in its script (search for `teamLines`).

- **The controller provides** (same as `HomeController.Intake()` does):
  - `ViewBag.OrgStructureJson` = `JsonSerializer.Serialize(OrgStructure.BranchLines)`
  - `ViewBag.TeamLinesJson` = active teams with a non-null `Line`, as `{ name: line }`
  - `ViewBag.MyLineJson` = serialized `_currentUser.Line`
  - `ViewBag.MyBranchJson`: if the user has a Line, `OrgStructure.BranchFor` of it;
    else their `Branch`; else `""`
  - `ViewBag.ActiveTeams` = active teams ordered by name
- **The page declares** `const orgStructure`, `const teamLines`, `const myLine` and
  `const myBranch` at the top of its own `<script>`.
- **Behavior:**
  - Branch options come from `orgStructure`. Changing Branch refills Line.
  - On load, a user with a Line gets Branch (derived) and Line preselected. A
    whole-Branch user gets Branch preset and must pick the Line. A user with
    neither gets nothing preset.
  - Picking a Team that has a home Line in `teamLines` overwrites Branch and Line
    to match. A team without one, or going back to N/A, changes nothing. Both
    stay editable afterwards.
  - **Re-seed on every open of the modal** (`shown.bs.modal`), so a second Add
    after a partly-filled first one starts clean.
- **Server side:** Line is required and must pass `IsValidLine`. Team must be `""`
  or an active team's exact name; `N/A` folds to `""`.

**Buttons:**

- **Save**
- **Save & add another:** posts with `addAnother=1`. After saving, the page reopens
  Add with **Package/Split, Line, Team and Model kept**, and Serial, Sample and
  Notes cleared. Carry the kept values through `TempData["UnitAddCarry"]` as JSON.
  When it's present, the page reopens the modal on load and fills those fields
  (filling Branch from the kept Line), instead of the user's own seed.

Success toast: `Unit #<Id> added (RACG180D, Split).` through `TempData["Success"]`.
`_Layout` already renders it on every page.

### 6.4 Edit modal

One modal for every user. It opens prefilled with the row's values. To avoid a
second request, put the data on the row as `data-` attributes, or embed one JSON
blob of the rows built with `JsonSerializer`, which escapes `<`.

- **Standard and up:** Model, Package/Split, Serial, Sample and Notes are editable.
  Branch/Line/Team are editable only at **Engineer and up**; for Standard they're
  shown disabled, with the small note "Moving a unit to another Line or Team needs
  Engineer access."
- **Viewer (L1):** every field is shown read-only except **Notes**. The Save
  button saves Notes only.
- **The form posts to one of two actions,** depending on the viewer's level:
  - `POST /UnitTracker/Update` (Standard+) takes every field.
  - `POST /UnitTracker/UpdateNotes` (anyone signed in) takes `id` + `notes` only
    and changes nothing else, whatever else is posted.
- **When an Engineer changes the Line:** the Branch/Line/Team cascade seeds from
  the row's current Line with `setLineCascade` from `site.js`, or by copying
  Intake's local `fillLines` logic. Use one approach on this page, not both. Picking
  a Team with a home Line overwrites Branch/Line, the same as Add.
- **A Line or Team that has since been hidden in Settings** must stay selectable as
  the current value, labelled "(current, hidden)". `OrgStructure.BranchLines` and
  the active-team list only hold active values. Without this, opening and saving
  an untouched row would fail `IsValidLine`, or silently blank the Team. **Validate
  Line/Team only when they actually changed.**
- **An edit that changes nothing** saves nothing, writes no log, and toasts
  "Nothing changed."

### 6.5 Delete

- Engineer and up only. **Delete** opens a small confirm modal reading "Delete unit
  #12 (RACG180D, Split)? This can't be undone. The audit log keeps a record."
- It posts to `POST /UnitTracker/Delete` with `[RequireLevel(AccessLevels.Engineer)]`.
- It's a hard delete of the row. The log row written at delete time is the record.
- Use one modal at a time. If the Delete button sits inside the Edit modal, chain
  through `hidden.bs.modal`. Simpler: keep Delete on the table row only, so it's
  never inside another modal.

### 6.6 After every POST

Redirect back to the Referer, falling back to `/UnitTracker`, with a
`TempData["Success"]` or `TempData["Error"]` toast. `HomeController.SmartRedirect`
is private, so give the new controller its own 3-line copy. Filters live in the
query string, so the Referer keeps the user's filtered view.

## 7. Logging

Write one `TransactionLog` row per unit per action. Write it **in the same
`SaveChanges`** as the change.

| Field | Value |
|---|---|
| `ActionType` | `Tracked Unit Added` / `Tracked Unit Updated` / `Tracked Unit Notes Edited` / `Tracked Unit Deleted` |
| `ItemId` | **`""`** (see below) |
| `ItemName` | `null` |
| `QuantityChange` | Added +1, Deleted −1, everything else 0 |
| `Details` | see below |
| `User` | `_currentUser.Name` |
| `Timestamp` | `DateTime.UtcNow` |

**Why `ItemId` is blank:** there's no tag yet, and a non-blank value that isn't an
item would be read as a deleted item. Blank is VIS's established "not about an
inventory item" marker (handoff Pass 14):

- The Command Center feed shows `ActionType` as the title and `Details` as the
  subtitle.
- View Logs shows it like any Settings action.
- `ApplyLogVisibility` shows it to everyone, which matches the list being visible
  to everyone.

**Don't** put the row id or model in `ItemId`.

**Don't reuse** `Unit Logged`. It's the compressor/motor roster's ActionType.

**`Details` formats:**

- **Added:** `Unit #12 · RACG180D · Split · Serial 25DD8075L · Sample 1a · Commercial Air › Commercial Packaged/Splits · Samurai`
  - Leave out the Serial / Sample / Team parts when blank.
- **Updated:** `Unit #12 (RACG180D): Model 'RACG180' → 'RACG180D'; Team '' → 'Samurai'; Notes edited.`
  - One `Field 'old' → 'new'` per changed field, joined with `; `, in table-column
    order.
  - Blank shows as `''`.
  - Notes changes are logged as `Notes edited` only, never their text. Notes can
    be long and are free for everyone to write.
- **Notes Edited:** `Unit #12 (RACG180D): notes edited.`
- **Deleted:** `Unit #12 deleted · RACG180D · Split · Serial … · Sample … · <Line> · <Team>`
  - Enough that the log alone says what was removed.

## 8. Validation and edge cases

**Handled in `TrackedUnitService`:**

- **Trim** every text field. A Model that is blank after trimming is refused:
  "Model number is required."
- **Length over the max** is refused with a message naming the field. **Never
  truncate silently.** Forms carry `maxlength` too, but the server is the check:
  forms here submit through `form.submit()`, which skips HTML validation (handoff
  Pass 39 and VIS-12).
- **PackageType** must be exactly `Package` or `Split` (case-insensitive in,
  canonical out). Anything else is refused. Since 2026-10-18 a blank is accepted and
  stored as `""`; on Update, a field that wasn't posted leaves the value alone and a
  posted blank clears it.
- **Line and Team** rules as in §6.3 and §6.4.
- **Duplicates warn, never block.** After a successful Add or Update, append a
  warning to the success toast when:
  - **Same serial:** another row has the same non-blank SerialNumber, compared
    case-insensitively. "Heads up: serial 25DD8075L is also on #7."
  - **Same model + Sample # with no serial:** another row has the same ModelNumber
    and SampleNumber (both non-blank, case-insensitive), and **neither row has a
    serial**. "Heads up: RACG180D Sample 1a is also on #9."
  - Compare after materializing (`.ToList()` and then LINQ to Objects). **EF Core
    can't translate `string.Equals(a, b, StringComparison…)` against a live
    query** (handoff Pass 29 trap). Use `.ToLower()` inside the query, or compare
    in memory.
  - Blocking on either case is an open question for Conner. Don't block.
- **A deleted or missing id** on Update, UpdateNotes or Delete gets the error toast
  "Unit #<id> no longer exists." No exception reaches the user.
- **Concurrency:** last write wins. VIS has no concurrency tokens anywhere, and this
  table doesn't add any.
- **Multi-line notes:** show line breaks in the Edit modal (a textarea does this).
  In the table, the truncated preview collapses them to spaces.

## 9. Traps that apply here (from the handoff)

- **`@` in `.cshtml`:** inside a `<script>` block or a JS comment, `@` is still a
  Razor transition. Writing `@RenderBody()` in a JS comment once took the dashboard
  down.
- **Script order:** declare `orgStructure` / `teamLines` / `myLine` / `myBranch`
  above any code that uses them, in the same or an earlier `<script>` tag. `site.js`
  loads before the page's own scripts, and its cascade helpers read `orgStructure`
  as a global at call time.
- **Markup the script touches** must exist before the script runs: put the script
  after the markup, or wait for `DOMContentLoaded`.
- **Modals:** one at a time, all at z-index 2200. Hop between modals with
  `hidden.bs.modal`, never by stacking.
- **Never build `<option>`s with `String(null)`**: it renders a real option called
  "null". Filter nulls on the server.
- **Fixed-colour surfaces** (badges, the Package/Split pills) must be checked in
  **both** themes. Measure contrast the reliable way: POST `SetTheme`, re-fetch the
  page, then read computed styles. Toggling `data-theme` in-page gives false
  readings (Pass 35).

## 10. Verification — done means all of this

Test live in the real app, the way earlier passes were verified.

**Setup:**

- Use a **copy** of `inventory.dev.db` in your scratchpad, so the dev db is never
  written.
- Start the app through the Browser pane's `preview_start`. It reads the **parent
  folder's** `.claude/launch.json` (`C:\Users\woods\source\repos\woodskason0-bot\.claude\launch.json`),
  not the VIS one. Add a temporary configuration there:

  ```
  dotnet run --no-build --project <repo>/Visual_Inventory_System.csproj
    --launch-profile http -- --contentRoot <repo> --urls http://localhost:5097
    --ConnectionStrings:DefaultConnection=Data Source=<scratchpad copy>
  ```

  Remove it afterwards.
- Sign in through the real sign-in page with a roster name (name only, no password).

**Checks:**

1. **Build:** 0 errors, 14 warnings.
2. **Migration:** applies on startup. `__EFMigrationsHistory` has 40 rows, no
   `PendingModelChangesWarning`, `PRAGMA integrity_check` ok, and only
   `TrackedUnits` is new (compare the table list before and after).
3. **Add, as Kason.Woods (Admin, Commercial Packaged/Splits):**
   - Branch and Line come preset. Picking Team Samurai keeps them; picking Falcon
     switches them to Residential Air › Residential OD.
   - Save a row and check every column in the database directly, not just the
     toast.
4. **Save & add another:** the modal reopens with Model, Package/Split, Line and
   Team kept, and Serial/Sample/Notes empty.
5. **Server-side refusals, with a raw `fetch` POST that bypasses the form**
   (antiforgery token included):
   - blank Model
   - Package = `Packaged`
   - Model of 61 characters
   - an unknown Line
   - an inactive Team

   Each is refused, and the row count is unchanged.
6. **Access levels, by signing in as real roster users of each level** (check the
   `Users` table for current names and levels):
   - **Viewer:** sees the list and no Add button. Edit shows read-only fields
     except Notes, and saving Notes works. A raw POST to `Update` and to `Add` is
     refused.
   - **Standard** (e.g. Colton Reather, Residential OD): Add works. Edit works on
     Model/Serial/Sample/Notes. A raw POST changing Line is refused, and Delete is
     refused.
   - **Engineer:** can change Line/Team and can Delete.
   - **Whole-Branch user** (e.g. Karthig Kathirvel or Luis Zapata): Add presets
     Branch only and requires picking a Line.
7. **Logging:**
   - Each action writes exactly one row, with blank ItemId and the Details formats
     in §7.
   - A no-change Update writes nothing.
   - Notes text never appears in a log row.
   - The rows show in View Logs and in the Command Center feed, and for a user on
     another Line too.
8. **Duplicate warnings:** both warnings fire as described, and neither blocks.
9. **A Line hidden in Settings** (or an UPDATE on your copy setting
   `OrgLines.IsActive = 0`): a row on that Line opens in Edit with the Line shown as
   "(current, hidden)", and an untouched Save gives "Nothing changed."
10. **Sidebar:** only Unit Tracker lights up on its page, and Settings no longer
    lights Command Center.
11. **XSS check:** a Model / Notes value of `<img src=x onerror="document.body.dataset.x=1">`
    renders as literal text in the table, the title tooltip and the Edit modal, and
    `document.body.dataset.x` stays unset.
12. **Both themes and 375 px:** both themes after a real `SetTheme`; zero contrast
    failures on the new page; at 375 px no horizontal page overflow and the modals
    go full-screen; zero console errors, zero server `fail` lines.
13. **Real phone:** the in-app browser only proves layout, not real-device behavior
    (Pass 26). List "Kason checks on his phone" as the one thing left.

## 11. When it's verified

Report to Kason, without committing:

- the files changed
- the migration name
- the checks run, with real numbers
- anything that didn't behave as this spec says, and anything this spec got wrong

Write the docs **after** Kason says commit, in the repo's established pattern:

- **The code commit first.**
- **Then a docs commit** that contains:
  - a `Commit_History.md` entry naming that hash
  - a handoff OPEN line: committed, not pushed, in no release, migration 40 included
  - a handoff Pass log entry

Project narrative docs (`docs/`) are written in Kason's first-person voice.

## 12. Open, not for this pass

These come from the full Unit Tracker scope and are waiting on Conner (on site the
week of 2026-10-19) or on Kason's nomenclature charts:

- **For Conner:** should a duplicate model + Sample # with no serial block?
- **For Conner:** how to tell apart rebuilt units with no nameplate and no Sample #.
- **For Conner:** in-between statuses, the units given to the gas team / ATI,
  extra Designations, and what voltage letters C/D/Y mean.
- **Waiting on the charts:** the tag rule, decoding, required voltage, and a
  sanity check on tonnage.
