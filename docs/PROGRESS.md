# Progress

| Phase | Status | Notes |
|---|---|---|
| 0 Kickoff | Done 2026-11-13 | SDK chosen (ModelContextProtocol 2.4.0), design drafted in `docs/DESIGN.md`, repo scaffolded |
| 1 Database | **Done 2026-11-13** | `docker compose up -d` from a clean volume: db-init exits 0; `dotnet test` 61/61 pass. See log below |
| 2 MCP server | Done 2026-11-13 | Four tools verified through Claude Code against container; 67 tests pass. See PHASE2.md. |
| 3 Permission proof | **Done 2026-11-13** | 109/109 tests pass covering DB-side denials (dbo SELECT/writes, view writes, DDL, roles), excluded sensitive columns, server AST guard rejections (DML/DDL, multi-statement/batch, EXEC, INTO, TVFs/OPENROWSET, hints, FOR XML, scalar UDFs, post-2012 syntax), row cap truncation, command timeout, and JSONL audit log. |
| 4 Reconciliation and docs | **Done 2026-11-13 (demo clip pending)** | 5 reference reports + independent base-table checks, real Claude transcript, README complete; 120/120 tests pass. Demo clip and CI not done. |
| 5 Stretch: Data API Builder | **Done 2026-11-13** | DAB 2.1.7 container (compose profile `dab`) serves the 8 views over its MCP endpoint; denial checks and 3 questions verified through it. |

## Session log

- **2026-11-13 (kickoff):** Design pending owner approval. Machine check: .NET runtime present but **no .NET SDK**; **Docker not installed**; `gh` CLI not installed.
- **2026-11-13 (Phase 1):** Built the database.
  - Delivered: `docker-compose.yml` (`sqlserver` + one-shot `db-init`), `db/setup.sql` + `01..05`, `HarborSupply.sln`, `tests/HarborSupply.Db.Tests`.
  - Verified: clean volume -> db-init exit 0 -> `dotnet test` 61 passed, 0 failed. db-init run a second time on the same volume exits 0 and leaves the data byte-identical (fixed fingerprint in `SeedTests.cs`). Mutation check: granting `SELECT` on `dbo` to `mcp_reader` turns the 9 dbo-select tests red; restoring via db-init turns them green.
  - Seed result: Customer 150, Vendor 40, Item 300, Invoice 6,075 (67 void), InvoiceLine 24,305, CustomerPayment 5,113, PaymentApplication 5,355, Bill 1,200, BillPayment 1,077. 312 invoices >60 days past due; open vendor bills in all five aging buckets.
  - Machine: Docker Desktop and .NET 8 SDK now installed. Tests read `HARBOR_MCP_CONNECTION` and `HARBOR_ADMIN_CONNECTION` from the environment, falling back to the repo-root `.env`.

- **2026-11-13 (Phase 2):** Built and verified the stdio MCP server (`src/HarborSupply.Mcp/Program.cs`, `tests/HarborSupply.Db.Tests/McpTests.cs`, `docs/PHASE2.md`). 67 tests pass.
- **2026-11-13 (Phase 3):** Completed permission proof and server-guard test coverage.
  - Hardened `ReportingTools.Validate` in `src/HarborSupply.Mcp/Program.cs` to also reject table hints (`WITH (NOLOCK)`), `FOR XML`/`FOR BROWSE` clauses, and schema/target-qualified scalar function calls (`dbo.fn...`) in addition to non-SELECT statements, `INTO`, multiple batches/statements, unapproved objects, and non-view table sources.
  - Expanded `tests/HarborSupply.Db.Tests/McpTests.cs` to cover 38 rejected SQL patterns, 6 valid reporting query shapes, end-to-end stdio rejection of unapproved objects/sensitive columns/invalid `sample_rows` bounds, row-cap truncation (`HARBOR_MCP_ROW_CAP=10` returning `rowCount=10, truncated=true` vs `truncated=false`), command timeout enforcement (`HARBOR_MCP_TIMEOUT_SECONDS=1` aborting a heavy cross join), and JSONL audit log verification (`ok` and `error` outcomes).
  - Verified: `dotnet test -m:1 --no-restore` passes 109/109 tests.

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
- **2026-11-13 (Phase 4):** Reconciliation and docs.
  - Delivered: `db/reports/q1..q5.sql` (reference reports on the `reporting` views, 2012-safe, no `GETDATE()`), `tests/HarborSupply.Db.Tests/ReconciliationTests.cs` (each report run through the real stdio MCP server and compared cell for cell with an independent base-table calculation as sa; pinned headline answers; guard/determinism check on the files), `db/reports/TRANSCRIPT.md` (real `claude -p` runs using only the harbor MCP tools), README reconciliation table, limits section.
  - Verified: `dotnet test -m:1` 120/120 pass. Reference headlines: Q1 12,888,296.32; Q2 top customer Eastport Builders 317,351.52; Q3 312 invoices / 1,012,732.00; Q4 HS-PLM-0031, HS-FAS-0009, HS-ELE-0022, HS-HND-0003, HS-ELE-0014; Q5 1,770,371.07.
  - Finding: Claude matched Q1, Q3, Q5 cold. On Q2 and Q4 it picked a different, stated interpretation (2026 YTD vs full 2025; partial Q4 vs Q3). With the basis stated in the question both match. Recorded honestly in README and transcript.
  - A first reference-query bug (Q5 bucket edges in the test's independent SQL, not the report) was caught by the reconciliation test and fixed.
  - Not done: demo clip (needs recording by owner), GitHub Actions CI (BRIEF §9), repo topics/pinning. Note `docs/` and `BRIEF.md` are in `.gitignore`; the transcript lives in `db/reports/` so it is publishable.
- **2026-11-13 (wrap-up):** Added `.github/workflows/ci.yml` (compose up, wait for db-init, `dotnet test`; **not yet run on GitHub**, check the first Actions run), CI badge, `demo/SCRIPT.md` (clip must be recorded by the owner). Phase 5: `dab/dab-config.json`, `dab` compose profile, README section. DAB checks: tools/list, describe, read, unlisted entity denied, create_record disabled; Claude answered Q2/Q3/Q5 via DAB matching the reference. Finding: DAB minimum SQL Server 2016; no joins/free SQL.
- CI green on GitHub (first run, commit 3a7630b). Repo description and topics set.
- Next: record demo clip, pin repo, Upwork/LinkedIn (BRIEF §9).

