# Behavior.md (VIS-local)

Stack-specific supplement to the global `Behavior.md` (`~/.claude/Behavior.md`),
which covers the gates that don't change between projects (spiterate, voice
discipline, ask-before-commit). This file only holds what's specific to VIS.

## Session mechanics

- `dotnet build` after every real chunk of work, not just at the end.
- "Live" (per the global file's verify-live rule) means: the real ASP.NET
  Core app running, against the real copied-in `inventory.db`, not a
  fixture — clicking through it, reading the DOM, checking the server's
  live SQL log.
