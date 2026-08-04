---
title: VIS — Consolidated Context Addendum
description: Merged from four sources. Companion to VIS_Handoff_State.md, which stays authoritative for code and architecture.
sources: Pass 9 handoff reconciliation · Passes 1–4 thread · Cowork go-live & intake thread · Passes 5–9 working method
merged: 2026-08-04
status: consolidated
---

# VIS — Consolidated Context Addendum

**Authority order when two sources disagree:**
1. `VIS_Handoff_State.md` (Pass 9) — for code, schema, and architecture.
2. This file — for everything that never lived in the repo.
3. On the **IT intake track only**, the Cowork thread outranks the handoff — it ran into
   early August and the handoff has no visibility into it at all.

---

## Timeline — how the threads actually order

The pass numbers looked out of sequence because **one of these threads isn't in the pass
sequence at all.** Resolved:

| When | Thread | What it was |
|---|---|---|
| **Jul 9 – 15** (deliverables)<br>**→ early Aug** (planning) | **Cowork go-live & intake** | Not a numbered pass. Deployment, the real-data import, repo cleanup, and the IT intake track. Runs *parallel* to everything below. |
| ~Jul 15 – 24 | **Passes 1–4 thread** | Superuser Settings, users/notifications, Rheem PN `N/A`, Branch/Line, compressor units. |
| Jul 25 | Pass 5 + compressor rebuild | `AllowNARheemPartNumber`. The three stacked migration bugs. |
| Jul 26 – Aug 1 | **Passes 5–9 session** | 6A/6B/7A/7B/7C/8/9. Ends at migration 26, `AddIntakeBatches`. |
| Aug 3 | Handoff written | |

**The thing to internalize:** the Cowork thread is a long-running *parallel* track, not a
predecessor. Its **code** knowledge froze on July 15 (15 migrations, hardcoded
`LocationCodec`, Team visibility) and is stale everywhere. Its **IT/organizational**
knowledge runs to early August and is the newest of any source here. Read it that way and
the apparent contradictions disappear — it isn't behind, it's looking at a different axis.

---

## Corrections — do not carry these forward

Superseded, not merely outdated:

1. **Location vocabulary is no longer fixed.** Pass 7C replaced four hand-kept copies with a
   managed `Locations` table; Pass 9 found three more. 18 locations, editable in Settings.
   The Cowork thread's planned `LocationCodec.CanonicalNames` array edit was **never
   executed and must not be resurrected.**
2. **Visibility is Line, not Team.** `item.Line == "" || item.Line == user.Line`, Level 5
   bypasses, blanks fail open by design. **Consequence:** the four IT deliverables built in
   the Cowork thread (`VIS_Intake_Documentation.docx`, `VIS_Copilot_Context_Primer.txt`,
   `VIS_Lab_Overview.docx`, `VIS_Implementation_Guide.docx`) all describe Team-scoped
   visibility and need a correction before further distribution.
3. **Group is derived and frozen at creation**, removed from the registration form in Pass 7A.
4. **Migration count 15 → 26.** Anything reasoning from migration numbers is stale.
5. **Claude hand-authors migration files now.** Old rule was "supply the model + DbSet diff
   and stop." Both the Cowork and Passes 1–4 threads ran under the old rule. Kason still
   compiles, runs, and tests. Real reversal — don't apply the old rule.

---

## Environment and build

None of this is visible in code; all of it has cost debugging time.

- **Stale Debug build (.NET 10).** Compiled binary silently runs old code. Cost a full
  session diagnosing role gating that was already correct on disk. F5 defaults to Debug, no
  Razor runtime compilation. Clean → Rebuild after every apply. Runtime compilation was
  considered and rejected: deprecated in .NET 10, kills Hot Reload. *The handoff leans on
  this in carried-item 5 without ever defining it.*
- **PMC can't run EF migrations.** NU1903 on SQLitePCLRaw trips `$ErrorActionPreference =
  "Stop"`. Use `dotnet ef` from Developer PowerShell.
- **Firewall blocks port 5000.**
  `New-NetFirewallRule -DisplayName "Inventory Dev Two" -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow`
  Needs admin. Office network permissive, home not.
- **Two machines means stale artifacts.** A zip once predated a migration already applied on
  the working machine. Worse variant from the Cowork thread (Jul 9): a zip built from a stale
  sandbox read delivered a `Program.cs` **cut off mid-line** — it compiled fine and the app
  exited silently before `app.Run()`. **Standing rule: byte-compare and tail-check every
  delivered artifact.** Verify against live schema, never the zip.
- **Two `appsettings.json` copies** (project vs publish) cost one session. Check
  `appsettings.Development.json` layering when config seems ignored.

## Z-index registry

**Absent from the handoff** and load-bearing — the worst bug in project history was a
z-index collision.

| Layer | z-index |
|---|---|
| holo overlay | 2000 |
| modal-backdrop | 2150 |
| modal | 2200 |
| not-authorized banner | 4000 |
| toast | 12000 |

Bootstrap's confirm modal defaults to 1055 and opened *behind* the holo overlay at 2000.
"Submit Now" was visible and completely unclickable — zero network requests on every attempt.
All modals pinned to 2200 with `!important`, which is why new modals chain off
`hidden.bs.modal` rather than stacking. The 2000/2150/2200/12000 rows were verified in source
on Jul 9; the 4000 banner is later.

## Code-level traps

- **`@` is a Razor transition inside `<script>` blocks and JS comments.** `@RenderBody()` in
  a JS comment invoked it for real and took the dashboard down. No such thing as a safe
  comment in a `.cshtml`.
- **`someNullable?.TryGetValue(key, out var x)` doesn't compile** — "use of unassigned local
  variable." The null-conditional skips the entire call including the `out` assignment, and
  the compiler can't see past it. Bit `OrderService.PickUpOrder` once. Fix:
  ```csharp
  T? x = null;
  someNullable?.TryGetValue(key, out x);
  ```
  Grep for `?.TryGetValue(` before shipping anything touching an optional dictionary param.
- **`PendingModelChangesWarning` aborts `Migrate()` before any SQL runs.** First thing to
  check when migrations "silently don't apply."
- **`site.js` loads after `@RenderBody()`** — a view's inline script runs first. Moving shared
  JS there breaks any IIFE calling it at parse time.
- **Hardcoded vocabulary hides in more shapes than you expect.** Eight copies of the location
  list, each storing something different: codes (`value="RLB"`), names (`value="RD Lab"`), a
  JS object, an image map, a stats dictionary keyed by name.

## Migration recovery — three variants

`Program.cs` wraps `Migrate()` and first-run seeding in one try/catch that only
`Console.WriteLine`s — never rethrows, never crashes. **Kason was offered hardening and
declined; the handoff says twice, the Passes 1–4 thread records one of them explicitly.
Don't re-offer it as new.**

1. **Migration ran against a stale dev database** — swap the intact pre-migration backup `.db`
   into the app path and let `Migrate()` replay against real data.
2. **Dangling history stamp** after a migration file was hand-deleted before
   `migrations remove` ran —
   `DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%<name>%'`, write changes, clean
   rebuild, re-add.
3. **Silent no-op on a clean rebuild** (hit twice: Pass 3 `Users.Line`/`InventoryItems.Line`,
   Pass 4 `CompressorUnits`). Hand-run the migration's `Up()` as raw SQL against
   `C:\VIS_Inventory\inventory.db` in DB Browser, then insert the history row so `Migrate()`
   won't retry-and-fail next boot:
   `INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ('<id>', '10.0.8');`
   `10.0.8` confirmed from the snapshot's `ProductVersion` annotation.

**Open:** the root cause was never read off the console either time in variant 3. If a third
migration fails to auto-apply, that's a pattern — read the swallowed line before reaching for
the workaround again.

---

## The go-live import (Jul 13) — data provenance

Real inventory replaced test data via a scripted rebuild from the Samurai/Ninja `.ods` sheets:
**309 item families, 318 piles, ~2,859 units, 29 users.** Full record in
`Import_Decision_Summary.txt`. (Note: the later **compressor rebuild** is a *separate* event
that replaced the compressor slice — 825 units across two locations. Deployed state of 487
items reflects both.)

- **27 Ninja rows with negative quantities were SKIPPED — open data debt.** The team must
  re-add those with true counts. List is in the decision file. This appears nowhere in the
  handoff.
- 12 zero-quantity rows imported as catalog entries. Embedded multi-location splits parsed
  into piles, math verified. `staticdata` legacy table dropped.
- **Category vocabulary was created at import**, which is where the handoff's "`Type` is free
  text, 14 values" comes from: Motor (generic — the Ninja sheet had no ID/OD distinction),
  VFD, Control, Valve, Sensor, Filter Drier, Hardware, and **"Plant"** — a temporary bucket
  for 10 unidentified Rheem part numbers, meant for later recategorization.
- **Prefix collision: Control and Coil both generate `CCL-`**, sharing a sequence.
  App-faithful, deliberate.
- **"2-6" is an Excel artifact of "20-6"** — date auto-format mangling. Both encode to stored
  code `26`. Cells 10-1 / 10-5 / 10-6 / 10-8 are the same naming family. The handoff still
  lists the Major as `2-6`.
- Project codes: Samurai **7166**, Ninja **7165**.

**Roster provenance (Jul 13):** 29 users = 20 pre-existing + 9 added at go-live — Luis Zapata,
Karthig Kathirvel, Derek Brausell, David Gudapati, Preston Davis, Javier Lucio, Swapnil
Khaire, Andrew Blevins (all Level 4), Hunter Little (Level 2). Also fixed Huunt → **Hunnt**
Hickman. Kason is the sole Level 5.

## People and the decision path

The handoff names only Kason. Two tracks, and the second one is above the first.

**Lab / operational**
- **Kevin Ray** — Sr. Manager, Engineering R&D. Business owner and sponsor.
- **Conner** (Samurai) and **James** (Ninja) — supervisors. **Kevin** — null team, sees all.
- **Gavin** — Standard-level runner, handles pickups.
- **Codey** — flagged the company is moving to **Azure SQL**, not VM-hosted SQL Server.

**IT / organizational — absent from every other source**
- **Garrison Musgraves (IT)** — leading a formal **IT intake process** as project manager:
  architecture, functionality, business purpose, long-term ownership documentation. A meeting
  with Kason was scheduled for early August.
- **Karthig Kathirvel** (Directing Engineer) — sent written support citing lab visibility and
  potential NA-wide standardization. Reports to **Derek Brausell** (Sr. Director, R&D
  Engineering, Ft. Smith), also supportive. **Both sit above Kevin Ray**, and both are in the
  Users table.
- **Server Hosting Review** — Chris Tovar, Aaron Roberts, Andy, Byron, Ashley (Cybersecurity).
- **Unresolved as of 2026-08-04:** how the IT intake relates to the Server Hosting Review.
  It may absorb, precede, or reframe it. **This is a question for Garrison at the intake
  meeting, not something to infer** — it changes what gets prepared. If intake absorbs the
  review, the SQLite and no-password answers get made once, to IT. If they're separate
  gates, the same hard questions get answered twice to audiences with different concerns,
  and the Copilot primer's scripted lines were written for the review, not for a project
  manager doing ownership documentation.

## Hosting and security

- **[decided] Phased, not simultaneous.** Initial deployment stays on SQLite; engine swap is
  a separate later move. Kason's reasoning: the SQLite file reads more easily than an Excel
  export, so data access isn't a concern at this stage.
- **Target is Azure SQL**, per Codey — changes the connection string, not the EF Core
  provider. SQL Server Express is a local sandbox only; that config doesn't travel.
- **SSO via Microsoft Entra ID / `Microsoft.Identity.Web`.** `First.Last` maps straight onto
  `firstname.lastname@rheem.com`. Users roster and access levels stay as authorization; Entra
  handles authentication only. This is the answer to the review's hardest question.
- **The two hardest questions**: session identity with no password layer, and SQLite for
  multi-user use. Both still true at Pass 9.
- **The SQLite objection is about concurrency and durability, not readability.** Expect
  single-writer locking, the file needing local disk rather than a share, and "no backup
  story — `inventory.db` is hand-copied" to be the line pulled out of the handoff. At 29
  users SQLite is probably fine in practice; *fine in practice* and *answerable to a review*
  are different standards, and the backup gap has no defence.
- **The Copilot primer contains scripted honest lines** for both hard security questions plus
  a deliberate do-not-commit list (no dates, no scope, defer Azure-vs-on-prem to IT standard).
  **The plaintext superuser passcode postdates those scripts — the honest lines don't cover
  it.**

### Repo hygiene — one item, two halves, closure unconfirmed
- The **original repo had the full real `inventory.db` plus sidecars committed** — real stock,
  orders, and the employee roster. Remedy: a new clean repo **`Visual_Inventory-0726`** with
  fresh `.git` and extended ignores (`*.db`, `*.db-shm`, `*.db-wal`, `*.sqbpro`); the old repo
  to be verified Private and then deleted. **Status as of 2026-08-04: private, still up.**
  Private is fewer eyes, not no eyes, and history keeps everything regardless of later
  commits. Deleting is the actual close — confirm the new repo carries everything first,
  since deletion is one-way.
- The **superuser passcode is plaintext in `appsettings.json`**, tracked in git. History keeps
  the old value even after a change, so **rotating matters more than removing the line, and
  deleting the repo doesn't unring it for anyone who already cloned.**
- These are the same problem and Cybersecurity is in the room. Treat as one line item.

### Deployment lineage
- First-publish "no Users table" = the published app opening a fresh db in its working
  directory. Resolved with an absolute connection string.
- **The database was deliberately moved out of the publish folder** to
  `C:\VIS_Inventory\inventory.db` (Jul 13 era) so republishing can never touch data. Handoff
  confirms app at `C:\VIS_Publish`, data at `C:\VIS_Inventory` — separation survived.
- Served via `$env:ASPNETCORE_URLS = "http://0.0.0.0:5000"`. A migration of hosting to a
  second laptop (VS install, repo clone, live-db copy, firewall rule, new IP for teammates'
  bookmarks) was specced late in the Cowork thread; execution unconfirmed.
- `.sqbpro` files are DB Browser session sidecars — created only by that GUI, never by the app
  or EF Core. Safe to delete, zero signal.

---

## Working method

### Kason's own conventions

- **"Spiterate before building."** His term, used verbatim twice. On anything that's a
  genuinely new mechanic — not a routine addition to an existing pattern — stop and write the
  *complete* scope back in prose (every field, every access-level gate, every edge case) and
  wait for explicit confirmation before opening a file. Stated reason: prevents piecing out
  bad context and shipping "functional garbage." Distinct from the general "ask before schema
  changes" rule. **Recognize the word if he uses it.**
- **"Parked"** means explicitly deferred and does not creep back in. His deferrals are real
  and don't need re-proposing.
- Follow-on features take **2a/2b suffixes**. Builds stop deliberately to preserve context.
- **"I'll leave it" / "your call"** means surface options, don't decide.
- **Physical analogies.** Filing cabinets, rooms and doors, keycards, eggs in a carton. The
  handoff's own "master key not stored in any lockbox it opens" is this working.
- **Verify in both light and dark themes.** There's a toggle and the app remembers each
  user's last choice across logins, so a light-mode contrast bug ships invisible to whoever's
  sitting in dark.
- **Structured multiple-choice beats open prose** for clarifying-question rounds — four Pass 4
  open questions resolved in one round that way.

### How he operates

- **He tests every pass before asking for the next.** A request for the next pass *is* the
  confirmation the last one landed; he won't say "that worked," he'll say "okay now pass 7."
  Assuming silence means untested wasted a round.
- **When he says something works, believe him and re-diagnose.** Told dev and host were
  "equally stuck," he came back with *"I don't know if they are equally stuck, they worked
  fine for me which is strange"* — and the correction was right. Don't restate the theory
  louder.
- **He supplies real artifacts, not descriptions.** Actual `.db` files, CSVs, console output,
  screenshots. The migration mystery that survived three passes cracked the moment he pasted
  the `PendingModelChangesWarning` text. Ask for the real thing.
- **He decides fast and doesn't re-litigate.** "Confirm." "Go on." "Boom." Going back to check
  is friction, not diligence.
- **He reshapes proposals rather than accepting them.** The map editor, the location tree, and
  "request first so locations can't be invented" were all his and all better than what was
  offered. When he redirects a design, the redirect is usually the insight.
- **He catches his own scope creep** and means it.

### What works from the other side

- **Verify against his real data, don't assert.** Load the actual database and run it —
  the pixel round-trip on map zones, the migration chain replay, `LoanableQuantity` across
  every type/qty combination. Simulating the *old broken* behaviour to prove a diagnosis, not
  just the fix, is what turns "I think this is why" into "here it is failing on your data."
- **Own mistakes flatly and immediately.** Six were introduced in one session — a dead-helper
  edit that changed nothing, a missing `using`, a form-sync bug, a null-deref in a view, the
  `@RenderBody()` comment. Each named in one sentence and fixed. No hedging, no apology
  spiral. He was never put out by a mistake; he wanted it identified and gone.
- **Refuse to guess when it matters.** GMCC as a brand, the `Mezz A3` names behind `MZA3`, the
  lab number format — all asked rather than assumed. He answered every time.
- **Flag what he didn't ask about**, with evidence. The passcode in git, the eight hardcoded
  location copies, Rack/Row missing from the breadcrumb, the false `IsControlType` comment. He
  acted on most.
- **Tell him when his instinct contradicts his own reasoning.** On the compressor count he
  leaned conservative; his own "the two halves should be even" test pointed the other way, and
  saying so got *"i like b thats creative kinda."* He'd rather be argued with using his own
  logic than agreed with.
- **Register:** direct, low ceremony, no preamble. Findings before conclusions. He matches
  energy — "kachow," "boom," "ngl" — fine to meet that without getting unserious about the
  work. Length tracks stakes: a schema change earns full reasoning, a one-line fix earns one
  line. He reads the code and will notice what got skipped.

---

## Open items — consolidated

**Confirmed defects**
1. **Short-pull logs ordered quantity, not pulled.** `QuantityChange = -it.Quantity` should be
   `-pulledQty`, which is already computed two lines up and already used for
   `LoanOutstanding`. `ExportToCsv` sums `QuantityChange`, so short pulls overstate in
   exports. Fix regardless of the below — it's the fallback if one ever gets through.

   **[decided] A short pull is not a fulfillment variation, it's a data-integrity event.**
   The correct sequence is: **cancel the order → adjust stock to the true count → re-order
   the real quantity → pick up clean.** No partial-completion state, no remainder-owed
   ledger, no order that closes short. Rationale in Kason's words: competence and aptitude
   are meant to maintain the system, not make it idiot-proof, and pickup logs already carry
   enough to reconstruct what happened. The rejected alternatives were complete-with-record
   and leave-open-with-remainder; both accept a wrong stock count and only argue about how
   to log it.

   **Consequence — [decided], backlog priority 1, targeted at the next rollout pass.**
   `PickUpOrder` currently sets `order.Status = "Completed"` unconditionally, so today the
   wrong path is the one-click path: a runner who just hits pickup gets a silently-completed
   order and a bad log without ever being told stock was short. The gate to build:

   ```
   if (orderedQty > availableQty)
       refuse the pickup
       prompt: how many are actually on the shelf for Order <n>?
   ```

   The picker cannot take more than is physically there, and the discrepancy gets surfaced
   at the moment it's discovered rather than absorbed.

   **Watch this distinction when building it — the same screen can mean two opposite
   things.** The number the picker types must be *a count of the shelf*, not *a quantity to
   fulfill*. If it becomes a partial fulfillment the order closes short, which is exactly
   the behaviour rejected above. Correct wiring: the entered number is a **stock
   adjustment**, the order is **cancelled**, and a true order is re-issued against the
   corrected count. Same UI, opposite semantics, and the tell is whether an order can ever
   close for less than it asked for. It can't.

   **Permissions — [decided] pending manager sign-off:** widen Gavin's rights so the picker
   can complete the correction he discovered, rather than the sequence spanning three
   people. The picker is the one actually looking at the shelf, so he's the best-informed
   person to state the count; the safeguard is that adjustments are logged and attributed,
   which pickup logging already covers. **One asymmetry worth a rule:** adjusting *down* is
   the picker confirming what he can see. Adjusting *up* — finding more than recorded —
   usually means stock is filed in the wrong place, not that the count was low, and it
   shouldn't be silently absorbed by the same path.
2. **27 skipped negative-quantity Ninja rows** — data debt from go-live, never re-added.
3. **`AlertThreshold` is 0 on all 244 compressors** — 825 units, no low-stock warning ever.
   `SetDefaultThreshold(team, threshold)` exists to bulk-fix. (The boot-time `UPDATE` test SQL
   that used to set it was removed Jul 9.)

**Still present, cosmetic**
4. `.map-stats-bar` / `.map-live-bar` are `position: absolute`; wanted in flex-column flow.

**Unverifiable from code**
5. **TC counts aren't reserved** across concurrent pending orders. No reservation logic
   exists, so the behaviour is plausible; confirming needs two concurrent orders against the
   same TC stock.
6. **Role-aware gating** from `roles_and_modal.zip` — symptoms were traced to a stale build
   but all three roles were never re-confirmed. Needs a manual pass per role.
7. Disposed-transaction rollback in a `catch` in `ReturnLoan`/`ScrapLoan`. Practically
   unreachable. Lowest priority.

**Closed on inspection — do not re-investigate**
8. Fresh-database Viewer lockout — **fixed Jul 9** in the Cowork thread; `Program.cs` seeds
   four users with explicit levels, Kason as Admin. The model default *is* Viewer, which is
   what the original report saw, but the seeder overrides it.
9. Leftover test scaffolding in `Program.cs` — none present.
10. `syncHeaderOffset()` not executing — wired correctly (defined 2711, bound to load/resize,
    invoked 3010). If `--vis-header-bottom` reads empty, look at stale build or the measured
    element not existing at call time.
11. `.holo-viewer` collapsing — both now carry explicit widths.
12. `<select>` imbalance in `Index.cshtml` — false positive, a `<select>` inside a JS comment.
    Pre-existing, harmless, don't chase it.
