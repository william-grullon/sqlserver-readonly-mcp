# Handoff prompt: Phase 1 (paste into a new Sonnet session in this folder)

```
Read CLAUDE.md, BRIEF.md, docs/DESIGN.md, and docs/PROGRESS.md. The design in
docs/DESIGN.md is approved; build it exactly. Ask before deviating.

Build Phase 1 only (BRIEF.md §7): the database.

Deliverables
1. docker-compose.yml: service `sqlserver` (mcr.microsoft.com/mssql/server:2022-latest,
   Developer edition, port from MSSQL_PORT, healthcheck using sqlcmd) and one-shot
   service `db-init` that waits for service_healthy and runs
   /opt/mssql-tools18/bin/sqlcmd -C -i /db/setup.sql -v McpReaderPassword=...
   All secrets from .env via compose variable substitution.
2. db/setup.sql using :r includes of 01..05 as listed in DESIGN.md §3.
   Scripts must be re-runnable and 2012-safe (see DESIGN.md §5).
3. Deterministic seed per DESIGN.md §4 (volumes, date range 2024-11-01..2026-10-31,
   CHECKSUM-based pseudo-randomness, deliberate shapes for the five questions).
4. reporting views and mcp_reader exactly as DESIGN.md §4.
5. HarborSupply.sln + tests/HarborSupply.Db.Tests (xUnit, net8.0,
   Microsoft.Data.SqlClient). Tests read HARBOR_MCP_CONNECTION (mcp_reader) and an
   admin connection from env. Cover at minimum:
   - compatibility_level = 110
   - row counts in the expected ranges; seed is identical on a second run
     (e.g. checksum of invoice totals is a fixed constant)
   - every reporting view returns rows as mcp_reader
   - SELECT on any dbo table fails as mcp_reader
   - INSERT/UPDATE/DELETE on dbo and on a reporting view fail as mcp_reader
   - sensitive columns (TaxId, CreditLimit, UnitCost, RemitBankAccount) appear in no
     reporting view (query sys.columns)

Done when: `docker compose up -d` from a clean volume completes db-init with exit 0,
and `dotnet test` passes. Show me the commands you ran and their output.
Then update docs/PROGRESS.md and stop. Do not start Phase 2.
```

## Before starting

- Install **.NET 8 SDK** and **Docker Desktop** (WSL2 backend). Neither is on this machine yet.
- `copy .env.example .env` and set strong passwords.
- The repo lives at `C:\Users\ejayw\dev\sqlserver-readonly-mcp` (moved out of OneDrive so sync does not touch `.git`, `bin/` and `obj/`). Open the Sonnet session in that folder.
