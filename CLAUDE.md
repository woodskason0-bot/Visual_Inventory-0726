# CLAUDE.md

Instructions for Claude Code working in this repo.

## Read first, every session

Before doing anything else, read both of these in full:
- `docs/VIS_Handoff_State.md` — architecture, current deployed state, load-bearing
  facts, known traps, backlog. Authoritative for code/schema.
- `docs/vis-addendum-consolidated.md` — stakeholders, hosting/security decisions,
  working method, everything that doesn't live in the code itself.

For commit-level detail (what changed, when, which files), see
`docs/Commit_History.md`.

These three files are kept current at the end of every working session. If something
in the live code or git history contradicts them, trust what's actually there and
flag the discrepancy — don't silently assume the docs are right.

## Project

Visual Inventory System (VIS) — ASP.NET Core MVC (`net10.0`) inventory app built for
Rheem's R&D lab (Samurai/Ninja/etc. teams). EF Core + SQLite, Razor, Bootstrap 5 dark
theme. Session-based name-only "identify" (no password) plus a numeric `AccessLevel`.

## Working conventions

- Read a file in full before editing it. No blind edits.
- `dotnet build` after every real chunk of work, not just at the end.
- Test live against the real app and real db when possible — a clean build is not the
  same as a verified feature.
- Ask before `git commit`/`push`. Never do either without being told to.
- For a genuinely new mechanic — not a routine addition to an existing pattern — write
  back the complete scope in prose (every field, every gate, every edge case) and wait
  for explicit confirmation before opening a file. Kason's term for this is
  "spiterate before building" — recognize it if he uses it.
- Tone: direct, low-formatting, no unneeded explanation.
