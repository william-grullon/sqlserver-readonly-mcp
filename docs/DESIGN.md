# Design: sqlserver-readonly-mcp

Status: **Proposed, awaiting owner approval** (drafted 2026-11-13). Covers the two "before writing code" items from BRIEF.md §10. Once approved, Phase 1 builds exactly this.

## 1. MCP SDK choice

| Item | Choice |
|---|---|
| Package | `ModelContextProtocol` **2.4.0** (NuGet, stable, published 2026-11-07, Apache-2.0) |
| Also needed | `Microsoft.Extensions.Hosting` (generic host), `Microsoft.Data.SqlClient` |
| Not used | `ModelContextProtocol.AspNetCore` (HTTP transport; we use stdio), `ModelContextProtocol.Core` alone (we want the hosting/DI extensions) |
| Target framework | `net8.0` (LTS; package ships net8.0/net9.0/net10.0) |
| Pattern | `builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`, tools as `[McpServerToolType]` classes with `[McpServerTool, Description(...)]` methods. All logging to **stderr** (`LogToStandardErrorThreshold = LogLevel.Trace`) so stdout carries only protocol. |

Caveat: the public getting-started page still describes v1. Phase 2 must confirm the pattern compiles against 2.4.0 (check the repo's `samples/` folder) before building the tools.

## 2. Query guard parser

Use **`Microsoft.SqlServer.TransactSql.ScriptDom`** with `TSql110Parser` (the SQL Server 2012 grammar) instead of regex. Parse the input, then walk the AST:

- exactly one batch, exactly one statement, and it is a `SelectStatement`
- no `INTO` clause, no `EXEC`, no DML/DDL anywhere (including inside CTEs/subqueries)
- every `NamedTableReference` is schema `reporting` and on the allowlist (CTE names excepted)
- a parse error = reject

This is stronger and easier to test than string matching, and it enforces 2012 syntax for free.

## 3. Folder structure

```
sqlserver-readonly-mcp/
├─ BRIEF.md                 spec (source of truth)
├─ CLAUDE.md                rules for Claude Code sessions
├─ README.md                Phase 4
├─ LICENSE                  MIT
├─ .gitignore  .gitattributes  .env.example
├─ docker-compose.yml       sqlserver + one-shot db-init service
├─ db/
│  ├─ setup.sql             single entry point; :r includes the files below, in order
│  ├─ 01-database.sql       CREATE DATABASE HarborSupply, COMPATIBILITY_LEVEL = 110
│  ├─ 02-tables.sql         dbo base tables, PKs, FKs
│  ├─ 03-seed.sql           deterministic synthetic data
│  ├─ 04-views.sql          reporting schema + views
│  ├─ 05-security.sql       login/user mcp_reader, GRANT/DENY
│  └─ reports/              Phase 4: five reference report queries (q1..q5.sql)
├─ src/
│  └─ HarborSupply.Mcp/     MCP server (stdio): Tools/, Guards/, Audit/, Program.cs
├─ tests/
│  ├─ HarborSupply.Mcp.Tests/         unit tests (guards, no DB)
│  └─ HarborSupply.Db.Tests/          integration tests against the container
├─ docs/                    DESIGN.md, reconciliation transcript, diagram
├─ .github/workflows/ci.yml           Phase 3/4
└─ HarborSupply.sln
```

Container init: the SQL Server image has no init-script hook, so `docker-compose.yml` runs a second short-lived `db-init` service (same image, which ships `/opt/mssql-tools18/bin/sqlcmd`) that waits for the healthcheck and runs `sqlcmd -i db/setup.sql -v McpReaderPassword=...`. Passwords come from `.env` only. Scripts are re-runnable (guarded with `IF OBJECT_ID(...) IS NULL` / `IF DB_ID(...)`, since `DROP IF EXISTS` and `CREATE OR ALTER` are not 2012).

## 4. Database schema (`HarborSupply`, fictional distributor Harbor Supply Co.)

### Fixed reporting date

Seed data runs **2024-11-01 through 2026-10-31**. "Today" for every report is **2026-10-31**, exposed as view `reporting.vReportingCalendar` (one row: `AsOfDate`). Reports and Claude both use it instead of `GETDATE()`, so reconciliation answers never drift.

### Base tables (`dbo`, no access for `mcp_reader`)

| Table | Key columns | Notes |
|---|---|---|
| `Customer` | CustomerId, CustomerCode, Name, City, State, PaymentTermsDays, CreatedDate, **TaxId**, **CreditLimit** | bold = sensitive, never in a view |
| `Vendor` | VendorId, VendorCode, Name, City, State, PaymentTermsDays, **TaxId**, **RemitBankAccount** | bold = sensitive |
| `Item` | ItemId, Sku, Description, Category, ListPrice, **UnitCost** | UnitCost excluded (margin is sensitive) |
| `Invoice` | InvoiceId, InvoiceNo, CustomerId, InvoiceDate, DueDate, Status (`Open`/`Paid`/`Void`) | |
| `InvoiceLine` | InvoiceLineId, InvoiceId, LineNo, ItemId, Quantity, UnitPrice, ExtendedAmount | |
| `CustomerPayment` | PaymentId, CustomerId, PaymentDate, Amount, Method | |
| `PaymentApplication` | PaymentId, InvoiceId, AppliedAmount | links payments to invoices |
| `Bill` | BillId, BillNo, VendorId, BillDate, DueDate, Amount, Status | AP side |
| `BillPayment` | BillPaymentId, BillId, PaymentDate, Amount | |

Money columns `DECIMAL(19,4)`; dates `DATE`.

### Reporting views (`reporting`, the allowlist)

| View | Purpose |
|---|---|
| `vReportingCalendar` | single row, `AsOfDate = 2026-10-31` |
| `vCustomers` | customer master minus TaxId, CreditLimit |
| `vItems` | item master minus UnitCost |
| `vVendors` | vendor master minus TaxId, RemitBankAccount |
| `vSalesInvoices` | header + InvoiceTotal, AmountPaid, BalanceDue, DaysPastDue (vs AsOfDate); excludes Void |
| `vSalesInvoiceLines` | lines joined to invoice date, customer, item SKU/category |
| `vCustomerPayments` | payments |
| `vVendorBills` | bills + AmountPaid, BalanceDue, DaysPastDue |

All views are plain views (no `SCHEMABINDING`, no indexed views). Each view's one-line description lives in the MCP server's allowlist config, not in the DB, so `list_views` can return it.

### Seed volumes (deterministic, no `NEWID()`/`RAND()`)

| Table | Rows (approx) |
|---|---|
| Customer | 150 |
| Vendor | 40 |
| Item | 300 (8 categories) |
| Invoice | 6,000 (~250/month, gentle growth + Q4 seasonality) |
| InvoiceLine | 24,000 (1 to 7 lines each) |
| CustomerPayment / PaymentApplication | ~5,400 / ~5,600 (about 6% of invoices left past due) |
| Bill / BillPayment | 1,200 / ~1,050 |

Generation: set-based T-SQL from a numbers CTE, with pseudo-randomness from `ABS(CHECKSUM(n, salt)) % k` on integer inputs, which is deterministic across runs. Same script, same data, same reference answers. Deliberate shapes so the five questions have interesting answers: a few customers that shrink year over year, several items with a sharp Q3-to-Q4 2026 drop, and AP spread across all aging buckets.

### Security (05-security.sql)

```sql
CREATE LOGIN mcp_reader WITH PASSWORD = '$(McpReaderPassword)', CHECK_POLICY = ON;
CREATE USER mcp_reader FOR LOGIN mcp_reader;          -- member of public only
GRANT SELECT ON SCHEMA::reporting TO mcp_reader;
DENY SELECT, INSERT, UPDATE, DELETE, EXECUTE, ALTER ON SCHEMA::dbo TO mcp_reader;
```

Views and tables share owner `dbo`, so ownership chaining lets the views read `dbo` while the user cannot touch `dbo` directly.

## 5. T-SQL 2012 rule (enforced in review)

Allowed: `IIF`, `CONCAT`, `EOMONTH`, `DATEFROMPARTS`, `TRY_CONVERT`, `FORMAT`, `OFFSET/FETCH`, window functions with `ROWS/RANGE`.
Forbidden: `STRING_AGG`, `STRING_SPLIT`, `TRIM`, JSON functions, `DROP ... IF EXISTS`, `CREATE OR ALTER`, `GENERATE_SERIES`, `DATE_BUCKET`, `GREATEST/LEAST`, `APPROX_COUNT_DISTINCT`.
