# VIS — Front Load

Written 2026-11-11, against `f616430`. VIS is headed for a real server, so this is
everything I want thought through up front instead of discovered after it's live.

**How this page works.** Nothing on it is approved to build. Each row is a thing
that's needed and possible, with a size and what it needs from me. When I pick one, it
gets its own scope written back first, the same as any new mechanic. Sizes: **S** = an
hour or two, **M** = a pass, **L** = several passes.

The first half is what I had Claude look up and what it measured in the code. The
list itself starts at "The front-load list."

---

## What the research said

I asked for what people actually report, not vendor checklists. Sources are at the
bottom. Most are blogs and forum threads, so the percentages are loose; the patterns
repeat across all of them.

**What makes an app read as vibe-coded**

- *In the code:* comments everywhere. It's the first and most common tell in every
  write-up, and OX Security's review of 300+ repos names it as anti-pattern number
  one. The specific complaints: comments that narrate what the code does, comments
  that record the history of the change, and comments so confident they make review
  harder. After that: the same logic pasted in several places, very large files that
  never get split, and one generic try/catch around everything.
- *On the screen:* purple-to-indigo gradients, glow effects on dark backgrounds,
  emoji used as icons, a card around every block, coloured side-bars and status dots
  that mean nothing, and template leftovers nobody edited (default titles, default
  error pages, placeholder pages).
- *In use:* the happy path works and the edges don't. Empty submissions, a second
  person editing the same thing, a dropped connection. Every error says the same
  thing. Nothing can be undone.
- The honest caveat in the better write-ups: no single sign proves anything, and
  hurried hand-written code has the same gaps. What people react to is the absence of
  judgment, not the tool.

**What makes it safe on an internal network without passwords**

- "Behind the firewall" isn't a security boundary. The audits of AI-built apps find
  the same short list every time: secrets in the repo, routes that check you're
  signed in but not that the thing is yours, no security headers, no rate limits.
- No passwords doesn't have to mean no identity. On a Windows network the server can
  take the identity from the Windows or Entra sign-in the person already did. Nobody
  types anything, and nobody can sign in as someone else.
- Cookie-based apps still need anti-forgery tokens with single sign-on. HTTPS, cookie
  flags and persisted data-protection keys are the standard ASP.NET hardening list.

**What makes it easy to use (internal tools)**

- People use it under time pressure, many times a day. Fewest clicks to the common
  action, errors that say what happened and what to do, empty screens that say what
  goes there, confirmation or undo on anything destructive, and nothing typed is ever
  lost because a save failed.

**What makes it nice to work on (people and agents)**

- One documented way to build, run and test. A short README. Tests that run fast.
- For agents: a short instruction file at the root with the exact commands and the
  project's own traps, linking out to longer docs instead of containing them. The
  guides converge on "under a couple hundred lines, specific, current."

---

## Where VIS stands (measured 2026-11-11, the table again 2026-11-16)

**Already right. Nobody needs to re-audit these.**

- All 68 POST actions carry an anti-forgery token. No raw SQL anywhere; everything
  goes through EF.
- Access level, Line and Branch are re-read from the roster on every request, so a
  demotion applies on the next click.
- Sign-in's return address is checked as local. The toast and autocomplete injection
  paths (VIS-7, BC-10) are closed.
- On screen: no emoji, one icon set, one font, no gradient or glow decoration, no
  leftover `console.log`. Refusals say what happened and why. Light and dark both
  work. The responsive pass is done.

**Not right.** The 2026-11-16 column is after the comment move (B1, `80faae7`).

| What | 2026-11-11 | 2026-11-16 |
|---|---|---|
| Comment lines in the source | 4,607 of 25,950 (17.8%) | 1,537 of 22,879 (6.7%) |
| Comment blocks of 3+ lines / 5+ lines | 640 / 339 | 64 / 5 |
| Banner comments (`// =====`) | 101 | 0 |
| Comments carrying a Pass number, date or bug id | 204 | 0 |
| Largest files | `HomeController.cs` 2,504 lines, `InventoryService.cs` 2,354, `OrderService.cs` 1,879, `SearchCenter.cshtml` 1,878 | `HomeController.cs` 2,116, `InventoryService.cs` 1,868, `SearchCenter.cshtml` 1,745, `OrderService.cs` 1,509 |
| Inline `style="..."` in views / hard-coded hex colours | 310 / 126 | 310 / 122 |
| Automated tests | 0 | 0 |
| README | none | none |
| Lines an agent is told to read before starting | 4,336 (handoff 3,962 + addendum 374) | 4,386 (handoff 4,012 + addendum 374) |

The banner and dated rows first said 296 and 429. The search behind them also counted
the three old copies under `.claude/worktrees/` (D4); the repo by itself was 101 and
204. The four hex colours that went were only in comments.

---

## The front-load list

### A. Before it goes on a real server

| # | What | Why | Size | Needs from me |
|---|---|---|---|---|
| A1 | **Real identity without a password.** Take the signed-in person from Windows or Entra instead of the name picker. The roster and access levels stay exactly as they are; `First.Last` already matches the sign-in name. | Today anyone can pick anyone's name, so the audit trail and every access level are on the honour system. This is the one thing a security review will stop on. | L | Which route. Entra ID (`Microsoft.Identity.Web`) is what I already told IT; it works on phones and needs an app registration and HTTPS. Windows Authentication needs no registration but only works cleanly from domain PCs. |
| A2 | **HTTPS.** A certificate on the server, an HTTPS binding, session cookie marked Secure with SameSite set. | The host serves plain HTTP, so the session cookie and the Settings passcode cross the network readable. `UseHttpsRedirection` and `UseHsts` are already in `Program.cs` but there's no HTTPS endpoint, so they do nothing. | S | A certificate from IT. |
| A3 | **Settings passcode out of the repo.** Read it from a server environment variable (`Superuser__Passcode`), change it, delete it from `appsettings.json`. Limit wrong attempts. | It's in plain text in a tracked file and in git history, and it's the only real lock on Settings while sign-in is a name picker. Wrong guesses are unlimited. A change made only on the host is undone by the next publish. | S | When to rotate. |
| A4 | **Security headers.** `X-Content-Type-Options`, frame blocking, `Referrer-Policy` now. A Content-Security-Policy later. | None are set. The first three are one small middleware. A real CSP is blocked by inline scripts and the 310 inline styles (see B3). | S, CSP is L | Nothing for the first three. |
| A5 | **Icons served from VIS, not a CDN.** Bootstrap Icons load from `cdn.jsdelivr.net` on every page. | On a locked-down server network the icons vanish if the client can't reach the internet, and every page load calls an outside host. | S | Nothing. |
| A6 | **The flagged SQLite package.** `SQLitePCLRaw.lib.e_sqlite3` 2.1.11, CVE-2025-6965, rated high. It comes in under EF Core's SQLite package. Also move `System.Drawing.Common` off 9.x. | VIS has no raw SQL, so it's hard to reach in practice, but every scanner IT runs will flag it and the build prints it twice. No patched 2.1.x exists; the fix is whichever SQLitePCLRaw build bundles SQLite 3.50.2 or later, tried on a copy first. | S to M | Nothing. |
| A7 | **Data-protection keys kept on disk.** A keys folder beside the database. | Sessions live in memory, so a restart signs everyone out either way. If the keys aren't kept too (which depends on how the service account runs), a restart also breaks every form that was open, because its anti-forgery token no longer checks out. | S | Nothing. |
| A8 | **Backups that run themselves.** A nightly SQLite backup and a copy of the photo folder to a second place, with a number of days kept. | The database is hand-copied. It's the one question about SQLite I have no answer for in a review. | S to M | Where the copies go (IT). |
| A9 | **Two people at once.** BC-12 (two stock changes overwrite each other) and BC-11 (two registrations get the same ItemId). | Both are P1 in `Bug_Checks.md` and both get more likely with every user added. BC-11 needs a unique index, so a migration. | M each | Go-ahead. BC-11 gets a full scope first. |
| A10 | **A real error page and a not-found page.** | The error page is the untouched template: it tells users to switch the server to Development mode. An unknown address gets no VIS page at all, only the browser's own bare 404. | S | A look at the wording. |
| A11 | **Logs that survive.** Replace the `Console.WriteLine(">>> ...")` lines with the built-in logger and write to a file or the Windows event log. Record sign-ins. | On a server nobody is watching a console. A failed migration at startup currently prints one line and the app carries on. Sign-ins aren't recorded anywhere. | S to M | Nothing. |
| A12 | **Small hardening.** `AllowedHosts` set to the server's name. `SmartRedirect` checks the Referer's host before redirecting to it. A pixel-size limit before a delivery photo is decoded. | Each is a line or two. They were on the 10/22 review list I said "not now" to. | S | Nothing. |

### B. What makes the code read as vibe-coded

| # | What | Why | Size | Needs from me |
|---|---|---|---|---|
| B1 | **Long comments out of the code and into `docs/Code_Notes.md`.** | The numbers above. In the views it was worse than a look: HTML, script and CSS comments were sent to the browser, so anyone could read the reasoning with View Source. Since 2026-11-15 every view comment is a Razor `@* *@`, which the server strips. Only `site.css` (73 comment lines) and `site.js` (24) still ship theirs, because they're served as plain files. | L | **Nothing.** Done, committed as `80faae7` and pushed on 2026-11-16. I picked the voice on 2026-11-11 (terse, label style) and said go the same day. All 74 files are done: 52 C#, `site.js`, `site.css` and 20 views. A script confirms every file is the same code once comments are stripped, the C# compiles to a byte-identical DLL, the views were clicked through on a scratch copy of the dev db on 2026-11-12, and the build is clean. `Code_Notes.md` is sorted by folder. |
| B2 | **Template leftovers.** `Privacy.cshtml` ("Use this page to detail your site's privacy policy"), the default error page (A10), the `http` launch profile that actually runs HTTPS, `smtp.dummyrheem.com`, real names and emails seeded in `Program.cs`. | Each one is the "Vite + React" tab title of an ASP.NET app. | S | Delete Privacy? Keep the seed names? |
| B3 | **Styles into the stylesheet.** 310 inline `style` attributes, 12 `<style>` blocks and 122 hard-coded colours in the views. | Every hard-coded colour is a light-theme bug waiting, and it's what blocks a CSP. | M to L | Go-ahead. Screen by screen, checked in both themes. |
| B4 | **Split the four big files.** `HomeController` by area (search, orders, pickup, intake, deliveries, sign-in), the two big services by job, the Search Center script into its own file. Addresses stay the same. | 2,000-line files are the "monolith" tell, and they're the slowest thing for a person or an agent to work in. | L | Go-ahead, and not before there are tests (B5). |
| B5 | **A test project.** Start with the `Bug_Checks.md` reproductions and the reservation rules as tests against a throwaway SQLite file. | There are no tests. Every check so far was done by hand in a browser or in a harness that got thrown away. Tests are what make B4 and A9 safe to do. | M to start | Go-ahead. |
| B6 | **Repo root.** Delete the three `.sqbpro` files and `structure.txt` (a tree listing from when the project was called InventoryDevTwo). Add a README. | First thing anyone sees. | S | OK to delete. |

### C. Using it

| # | What | Why | Size | Needs from me |
|---|---|---|---|---|
| C1 | **Old pages acting on new data.** BC-03 (a deleted stack silently becomes the primary one), BC-04 (an old Unit Tracker form restores old notes), BC-05 and BC-06 (roster saves). | These are the ones people will actually feel: the wrong shelf changes, or someone's notes disappear, with no message. | M | **Nothing.** BC-03, BC-05 and BC-06 fixed 2026-11-16, and BC-04 the same night at my call: a stale save is refused ("changed since you opened it"), and the open form shows who else has the unit open, checked every 45 seconds with no reload. Committed and pushed as `0f949b3`. A refused save still loses what was typed; that's C2. |
| C2 | **Never lose what was typed.** A failed save comes back with the form still filled in. | BC-06 loses a whole roster entry on one duplicate serial. A 30-minute idle timeout sends the next click to sign-in. | M | Go-ahead. Mostly solved by A1 for the timeout. |
| C3 | **One way to confirm.** The two Release buttons on reservations use the browser's own confirm box; everything else uses a VIS dialog. | Mine, from the reservations build. Small, but it's the kind of inconsistency the research calls out. | S | Nothing. |
| C4 | **A click-through I haven't done.** Every table when it's empty, every button that takes more than a second, the whole app by keyboard only, and Reservations on a real phone. | Not checked, so not claimed either way. | S to check | Nothing. |
| C5 | **The reservation colour.** Holds are violet. It's one colour with one meaning, and red, amber, green and blue were taken. | Purple is the colour people associate with AI-made screens. Listed so it's a choice and not a default. | S | Keep or change. |

### D. Working on it

| # | What | Why | Size | Needs from me |
|---|---|---|---|---|
| D1 | **README.** What VIS is, how to build and run it, that it needs a database copied to `C:\VIS_Inventory\inventory.dev.db` first, where the docs are. | There isn't one. | S | Nothing. |
| D2 | **`.editorconfig`.** | Files are a mix of CRLF and LF, with and without a byte-order mark. Every tool that touches a file risks rewriting all of it. | S | Nothing. |
| D3 | **A publish script.** One command that builds the dated release folder the way I do it by hand. | Fewer steps to get wrong on release day. | S to M | Whether I want it. |
| D4 | **Old worktrees.** Three under `.claude/worktrees/` at old commits. | Every search returns four copies of every file. None of the three has uncommitted work (checked 11-11). | S | OK to remove. |

### E. Agents working on it

| # | What | Why | Size | Needs from me |
|---|---|---|---|---|
| E1 | **Split the handoff.** A current-state file (open items, deployed state, conventions, load-bearing facts, traps) short enough to read every session, and the Pass log and history in a second file read on demand. | `CLAUDE.md` tells every session to read 4,386 lines before doing anything. That's most of a session's attention spent before the first question, and the open-items heading still says 9/24. | M | Go-ahead. Nothing is deleted, only moved. |
| E2 | **Commands in `CLAUDE.md`.** Build, run on the dev db, run on a scratch copy of it, add a migration. | Each session works these out again. The scratch-copy run is the one that protects the dev db. | S | Nothing. |
| E3 | **`AGENTS.md` at the root** that points at `CLAUDE.md`. | Copilot and the other tools read that name, and I've already used Copilot on this repo. | S | Nothing. |
| E4 | **Reasoning by file.** `docs/Code_Notes.md` (B1). | An agent looks up why a file is the way it is by its name, instead of reading history in comments. | with B1 | With B1. |

---

## The order I'd do it in

1. **B1**, because it's started and it touches every file. Doing it first means every
   later change lands in clean files. Done, `80faae7`.
2. **A3, A4 (first three headers), A5, A7, A10, A12, B2, B6, C3, D1, D2, E2, E3.** All
   small, none change how the app behaves for a user. One pass.
3. **B5**, the test project, before anything that moves code.
4. **A9 and C1**, the open bugs that get worse with more users.
5. **A2 and A1**, once IT has given me a server name, a certificate and the Entra
   answer. A8 and A11 go with the server.
6. **E1**, then **B3** and **B4** when there's time. They're the biggest and the least
   urgent.

---

## Already ruled. Not reopened here.

- The first deployment stays on SQLite. Azure SQL is a separate, later move.
- The name picker stays until single sign-on replaces it. It isn't being patched in
  between.
- `Program.cs` catching a failed migration at startup and carrying on: I declined
  hardening it. A11 only makes the failure visible.
- BC-09, a brand-new empty database can't run the migration chain: I copy a database
  over instead.
- No rollback step in a release.
- Email stays switched off. If it's ever turned on, its content needs encoding and
  the connection needs TLS first.

---

## Sources

- [7 Signs a UI Has Been Vibe Coded](https://www.thefountaininstitute.com/blog/signs-vibe-coded-ui) — Fountain Institute
- [How to tell if an app was vibe coded](https://axonbuild.com/blog/how-to-tell-if-an-app-was-vibe-coded/) — Axon
- [I analyzed 100 vibe-coded websites](https://dev.to/kaplich/i-analyzed-100-vibe-coded-websites-and-found-these-common-mistakes-5275) — DEV Community
- [I reviewed 6 vibe-coded codebases](https://dev.to/jsgurujobs/i-reviewed-6-vibe-coded-codebases-here-is-what-i-found-m0o) — DEV Community
- [The AI code maintainability debate on Hacker News](https://www.developersdigest.tech/blog/ai-code-human-maintainability-hn-debate) — Developers Digest
- [Vibe coding's real problem isn't bugs, it's judgment](https://www.securityweek.com/vibe-codings-real-problem-isnt-bugs-its-judgment/) — SecurityWeek
- [Army of Juniors: the AI code security crisis](https://www.ox.security/army-of-juniors) — OX Security
- [AI writes code like a junior dev](https://www.helpnetsecurity.com/2025/10/27/ai-code-security-risks-report/) — Help Net Security
- [Vibe-coded apps riddled with exploitable flaws](https://www.securityweek.com/vibe-coded-apps-riddled-with-exploitable-security-flaws/amp/) — SecurityWeek
- [AI-built apps security scan data 2026](https://hyrax.dev/blog/ai-built-apps-security-scan-data-2026) — Hyrax
- [Configure Windows Authentication in ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/windowsauth) — Microsoft Learn
- [Security headers in ASP.NET Core](https://meziantou.net/security-headers-in-asp-net-core.htm) — Meziantou
- [Harden ASP.NET on IIS: headers, cookies, secrets](https://aspnix.com/posts/harden-aspnet-on-iis-headers-cookies-secrets) — ASPnix
- [CVE-2025-6965 advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q) — GitHub
- [Internal tools UX](https://www.dronahq.com/internal-tools-ux/) — DronaHQ
- [UX writing for business applications](https://engineering.cloudflight.io/ux-writing-for-business-applications) — Cloudflight
- [How to write a good AGENTS.md](https://docs.plannotator.ai/learn/ai-development/how-to-write-a-good-agents-md) — Plannotator
- [The agent-native repo](https://www.harness.io/blog/the-agent-native-repo-why-agents-md-is-the-new-standard) — Harness
- [Five build commands to make onboarding faster](https://spin.atomicobject.com/faster-onboarding/) — Atomic Object
