# VIS — Bugs and Debugging Log

Filed in this repository on **2026-10-03**, split from the shared OneDrive debugging records. This is the VIS-specific home for subsequent debugging session notes.

**Recorded status:** VIS-1 through VIS-6 were fixed in `1df2e45` and staged as `VIS_Release_20260925`. The latest recorded VIS deployment status still says not installed on the host; installation has not been checked again here. VIS-7 through VIS-14 were reported open in the 2026-09-26 audit. The local HEAD at filing was `a35850b`, the same revision examined by that later audit.

**Update 2026-10-04:** VIS-7, VIS-8 and VIS-10 through VIS-14 are fixed in `0b03c94` (Pass A), `a077663` (Pass B) and `d39553d` (Pass C), committed and pushed; none of it is in a release yet. VIS-9 is the one audit item still open: it is a new mechanic, scoped on 2026-10-04 and held for its own pass. See the 2026-10-04 entry below.

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
| VIS-9 | P2 | Scrap, downward Adjustment, short-pull correction and "No serial" pickups leave recorded units On Hand past the stack's quantity | Open; live on CCR-0005 V1 (new) and CCR-0213 V2 (known). Scoped 2026-10-04 as its own pass, not started |
| VIS-10 | P2 | Cancel has no status guard or log: a stale page flips a picked-up order to Cancelled | Fixed in 0b03c94 (Pass A); not in a release |
| VIS-11 | P2 | Delete Item ignores loans with no unit rows (Controls); the loan can't be returned afterward | Fixed in 0b03c94 (Pass A); not in a release |
| VIS-12 | P2 | New Item Registry accepts a negative quantity (form.submit() skips min; no server bound) | Fixed in 0b03c94 (Pass A); not in a release |
| VIS-13 | P2 | AccessLevel/Line/Branch cached in the session: demoting or hiding a user doesn't reach an open session | Fixed in d39553d (Pass C); not in a release |
| VIS-14 | P2 | Intake mints the ItemId prefix from the submitter's Line, Registry from the item's; whole-Branch users always get C | Fixed in a077663 (Pass B): the item's Line decides, as Registry does; not in a release |

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


## Continuing a debugging session

Read this file and the repository handoff first. Confirm the source revision and working-tree state before relying on an old result. Test writes go to disposable database copies. Record local fixes and deployment as separate statuses; mark a defect fixed only after checking its reproduction against the repaired code.

Append a dated entry containing: purpose, source revision, issue IDs, changes made, tests actually run and observed results, remaining issues, deployment status, and artifact locations. Keep older entries as history.

The original shared log and reproduction artifacts remain archived in OneDrive at `C:\Users\woods\OneDrive\Documents\VIS and Sourcing Debug Sessions`. Links into that archive require the same local OneDrive path; the findings, session descriptions, and recorded statuses are included in this repository document.