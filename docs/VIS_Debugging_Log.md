# VIS — Bugs and Debugging Log

Filed in this repository on **2026-10-03**, split from the shared OneDrive debugging records. This is the VIS-specific home for subsequent debugging session notes.

**Recorded status:** VIS-1 through VIS-6 were fixed in `1df2e45` and staged as `VIS_Release_20260925`. The latest recorded VIS deployment status still says not installed on the host; installation has not been checked again here. VIS-7 through VIS-14 were reported open in the 2026-09-26 audit. The local HEAD at filing was `a35850b`, the same revision examined by that later audit.

**Update 2026-10-04:** VIS-7, VIS-8 and VIS-10 through VIS-14 are fixed in `0b03c94` (Pass A), `a077663` (Pass B) and `d39553d` (Pass C), committed and pushed; none of it is in a release yet. VIS-9 was held back as a new mechanic, scoped on 2026-10-04 and built the same night as Pass D (`9cfea2a`), also committed, pushed and in no release. That leaves no audit item open in code; the two stacks that already carry the drift still need a data repair by hand. See the 2026-10-04 entries below.

**Update 2026-10-14:** VIS-15 is new, found while checking the Unit Tracker spec: a deleted item's ItemId was handed to the next registration with the same prefix, history and all. Fixed and verified the same day in `1ca6651` (committed, not pushed, in no release); see the 2026-10-14 entry below. CCR-0255 already carries one reissue in the data.

**Update 2026-10-26:** the 2026-10-20 audit's 13 findings (BC-01 to BC-13) are recorded in `docs/Bug_Checks.md`, not repeated here. BC-10, the toast rendering its message as HTML, is fixed and verified in `282ad8e` (committed, not pushed, in no release); see the 2026-10-26 entry below. The other 12 are still open.

**Update 2026-11-09:** BC-01 and BC-07 are fixed and verified in the Reservations work, `3c08bd6` (migration 43, committed, not pushed, in no release); see the 2026-11-09 entry below. Ten BC findings are still open.

**Update 2026-11-16:** BC-03, BC-05, BC-06, BC-08, BC-13 and BC-14 are fixed, BC-02 is fixed for the case where the TC is on the shelves, and BC-15 is new and fixed. All verified before and after in the running app, none committed yet; see the 2026-11-16 entry below. Open: BC-04, BC-09, BC-11, BC-12, BC-02 when the TC isn't there, and BC-13's formula-leading values. Each of those needs a decision, not just code. Later the same day Kason ruled on BC-02's shortage case and it was built: see "the Non-TC Motor question" below. Then BC-04 was ruled and built: see "BC-04, two people editing one unit" below. All of it is committed as `0f949b3`, in no release. Pushed the same night: every commit named in these update lines is on GitHub.

**Update 2026-11-18:** BC-11 and BC-12 are fixed, and BC-13's formula-leading values at Kason's yes. Each was reproduced on the current build and checked on the fixed one in the running app, against copies of the 11/11 host db; committed as `fe9f8e6`. BC-11 brings migration 45. See the 2026-11-18 entry below. Open: BC-09, as ruled.

**Update 2026-11-18 (release):** everything above from `282ad8e` to `fe9f8e6` is in `VIS_Release_20261118`, built from `ade3271` and staged on `MASTER128`, not installed. The Deployment status lines in the entries below predate it.

This filing reorganizes existing evidence; it does not rerun the tests, apply fixes, or confirm today's production state. Test results and live-data observations below retain their original session dates. Source links open the current repository files; cited line numbers refer to the revision audited in that session.

## Issue register

The rows below preserve the latest recorded per-issue status. Historical findings later in this file describe the behavior before the fixes.

| ID | Priority | Issue | Recorded status |
|---|---|---|---|
| VIS-1 | P1 | Separate cart rows overbook the same stock | Fixed in 1df2e45; in VIS_Release_20260925, not installed |
| VIS-2 | P1 | Forced TC motor pickup omits loan/unit updates | Fixed in 1df2e45; in VIS_Release_20260925, not installed; transfer approval too |
| VIS-3 | P1 | Return/scrap unit selections lack quantity and loan checks | Fixed in 1df2e45; in VIS_Release_20260925, not installed; Return and Scrap both tested |
| VIS-4 | P1 | Location merges strand physical-unit records | Fixed in 1df2e45; in VIS_Release_20260925, not installed: automatic for whole-stack and whole-TC moves, unit picker for partial moves |
| VIS-5 | P2 | Motor pickup selects another shelf's unit | Fixed in 1df2e45; in VIS_Release_20260925, not installed; transfer approval too |
| VIS-6 | P2 | Partial pickup leaves issued units reserved | Fixed in 1df2e45; in VIS_Release_20260925, not installed; every reservation consumer |
| VIS-7 | P1 | Stored XSS: item name/PN, serial and lab # rendered through innerHTML (item autocomplete, Intake review, Pickup Queue serial picker) | Fixed in 0b03c94 (Pass A); not in a release |
| VIS-8 | P1 | Intake matches "same shelf" by exact FdaString while Modify Stock/Transfer/Return write a padded shape: Adjustment refused, Add mints a duplicate stack | Fixed in a077663 (Pass B); not in a release. The 8 padded stacks are untouched: the match now accepts either shape |
| VIS-9 | P2 | Scrap, downward Adjustment, short-pull correction and "No serial" pickups leave recorded units On Hand past the stack's quantity | Fixed in 9cfea2a (Pass D); not in a release. The drift already on CCR-0005 V1 and CCR-0213 V2 is not repaired by the code: it needs a data repair by hand |
| VIS-10 | P2 | Cancel has no status guard or log: a stale page flips a picked-up order to Cancelled | Fixed in 0b03c94 (Pass A); not in a release |
| VIS-11 | P2 | Delete Item ignores loans with no unit rows (Controls); the loan can't be returned afterward | Fixed in 0b03c94 (Pass A); not in a release |
| VIS-12 | P2 | New Item Registry accepts a negative quantity (form.submit() skips min; no server bound) | Fixed in 0b03c94 (Pass A); not in a release |
| VIS-13 | P2 | AccessLevel/Line/Branch cached in the session: demoting or hiding a user doesn't reach an open session | Fixed in d39553d (Pass C); not in a release |
| VIS-14 | P2 | Intake mints the ItemId prefix from the submitter's Line, Registry from the item's; whole-Branch users always get C | Fixed in a077663 (Pass B): the item's Line decides, as Registry does; not in a release |
| VIS-15 | P2 | GenerateItemId counts live items only: deleting the top-numbered item of a prefix reissues its ItemId, and the new item inherits the old one's logs, unit rows and order lines | Fixed in 1ca6651; not in a release. CCR-0255 was already reissued (8/27) and still shows the deleted test item's two log rows: a data decision |
| BC-10 | P1 | Toast text is HTML-encoded into a JS string (backslash escapes survive) and then inserted as HTML, so a stored item name can become markup in the success or duplicate-PN toast. From `docs/Bug_Checks.md` | Fixed in 282ad8e; not in a release |
| BC-02 | P2 | A TC line is filled with a plain motor while TC stock sits on another stack | Fixed 2026-11-16 in 0f949b3; not in a release: TC taken where it is; with none, the line waits for the Non-TC Motor answer (migration 44) |
| BC-03 | P2 | A stale stack id on Modify Stock changes the primary stack instead | Fixed 2026-11-16 in 0f949b3; not in a release |
| BC-04 | P2 | A Unit Tracker Edit form left open saves over someone else's newer change (their notes put back to the old text) | Fixed 2026-11-16 in 0f949b3; not in a release: a stale save is refused, and the open form shows who else has the unit open |
| BC-05 | P2 | Roster logging (and serials on Add) records more units than the stack holds | Fixed 2026-11-16 in 0f949b3; not in a release |
| BC-06 | P2 | The same serial twice in one roster save throws and saves nothing | Fixed 2026-11-16 in 0f949b3; not in a release |
| BC-08 | P2 | A short-pull correction changes another team's stack | Fixed 2026-11-16 in 0f949b3; not in a release |
| BC-11 | P1 | Two registrations at the same moment get the same ItemId (no unique index) | Fixed 2026-11-18 in fe9f8e6; not in a release: unique index (migration 45), the next number on a clash |
| BC-12 | P1 | Two stock changes at the same moment: the second saves over the first | Fixed 2026-11-18 in fe9f8e6; not in a release: Modify Stock and Quick Add lock before they read |
| BC-13 | P2 | CSV export shifts columns on a comma in Location, Type or Project Code | Fixed 2026-11-16 in 0f949b3; not in a release. Formula-leading values fixed 2026-11-18 in fe9f8e6 |
| BC-14 | P2 | Modify Stock's new-stack Team picker stays on screen after switching actions | Fixed 2026-11-16 in 0f949b3; not in a release |
| BC-15 | P2 | Reserve dialog: one new serial typed on two stacks hits the unique index, shows a raw SQLite error and loses the form | New and fixed 2026-11-16 in 0f949b3; not in a release |

## 2026-09-24 — Initial VIS business logic audit

Reviewed commit `25fda76495149e959330080eafec2ba6236881e5`. Six VIS defects reproduced in seven scenarios, plus a passing normal motor order → pickup → return control. The actual application services ran against disposable SQLite backup-API copies of the development database. The original database and application source were unchanged. The project compiled with existing warnings.

The initial findings are preserved below as historical evidence. VIS-1 through VIS-4 were P1; VIS-5 and VIS-6 were P2. All six were subsequently fixed as recorded in the September 25 session.

**VIS-1: One cart can reserve more units than exist.**

During submission, each cart row checks database availability independently. Earlier rows in that same submission have only been added to EF's change tracker; they are saved together after the loop. They therefore do not reduce the availability seen by later rows. The cart explicitly supports separate rows for the same item with different location preferences.

Reproduction: create an item with 5 units total across two locations; add two separate cart rows of 4 units, with different preferred locations; submit. The order is accepted and reserves **8 units against 5 physically available**. At pickup, one row can consequently be refused as a short pull despite the order having been accepted.

Fix direction: maintain cumulative allocations for the whole draft while validating it, including overlap between unrestricted and team-scoped stock pools. Keep validation and persistence in the existing transaction.

Source: [OrderService.cs:227](../Services/OrderService.cs), together with the additions at line 238 and the save after the loop.


**VIS-2: Thermocoupled motors can leave without a loan or unit-status update.**

The pickup loop correctly forces TC stock to be consumed when a shelf lacks enough ordinary motors. However, `pulledTc` is calculated as requested TC minus unfulfilled requested TC, rather than the sum actually removed. Forced TC above the requested amount disappears from loan and unit accounting. The transfer approval path repeats the same calculation.

Reproduction: shelf quantity 1, TC quantity 1, with one On Hand motor-unit record; order 1 motor with TC request 0; pick it up. Both shelf counts become **0**, but **LoanOutstanding stays 0 and the physical-unit record still says On Hand**. The user has no outstanding loan to return through the normal workflow.

Fix direction: sum actual TC taken inside the pull loop and use that total for motor-unit transitions and loan accounting. Apply the same actual-consumption rule to transfer approval. The pickup case was executed; the matching transfer calculation was confirmed by code inspection.

Source: [OrderService.cs:613](../Services/OrderService.cs); corresponding transfer calculation at [line 1434](../Services/OrderService.cs).


**VIS-3: Compressor return/scrap selections are not constrained to the quantity or loan being settled.**

Return and Scrap update every selected Picked Up compressor record for the item, independently of the quantity used to update stock and LoanOutstanding. The query also omits `OrderItemId`, so it can select another person's outstanding unit of the same model. Checking ownership of the submitted loan does not protect the selected unit records.

Two reproductions:

- Pick up 2 compressors, leave both unit checkboxes selected, change Return qty to 1, and return. Result: **1 unit on the shelf, 2 unit records On Hand, and 1 still outstanding on the loan**. This input is possible through the ordinary form: all unit checkboxes start selected, and changing quantity does not reconcile them.
- Submit your own loan ID but select a unit ID from another user's loan for the same item. Return succeeds, your outstanding count becomes 0, and the other user's unit becomes On Hand while their outstanding count remains 1. This second scenario requires a modified request; the normal page only lists the current line's units.

Fix direction: validate all selected units against the exact loan line, reject selection counts above the quantity, and reconcile named/anonymous unit dispositions with the quantity before making any changes. Apply the same checks to Return and Scrap. Both return scenarios were executed; Scrap's equivalent missing checks were confirmed by inspection.

Source: [OrderService.cs:1092](../Services/OrderService.cs), [Scrap query at line 1189](../Services/OrderService.cs), and [checkbox copying at MyActivity.cshtml:427](../Views/Home/MyActivity.cshtml).


**VIS-4: Location merges move quantities but strand the physical-unit roster.**

Location Transfer changes source and destination stock quantities and can retire the source variant, but does not move the associated On Hand compressor or motor unit records to the destination variant. A whole-pile relocation that keeps the same variant ID avoids this particular problem; merges and splits need explicit roster handling.

Reproduction: put a serialized compressor on shelf A; merge all of A into existing shelf B. The source becomes retired and B correctly has 2 units, but the serial still points to A. Picking that real serial from B is rejected as **already on record at a different location**. This can make ordinary stock movement break the next legitimate pickup.

Fix direction: move the affected unit records with the stock in the same transaction. Moving an entire source is unambiguous; a partial move needs a clear way to identify which tracked units moved. The full merge and subsequent refused pickup were executed; the split path's omission was confirmed in code.

Source: [InventoryService.cs:1879](../Services/InventoryService.cs).


**VIS-5: TC motor pickup can attach the wrong shelf's physical unit to an order.**

Stock is removed from the selected location/team, but motor-unit selection queries the entire item and takes the oldest On Hand records. It does not follow the variants actually pulled, unlike the compressor path.

Reproduction: one TC motor on each of two shelves, each with a distinct lab number; shelf A's record is older. Request and pick up the motor from B. **B's quantity becomes 0, but A's lab-numbered unit is marked Picked Up and B's stays On Hand.** This makes the unit's history disagree with the actual shelf and can affect team attribution on split items.

Fix direction: record TC taken per variant and transition motor records only from those variants, filtering in-memory statuses as necessary across multiple lines. Transfer approval has the same item-wide lookup; pickup was executed and the transfer path inspected.

Source: [OrderService.cs:685](../Services/OrderService.cs); transfer counterpart at [line 1469](../Services/OrderService.cs).


**VIS-6: Partial pickup leaves already-issued quantities reserved while another line is pending.**

Availability subtracts every order line whose parent order is Pending, regardless of the line's own status. A partial pickup marks the original line Split and creates a new pending order for its remainder. If another line keeps the original order Pending, its already-picked quantity is subtracted again from the remaining shelf stock.

Reproduction: start with 10 compressors, order 3 plus a different item, partially pick 1 compressor and leave the other item pending. There are **9 on shelf and 2 still requested**, so 7 are available. VIS reports **6** because it still reserves the original Split line's 1 unit as well.

Fix direction: include only genuinely pending lines in reservation and earlier-allocation queries, and check other reservation consumers for the same parent-status-only assumption.

Source: [InventoryService.cs:626](../Services/InventoryService.cs), with equivalent predicates at lines 649, 660 and 674.


## 2026-09-25 — VIS fixes and verification


Date: 2026-09-25
Purpose: Fix the audit's six VIS defects.
Source revision: VIS c1ff400 (the audited 25fda76 plus docs only) with uncommitted changes to Services/OrderService.cs, Services/InventoryService.cs, Controllers/HomeController.cs and Views/Home/_ModifyStockPartial.cshtml. No schema change, no migration.
Issues investigated: VIS-1 to VIS-6. The dev database was checked read-only first: no pending orders, no On Hand unit rows on a retired or missing variant, and no loan line whose outstanding count disagrees with its Picked Up unit rows — so there is no existing data to repair.
Changes made:
- VIS-1: Submit keeps a running total of what the cart has already claimed and subtracts it from each later row's availability. An unscoped line counts against every line of the item, and every line counts against an unscoped one. The refusal names the other cart lines; the whole order still rolls back.
- VIS-2: TC taken is summed inside the pull loop (forced TC included) and drives LoanOutstanding and the motor-unit flips. Same change in transfer approval.
- VIS-5: Motor units are taken per shelf, only from the variants the TC actually came off, through one shared helper for pickup and transfer approval. Found alongside: a second line of the same item on one order could re-claim a unit row the first line had already flipped, because EF hands back the tracked instance. Motor and compressor lookups now recheck status and location in memory.
- VIS-3: Return and Scrap validate the ticked units before anything changes. Each must be Picked Up on that exact loan line; no more may be ticked than the quantity; and the unticked remainder must fit in what the line carries as pure quantity (outstanding minus its unit rows). Otherwise the action is refused with nothing changed. Checked before a return's new-location mint.
- VIS-4: Location Transfer re-points On Hand unit rows to the destination variant when the move is unambiguous: the whole source stack (merge), or — motor rows being TC-only — all of the source's TC (merge or split). Saved in the same transaction as the stock move, and the log line counts and names the units moved.
- VIS-4, partial moves (option 1, approved): Modify Stock's Location Transfer pane shows "Which recorded units are moving?" when the move is partial and the source stack has recorded units that can't be settled automatically — any compressor rows, or motor rows when some but not all of the TC moves. Nothing is ticked by default; the list keeps count, states the allowed range, and greys out the rest once the maximum is ticked. The server refuses, with nothing changed: a ticked unit that isn't On Hand on that stack; more ticked than are moving; or fewer than it takes for the unticked rows to fit in what stays. That minimum is capped at what's moving, so a stack already carrying more rows than units (Modify Stock's Scrap lowers the count without touching unit rows) can still be moved — an edge found while building it, not in the approved write-up. Ticked units go to the merge target or the new split stack in the same transaction. Unit data per stack rides on the page's existing item list; no new endpoint.
- VIS-6: Every reservation query requires the line to be Pending as well as the order: GetAvailableQuantity, GetAvailableForOrder (both overloads each), Delete Item and Delete Stack, the Item Card's pending figure, and the Pickup Queue, which now lists and judges only pending lines.
Verification and observed results:
- Fix checks (29 cases, real services, backup-API copies of inventory.dev.db): all seven audit cases REPRODUCED on an unmodified export of c1ff400 and NOT_REPRODUCED on the fixed tree. The 22 added cases all PASS on the fixed tree; the 12 that exercise a fix FAIL on c1ff400; the 3 controls pass on both.
- Real app on a disposable seeded copy (dev db untouched): cart rows 4 + 4 against 3 + 2 refused on Submit with the cart kept. A partial pickup of 1 of 3 left order #11 listing only its other line; the remainder showed avail 9 (was 8); the Item Card read 9 on hand / 7 available / 2 pending (was 6 / 3). Picking a TC motor from shelf B flipped B's lab-numbered unit and left A's On Hand. A forced-TC pickup opened a 1-unit loan with the unit Picked Up. Return qty 1 with both units ticked was refused with nothing changed; with one unticked it returned exactly that serial. A merge carried the serial to the destination, logged "1 recorded unit moved with it". No server errors or console errors.
- Picker: 34 service-level checks in total now, all passing (the 29 above, with VIS-4-split-some-tc rewritten for the picker, plus 5 picker cases: a ticked serial following a partial merge, a required tick refused when missing and when over the limit, a unit from another stack refused, a stack with more rows than units, and a no-TC move needing no tick). In the app on a fresh seeded copy: moving 1 of 2 serialised compressors with nothing ticked was refused with the stack unchanged; ticking SN-PICK-B moved exactly that serial into the merge target and the log named it. Splitting 2 of 3 TC motors with 1 of 2 TC listed both lab #s ("exactly 1"), setting TC to 2 hid the list, and ticking LAB-PICK-2 moved exactly that row to the new stack. Checklist text measured after a real SetTheme POST: light 15.58 / 5.55 / warning 5.11 : 1, dark 14.79 / 12.28 / warning 11.18 : 1. No server or console errors.
- Build clean; the only warnings are the 12 that were already there.
Remaining issues: none from the audit. Noticed, not changed: the Item Card offers Delete Item on an item with 0 on hand while a unit is out on loan (the service refuses it); NU1903 advisory on SQLitePCLRaw.lib.e_sqlite3 2.1.11 in the build output.
Deployment status: committed and pushed as 1df2e45 (docs 5ad1029). Release built from 1df2e45 (self-contained win-x64, 544 files, dll 1.0.0+1df2e45, SHA-256 A136496A…B858) to C:\VIS_Host\september25threlease and staged on MASTER128 as VIS_Release_20260925\ (VIS_application_9.25.26\ + README_FIRST.txt, no database); all 544 files SHA-256-matched after the copy. Smoke test: the published exe in Production against a copy of the dev db - 10 main pages 200 while signed in, no errors, the new checklist data present for 90 real items, all 26 tables unchanged. Supersedes VIS_Release_20260924 (which it fully contains). Not installed on the host yet. No migration: a build from 1df2e45 runs on the host's current db as-is. The data check found nothing to repair in the 9/23 host copy; the host has taken writes since, so run datacheck.py (read-only) against the next host pull.
Links to saved artifacts: [results.json](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-09-25_VIS_1-6_Fixes/results.json>), [results on c1ff400](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-09-25_VIS_1-6_Fixes/results_HEAD_c1ff400.json>), [datacheck.py](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-09-25_VIS_1-6_Fixes/datacheck.py>), Sessions/2026-09-25_VIS_1-6_Fixes/Reproduction, Sessions/2026-09-25_VIS_1-6_Fixes/LiveSeed.


## 2026-09-26 — Further VIS code audit

### VIS — code audit (security, access control, data integrity)

Reviewed September 26, 2026 at VIS `a35850b` (code identical to `1df2e45`, the VIS-1 to VIS-6 fixes). **Eight new defects, VIS-7 to VIS-14, all reproduced in the running app** against a throwaway copy of `inventory.dev.db` on `localhost:5096`, plus ten minor items confirmed by code reading only.

Scope was what the 2026-09-24 audit left out: security, access control, concurrency and stale-page races, plus the paths it did not exercise (Intake, Registry, Delete Item, order cancel, Settings). The whole of `HomeController`, `SettingsController`, `InventoryService`, `OrderService`, the filters, `Program.cs`, `AppDbContext` and every `innerHTML`/`Html.Raw` sink in the views and `site.js` were read.

Nothing in the repo or the real databases changed. `C:\VIS_Inventory\inventory.dev.db` md5 `e05cfdd4…` and `C:\VIS_Inventory\inventory.db` md5 `5be3f996…` were unchanged before and after. The git tree is clean. The temporary `vis-audit` launch entry was removed afterward. All writes went to `audit_live.db` in the session scratchpad.

Side note: `inventory.dev.db` hashes `e05cfdd4…`, not the `4b989780…` the handoff's OPEN table calls current. It was written to after that table was updated, most likely by the VIS-1 to VIS-6 session. Not a defect, just a stale line.

---

#### Priority 1

**VIS-7. Stored XSS: Standard-level text is rendered as HTML.**
There are three sinks where user-entered strings are concatenated into `innerHTML`:
- [site.js:207](../wwwroot/js/site.js) builds the shared item autocomplete from item name and Rheem PN. It is used by Modify Stock, Alert Rules and New Item Registry.
- [Intake.cshtml:453](../Views/Home/Intake.cshtml) puts the item name in the "already registered" section header.
- [PickupQueue.cshtml:440](../Views/Home/PickupQueue.cshtml) builds serial-picker labels from serial and lab #. Only `"` in the value attribute is escaped.

A Standard user can set every one of these fields (Registry, Intake, Log Units, Modify Stock Add serials).

Reproduction: register CVE-0010 through the real New Item Registry form with the name `AUDITXSS <img src=x onerror="document.body.dataset.auditXss='fired'">`, then type `AUDITX` into Modify Stock's item search. The handler ran (`data-audit-xss="fired"`). Server-rendered pages encoded the same name correctly, so the only hole is the client-built HTML.

Impact: the script runs in whoever types or opens the page. That includes the superuser's session while Settings is unlocked. The antiforgery token is readable from the same origin, so the script can post to `/Settings/UpdateAccessLevel` and similar actions. That walks around the passcode gate, the one lock the app treats as a real boundary. Legitimate names containing `<` or `&` also render wrong.

Fix direction: build these rows with `createElement`/`textContent`, as the VIS-4 unit picker already does ([_ModifyStockPartial.cshtml:636](../Views/Home/_ModifyStockPartial.cshtml)).

**VIS-8. Intake decides "same shelf" by exact FdaString, but the app writes two shapes.**
Several writers produce the padded, uppercased form `PATS.0.0.LEAN-TO.0`:
- Modify Stock's Add at NEW location
- Location Transfer
- Loan Return to a new location

Registry and Intake write the compressed form `PATS.Lean-To`. [CommitIntakeStockBatch](../Services/InventoryService.cs) and [CommitIntake](../Services/InventoryService.cs) compare the strings with `==`.

Live data: 8 active stacks already carry the padded shape, from 10 NEW-location adds with the latest on 9/17. The Lean-To shelf is now stored both ways. Pass 38 normalized the data, but the writers keep producing the second shape.

Reproduction on CCR-0005, whose Variant 2 sits at `PATS.0.0.LEAN-TO.0`, posting the Intake batch-review fields:
- An Adjustment at PATS / Lean-To was refused with "no existing stock at this location to adjust". The same happened with the rack typed `LEAN-TO`.
- An Add minted Variant 3 with the identical FdaString and the same team, a duplicate stack on the same shelf.

The Intake page's own rack suggestion for PATS is `Lean-To`, so a user following the suggestion hits this. CommitIntake's existing-item branch (held-batch approval) mints a duplicate instead of skipping in the same case.

Fix direction: match on the Parent/Major/Sub/Rack/Row columns normalized the way Location Transfer's `mergeTarget` already does (blank equals `0`, case-insensitive). Separately, pick one FdaString builder for every writer.

#### Priority 2

**VIS-9. The recorded-unit roster drifts above stock.**
VIS-3 and VIS-4 enforce "recorded rows fit in the stack" for loans and moves. Four other paths lower quantity without retiring a row:
- Modify Stock Scrap
- A downward Adjustment
- A short-pull correction
- A pickup slot left on "No serial" when the shelf holds only serialized units. [AssignOneCompressorUnit](../Services/OrderService.cs) only takes anonymous rows, so it mints a new Picked Up row and leaves a serialized one On Hand.

Live data:
- **CCR-0005 V1** has 4 units and 5 On Hand serials. This is new drift, caused by the 9/17 Modify Stock Scrap.
- **CCR-0213 V2** has 0 units and 9 On Hand rows. This is the known pending cleanup from the 8/25 pickup, and the mechanism is still open for all-serialized shelves.

Consequences:
- Phantom serials are offered in the Pickup Queue picker and shown on the Item Card.
- Delete Stack refuses such a stack permanently ("still has a recorded unit on it").
- No UI path retires an On Hand unit row. Log Units only edits serial and lab #.

**VIS-10. Cancel works on orders that were already picked up, and leaves no trace.**
[CancelPersistedOrder](../Services/OrderService.cs) sets `Status = "Cancelled"` with no status check. It writes no TransactionLog and sends no notification.

Reproduction: submit Order #11 and open Order History in a second tab. Pick the order up from the first tab, then press CANCEL on the stale tab. The order now reads Cancelled while its line is Completed, the unit left the shelf, and a 1-unit loan is outstanding. Nothing records who cancelled it. This is the reverse of the Pass 18 race. The Cancel form also sits on Logs and on both Pickup Queue tables.

Fix direction: refuse anything that isn't Pending, and log the cancel.

**VIS-11. Delete Item ignores loans that have no unit rows.**
[DeleteItem](../Services/InventoryService.cs) treats Picked Up unit rows as the loan check. Controls never get unit rows; `OrderItem.LoanOutstanding` is the authority. That contradicts the rule that unit rows never drive counts.

Reproduction: order and pick up both on-hand units of CCL-0035. Delete Item then succeeded while 3 loan lines (4 units) were still out. Return Loan now fails with "That item no longer exists in inventory." Scrap Loan still works.

Latent today: every Control on loan in real data still has stock on hand.

Fix direction: refuse while any `OrderItems.LoanOutstanding > 0` for the item.

**VIS-12. New Item Registry accepts a negative quantity.**
"Save to Registry" calls `executeSubmit` → `form.submit()`, which skips `min="0"`. This is the same mechanism Pass 39 documented for Modify Stock. [CreateItem](../Controllers/HomeController.cs) has no server-side bound.

Reproduction: type `-3` in Quantity and press Save. CVE-0009 was registered with -3 on hand and logged `New Registry -3`.

Related: CreateItem binds the whole `InventoryItem`, so a crafted post can also set `Variants[]`, `RegisteredAt`, `AlertThreshold` or `Id`.

**VIS-13. Access changes don't reach an open session.**
Sign-in copies AccessLevel, Line and Branch into the session. [RequireLevel](../Services/RequireLevelAttribute.cs) and every visibility check read the session, not the Users row. The 30-minute idle timeout slides, so an active session keeps its old rights indefinitely.

Reproduction: sign in as Caden.Fuller (L3). Set the user row to Viewer and hidden, which is exactly what Settings writes. The open session still changed CCR-0001's alert threshold, an Engineer action, and the change was logged under his name.

Fix direction: re-read the user row per request (one indexed lookup) or stamp a per-user version the session must match.

**VIS-14. Intake and Registry mint different ItemId prefixes for the same item.**
- Registry derives Group from the item's Line. This was the Pass 10 fix.
- [CommitIntake](../Services/InventoryService.cs) and [ApproveIntake](../Controllers/SettingsController.cs) still derive it from the submitter's Line.

Whole-Branch users have a blank Line (Luis.Zapata, Karthig.Kathirvel, Sachin.Nehete), so Intake always gives them `C`.

Reproduction: as Sachin.Nehete, with the same Line (Residential OD) and Type (Valve), Intake minted **CVE-0011** and Registry minted **RVE-0001**. Group is frozen at creation, so every such ID is permanent.

The ApproveIntake comment defends the submitter rule deliberately, so which rule wins is a decision. The whole-Branch → always-Commercial case is wrong under either rule. No mismatches in real data yet (15 Intake registrations checked).

---

#### Minor, confirmed by code reading only

- **Unrecognized actionType logged as success.** `ModifyStock` treats an unrecognized `actionType` (e.g. `scrap` lower-case in a direct post) as a no-op but still logs it and reports success. The controller's gate is case-insensitive; the service's branches are case-sensitive.
- **Unbounded loops on posted numbers.** `PickUpPartialAndSplit` loops `pickupQty` times, and `PickUpOrderConfirmed` pads a list up to a posted unit index, both before any validation. A crafted Standard-level post with a huge number can exhaust memory.
- **Short-pull corrections not team-scoped.** `ReportShortPull` applies corrections to any stack of the item, including another team's stacks not shown on the form, and sets absolute values.
- **Reject on decided batches.** `RejectIntake` has no Pending check, so a stale Settings page can flip an Approved batch to Rejected.
- **No concurrency tokens.** "First claim wins" on tasks and deliveries isn't atomic. Nothing carries a concurrency token, and `ModifyStock`'s read-modify-write runs outside a transaction, so simultaneous writes to one stack can lose an update.
- **Settings script breaks on quotes.** [Settings/Index.cshtml:754](../Views/Settings/Index.cshtml) builds JS from location names by string interpolation. A `"` or `\` in a location name kills that page's script. Only the superuser enters these names.
- **CSV export escaping.** Commas are replaced in 4 fields only. Type, FdaString (free-text Rack/Row) and ProjectCode are unescaped, nothing is quoted, and there is no formula-injection guard. No live data breaks it today.
- **Email HTML unencoded.** `EmailService` builds HTML from item fields without encoding. Inert while SMTP points at the dummy host.
- **Deliveries lost when the recipient leaves.** A delivery addressed to someone later hidden or deleted drops off every board but still counts in Command Center's Incoming Shipments. None in data now.
- **Intake quantity coercion.** Intake silently turns a quantity ≤ 0 into 1.

#### Decisions rather than defects

- Order History, Order Details and Pickup Queue show every Line's orders. Logs have been Line-scoped since Pass 15.
- Name-only sign-in means Line visibility isn't confidential. An off-roster name gets Viewer with a blank Line, which sees the whole org. This is documented design and was not counted.

#### Checked and clean

- Antiforgery on every POST; `returnUrl` passes `IsLocalUrl`.
- Delivery photos are re-encoded to JPEG under GUID names.
- JSON blobs are embedded through `JsonSerializer`, which escapes `<`.
- Notification dismiss is owner-scoped; loan return and scrap are owner-checked.
- Transfer approve and deny re-check `CanApproveTransfer`.
- Submit, Pickup, Return, Scrap-loan, Short-pull and Transfer approval each run inside one SQLite transaction.

## 2026-10-04 — VIS-7, VIS-8 and VIS-10 to VIS-14 fixes

Date: 2026-10-04
Purpose: Fix the 2026-09-26 audit's open defects in three passes. VIS-9 was held back: it needs a new mechanic (which recorded unit leaves when stock drops), so it gets its own scope and go.
Source revision: VIS `1c565b7` (docs only on top of the audited `a35850b`, which has the same code as `1df2e45`). Pass A `0b03c94`, Pass B `a077663`, Pass C `d39553d`, each built and verified on the working tree before it was committed, all pushed to `origin/master`. No schema change and no migration in any of them.
Issues investigated: VIS-7, VIS-8, VIS-10, VIS-11, VIS-12, VIS-13, VIS-14, four of the audit's minor items, and the `SessionMiddleware[7]` report from the host's Sourcing console.
Decisions made for these passes: VIS-14, the item's Line decides the ItemId prefix (what Registry has done since Pass 10), so `ApproveIntake`'s submitter-Line rule is gone. VIS-13, a hidden or deleted user's open session drops to Viewer with a blank Line and Branch, exactly what signing in as an off-roster name gives; no forced sign-out.
Changes made:
- Pass A (`0b03c94`).
  - VIS-7: the item autocomplete (`site.js`), the Intake "already registered" header and the Pickup Queue serial picker, including the partial-pickup rows, build their rows from nodes and `new Option`, not HTML strings. The team name in Modify Stock's notes and the location names in the Command Center zone menus go through a new `escapeHtml`. The Intake header's model name was white on white in light mode (`text-white`); it follows `--vis-text` now.
  - VIS-10: `CancelPersistedOrder` refuses anything that isn't Pending (a distinct message for an order already cancelled), runs in one transaction and writes an "Order Cancelled" log entry with a blank ItemId, like "Intake Approved". A partly picked-up order that is still Pending can still be cancelled, as before; its picked lines' loans are untouched.
  - VIS-11: `DeleteItem` refuses while any order line has `LoanOutstanding > 0`.
  - VIS-12: `CreateItem` rejects a negative quantity and discards a posted `Variants`, `Id`, `RegisteredAt` and `AlertThreshold`, in the service and again at the top of the controller action (the location getters read a posted variant ahead of the staged form values).
  - Minors: `ModifyStock` throws on an unrecognized `actionType` instead of logging success; `RejectIntake` refuses a batch that is no longer Pending; the two pickup form loops are capped at 5000 units (`HomeController.MaxUnitsPerLine`); the Settings owner-picker lists are serialized as JSON, so a quote or backslash in a location name no longer breaks the script.
- Pass B (`a077663`).
  - VIS-8: a shared `SameShelf` compares the Parent/Major/Sub/Rack/Row columns, blank equal to `0`, case ignored. `CommitIntake`'s existing-item branch, `CommitIntakeStockBatch` and Location Transfer's merge target all use it. The writers still produce both FdaString shapes; the 8 padded stacks were not touched.
  - VIS-14: `CommitIntake` takes the Group from the item's Line. Its `submitterLine` parameter, and `ApproveIntake`'s lookup of it, are removed.
- Pass C (`d39553d`).
  - VIS-13: `RequireNameFilter` is an async filter with the database injected. Each click it re-reads the person's active roster row (the same case-insensitive lookup `Identify` uses) and sets the session's Level, Line and Branch to match, writing only what changed so an ordinary click sends no new Set-Cookie. Global filters run before every `[RequireLevel]`, so it sees the fresh level. Pages marked `[AllowWithoutName]` skip it; the session name and theme are never touched. Sourcing's own `RequireNameFilter` already worked this way, except that it rewrites the session on every click.
Verification and observed results:
- Service and filter checks (real services and the real filter against backup-API copies of `inventory.dev.db`, the 9/25 harness extended): Pass A 50 cases, Pass B 64, Pass C 80. On the revision before each pass the audit case reproduced (VIS-10, 11 and 12 in A; VIS-8 and VIS-14 in B; VIS-13 in C). Of the cases added in each pass, 7 (A), 6 (B) and 9 (C) failed there and the rest passed on both, as controls. On the fixed tree all 80 pass: 67 PASS and 13 NOT_REPRODUCED, which includes every VIS-1 to VIS-6 case.
- Live, on seeded scratch copies of the dev db (the real databases were not opened for writing; the dev db's modified time is still 2026-09-24):
  - A: an item named with an `<img onerror>` payload rendered as literal text in all three autocompletes, the Intake header and the Pickup Queue picker, including the partial-pickup rows; the old construction fires the same payload. A pickup followed by a stale cancel is refused and the order stays Completed; a second cancel is refused; the cancel shows in the Command Center feed. Delete Item with a loan out is refused. Quantity -3 through the real Registry form is refused and registers nothing. Settings (unlocked with a test-only passcode) works with a location named `Dock "A" \ B`.
  - B: CCR-0005's padded `PATS.0.0.LEAN-TO.0` stack took an Adjustment typed as `Lean-To` through the Intake page, 1 to 2 with no duplicate stack. As a whole-Branch user with a blank Line, an Intake import of a new Valve into Residential OD minted `RVE-0001`, Group Residential.
  - C: through the real Settings endpoints, an Engineer on Commercial Packaged/Splits was demoted, hidden, restored and moved to Residential OD. Each change showed on his next click: the Engineer action refused after the demotion, the whole org visible while hidden, his own Line back after the restore, a smaller view after the move. The superuser's session and the Settings gate were not disturbed, and unchanged clicks sent no Set-Cookie.
- Not exercised live: the held-batch approval (`ApproveIntake`). It was compile-checked and calls the same `CommitIntake` the service cases cover.
- Build clean after each pass; the only warnings are the 12 already there plus the NU1903 advisory.
- Found, not fixed: `BuildRackRowMap` groups on the raw `Parent|Major|Sub`, so the padded stacks (`"0"` levels) form keys of their own and their racks don't feed Rack suggestions.
SessionMiddleware[7] on the host's Sourcing console:
- Both apps now use their own cookie names (`.VisualInventory.Session`, `.SourcingTool.Session`), and the Sourcing fix `2437326` is inside the 10/02 release `17d4dba`. Browsers scope cookies by hostname, not port, so the two apps share a cookie jar and are kept apart only by the names.
- Reproduced locally: the new Sourcing ignores cookies named `.AspNetCore.Session` and `.VisualInventory.Session` (0 log lines). Two copies of Sourcing started from different folders on one hostname reproduce the error text ("The payload was invalid"): 2 log lines per click while the browser holds the other copy's cookie, until the person signs in again on that copy; going back and forth makes each switch cost a sign-in. A copy reading its own cookie logs nothing.
- Neither app polls in the background, so each log line is a real click. Sourcing writes the session on every click; VIS only wrote it at sign-in, and now writes only on a change.
- Cause on the host not found. Checks that tell: the `Set-Cookie` name in each app's response in the browser's developer tools (`.AspNetCore.Session` means that app is a build from before the rename); `Get-Process Sourcing_Tool | Select Id,Path` and `netstat -ano | findstr :5001` for a second copy; the exact text of the log line. No code was changed, and the Data Protection application-name pin that was discussed is not adopted.
Remaining issues: VIS-9. Its audit data is unchanged: CCR-0005 V1 (4 on the shelf, 5 recorded On Hand) and CCR-0213 V2 (0 and 9). Minor audit items not done: short-pull corrections aren't team-scoped, no concurrency tokens, CSV export escaping, unencoded HTML in emails, deliveries addressed to someone who leaves, and Intake's silent quantity coercion.
Deployment status: committed and pushed (`0b03c94`, `a077663`, `d39553d`). No release has been built from them. `VIS_Release_20260925` (built from `1df2e45`) is still the staged one and is still recorded as not installed on the host. No migration, so a build from `d39553d` runs on the host's current db as-is. Two behavior changes to know about: a hidden user's open session reads the whole org as a Viewer on its next click, and Intake's ItemId prefix now follows the item's Line.
Links to saved artifacts: [Pass A](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-10-03_VIS_PassA>), [Pass B](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-10-04_VIS_PassB>), [Pass C](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-10-04_VIS_PassC>); each holds the harness (`Reproduction`), `results_*` for the revision before and the fixed tree, and Pass C also the live Settings script and the two-copies cookie script.


## 2026-10-04 — VIS-9 fix (Pass D)

Date: 2026-10-04
Purpose: Fix VIS-9, the last open item of the 2026-09-26 audit: Scrap, a downward Adjustment, a short-pull correction and a "No serial" pickup left recorded On Hand units behind when a stack's count dropped, so a stack could carry more recorded units than it holds.
Source revision: VIS `ffac7b4` (docs on top of `d39553d`). Committed and pushed as `9cfea2a`. No schema change, no migration.
Issues investigated: VIS-9, and what the fix would break in the flows around it.
Decisions made for this pass: removed units become Scrapped (as Loan Scrap does); a "No serial" the picker chose on purpose is refused when every unit left on the shelf is recorded, rather than guessing the oldest; the short-pull correction is built in the same pass; the rows already drifted in the data are repaired by hand, not by code.
Changes made:
- `InventoryService.RetireSurplusUnits`, run after a variant's new Quantity (its TC count, for motors, whose unit rows are TC-only) is set. Taking the whole stack, or all its TC, scraps every recorded row with no question. A count that still has room for every row asks nothing. Otherwise exactly the surplus has to be ticked, each unit On Hand on that stack, or the action is refused with nothing changed. Retired units become Scrapped with `ItemVariantId` null, and the log line names them. It only fires when the stack actually shrank: an Adjustment that adds or changes nothing never asks, and neither does Add.
- `ModifyStock` takes `leavingCompressorIds` / `leavingMotorIds` and restores the tracked variant's counts if the check refuses. `ReportShortPull` takes per-variant ticks, and its correction form (`LocationStock` now carries each shelf's recorded units) asks which are gone.
- `AssignOneCompressorUnit` now knows how many units are physically left on the shelf. A "No serial" chosen in the picker is refused when no unnamed unit exists and every remaining unit is recorded with a serial, in a pickup, a partial pickup and a transfer approval. A slot with no per-unit choice at all, which is what a short-pull correction's own re-pickup passes, takes the oldest recorded unit instead. When the shelf holds more units than rows, an unrecorded unit exists and a new unnamed row is still the right outcome.
- UI: Modify Stock's Scrap and Adjustment panes show "Which recorded units are leaving?", and the Pickup Queue's short-pull form asks per shelf. Both use the checklist the Location Transfer picker already had, now one shared renderer. Execute and "Correct & Pick Up" stay disabled until the number of ticks matches.
- A refused short-pull correction puts its form back. The form only ever came from TempData, so a refusal used to strand the order (the line stayed flagged short, nothing could reopen it). `GetShortPullLine` rebuilds it, and `ReportShortPull` clears the change tracker after a rollback so the rebuild reads what the database holds.
Found while building, and handled: a blanket "No serial" refusal would have broken the short-pull correction on a fully recorded shelf, because the reissue after a correction passes no per-unit information. That is why only a deliberate choice is refused.
Verification and observed results:
- Service checks (the harness extended again, against backup-API copies of `inventory.dev.db`): 106 cases, 80 earlier plus 26 new. On the revision before this pass the four audit repros reproduced (Scrap, downward Adjustment, short-pull, "No serial" pickup), 16 of the added cases failed and the other 6 passed as controls. On the fixed tree all 106 pass: 89 PASS and 17 NOT_REPRODUCED. Test artifact worth knowing: a refused request leaves its tracked changes behind in a shared context, so two cases clear the change tracker before the next call; the app uses a fresh context per request.
- Live, on a seeded scratch copy of the dev db:
  - CCR-0005 V1, the real drift (4 on the shelf, 5 recorded): Scrap 1 showed the checklist ("tick which 2 are gone"), a submit with nothing ticked was refused with nothing changed, and with two ticked it went to 3 on the shelf and 3 recorded, the two serials Scrapped and named in the log. Boxes grey out at the maximum.
  - Cases that must not ask, checked in the page: CCR-0001 Scrap 2 (7 recorded, 10 on the shelf), Adjustment up, Add. Cases that ask: CCR-0001 Scrap 5, CCR-0002 Adjustment -2. Whole-stack Scrap shows a note and no boxes. Motors (CMR-0078, TC 5, 5 rows): a TC scrap asks for 1 TC unit, a non-TC scrap does not, scrapping all TC shows the note.
  - The Location Transfer picker, refactored onto the shared renderer, still moved exactly the two ticked serials to the new stack.
  - Pickup Queue: "No serial" on CCR-0059 (10 on the shelf, 10 recorded) was refused with nothing picked up; the untouched default named serial picked up.
  - Short-pull on CCR-0008: the button waited for the ticks and returned when the count went back to the recorded one; a submit forced past the guard was refused and the form came back with the error; the proper correction scrapped the two ticked units and the re-pickup took the two remaining recorded ones, leaving 0 on the shelf and 0 recorded.
  - Modify Stock's Execute: disabled while the ticks are short, enabled at the right number, and enabled again on switching to Add.
- Release smoke: a self-contained win-x64 publish of the working tree (544 files, 132 MB, the same shape as earlier releases) run in Production against a fresh copy of the dev db. No migrations applied at startup. 15 pages returned 200. 12 flow checks passed across Passes A to D: Scrap refused and then accepted with ticks, "No serial" refused and a named serial accepted, cancel once then refused, cancel of a picked-up order refused, Intake adjustment on the padded stack with no duplicate, a demoted Engineer refused on his next click. The console had no fail or crit lines and no `SessionMiddleware[7]`.
- Build clean; the only warnings are the 12 already there plus the NU1903 advisory.
Remaining issues:
- The drift already in the data is not repaired by the code, which only stops new drift. In the 9/24 dev copy: CCR-0213 V2 (`NWES.0.0.FLOOR.0`, quantity 0) carries 9 unnamed On Hand rows, ids 196 to 204, left from the 8/25 Add of 9 whose pickup (Order #8) was Loan-Scrapped for "wrong model number on intake". They should be Scrapped. CCR-0005 V1 (quantity 4) carries 5 serials (`25DD8075L`, `25DD8086L`, `25EA6354L`, `25EA6312L`, `25EA6346L`); one isn't physically there, and which one is for a person to say. The repair is a Status = Scrapped, ItemVariantId = NULL update on those rows, run on a host copy after a backup and after re-checking the ids against the host's database, which has taken writes since 9/23.
- A "Serial not in list" typed by hand on a fully recorded shelf can still add one phantom: that route exists for serials the roster doesn't know yet, so it was left alone.
- The minor audit items not done are unchanged from the entry above: short-pull corrections aren't team-scoped, no concurrency tokens, CSV export escaping, unencoded HTML in emails, deliveries addressed to someone who leaves, Intake's silent quantity coercion. Also unchanged: `BuildRackRowMap` and the host's `SessionMiddleware[7]` report.
Deployment status: committed and pushed (`9cfea2a`). No release has been built or staged from it. `VIS_Release_20260925` (built from `1df2e45`) is still the staged one and still recorded as not installed on the host. A build from `9cfea2a` carries every audit fix, VIS-1 to VIS-14, plus Passes 39 and 40 and the cookie fix. No migration, so it runs on the host's current database as-is. A build was planned for prod on 2026-10-05. Later the same night it was built from master @ `3e7f201` (the code of `9cfea2a` with docs on top) and staged as `VIS_Release_20261004\` on USB `LESLISGIFT`: `VIS_application_10.4.26\` (544 files, all matched the build by SHA-256 after the copy) with a README and a read-only host check script; no database. Not installed on the host as of this entry.
Links to saved artifacts: [Pass D](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-10-04_VIS_PassD>): the extended harness (`Reproduction`), `results_pre-PassD_ffac7b4.json` and `results_fixed_tree.json`, and the two smoke scripts (`smoke_pages.py`, `release_smoke.py`).


## 2026-10-14 — VIS-15, deleted item IDs reissued

Date: 2026-10-14
Purpose: Stop `GenerateItemId` handing a deleted item's ItemId to a new item. Found on 2026-10-13 while checking the Unit Tracker spec (`docs/Unit_Tracker_Spec.md`), whose unit tags need the same never-reissued rule.
Source revision: VIS `7b7ae7e` (docs only on top of `7a57d74`). No schema change, no migration.
Issues investigated: VIS-15 (new).
The defect: `GenerateItemId` took the highest number among **live** `InventoryItems` with the prefix. Delete Item, and the hand-SQL deletions done as data cleanup, remove the item row but keep everything else under the old ItemId: its `TransactionLogs`, Returned/Scrapped `CompressorUnits`/`MotorUnits`, completed `OrderItems` and decided `TransferRequests`. The Item Card, View Logs, My Activity and the compressor/motor rosters all read those back by that string. Deleting the top-numbered item of a prefix freed its number, and the next registration with that prefix took it, along with the dead item's history. `DeleteItem`'s own comment said "numbers are never reused", which held only for an item that wasn't the highest of its prefix.
Live data:
- **CCR-0255 was already reissued.** The "just a test model" Intake item (2026-08-13) was deleted by hand on 2026-08-19, leaving its New Registry and Item Deleted log rows (Ids 684 and 690). YRH083WAA (Samurai, Intake 2026-08-27) then minted CCR-0255 again, and its Item Card lists those two rows as its own history. The same rows are in `inventory.dev.db` and `inventory.db` (local production); the host most likely has them too, since dev is its 9/23 copy.
- Next in line under the old code: `CCO-0001` (deleted on the host 2026-09-10, the "Contro" typo). No live `CCO-` item exists, so the next Commercial item with a type starting C and ending O would have become CCO-0001 and shown its Scrap of 10. The other orphaned log IDs (`CCL-0001`, `CCL-0034`, `CMR-0028`, `CMR-0040`) sit below their prefix's live maximum and were safe.
Changes made: `GenerateItemId` takes the highest number ever used for the prefix across `InventoryItems`, `TransactionLogs`, `OrderItems`, `CompressorUnits`, `MotorUnits` and `TransferRequests` (each a `StartsWith(prefix)` query; the same `<PREFIX>-<digits>` parse, non-matching shapes ignored). The live-item collision guard is unchanged. Both callers (`HomeController.CreateItem` and `InventoryService.CommitIntake`) get it. Registry's client-side ID box is still a preview counted from the items on the page, and the success toast shows the real ID; it is unchanged.
Verification and observed results:
- The real app (`dotnet run --no-build`, Development profile) on localhost against two copies of `inventory.dev.db` (md5 `e05cfdd4…`), the connection string overridden on the command line. Signed in as Kason.Woods; the Registry form's own FormData posted with its antiforgery token.
- Before the fix (old build): registering Type "Contro" on Commercial Packaged/Splits minted **CCO-0001**, and its Item Card listed the old item's New Registry +10, Scrap -10 and Item Deleted.
- After the fix: the same registration minted **CCO-0002**, whose card shows only its own registration; CCO-0001 still 404s. Deleting CCO-0002 through Delete Item and registering again minted **CCO-0003**. Intake (the other caller) minted **CCO-0004**. A normal prefix is unchanged (`CCL-0046`, live max 0045), and a never-used one starts at 1 (`CGO-0001`).
- Both copies `integrity_check ok` afterward; `inventory.dev.db` md5 unchanged. Build: 0 errors, 14 warnings.
Remaining issues: the two CCR-0255 log rows from the deleted test item (684, 690) still sit under the real item's ID. The code no longer creates new cases; whether to re-key or annotate those two rows on the host is a data decision.
Deployment status: committed as `1ca6651` on master and pushed. Not in any release. No migration, so a build with it runs on the host's current database as-is.
Links to saved artifacts: [2026-10-14_VIS-15_ItemIdReuse](</C:/Users/woods/OneDrive/Documents/VIS and Sourcing Debug Sessions/Sessions/2026-10-14_VIS-15_ItemIdReuse>): `results.md`, `verify_in_page.js` (what was run in the browser console), and the two copies after the runs (`before.db`, `after.db`).


## 2026-10-26 — BC-10, toast text rendered as HTML

Date: 2026-10-26
Purpose: Fix BC-10 from the 2026-10-20 audit (`docs/Bug_Checks.md`), the first item in its suggested repair order.
Source revision: VIS `e1b1a2e` (docs only on top of the audited `94546b4`; no code changed between them). Committed as `282ad8e`. No schema change, no migration.
Issues investigated: BC-10.
The defect: `_Layout` wrote `TempData["Success"]` / `["Error"]` into a quoted JavaScript string as `"@TempData[...]"`. Razor's HTML encoding leaves backslash escapes like `\x3C` alone, JavaScript decodes them, and `showToast` pasted the message into an HTML template passed to `.append()`. A registrant's stored item name reaches it through the registration success toast and through another user's duplicate-PN error. The two boundaries depend on each other: today an apostrophe displays only because Razor writes `&#39;` and the HTML sink decodes it, so fixing only the sink would show `&#39;` in dozens of messages.
Changes made:
- `_Layout.cshtml`: the two TempData lines serialize the message with `JsonSerializer.Serialize` (as Identify, Intake and PickupQueue already embed JSON), which escapes quotes, backslashes, line breaks and `<`. `showToast` builds the toast from nodes and adds the message as a text node; the icon is one of two fixed `<i>` elements chosen by `type`. Same classes, 4-second delay and removal on hide.
- `UnitTracker/Index.cshtml`: `whToast` no longer runs `escapeHtml` first, which would now show `&amp;` on screen.
- `UnitTrackerController.ToastText`: no longer turns backslashes into `/` and line breaks into spaces, a workaround for the old JS-string boundary. The 400-character cap stays (TempData cookie size).
- Checked and left alone: Search Center's "Added to cart!" and Alert Rules' threshold message pass fixed text; the inline TempData alerts on Deliveries, Intake, LogDelivery, MyActivity and PickupQueue and the AuthError alert are Razor-encoded in an HTML context, which is correct; none of the 157 TempData assignments carries intended markup. The only other server value quoted into JS, `openItemCard('@it.ItemId', ...)` on the compressor/motor registries, is a minted ID whose prefix comes from Compressor or Motor there.
Verification and observed results:
- The real app (`dotnet run --no-build`, Development profile) on localhost:5097 against a scratch copy of `inventory.dev.db` (SHA-256 `15d21a55…`), the connection string overridden on the command line. Signed in as Kason.Woods through the sign-in page; registrations posted through the Registry form itself.
- Before the fix (old build): registering the audit's marker name `\x3Cb data-audit-toast=confirmed\x3ETOAST-MARKER\x3C/b\x3E` (CGL-0002) put a real `<b data-audit-toast="confirmed">` element in the success toast; a second registration with the same PN put it in the duplicate-PN error toast.
- After the fix: the duplicate-PN toast shows the escapes as literal text, no marker element, apostrophes as `'`. A registration named `Tom's "Dock" — C:\temp <img src=x onerror="document.body.dataset.x=1"> \x3Cb data-audit-toast=1\x3E` (CGL-0003): the toast text matched the typed name exactly, `document.body.dataset.x` stayed unset, no `<img>` or marker element, the toast body held only the icon and one text node. A toast hid and was removed after 4 seconds.
- Unit Tracker: Add with model `A&B\C <b>x</b>` toasted `Unit #45 added (A&B\C <b>x</b>).` as text, backslash kept; a real click on unit #45's In Lab box toasted `Unit #45: In Lab.` with no `&amp;`.
- Zero console errors on Command Center, Search Center and Unit Tracker; zero server error lines. Build: 0 errors, 14 warnings, the same as a build taken just before the change. The dev db's SHA-256 was unchanged afterward.
- Not checked: how the toast looks. The in-app browser doesn't paint toasts (the Pass 26 limit), so the checks above read the DOM. Search Center's AJAX "Added to cart!" toast was not triggered; it calls the same function with fixed text.
Remaining issues: BC-01 to BC-09 and BC-11 to BC-13 in `docs/Bug_Checks.md`.
Deployment status: committed as `282ad8e` on master, pushed 2026-11-16. Not in any release. No migration, so a build with it runs on the host's current database as-is.
Links to saved artifacts: none kept; the scratch copy of the dev db was session-only.


## 2026-11-09 — BC-01 and BC-07, fixed inside the Reservations work

Date: 2026-11-09
Purpose: Kason's rule 10 for Reservations (`docs/Unit_Reservations_Spec.md`): fix BC-01 and BC-07 in the same pass, because holds subtract from the same availability math.
Source revision: VIS `1765ffe` plus the Reservations work (migration 43), committed as `3c08bd6`.
Issues investigated: BC-01, BC-07.
The defects: BC-01 — `GetAvailableQuantity(item, team)` subtracted only Pending lines of the same team, so an unscoped line that had already claimed the stock was invisible to a team-scoped order. BC-07 — nothing counted thermocoupled units across orders; Submit only clamped TC to the line quantity.
Changes made:
- `Services/StockClaims.cs` (new): supply per stack, grouped by Team; claims are live holds (per stack) and Pending lines on Pending orders (per team, or unscoped). A claim set fits when each stack's holds fit it, each team group's holds plus scoped lines fit the group, and unscoped lines fit what the groups have left over, for quantity and for TC alike. "Available" is the most that can be added without making the fit worse, so data that is already over-claimed never blocks an unrelated request.
- `InventoryService.GetAvailableQuantity` / `GetAvailableForOrder` / new `GetAvailableTc` and `GetAvailableIgnoringPriority` go through it. `OrderService.Submit` checks the whole cart through it (replacing the in-cart pool list) and names TC in its refusal. `RequestTransfer` and `ApproveTransfer` check TC too, measured for the requester.
Verification and observed results (real app on a scratch copy of `inventory.dev.db`, migration 42 to 43 on start; orders placed through AddToCart/SubmitLedger as the named users):
- BC-01: CCL-0027 split into Ninja 14 and Samurai 5 (Add at a new location for Samurai). Dustin (no teams, so unscoped) ordered 19: accepted. Conner (Samurai) then saw Available 0 and was refused at 5 and at 1 ("only has 0 available"). Before the fix the Samurai count would have read 5 and accepted.
- Positive control: with Dustin's 19 cancelled, Dustin 10 unscoped accepted; Conner saw 5 and 5 was accepted; James (Ninja) saw 4, was refused at 5 and accepted at 4. 10 + 5 + 4 = 19, every unit spoken for once.
- BC-07: CMR-0078 (10 on the stack, 5 TC, Ninja). James ordered 5 with 5 TC: accepted. DeCory then asked for 1 with 1 TC: refused, "only has 0 thermocoupled available -- 1 asked for". The same 1 with 0 TC: accepted.
- Build 0 errors, 14 warnings, same as before. The dev db's SHA-256 was unchanged afterward.
- Not checked: BC-02 (a pickup can still fill a TC request with a non-TC motor when the order didn't ask for a stack); out of scope here.
Remaining issues: BC-02 to BC-06, BC-08, BC-09 and BC-11 to BC-13 in `docs/Bug_Checks.md`.
Deployment status: committed as `3c08bd6` on master, pushed 2026-11-16, not in any release. Ships with migration 43.
Links to saved artifacts: none kept; the scratch db was session-only.


## 2026-11-16 — BC-02, BC-03, BC-05, BC-06, BC-08, BC-13, BC-14, and BC-15 (new)

Date: 2026-11-16
Purpose: Kason asked for the serious open bugs to be fixed without changing how the app works, and for anything that needs a design call, a database write or a change in handling to come back to him instead.
Source revision: VIS `666c9fa` (docs only on top of `80faae7`, the comment move). Not committed: Kason is following the changes in Visual Studio. No schema change, no migration.
Issues investigated: every open finding in `docs/Bug_Checks.md`, re-checked against today's code, since the Reservations work rewrote availability after the audit. A read of the newest code (`StockClaims`, `ReservationService`, `ReservationsController`). Sweeps for POST actions without an anti-forgery token, and for `Html.Raw` of anything not built by `JsonSerializer`: none of either. Every POST without a level attribute is open on purpose or gated at class level (Settings).
Changes made:
- BC-03 (`InventoryService.ModifyStock`): a stack id that was posted and isn't an active stack of the item is refused, with nothing changed, for Add, Scrap, Adjustment and Location Transfer. A blank id still means the primary stack, so single-stack items and Internal - Transfer behave as before.
- BC-05 (`LogCompressorUnits`, `LogMotorUnits`, `LogIntakeSerials`): a new roster row is refused once the stack's On Hand rows reach its quantity (its TC count, for motors). Edits to existing rows are never blocked, so a stack that already carries extra rows (CCR-0005 V1) can still be corrected. Serials typed on an Add (Modify Stock, Registry, Intake) are capped the same way; extras are skipped and listed in Intake's skipped list.
- BC-06 (`LogCompressorUnits`): the same serial twice in one save is refused on the second row, case-sensitive like the unique index. The first row and every other good row still save, instead of the whole save failing on the index.
- BC-08 (`OrderService.ReportShortPull`): every posted correction is checked against the line's team before anything changes, the same team rule the pull uses. Missing or retired ids are still skipped.
- BC-02 (`PlanTake`, `FulfillOrderItem`, `ApproveTransfer`): when the free TC across the pull's stacks covers what the line asks for, a stack gives only as many plain units as the line's plain share, so the TC comes off the stacks that have it. The pull order is unchanged. When the TC isn't there, nothing changed: the line still fills with plain units, as before.
- BC-13 (`ExportToCsv`): every text field goes through one quoting rule (quoted when it holds a comma, a quote or a line break). The existing comma-to-semicolon swaps stay, so those columns read as before.
- BC-14 (`_ModifyStockPartial.cshtml`): the new-stack Team picker is shown and enabled only for Add + "New location...", by one function both places call. Hidden, its select is disabled, so `variantTeam` no longer posts with every action.
- BC-15, new (`ReservationService.Save`): a new serial typed on two stacks in one save is refused by name before anything is written. It used to reach the unique index: "Reserve failed: SQLite Error 19", with the form lost.
Verification and observed results:
- Two builds side by side: the committed one (`bin/Debug`, built at 13:50 from the same code as HEAD) on :5009, the fixed one on :5008, each against its own copy of `inventory.dev.db` with the same fixtures, written to the copy only. The same script drove both through the real HTTP endpoints, signed in as Kason.Woods, and read each copy afterwards.
  - BC-03: Add 1 with stack id 999999 on CEV-0027. Before: "applied", the primary stack went 15 to 16. After: refused, 15.
  - BC-06: DUPA twice on CCR-0066 V2. Before: "An error occurred while saving the entity changes", nothing saved. After: the first saved, "Serial 'DUPA' is entered twice in this save."
  - BC-05: five new rows on that stack (quantity 6). Before: 8 rows recorded on 6. After: stopped at 6, each extra named. Motors, CMR-0078 (TC 5, 5 rows): before 7 rows, after 5. Add 1 with three serials on CCR-0014 V1 (full at 3): before 6 rows on 4, after 4 on 4.
  - BC-02: CMR-0021 set to V1 2 plain and V2 2 TC, an order for 1 with 1 TC. Before: the unit came off V1, loan 0, no TC row. After: off V2 (TC 2 to 1), loan 1, its TC row Picked Up.
  - BC-08: CCR-0025, a Samurai line flagged short, a correction posting Ninja's stack at 0. Before: Ninja's 6 went to 0. After: refused by name, 6 kept, the line still flagged short. The line's own correction afterwards went through and reissued as usual.
  - BC-13: EEV export with a primary rack of "RACK, 1" and a name with an inch mark. Before: rows of 14 and 15 fields. After: 14 on every row, the location and name intact.
  - BC-15: CCR-0028, DUPSN on both stacks. Before: the raw SQLite error. After: "Serial DUPSN is typed on more than one stack." Nothing written either way.
- Controls on the fixed build: editing a lab number and a serial on stacks that are already full both saved. A plain motor order still took the plain unit first. A TC order with too little TC filled exactly as before (one plain, one TC, loan 1). An Add to a new location for Falcon made the stack on Falcon. A Location Transfer by a real stack id split as usual.
- BC-14 in the browser on the fixed build: the picker shows and posts only for Add + New location; Location Transfer, Internal - Transfer, Edit Details, Scrap and Adjustment each hide it and leave `variantTeam` out of the form; it comes back on picking New location again. No console errors.
- No server errors on either build. Build 0 errors, 14 warnings. `inventory.dev.db` SHA-256 `15d21a55…` unchanged.
- Not checked: transfer approval's TC pull (the same helper as pickup, code-read only); Intake's own serial path (the cap was exercised through Modify Stock's Add); BC-15's positive case, distinct new serials on two stacks (that path is unchanged apart from the one added check); a real phone.
Remaining issues, each a decision rather than a code fix:
- BC-02 when the TC isn't on the shelves: fill with plain units (today), refuse the line, or split it.
- BC-04: a stale Unit Tracker form restores old notes. Either keep only the fields the person changed, or refuse with a "changed since you opened it" message.
- BC-11 and BC-12 (Front_Load A9): a unique index on ItemId (a migration, after checking the data for duplicates) and a concurrency check on stock writes.
- BC-13: whether a value starting with `=`, `+`, `-` or `@` gets a leading quote in the CSV so a spreadsheet can't run it.
- BC-09 stays as ruled.
Deployment status: committed as `0f949b3` on master (2026-11-16, with the two entries below) and pushed the same night, not in any release. These fixes alone need no migration, but the commit carries migration 44 (next entry).
Links to saved artifacts: none kept; the fixtures, the HTTP script and both result files were in the session scratchpad.


## 2026-11-16 — BC-02, the Non-TC Motor question

Date: 2026-11-16 (evening)
Purpose: BC-02's remaining case, a TC line at pickup with no TC on its shelves, which still filled with plain motors. Kason's ruling, scoped back to him and confirmed before any file was opened: the whole line waits, nothing leaves, and the orderer is asked "Is a Non-TC Motor okay for this order?"; the orderer or an Engineer and up on the item's Line can answer; Yes resubmits and rechecks against every other order; No cancels the line; the wording is "Non-TC Motor"; the note on the order line gets its own column (migration 44).
Source revision: `666c9fa` plus the uncommitted fixes from the entry above. Migration 44, `20261116214123_AddOrderItemNote`: one nullable `Note` column on `OrderItems`, nothing else.
Changes made:
- `FulfillOrderItem`: after the quantity and holds checks, a motor line whose TC asked is more than the free TC on its team's shelves (other people's holds excluded) is set `Waiting`, gets a note ("Thermocoupled not available at pickup, date (picker): N asked, M on the shelves."), writes a `TC Not Available` log row, and pulls nothing. Other lines on the order pick up as usual; the order stays Pending while any line waits, and records the picker if anything left.
- `AnswerNonTc` (`OrderService`, POST `/Home/AnswerNonTc`, Standard and up, then the orderer, Engineer and up on the item's Line, or Admin). Yes: a new order for the same quantity, linked with `SplitFromOrderId`, at the back of the line, taking whatever TC is free then, refused with "Only N available now" if it doesn't fit alongside every pending order and hold; the old line becomes `Resubmitted`. No: the line becomes `Declined`. Either way the order closes once nothing is Pending or Waiting, and a log row and the line's note record who answered. A Yes notifies the pickup-alert subscribers like a new order; an answer by someone else notifies the orderer.
- A short-pull correction's reissue goes through the same check, so it can wait too.
- Waiting lines don't count against stock (only Pending lines do), so the recheck on Yes is real.
- Pages: Pickup Queue warns on the line before Pick Up and lists held lines under "Waiting on an Answer" with Yes / No for whoever may answer; Order Details shows each line's note, a badge, and the question with "Yes, resubmit it" / "No, cancel this line"; My Activity opens with "Needs Your Answer". The orderer's notification is a new "OrderQuestion" category (the bell's default icon).
- Also: the "your order was picked up" notification now goes out only when a line actually left the shelf. It used to go out even when every line came up short.
Verification and observed results (fixed build on a scratch copy of `inventory.dev.db`, migrated 42 to 44 on start; five fixture orders written to the copy; driven through the real endpoints as six roster users):
- Pickup Queue showed "1 thermocoupled asked, 0 on the shelves. Pick Up holds this line…" before Pick Up, and Kason's own order sat under Ready to Pick: an orderer sees and picks up their own order. The page just had no sidebar link (only the Command Center "Pending" card and the notification led there), likely why Kason once couldn't find his own order; a "Pickup Queue" sidebar link for everyone signed in was added later that night at his word, checked as a Viewer.
- Kason picked up his own order (a TC motor line and an EEV line): the EEV line completed, the motor line waited with its note and log row, the stack was untouched, the order stayed Pending with Kason as picker. The queue moved it to Waiting on an Answer; My Activity and Order Details showed the question.
- Dustin's order (2 TC asked, none on the shelf), picked up by Kason: held, and Dustin got the notification. Colton (Standard, another Line) and Apoorva (Engineer, another Line) were refused by name; Hunter (Viewer) was stopped by the level gate; the line stayed Waiting.
- With another Pending order taking one of the two plain units, Dustin's Yes was refused ("Only 1 … available now … needs 2"), line still Waiting. Brady (Engineer, same Line) answered No: line Declined with the note, order Cancelled, Dustin notified. Answering again was refused.
- Kason's Yes: Order #22 with 1 Non-TC Motor, the old line Resubmitted with its note, the old order Completed, 8 pickup subscribers notified. Picking up #22 took a plain motor, loan 0.
- 3 TC asked with 1 TC on the shelf: the whole line waited and nothing moved; Yes reissued 3 with 1 TC; its pickup took that TC unit, loan 1.
- TC on the shelf: an ordinary pickup, TC taken, loan 1.
- A short pull on a TC line, corrected, reissued as #24 and held for the answer, with its own toast.
- In the browser: Order Details, Pickup Queue and My Activity rendered as above; no console errors; no server errors. Build 0 errors, 14 warnings. `inventory.dev.db` and `inventory.db` hashes unchanged (the migration was generated with a scratch connection string).
- Not checked: a real phone; the bell's look for the new category.
Remaining issues: BC-04, BC-11, BC-12, BC-13's formula-leading values (decisions); BC-09 as ruled.
Deployment status: committed and pushed as `0f949b3`, with the other fixes, in no release. Migration 44 rides with 43: the next release moves a database from 42 to 44 on first start. Back up first.
Links to saved artifacts: none kept; fixtures, script, results and screenshots were in the session scratchpad.


## 2026-11-16 — BC-04, two people editing one unit

Date: 2026-11-16 (late)
Purpose: BC-04, a Unit Tracker Edit form left open while someone else saves the same unit, then saved over their change. The audit's case: the old form's notes put back over a colleague's newer notes. Kason's ruling, scoped back to him and confirmed: refuse a stale save rather than merge it, and while the form is open, check in every 45 seconds in the background, with no page reload, to show who else has the unit open and whether it has changed. Unit Tracker only; Modify Stock's stale writes are BC-12.
Source revision: `666c9fa` plus the uncommitted fixes from the two entries above. No schema change, no migration.
Changes made:
- Edit form (`Views/UnitTracker/Index.cshtml`): seven hidden `orig_*` fields hold what the form showed when it opened (model, package, serial, sample, Line, Team, notes), filled by `openEdit` from the same row data as the visible fields.
- `TrackedUnitService.Update` and `UpdateNotes` take those values (`UnitSnapshot`) and refuse, with nothing saved and no log row, when any of them differs from what's stored now (both sides trimmed, line breaks folded): "Unit #N was changed since you opened it (last saved by X at h:mm PM). Your edit wasn't saved. Open the unit again to see the change, then make yours." It compares values, not `UpdatedAt`, so a Where or Build click (not on the form) doesn't count. A Viewer's Notes save compares Notes only, the one field a Viewer can change. A form posted without the `orig_*` fields (a page loaded before this build) saves as before. A refused save loses what was typed, as every refused save on this page does; the check-in below is the warning before that happens.
- `EditPresence` (new, one for the whole app): who has which unit's Edit form open, in memory only. A heads-up, not a lock: a restart forgets it, and the refusal above is the real guard. Someone who stops checking in drops off after 100 seconds, a little over two check-ins.
- `UnitTrackerController.EditCheckIn` (POST, anti-forgery; no level attribute, since Viewers edit Notes in the same form): records the caller and answers JSON with everyone else on that unit and how long ago they opened it, plus the changed sentence, ending "Reload before you save.", if the unit changed since the form opened. `leaving=true` takes the caller off.
- The form's script checks in when the form opens and every 45 seconds after, by `fetch`, so the page never reloads. The answer shows in a yellow box at the top of the form: "Brady Brasher (opened just now) is also editing this unit. Only the first save will go through." Closing the form or leaving the page sends `leaving`. The check-ins stop after 30 minutes, so a form left open doesn't keep a session alive forever.
Verification and observed results:
- The audit's reproduction, the same script on both builds, each on its own copy of `inventory.dev.db`: Kason's form opens on unit #15 with "Original note.", Brady saves "New note from colleague.", Kason's old form changes only the serial. Before (the play build, `bin/Debug` from 15:48, which has the BC-02 work and not this): saved, the notes back to "Original note.", Brady's note gone. After: refused with the sentence above; Brady's note and the old serial kept.
- Fixed build on a scratch copy, through the real endpoints as Kason (Admin), Brady (Engineer), Dustin (Standard) and Hunter (Viewer):
  - Kason's stale save after Brady's serial change: refused, no log row. Reopened, it saved.
  - Hunter's stale Notes save after Dustin's note: refused, Dustin's note kept. Brady changing only the serial didn't block Hunter's note.
  - Dustin, who can't move a unit, was refused after Brady moved it to Ninja.
  - Where and Build set by Dustin while Kason's form was open: Kason's save went through.
  - A two-line note posted with CRLF line breaks: a change saved, an untouched save answered "Nothing changed." A blank Team and a form with no `orig_*` fields both saved.
  - A unit deleted while open: "Unit #22 no longer exists."
  - Check-ins: each user saw the others; a Viewer could check in; `leaving` took a user off at once; after Dustin saved, both open forms got the changed sentence; no token was a 400; an unknown unit answered "no longer exists". A user who went quiet was still listed at 60 seconds and gone at 105.
- In the browser, as Kason on unit #10 (a two-line note), with Brady and Dustin driven by a script: the heads-up showed on open, cleared on the next 45-second tick after Brady left, then showed the changed sentence after Dustin saved. Three check-ins, no reload (a marker set on the page survived). Saving that form: refused, the toast as above, nothing written. Closing the form, and leaving the page by saving, each took Kason off the list. Reopened: an untouched save said "Nothing changed.", an edit saved. No console errors, no server errors.
- Build 0 errors, 14 warnings. `inventory.dev.db` and `inventory.db` hashes unchanged.
- Not checked: a real phone; two real browsers on two machines (the second user was a script on the same endpoints); the 30-minute stop.
Remaining issues: BC-11, BC-12, BC-13's formula-leading values (decisions); BC-09 as ruled.
Deployment status: committed and pushed as `0f949b3`, with the fixes above, in no release. No migration of its own.
Links to saved artifacts: none kept; the scripts and results were in the session scratchpad.


## 2026-11-18 — BC-11, BC-12, and BC-13's formula part

Date: 2026-11-18
Purpose: the last open audit findings before a release. BC-11, two registrations at once getting the same ItemId; BC-12, two stock changes at once, the second saving over the first; BC-13's formula-leading values. Kason confirmed the scope as written back: BC-11 with a unique index and the next number on a clash; BC-12 by locking before the read (no migration, no refusal), over a version column that would refuse the second change; BC-13 with a leading `'`.
Source revision: `8b081c6` (code `0f949b3`) plus these changes, committed as `fe9f8e6`. Migration 45 (`UniqueItemId`).
Changes made:
- `Data/AppDbContext.cs`: a unique index on `InventoryItems.ItemId`; migration `20261118205859_UniqueItemId` creates `IX_InventoryItems_ItemId`. No duplicate ItemId in the 11/11 host copy (MASTER128 `Inventory_Bus`), `inventory.dev.db` or local `inventory.db`, checked on copies.
- `InventoryService.SaveNewItem`: saves a new item, and when the index refuses its ItemId (SQLite error 19 on `InventoryItems.ItemId`), generates the next one, updates the item and its New Registry log, and saves again, up to 5 tries. After 5 the item is taken back out: Registry says "Couldn't assign an Item ID. Nothing was registered. Try again."; an Intake row gets that as its error and the rest of the batch goes on. Used by `CreateItem` and `CommitIntake`.
- `GenerateItemId` takes the ids an Intake preview has already handed out, so a batch's new items preview one after another. The never-reuse rule (VIS-15) is unchanged.
- `ModifyStock` now opens the transaction, SQLite's `BEGIN IMMEDIATE`, before it reads the item, and reloads anything the request already had loaded; the old body is `ModifyStockLocked`. Inside a caller's transaction (the Intake stock batch) it runs as before. `AddStock` (Quick Add) does the same. Pickup, short-pull, returns and the batch already opened theirs first.
- `Csv()`: a value starting with `=`, `+`, `-` or `@` gets a leading `'`. Only text fields go through it; quantities don't.
Verification and observed results:
- Through the real endpoints, 8 signed-in sessions posting at the same instant, each build on its own copy of the 11/11 host db (the fixed build migrated its copy 44 to 45 on start):
  - Registrations, 4 rounds of 8 Commercial valves. Before: round one all 8 got CVE-0009; 21 duplicate ids in 4 rounds, each toast naming the duplicate. After: CVE-0009 to CVE-0040, 32 distinct, every New Registry log's ItemId matching its item.
  - Stock on CCL-0030 V283, compared with the sum of its new log rows. Before: 8 scraps of 1 moved the stack by 1 (logs said 8); 4 adds of 2 against 4 scraps of 1, +2 (logs +4); 4 Quick Adds of 1 against 4 adjustments of -1, -2 (logs 0); a pickup of 3 during 4 adjustments of -1, -2 (logs -7). After: -8, +4, 0 and -7, each matching.
- After, one at a time on the fixed build: an Intake commit of 3 new valves and 4 registrations at the same instant got 7 distinct ids. An Intake preview of 3 new valves showed CVE-0043, 0044, 0045 (before: CVE-0022 three times). CVE-0041 registered at quantity 0 and deleted; the next registration got CVE-0042. A Location Transfer split, an Intake stock-batch Add, and a refused Scrap on a stack that's gone (nothing changed, and the next change saved, so no lock was left held) all behaved as before.
- CSV: an item named `=1+1 formula valve`, part number `-5-X`, brand `@home` exported as `'=1+1 formula valve`, `'-5-X`, `'@home`; `+plus valve` as `'+plus valve`. 51 rows, 14 fields on every one.
- Build 0 errors, 14 warnings. The MASTER128 original, `inventory.dev.db` and `inventory.db` hashes unchanged.
- Not checked: recorded-unit changes racing a stock change; the task and delivery "first claim wins" races; a real phone.
Remaining issues: BC-09, as ruled. BC-12's fix leans on SQLite's one-writer lock; a move off SQLite needs another look.
Deployment status: committed as `fe9f8e6`, in no release. Migration 45 rides with 43 and 44: the next release moves a database from 42 to 45 on first start.
Links to saved artifacts: none kept; the race scripts and results were in the session scratchpad.


## Continuing a debugging session

Read this file and the repository handoff first. Confirm the source revision and working-tree state before relying on an old result. Test writes go to disposable database copies. Record local fixes and deployment as separate statuses; mark a defect fixed only after checking its reproduction against the repaired code.

Append a dated entry containing: purpose, source revision, issue IDs, changes made, tests actually run and observed results, remaining issues, deployment status, and artifact locations. Keep older entries as history.

The original shared log and reproduction artifacts remain archived in OneDrive at `C:\Users\woods\OneDrive\Documents\VIS and Sourcing Debug Sessions`. Links into that archive require the same local OneDrive path; the findings, session descriptions, and recorded statuses are included in this repository document.