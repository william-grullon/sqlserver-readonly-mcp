# Progress

| Phase | Status | Notes |
|---|---|---|
| 0 Kickoff | Done 2026-11-13 | SDK chosen (ModelContextProtocol 2.4.0), design drafted in `docs/DESIGN.md`, repo scaffolded |
| 1 Database | **Done 2026-11-13** | `docker compose up -d` from a clean volume: db-init exits 0; `dotnet test` 61/61 pass. See log below |
| 2 MCP server | Done 2026-11-13 | Four tools verified through Claude Code against container; 67 tests pass. See PHASE2.md. |
| 3 Permission proof | Not started | DB-side denial tests already exist in `tests/HarborSupply.Db.Tests` (Phase 3 adds the server-guard tests) |
| 4 Reconciliation and docs | Not started | |
| 5 Stretch: Data API Builder | Not started | |

## Session log

- **2026-11-13 (kickoff):** Design pending owner approval. Machine check: .NET runtime present but **no .NET SDK**; **Docker not installed**; `gh` CLI not installed.
- **2026-11-13 (Phase 1):** Built the database.
  - Delivered: `docker-compose.yml` (`sqlserver` + one-shot `db-init`), `db/setup.sql` + `01..05`, `HarborSupply.sln`, `tests/HarborSupply.Db.Tests`.
  - Verified: clean volume -> db-init exit 0 -> `dotnet test` 61 passed, 0 failed. db-init run a second time on the same volume exits 0 and leaves the data byte-identical (fixed fingerprint in `SeedTests.cs`). Mutation check: granting `SELECT` on `dbo` to `mcp_reader` turns the 9 dbo-select tests red; restoring via db-init turns them green.
  - Seed result: Customer 150, Vendor 40, Item 300, Invoice 6,075 (67 void), InvoiceLine 24,305, CustomerPayment 5,113, PaymentApplication 5,355, Bill 1,200, BillPayment 1,077. 312 invoices >60 days past due; open vendor bills in all five aging buckets.
  - Machine: Docker Desktop and .NET 8 SDK now installed. Tests read `HARBOR_MCP_CONNECTION` and `HARBOR_ADMIN_CONNECTION` from the environment, falling back to the repo-root `.env`.

### Deviations from DESIGN.md (owner to confirm or reject)

1. **Seed hash is MD5, not `CHECKSUM`.** DESIGN §4 specified `ABS(CHECKSUM(n, salt)) % k`. First build showed it does not work: with small integer inputs different salts correlate, so 0 void invoices, customers 3/5/7/9 and items 3/14/22/31 got no rows, and the deliberate shapes vanished. Replaced by `dbo.fnSeedHash(a,b,c)` = first 4 bytes of `HASHBYTES('MD5', ...)`, deterministic and 2012-safe. The function is created and dropped inside `03-seed.sql`; nothing is left in the database. The "no `NEWID()`/`RAND()`/`GETDATE()`" rule still holds.
2. **`[LineNo]` must be bracketed.** `LINENO` is a reserved word in T-SQL. Column name kept as in the design; every query (including Claude's) must write `[LineNo]`. Phase 2 tool descriptions should say so.
3. **Extra DENY on `reporting`.** `05-security.sql` adds `DENY INSERT, UPDATE, DELETE, EXECUTE, ALTER ON SCHEMA::reporting` so even the simple, updatable views can never be written through. Stricter than DESIGN §4, never looser.
4. **Question 4 uses last full quarter.** DESIGN §4 said "Q3-to-Q4 2026 drop", but data ends 2026-10-31 so Q4 is one month old. Shape is built for **Q2 2026 -> Q3 2026** (items 3, 9, 14, 22, 31). Phase 4's reference report should compare those two quarters.
5. **`.env.example` gained `HARBOR_ADMIN_CONNECTION`** (sa, tests only; never used by the MCP server).

### Notes for later phases

- On Windows Git Bash, `docker exec ... /opt/...` paths get rewritten; use `MSYS_NO_PATHCONV=1`.
- The local `.env` had a reader password in `HARBOR_MCP_CONNECTION` that did not match `MCP_READER_PASSWORD`; it was synced (gitignored file, values not shown). Keep the two equal when changing either.
- Expected-answer anchors for Phase 4 are the fingerprint constants in `tests/HarborSupply.Db.Tests/SeedTests.cs` (sales invoice total 23,669,600.17; AP bill total 15,633,431.91).
- Next: Phase 3 (permission proof).

