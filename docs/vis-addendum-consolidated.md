---
title: VIS — Consolidated Context Addendum
description: Merged from four sources. Companion to VIS_Handoff_State.md, which stays authoritative for code and architecture.
sources: Pass 9 handoff reconciliation · Passes 1–4 thread · Cowork go-live & intake thread · Passes 5–23 working method
merged: 2026-08-04, updated 2026-08-13
status: consolidated
---

# VIS — Consolidated Context Addendum

**Authority order when two sources disagree:**
1. `VIS_Handoff_State.md` — for code, schema, and architecture.
2. This file — for everything that never lived in the repo.
3. On the **IT intake track only**, the Cowork thread outranks the handoff — it ran into
   early August and the handoff has no visibility into it at all.

---

## Timeline — how the threads actually order

The pass numbers looked out of sequence because one of these threads isn't in the pass
sequence at all. Resolved:

| When | Thread | What it was |
|---|---|---|
| **Jul 9 – 15** (deliverables)<br>**→ early Aug** (planning) | **Cowork go-live & intake** | Not a numbered pass. Deployment, the real-data import, repo cleanup, and the IT intake track. Runs *parallel* to everything below. |
| ~Jul 15 – 24 | **Passes 1–4 thread** | Superuser Settings, users/notifications, Rheem PN `N/A`, Branch/Line, compressor units. |
| Jul 25 | Pass 5 + compressor rebuild | `AllowNARheemPartNumber`. The three stacked migration bugs. |
| Jul 26 – Aug 1 | **Passes 5–9 session** | 6A/6B/7A/7B/7C/8/9. Ends at migration 26, `AddIntakeBatches`. |
| Aug 3 | Handoff first written | |
| Aug 5–6 | **Passes 10–12** | Went live for real. See handoff. |
| Aug 6–7 | **Pass 13** | Data reconciliation, access-control pass, Delete Item, Branches/Lines redesign. See handoff. |
| Aug 7 | **Pass 14** | RCR compressor rename, Quick Filter/access polish, Activity Feed fix, 9 new users. The Pass 13/14 db is now actually on the host, not just staged. See handoff. |
| Aug 7 | **Pass 15** | Mandatory Line on new registrations; View Logs/Activity Feed now Line-scoped like browsing already was. Code only, no data changes. See handoff. |
| Aug 10 | **Pass 16** | Unclaimed compressor/motor filter fixed (AND → OR); 242 non-compressor items bulk-reconciled onto Commercial Packaged/Splits; Add User gained whole-Branch assignment at creation; `User.Team` rebuilt as many-to-many with a team-centric membership picker. See handoff. |
| Aug 11 | **Pass 17** | Serial (compressors) / TC-count (motors) capture added to New Item Registry and Bulk Intake; a real pre-existing bug in Bulk Intake's "hold for unrecognized location" path (never actually worked -- `"__NEW__"` leaked into real data) found and fixed. See handoff. |
| Aug 12 | **Pass 18** | Cancelled-order pickup race closed: `PickUpOrder` now rejects any non-Pending order, so a stale Pickup Queue page can no longer pull real stock against an order an Engineer already cancelled (which also silently flipped it back to Completed). One guard line; cancelled orders' lines deliberately stay `Pending`. See handoff. |
| Aug 12 | **Pass 19** | Merged same batch as Pass 18, three more worktree-session fixes: `ReportShortPull` reissue now carries TC count/requested location forward (was silently dropping both); `ReturnLoan` no longer stores a dangling `ItemVariantId = 0` on units returned to a brand-new location; three misleading user-facing messages (Scrap log overstatement, Ownership silent no-op, intake "nothing imported" on a partial failure) corrected. See handoff. |
| Aug 12–13 | **Pass 20** | Facility map: removed the red tracker dot, made zone row-counts hover-only. Bulk Intake gained the same Team→Branch/Line autofill Registration already had, seeded from the signed-in user. New Item Registry's name-match dropdown now jumps into Modify Stock instead of doing nothing, and Modify Stock's Add action gained compressor serial capture to match. See handoff. |
| Aug 13 | **Pass 21** | Bulk Intake no longer silently quick-adds a name match to existing stock -- the row moves to an "Already registered" list instead, merged by item and applied through a new batch Modify Stock review modal (Add/Adjustment only, one acknowledge checkbox per section, Apply All gated on all of them, applied in one transaction). Verified live with a real +1/-1 round trip on CCR-0001. See handoff. |
| Aug 13 | **Pass 22** | Feedback that the batch review modal "felt like a dangling process" -- rebuilt as live inline sections on the Intake page itself (a match creates one immediately; a second match for the same item grows it in place, preserving hand-edits), acknowledge checkboxes dropped since nothing's hidden behind a modal anymore. Also gave Intake's Type field, Registry's Type field, and the sign-in name field the same styled dropdown look used elsewhere, and fixed a real pre-existing broken regex on the sign-in field found in the process. See handoff. |
| Aug 13 | **Pass 23** | Delete Stack: a new Admin-gated variant-level hard delete for the gap Delete Item doesn't cover (one empty stack on an item that still carries stock elsewhere) -- built and immediately used for real on the actual stuck item (CCR-0013) that surfaced the gap. Rack/Row fields on Registry, Intake, Export Wizard (new there), and Modify Stock now cascade-suggest from real stock data under the picked Parent/Major/Sub, still fully free text. See handoff. |

The Cowork thread is a long-running parallel track, not a predecessor. Its **code**
knowledge froze on July 15 and is stale everywhere. Its **IT/organizational** knowledge
runs to early August and, as of that point, was the newest of any source here.

---

## Corrections — do not carry these forward

Superseded, not merely outdated:

1. **Location vocabulary is not fixed.** Pass 7C replaced four hand-kept copies with a
   managed `Locations` table; Pass 9 found three more. 18 locations, editable in Settings.
2. **Branch/Line vocabulary is not fixed either, as of Pass 13.** It used to be a
   hardcoded 2-Branch structure; it's now a managed `Branches`/`OrgLines` pair, same
   Settings-editable pattern as Teams and Locations. Anything describing the org
   structure as fixed is now stale, not just the location vocabulary.
3. **Visibility is Line, not Team.** `item.Line == "" || item.Line == user.Line`, Level 5
   bypasses, blanks fail open by design. Consequence: the four IT deliverables built in
   the Cowork thread (`VIS_Intake_Documentation.docx`, `VIS_Copilot_Context_Primer.txt`,
   `VIS_Lab_Overview.docx`, `VIS_Implementation_Guide.docx`) all describe Team-scoped
   visibility and need a correction before further distribution.
4. **Group is derived and frozen at creation**, removed from the registration form in
   Pass 7A.
5. **Migration count.** 15 → 26 → 33 as of Pass 13 → 34 as of Pass 16 → 35 as of
   Pass 17. Anything reasoning from migration numbers is stale.
6. **Claude hand-authors migration files now, when it has shell access.** Old rule was
   "supply the model + DbSet diff and stop." Both the Cowork and Passes 1–4 threads ran
   under the old rule. I still compile, run, and test everything myself either way.

---

## Environment and build

None of this is visible in code; all of it has cost debugging time.

- **Stale Debug build (.NET 10).** Compiled binary silently runs old code. F5 defaults to
  Debug, no Razor runtime compilation. Clean → Rebuild after every apply.
- **PMC can't run EF migrations.** NU1903 on SQLitePCLRaw trips
  `$ErrorActionPreference = "Stop"`. Use `dotnet ef` from Developer PowerShell.
- **Firewall blocks port 5000.**
  `New-NetFirewallRule -DisplayName "Inventory Dev Two" -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow`
  Needs admin. Office network permissive, home not.
- **Two machines means stale artifacts.** Standing rule: byte-compare and tail-check
  every delivered artifact. Verify against live schema, never the zip.
- **Two `appsettings.json` copies** (project vs publish) cost one session. Check
  `appsettings.Development.json` layering when config seems ignored.
- **A copied `.db` file needs its `-wal`/`-shm` sidecars copied as one atomic set, or
  not at all (Pass 13).** A stale sidecar pair left behind from an earlier backup, next
  to a freshly-copied newer main file, produces `database disk image is malformed` even
  under strict read-only access — the corruption is real, in the bytes, not a locking
  artifact. Re-copying as a clean set (or dropping the stale sidecars if the source is a
  standalone file) is the fix.

## Z-index registry

Load-bearing, easy to lose track of — the worst bug in project history was a z-index
collision.

| Layer | z-index |
|---|---|
| holo overlay | 2000 |
| modal-backdrop | 2150 |
| modal | 2200 |
| not-authorized banner | 4000 |
| toast | 12000 |

Bootstrap's confirm modal defaults to 1055 and opened *behind* the holo overlay at 2000
— visible and completely unclickable, zero network requests on every attempt. All
modals pinned to 2200 with `!important`, which is why new modals chain off
`hidden.bs.modal` rather than stacking.

## Code-level traps

- **`@` is a Razor transition inside `<script>` blocks and JS comments.** `@RenderBody()`
  in a JS comment invoked it for real and took the dashboard down. No such thing as a
  safe comment in a `.cshtml`.
- **`someNullable?.TryGetValue(key, out var x)` doesn't compile** — "use of unassigned
  local variable." The null-conditional skips the entire call including the `out`
  assignment. Fix:
  ```csharp
  T? x = null;
  someNullable?.TryGetValue(key, out x);
  ```
  Grep for `?.TryGetValue(` before shipping anything touching an optional dictionary param.
- **`PendingModelChangesWarning` aborts `Migrate()` before any SQL runs.** First thing to
  check when migrations "silently don't apply."
- **`site.js` loads after `@RenderBody()`** — a view's inline script runs first. Moving
  shared JS there breaks any IIFE calling it at parse time.
- **Hardcoded vocabulary hides in more shapes than you expect.** Eight copies of the
  location list turned up across four passes, each storing something different.

## Migration recovery — three variants

`Program.cs` wraps `Migrate()` and first-run seeding in one try/catch that only
`Console.WriteLine`s — never rethrows, never crashes. I was offered hardening for this
and declined it; no need to re-offer.

1. **Migration ran against a stale dev database** — swap the intact pre-migration backup
   `.db` into the app path and let `Migrate()` replay against real data.
2. **Dangling history stamp** after a migration file was hand-deleted before
   `migrations remove` ran —
   `DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%<name>%'`, write changes,
   clean rebuild, re-add.
3. **Silent no-op on a clean rebuild.** Hand-run the migration's `Up()` as raw SQL
   against `C:\VIS_Inventory\inventory.db` in DB Browser, then insert the history row so
   `Migrate()` won't retry-and-fail next boot:
   `INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('<id>', '10.0.8');`

---

## The go-live import (Jul 13) — data provenance

Real inventory replaced test data via a scripted rebuild from the Samurai/Ninja `.ods`
sheets: 309 item families, 318 piles, ~2,859 units, 29 users. Full record in
`Import_Decision_Summary.txt`. (The later compressor rebuild is a separate event that
replaced the compressor slice — 825 units across two locations.)

- **27 Ninja rows with negative quantities were skipped at import — accepted as data
  debt, not being chased further.** I asked the original team about it directly; the
  honest answer was they'd just count better going forward, no better record existed to
  recover. Imperfect starting data that improves through normal use beats spending more
  time trying to reconstruct numbers nobody can verify anymore.
- 12 zero-quantity rows imported as catalog entries. `staticdata` legacy table dropped.
- **Category vocabulary was created at import**: Motor, VFD, Control, Valve, Sensor,
  Filter Drier, Hardware, and "Plant" — a temporary bucket for unidentified Rheem part
  numbers, meant for later recategorization.
- **Prefix collision: Control and Coil both generate `CCL-`**, sharing a sequence.
  App-faithful, deliberate.
- **"2-6" is an Excel artifact of "20-6"** — date auto-format mangling. Both encode to
  the same stored code.
- Project codes: Samurai 7166, Ninja 7165.

**Roster provenance (Jul 13):** 29 users at go-live = 20 pre-existing + 9 added, including
Luis Zapata, Karthig Kathirvel, Derek Brausell, David Gudapati, Preston Davis, Javier
Lucio, Swapnil Khaire, Andrew Blevins, Hunter Little. Not the sole Level 5 anymore as of
Pass 13 (Derek and Luis Zapata also have full Admin now). Roster is at 51 users as of
Pass 14 — see the handoff for what's changed since.

## People and the decision path

Two tracks, and the second one is above the first.

**Lab / operational**
- **Kevin Ray** — Sr. Manager, Engineering R&D. Business owner and sponsor.
- **Conner** (Samurai) and **James** (Ninja) — supervisors. **Kevin** — null team,
  sees all.
- **Gavin** — Standard-level runner, handles pickups.
- **Codey** — flagged the company is moving to Azure SQL, not VM-hosted SQL Server.

**IT / organizational**
- **Garrison Musgraves (IT)** — leading a formal IT intake process as project manager:
  architecture, functionality, business purpose, long-term ownership documentation. Met
  with me early August.
- **Karthig Kathirvel** (Directing Engineer) — sent written support citing lab
  visibility and potential NA-wide standardization. Reports to **Derek Brausell** (Sr.
  Director, R&D Engineering, Ft. Smith), also supportive. Both sit above Kevin Ray, both
  in the Users table. As of Pass 13, Karthig is scoped to see all of Commercial Air
  (via the new `User.Branch`), Derek has full Admin visibility.
- **Server Hosting Review** — Chris Tovar, Aaron Roberts, Andy, Byron, Ashley
  (Cybersecurity).
- **Unresolved as of 2026-08-04, still open:** how the IT intake relates to the Server
  Hosting Review — absorb, precede, or reframe. A question for Garrison at the intake
  meeting, not something to infer, since it changes what gets prepared.

## Hosting and security

- **[decided] Phased, not simultaneous.** Initial deployment stays on SQLite; engine
  swap is a separate later move. My reasoning: the SQLite file reads more easily than an
  Excel export, so data access isn't a concern at this stage.
- **Target is Azure SQL**, per Codey — changes the connection string, not the EF Core
  provider.
- **SSO via Microsoft Entra ID / `Microsoft.Identity.Web`.** `First.Last` maps straight
  onto `firstname.lastname@rheem.com`. Users roster and access levels stay as
  authorization; Entra handles authentication only. This is the answer to the review's
  hardest question.
- **The two hardest questions**: session identity with no password layer, and SQLite for
  multi-user use. Both still open.
- **The SQLite objection is about concurrency and durability, not readability.** At 29+
  users SQLite is probably fine in practice; *fine in practice* and *answerable to a
  review* are different standards, and the backup gap has no defence yet.
- **The Copilot primer contains scripted honest lines** for both hard security questions
  plus a deliberate do-not-commit list. The plaintext superuser passcode postdates those
  scripts — the honest lines don't cover it.

### Repo hygiene — one item, two halves, closure unconfirmed
- The original repo had the full real `inventory.db` plus sidecars committed — real
  stock, orders, and the employee roster. Remedy: a new clean repo
  (`Visual_Inventory-0726`) with fresh `.git` and extended ignores; the old repo to be
  verified Private and then deleted. **Status as of 2026-08-04: private, still up** —
  needs a follow-up check.
- The superuser passcode is plaintext in `appsettings.json`, tracked in git. History
  keeps the old value even after a change, so rotating matters more than removing the
  line, and deleting the repo doesn't unring it for anyone who already cloned.
- These are the same problem and Cybersecurity is in the room for the hosting review.
  Treat as one line item.

### Deployment lineage
- The database was deliberately moved out of the publish folder to
  `C:\VIS_Inventory\inventory.db` so republishing can never touch data.
- Served via `$env:ASPNETCORE_URLS = "http://0.0.0.0:5000"`.
- `.sqbpro` files are DB Browser session sidecars — created only by that GUI, never by
  the app or EF Core. Safe to delete, zero signal.

---

## How I like to work

- **"Spiterate before building."** My own term, for anything that's a genuinely new
  mechanic rather than a routine addition to an existing pattern: stop and write the
  complete scope back in prose — every field, every access-level gate, every edge case —
  and wait for my explicit confirmation before opening a file. Prevents piecing out bad
  context and shipping functional garbage. Used successfully across most of what shipped
  in Passes 11–13: Team.Line, MotorUnit, the Sustaining branch, Delete Item, and the
  Branches/Lines redesign all went through this.
- **"Parked"** means explicitly deferred and doesn't need re-proposing later.
- Follow-on features take 2a/2b suffixes. I stop builds deliberately to preserve
  context rather than letting scope run long.
- **"I'll leave it" / "your call"** means surface the options, don't decide for me.
- I like physical analogies for describing system behavior — filing cabinets, rooms and
  doors, keycards, eggs in a carton. "Master key not stored in any lockbox it opens" is
  one of these.
- I verify in both light and dark theme — the app remembers each user's last choice
  across logins, so a light-mode contrast bug ships invisible to whoever's in dark.
- Structured multiple-choice beats open prose for clarifying-question rounds.
- **I test every pass before asking for the next one.** A request for the next pass is
  the confirmation the last one landed — I won't necessarily say "that worked," I'll
  just move on to "okay now pass 7." Silence isn't a signal that something's untested.
- **When I say something works, believe it and re-diagnose from there** rather than
  restating the original theory louder.
- **I'll supply real artifacts, not just descriptions** — actual `.db` files, CSVs,
  console output, screenshots — when something's hard to pin down from a description
  alone. Ask for the real thing if a bug isn't reproducing.
- **I decide fast and don't re-litigate.** Going back to double-check a decision I've
  already made is friction, not diligence.
- **I'll reshape a proposal rather than just accepting it**, and the redirect is usually
  the actual insight — worth taking seriously, not working around.
- I own my own scope creep when I catch it, and I mean it when I do.

### What's worked well from the other side

- **Verify against my real data, don't assert.** Load the actual database and run it.
  Simulating the old broken behavior to prove a diagnosis, not just the fix, is what
  turns "I think this is why" into "here it is failing on your data."
- **Own mistakes flatly and immediately** — named in one sentence and fixed, no hedging,
  no apology spiral. I'd rather have it identified and gone than smoothed over.
- **Refuse to guess when it matters** — ask rather than assume on anything where a wrong
  guess would need to be found and unwound later (a brand name, a naming convention, a
  format). I'll answer.
- **Flag what I didn't ask about, with evidence** — I act on most of what gets surfaced
  this way.
- **Push back when my own instinct contradicts my own stated reasoning** — I'd rather be
  argued with using my own logic than agreed with by default.
- **Register:** direct, low ceremony, no preamble, findings before conclusions. Length
  should track stakes — a schema change earns full reasoning, a one-line fix earns one
  line.

---

## Open items — consolidated

**Confirmed defects, resolved**
1. ~~Short-pull logs ordered quantity, not pulled.~~ Fixed in Pass 10 — a short pull
   refuses outright instead of silently under-fulfilling. Rationale: a short pull is a
   data-integrity event, not a fulfillment variation. Correct sequence is cancel the
   order → adjust stock to the true count → re-order the real quantity → pick up clean.
   No partial-completion state, no remainder-owed ledger.

**Still present, cosmetic**
2. `.map-stats-bar` / `.map-live-bar` are `position: absolute`; wanted in flex-column flow.

**Data debt, accepted**
3. 27 skipped negative-quantity Ninja rows from go-live — see above, not being chased.

**Resolved, Pass 13**
4. `AlertThreshold` is 0 on all compressors — still true, `SetDefaultThreshold` exists
   to bulk-fix whenever I get to it.

**Unverifiable from code**
5. TC counts aren't reserved across concurrent pending orders. No reservation logic
   exists; confirming needs two concurrent orders against the same TC stock.
6. Disposed-transaction rollback in a `catch` in `ReturnLoan`/`ScrapLoan`. Practically
   unreachable. Lowest priority.

**Closed on inspection — do not re-investigate**
7. Fresh-database Viewer lockout — the seeder assigns explicit levels, me as Admin.
8. Leftover test scaffolding in `Program.cs` — none present.
9. `syncHeaderOffset()` not executing — wired correctly.
10. `.holo-viewer` collapsing — both now carry explicit widths.
11. `<select>` imbalance in `Index.cshtml` — false positive, a `<select>` inside a JS
    comment.
