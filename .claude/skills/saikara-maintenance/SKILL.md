---
name: saikara-maintenance
description: Periodic upkeep of the Saikara autonomous-dev environment — sync roadmap/issues, refresh memory, update CLAUDE.md, and keep skills/agents from drifting. Run at phase boundaries or every few sessions.
---

# Saikara maintenance pass

Run at phase boundaries or every few work sessions, and whenever the structure changes.

- **Roadmap & issues:** tick completed items in `docs/ROADMAP.md`; make open GitHub issues
  reflect the real remaining work; close finished ones.
- **CLAUDE.md:** update the "Status" line; fix anything that no longer matches the code
  (paths, commands, conventions, package choices).
- **Memory:** update the harness memory files (especially the `saikara-project` status) and
  `MEMORY.md`; delete anything now false.
- **Skills & agents:** re-read each `.claude/skills/*/SKILL.md` and `.claude/agents/*.md`;
  fix drifted steps/paths; add a skill or agent for any newly-recurring task; remove dead ones.
- **Health:** confirm `main` is green, `dotnet test` passes locally, and there are no stale
  branches (`git branch`, `gh pr list`).
