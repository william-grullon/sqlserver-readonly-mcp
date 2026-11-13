# CLAUDE.md

Read-only MCP server (C#/.NET 8) over a synthetic SQL Server database. Public portfolio repo.

- **Spec:** `BRIEF.md` is the source of truth. **Design:** `docs/DESIGN.md` (SDK version, folder layout, schema, views, security). Do not change the design without asking the owner.
- **Status:** `docs/PROGRESS.md` — update it at the end of every session (phase, what passed, what is next).
- One phase per session. Stop when the phase's acceptance criteria in BRIEF.md §7 pass and show how you verified them.

## Hard rules

1. T-SQL must be valid on SQL Server 2012 (database compatibility level 110). Forbidden list is in `docs/DESIGN.md` §5.
2. No secrets in the repo. Connection strings and passwords come from environment / `.env` only. `.env` is gitignored; keep `.env.example` current.
3. All data is synthetic (Harbor Supply Co.). No real company, employer, client, or AccountMate names or schemas anywhere.
4. Seed data is deterministic: no `NEWID()`, `RAND()`, or `GETDATE()` in seed or report logic. "Today" is `reporting.vReportingCalendar.AsOfDate` (2026-10-31).
5. Write tests alongside each phase, not at the end.
6. MCP server: stdout is protocol only. All logs to stderr.
7. Never weaken a permission or guard to make a test pass. A failing denial test is a real bug.

## Commands (fill in as they become real)

```
docker compose up -d          # SQL Server + one-shot db-init
dotnet test                   # all tests
```
