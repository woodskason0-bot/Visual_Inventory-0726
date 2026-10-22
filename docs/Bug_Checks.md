# Bug_Checks

Visual Inventory System — repository audit

**Reviewed:** October 20, 2026, America/Chicago (October 21 UTC)

**Source:** `C:\Users\woods\source\repos\woodskason0-bot\Visual_Inventory-0726`

**Local HEAD reference:** `master`, `94546b437131bad4220e8f79189a3a30cd2c0b49`

**Status:** Findings remain open. The audit did not implement application fixes. This report is being added to the repository as documentation only.

## Result

There are **13 actionable findings: 5 P1 and 8 P2**. Twelve were reproduced with isolated service/database checks; one HTML-injection path was established by source tracing and an executed JavaScript sink check. None is represented as a completed browser test.

The highest-impact problems concern reservation overlap, duplicate family IDs, lost stock updates, clean-database initialization, and unsafe toast rendering. The defects cluster around three inconsistencies: different actions use different stock-allocation rules; form snapshots are trusted after the underlying record changes; and database constraints do not enforce every business invariant.

P1 means prioritize in the next repair cycle because the issue can corrupt inventory/history, expose an injection path, or block initialization. P2 means a reproducible workflow or data-quality defect that should follow. These are remediation priorities, not claims that the defects have occurred in production.

| ID | Priority | Finding | Evidence | Relationship to previous audit |
| --- | --- | --- | --- | --- |
| BC-01 | P1 | Team-scoped orders ignore existing unscoped reservations | Service reproduction | Distinct from fixed VIS-1 |
| BC-02 | P2 | TC request completes with a non-TC motor despite TC stock elsewhere | Service reproduction | Distinct from fixed VIS-2 |
| BC-03 | P2 | Deleted or retired target stack silently falls back to primary | Service reproduction | New in this report |
| BC-04 | P2 | Stale Unit Tracker edit overwrites newer notes | Service reproduction | New in this report |
| BC-05 | P2 | Roster logging creates more on-hand records than physical stock | Service reproduction | Different entrance from fixed VIS-9 |
| BC-06 | P2 | Duplicate serials within one save cause a database exception | Service reproduction | New in this report |
| BC-07 | P2 | Separate orders can reserve the same TC motor | Service reproduction | Separate from BC-02 allocation |
| BC-08 | P2 | Short-pull correction accepts another team's stack | Service reproduction | Known minor, reconfirmed |
| BC-09 | P1 | Empty database cannot complete the migration chain | Migration reproduction | Separate from accepted startup error handling |
| BC-10 | P1 | Toast message can become active HTML through JavaScript escapes | Source + JavaScript sink reproduction | Different sink from fixed VIS-7 |
| BC-11 | P1 | Overlapping registrations can persist duplicate ItemIds | Two-context reproduction | Different race from fixed VIS-15 |
| BC-12 | P1 | Overlapping stock changes lose an update | Controlled stale-context reproduction | Known concurrency minor, reconfirmed |
| BC-13 | P2 | CSV export shifts columns when location contains a comma | Export reproduction | Known minor, reconfirmed |

## Scope and verification

The review covered the MVC controllers, inventory/order/unit services, EF model and migrations, relevant Razor/JavaScript entry points, and the existing handoff/debugging documents. Particular attention went to registration, stock changes, reservations, pickup/TC loans, unit rosters, Unit Tracker editing, short-pull corrections, and export.

The audit used a writable source copy at `C:\workspace\Visual_Inventory-0726-audit`. After testing, 211 corresponding source/project/document files were compared with the original checkout and had identical hashes. The temporary startup adaptation used for local hosting was restored. Only audit deliverables were subsequently added to this copy.

| Check | Result and limit |
| --- | --- |
| Original application build | Succeeded: 0 errors, 12 warnings, .NET SDK 10.0.401. Dependencies restored from the existing local cache. |
| Service/database reproductions | Used the actual application services with disposable SQLite databases. Service fixtures used `EnsureCreated`; migration behavior was tested separately using `Migrate`. |
| Positive controls | Valid stack targeting added stock correctly; two distinct serials fit a two-unit shelf; a single-shelf TC pickup produced the expected loan; oversized same-cart reservations were refused. |
| Model/migrations | No pending model-snapshot changes; 42 migrations discovered. The existing development-database copy had all 42 applied and initialized successfully. An empty database failed as described in BC-09. |
| Existing schema check | The development copy had a unique Rheem-part-number index and no unique ItemId index. |
| Local hosting | App reached its listening state against the copied development database. Ephemeral data protection was used for the temporary audit host because profile-key writes were restricted. Host-specific key-storage errors are not reported as product bugs. |
| Browser/HTTP | Browser session failed; direct localhost requests were denied with `EACCES`. No authenticated page walk, visual/layout assessment, or end-to-end form verification was completed. |
| Dependency security | No current vulnerability-feed audit: network restore was unavailable, and NuGet audit was disabled for offline restore. |
| Git provenance | Local HEAD was readable, but native Git access to the original checkout was denied. Clean-working-tree status, remote parity, and unpushed changes were not established. Findings apply to the inspected files, not a certified pristine commit. |
| Data protection | Production database was not opened or modified. Original development DB SHA-256 was unchanged before/after: `15D21A556D5576AA6FAB4A5AB3EBCD1DA4D11829905A264A33C725B302B7BD0D`. No email or external notification was sent. |

All source locations below are relative to the repository root and refer to this snapshot. The small reproductions use synthetic item names, teams, and serials.

## Findings

### BC-01 — Unscoped reservations disappear from team-scoped availability

**P1 · Reproduced · Reservation correctness**

**Location:** `Services/InventoryService.cs:679–715`; `Services/OrderService.cs:203–309`.

The team overloads subtract only pending lines with `oi.Team == team`. An existing blank-team line can consume the entire item, but is omitted when another user orders a particular team's stock. `Submit` accounts for overlapping claims within the current cart, so that earlier fix does not protect separate orders.

**Reproduction:** Give a control item Alpha quantity 5 and Beta quantity 5. A user with no matching team submits an unscoped order for 10. An Alpha member then requests 5. Both orders are accepted. The team availability and earlier-order availability checks both return 5, while total reservations are **15 against 10 on hand**. No concurrent requests are required.

**Impact:** Accepted orders promise unavailable stock, and the pickup queue can advertise an incorrect fulfillable quantity.

**Repair:** Define one reservation-overlap rule shared by submit, availability, pickup queue, and transfer logic. Account for blank-team reservations competing with scoped claims without changing the intentional availability of unscoped ordering.

**Acceptance check:** Repeat in both submission orders, with two distinct teams and within one cart. Total accepted claims must fit a feasible allocation; existing same-cart protection must remain intact.

### BC-02 — Pickup consumes an ordinary motor instead of the requested TC motor

**P2 · Reproduced · Wrong subtype fulfilled**

**Location:** `Services/OrderService.cs:606–819`, particularly the pull loop at `663–693`.

The loop chooses `take` from total quantity before considering how many TC motors remain owed. It stops once total quantity is satisfied, even if `remainingTc` is nonzero. The line then completes.

**Reproduction:** Motor V1 has quantity 2 / TC 0; V2 has quantity 2 / TC 2; both belong to Alpha. Submit quantity 1 / TC 1 without a preferred shelf and pick it up. V1 drops to 1, V2 stays at 2 TC, the order becomes **Completed**, and loan outstanding is **0**, although requested TC remains 1.

**Impact:** The recorded completed order does not satisfy the requested subtype. TC stock can remain untouched while an ordinary motor leaves.

**Repair:** Build a feasible total-and-TC allocation before changing stock. Honor shelf preference only where compatible with the requested subtype, and reject or explicitly split an unsatisfied TC request. Preserve the existing rule that loans reflect actual TC units removed.

**Acceptance check:** Mixed shelves in both sort orders, a preferred non-TC shelf, a TC shortage, and mixed ordinary/TC quantities. No completed line may silently owe TC units. Review the similar transfer-approval loop at `1638–1659`; that entrance was not separately executed here.

### BC-03 — An explicit stale stack ID changes the primary stack

**P2 · Reproduced · Wrong stock location changed**

**Location:** `Services/InventoryService.cs:1843–1856`; controller entry at `Controllers/HomeController.cs:520–568`.

After resolving an explicit variant ID among active variants, `pv ??= item.PrimaryVariant` silently replaces a missing target. This treats a stale selection the same as an intentionally omitted selection.

**Reproduction:** V1 contains 10; V2 is empty. Open an Add form for V2, then delete V2 from another page. Submit Add 5 with V2's original ID. The operation succeeds, changes **V1 to 15**, and logs an addition to V1.

**Impact:** The quantity and location history no longer describe the shelf the user selected. Other Modify Stock actions share this resolver.

**Repair:** Default to the primary variant only when no explicit target was supplied. Reject invalid, missing, foreign, or retired IDs with a reload message before any mutation.

**Acceptance check:** Stale Add, Scrap, Adjustment, and Location Transfer requests; malformed IDs; valid explicit IDs; and the intended omitted-target single-stack flow.

### BC-04 — A stale Unit Tracker form restores old notes

**P2 · Reproduced · Silent collaborative-edit loss**

**Location:** `Controllers/UnitTrackerController.cs:65–81,122–134`; `Services/TrackedUnitService.cs:226–315,318–335`.

The page snapshots all editable values. A full edit submits that snapshot, and `Update` assigns its notes over the latest stored notes without a version or original-value check.

**Reproduction:** Open a unit with “Original note.” A colleague saves “New note from colleague.” From the old form, change only the serial and save. The serial changes successfully, but notes revert to **“Original note.”** The log only records that notes were edited; the overwritten text is not recoverable from that log.

**Repair:** Add conflict detection or submit field-specific changes with original values. Apply the same conflict policy to note autosave. Logging note contents is not required to fix the overwrite and should remain a separate product decision.

**Acceptance check:** Two editors changing different fields, two note editors, and a stale full edit after autosave. The app must preserve unrelated newer values or present a clear conflict.

### BC-05 — Stale roster slots can create more records than shelf stock

**P2 · Reproduced for compressors · Motor sibling identified in source**

**Location:** `Services/InventoryService.cs:1137–1219,1245–1311`.

New roster rows validate that a stack exists, but do not check its remaining capacity after existing on-hand records. The UI's number of blank slots cannot enforce this invariant after another user fills a slot.

**Reproduction:** A compressor stack has quantity 1 and no recorded units. Two pages each contain its one blank slot. Submit serial ONE, then submit serial TWO from the stale page. Both calls succeed with no reported errors. The stack still has **quantity 1**, but now has **2 on-hand unit records**. This works sequentially, without a race during database writes.

**Impact:** Phantom roster entries undermine pickup, scrapping, and recorded-unit selection. The motor logger has the same missing capacity check against TC quantity; that sibling was source-reviewed, not independently reproduced.

**Repair:** Check capacity using current on-hand records plus all new rows in the batch, per active stack. Compressor capacity is quantity; motor roster capacity is TC quantity. Enforce the check atomically. Do not silently increase physical stock to accommodate a stale roster form.

**Acceptance check:** Sequential stale submissions, simultaneous submissions, multiple new rows, mixed edits/new rows, and the motor equivalent. The earlier VIS-9 retirement fixes must still pass.

### BC-06 — Duplicate serials in one submission escape validation

**P2 · Reproduced · Save fails and entered data is lost**

**Location:** `Services/InventoryService.cs:1183–1216`; `Controllers/HomeController.cs:2243–2271`; unique constraint in `Data/AppDbContext.cs:183`.

Each duplicate check queries persisted rows. Newly added rows in the same unsaved batch are not included, so duplicate serials reach the unique database constraint.

**Reproduction:** On a two-unit compressor stack, fill both blank rows with serial SAME and save. The call throws `DbUpdateException` / SQLite error 19 for `CompressorUnits.ItemId, CompressorUnits.SerialNumber`. **Zero rows are saved.** The controller's generic exception/redirect path does not preserve those edits.

**Repair:** Normalize and validate the entire proposed serial set before applying it, including edits and new rows. Keep the database constraint as the final guard and translate conflicts into useful validation with retained input.

**Acceptance check:** Duplicate new rows, an edit colliding with a new row, a serial swap, whitespace/case policy, and concurrent duplicate saves. Distinct serials within capacity must continue to save.

### BC-07 — TC availability is not reserved across orders

**P2 · Reproduced · Subtype overbooking**

**Location:** `Services/OrderService.cs:118–147,251–277` and fulfillment at `663–693`.

Submission validates total available quantity and clamps TC requested to the line quantity. It does not reserve or validate the available TC subset against pending orders.

**Reproduction:** One Alpha motor stack has quantity 5 / TC 1. Submit two separate orders, each quantity 1 / TC 1. Both are accepted. Pick both: the first completes with loan 1; the second also completes, but with **loan 0 while requested TC is 1**.

**Impact:** Two users can be promised the same special-purpose unit. This is an acceptance/reservation defect separate from BC-02, which occurs even when enough TC stock exists elsewhere.

**Repair:** Validate and reserve both total quantity and TC quantity across persisted pending orders and the current cart. Recheck subtype fulfillment before stock mutation; ensure cancellation, split, and partial pickup release the correct quantities.

**Acceptance check:** Two sequential TC orders, multiple cart rows, mixed teams/unscoped orders, cancellation, and partial pickup. Ordinary stock must not be treated as available TC stock.

### BC-08 — Short-pull correction accepts out-of-scope stacks

**P2 · Reproduced · Previously documented minor**

**Location:** `Services/OrderService.cs:1067–1131`, specifically stack selection at `1090`; POST parsing at `Controllers/HomeController.cs:2164–2197`.

The correction loop accepts any active stack belonging to the item. It does not apply the order line's Team restriction, although the correction form and subsequent pickup are team-scoped.

**Reproduction:** An Alpha line is flagged short. Its item has Alpha quantity 1 and Beta quantity 6. Include Beta's variant ID with correction 0 in the submitted correction map. The method changes **Beta from 6 to 0** and marks the Alpha line corrected.

**Impact:** This action can modify stock outside the set it presents for reconciliation. This is a server/form scope inconsistency; it is not a claim that the app's broader stock-adjustment permissions should become team-restricted.

**Repair:** Resolve an allowed correction set from the order line and reject unexpected IDs before changing any stack. Preserve intentionally unscoped lines.

**Acceptance check:** A crafted extra variant ID, retired/missing IDs, normal own-team corrections, and an explicitly unscoped line. Unexpected targets must leave all stock and statuses unchanged.

### BC-09 — Fresh database migration stops at the missing Users table

**P1 · Reproduced · New installation/rebuild blocker**

**Location:** `Migrations/20260619234634_AddUsersTable.cs:12–15`; `Migrations/20260620011043_SyncUsersTable.cs:12–19`; `Migrations/20260625200846_AddUserTeam.cs:14–19`.

`AddUsersTable.Up` is empty, and `SyncUsersTable.Up` contains only commented code. The later `AddUserTeam` migration attempts to add a column to `Users`, which does not exist when the chain starts from empty.

**Reproduction:** Create an empty SQLite database and call the application's `Database.Migrate()`. Migration `20260625200846_AddUserTeam` fails with **SQLite error 1: no such table: Users**. A separate app startup against a disposable empty file hit the same failure. The existing development copy, with 42 applied migrations, initialized successfully.

**Impact:** A clean installation or recovery that rebuilds solely from migrations cannot initialize. The first missing table may conceal later baseline problems; this test does not establish that Users is the only missing prerequisite.

**Repair:** Establish a safe, complete fresh-database baseline while preserving the upgrade path for existing installations. A new final migration alone cannot fix an earlier failing migration. Do not casually rewrite migration history on deployed databases.

**Acceptance check:** Empty database to latest; supported older database copies to latest; application seeding; and model-snapshot consistency. The already discussed startup exception-handling policy is not reopened as a separate finding.

### BC-10 — Toast text is interpreted as HTML after JavaScript escape decoding

**P1 · Source and executable sink proof · Browser exploit not executed**

**Location:** `Views/Shared/_Layout.cshtml:262–278,581–587`; stored-name entry/echo at `Controllers/HomeController.cs:627–629,703`.

The layout inserts TempData into a quoted JavaScript string using HTML encoding. HTML encoding leaves JavaScript backslash escapes intact. JavaScript decodes those escapes, and `showToast` interpolates the resulting message into HTML passed to jQuery `.append()`.

A permitted registrant can store an item name containing encoded markup. Another user's duplicate-part-number error includes that stored name. This gives the common toast sink a stored-input path, separate from the previously fixed autocomplete/intake/pickup sinks.

**Safe proof:** Use the literal name `\x3Cb data-audit-toast=confirmed\x3ETOAST-MARKER\x3C/b\x3E`. The framework HTML encoder preserves those escapes. Executing the same quoted-string interpolation and the source's actual `showToast` function produces **`<b data-audit-toast=confirmed>TOAST-MARKER</b>` at the HTML append sink**. The proof used only inert bold markup, a stubbed append sink, and no event handler, browser script execution, or network payload.

**Impact:** Stored text reaches a markup-capable sink; active markup can create an XSS path in users' authenticated pages. Full browser exploitation was not verified because browser/HTTP access was blocked.

**Repair:** Serialize server-to-JavaScript values safely and render message content with `textContent`/jQuery `.text()`. Keep trusted icon markup separate. Both the JavaScript-string boundary and the HTML insertion boundary need correction.

**Acceptance check:** Literal backslashes, quotes, newlines, `<`/`>`, the harmless marker above, and stored duplicate-PN errors. Messages must remain text without script syntax errors or added DOM elements.

### BC-11 — Concurrent registrations persist the same family ID

**P1 · Reproduced with two contexts · Identity/history corruption**

**Location:** `Services/InventoryService.cs:268–303,324–389`; `Controllers/HomeController.cs:668–702`; `Data/AppDbContext.cs:43–51`; `Models/InventoryItem.cs:26–27`.

ID generation reads the current maximum and checks for an existing candidate before insertion. Two registrations can both pass before either saves. `Id` is the database primary key; `ItemId` has no unique constraint. The existing development-copy schema confirmed that absence.

**Reproduction:** Two independent contexts generate an ID for Commercial/Control before either inserts. Both receive **CCL-0001**. Save two differently named items with distinct, valid part numbers. Both saves succeed, leaving **two InventoryItems with ItemId CCL-0001**.

**Impact:** Item lookup uses `FirstOrDefault` by business ID, and logs/order/unit associations also use that string. Different physical item families can share or cross-contaminate history and actions.

**Repair:** Enforce database uniqueness on ItemId, audit existing duplicates before adding the constraint, and allocate IDs atomically or retry on a uniqueness conflict. The historical “never reuse a deleted ID” logic remains necessary but does not provide concurrency protection.

**Acceptance check:** Synchronized registrations and intake registrations sharing a prefix must receive distinct IDs or a clean retry. Verify independent histories and preserve the VIS-15 deleted-ID behavior.

### BC-12 — Overlapping stock writes can overwrite each other

**P1 · Controlled interleaving reproduced · Previously documented concurrency minor**

**Location:** `Services/InventoryService.cs:1827,1928–1956,2235–2243`; `Models/ItemVariant.cs`; `Data/AppDbContext.cs:53–65`.

Stock changes use a tracked entity's read-modify-write quantity without a concurrency token or atomic arithmetic update. Any transaction begun for related unit moves starts after the stock read and does not by itself reject stale values.

**Reproduction:** Two contexts hold the same stack at quantity 10. A scraps 3 and saves. B applies Scrap 4 using the stale value 10 and saves. Final quantity is **6**, rather than **3**, while transaction logs correctly contain **7 total units scrapped**. The harness controlled the overlapping-read schedule; this was not a timed multi-browser load test.

**Impact:** Inventory totals disagree with their own transaction history. Inbound stock can likewise be overwritten by another update.

**Repair:** Use a supported version/concurrency check with retry or conflict feedback, or guarded atomic quantity updates. Keep quantity, TC quantity, roster effects, and logs consistent in the same operation. SQLite serializing writes does not prevent this stale-read overwrite by itself.

**Acceptance check:** Two scraps, add versus scrap, adjustment versus pickup, and recorded-unit changes. Verify final quantities and logs together. Task/delivery “first claim wins” uses a related pattern, but those races were not independently executed here.

### BC-13 — CSV location values can create extra columns

**P2 · Reproduced · Previously documented minor**

**Location:** `Services/InventoryService.cs:629–649`.

Export replaces commas in selected text fields and emits the remaining fields without CSV quoting. Location includes free-text rack/row components and is emitted directly. Type and ProjectCode are also emitted directly.

**Reproduction:** Export a stocked item with location `NWES.0.0.RACK, 1.0`. The output has **14 header fields but 15 data fields** because the comma in the location is interpreted as a delimiter. Subsequent quantities and ownership fields shift columns.

**Repair:** Use one CSV writer/escaping function for every field, preserving original text with proper quote escaping. Decide a spreadsheet-safe handling policy for formula-leading values as part of the same export change; formula execution was not tested here.

**Acceptance check:** Commas, embedded quotes, CR/LF, empty fields, Unicode, and formula-leading text. Parse the export with an independent CSV reader and assert exactly 14 correctly valued columns per row.

## Suggested repair sequence

1. Fix BC-10's two rendering boundaries and BC-09's fresh-database baseline.
2. Protect identity and quantity invariants: BC-11 and BC-12. Keep schema repair and concurrency behavior explicit.
3. Unify total/TC allocation and reservation behavior: BC-01, BC-02, BC-07, and the corresponding transfer paths.
4. Reject stale targets and protect collaborative edits: BC-03, BC-04, BC-05, BC-06.
5. Close the known scope/export issues: BC-08 and BC-13.

The grouped fixes should share tests where behavior overlaps, but each finding should retain its own acceptance case. A general concurrency fix will not solve BC-01 or BC-05: both reproduced with sequential requests.

## Previous findings and remaining coverage

`docs/VIS_Debugging_Log.md` records VIS-1 through VIS-15 as fixed. This report does not reopen those exact cases without evidence. In particular, intra-cart overselling, forced-TC loan accounting, units retired during removal, JSON serialization of Settings picker lists, closed-intake rejection, and deleted-ID history protection are distinct from the new paths described above.

Three previously documented minor findings were reproduced here: team scope on short-pull correction, stock concurrency, and CSV escaping. Other historical minor concerns—task/delivery claim races, deliveries whose recipient becomes inactive, intake quantity coercion, and email HTML encoding—remain follow-up items rather than newly reproduced findings in this report.

Intentional name-picker authentication, existing blank-Line fail-open rules, whole-organization Search visibility, the holdover Unit Tracker scope, and accepted visual design choices were treated as requirements, not bugs. No new authorization scheme or larger Unit Tracker implementation is implied by these recommendations.

A complete release check still needs authenticated browser workflows on an approved disposable database, visual checks at the supported viewport sizes/themes, external integration checks where configured, and a current dependency audit. Passing the build and these targeted checks does not certify those untested areas.

## Audit evidence and placement

Repository destination: `docs/Bug_Checks.md`. The October 20 audit initially saved the report in a local audit copy because its session could not write to the original checkout. On October 22, the user requested repository publication for a follow-up audit. A fresh checkout of `master` was used to prepare this documentation-only change; the remote branch still matched the audited revision `94546b437131bad4220e8f79189a3a30cd2c0b49`. The source findings above have not been re-audited or marked fixed as part of publication.

Local supporting scratch material is under `C:\workspace\vis-audit-tools`: `build.log`, `observations.json`, `harness/Program.cs`, `harness/Audit.csproj`, `toast-proof.mjs`, and `toast-observation.json`. The observations file includes build output before its JSON array; the expected clean-database exception is recorded as an unsuccessful operation, not an unexecuted attempt. These scratch files are supporting audit evidence, not a committed test suite. Database copies and configuration secrets are not part of this document.
