# CLAUDE.md

Instructions for Claude Code working in this repo.

## Read first, every session

`~/.claude/CLAUDE.md` (Mind/Behavior/Soul, global — auto-loads regardless
of project) covers Kason as a person. This file's `@Behavior.md` is the
VIS-specific supplement to the global one (stack mechanics only).

@Behavior.md

Before doing anything else, also read both of these in full:
- `docs/VIS_Handoff_State.md` — architecture, current deployed state, load-bearing
  facts, known traps, backlog. Authoritative for code/schema.
- `docs/vis-addendum-consolidated.md` — stakeholders, hosting/security decisions,
  working method, everything that doesn't live in the code itself.

For commit-level detail (what changed, when, which files), see
`docs/Commit_History.md`.

These three `docs/` files are kept current at the end of every working session. If
something in the live code or git history contradicts them, trust what's actually
there and flag the discrepancy — don't silently assume the docs are right.

## Project

Visual Inventory System (VIS) — ASP.NET Core MVC (`net10.0`) inventory app built for
Rheem's R&D lab (Samurai/Ninja/etc. teams). EF Core + SQLite, Razor, Bootstrap 5 dark
theme. Session-based name-only "identify" (no password) plus a numeric `AccessLevel`.
