# Demo Build Brief: Read-Only Claude Access to SQL Server via MCP

Owner: William Grullon · Drafted November 13, 2026
Use: drop this file into an empty repo as `BRIEF.md` and point Claude Code at it.

## 1. Goal

A public GitHub repo that proves one thing: Claude can answer plain-English business questions against an on-premises style SQL Server, with read-only access to approved data only, and the answers reconcile to known reports.

It doubles as:

- Upwork portfolio item ("MCP Server: Claude on Business Data")
- A 60-second demo clip for proposals
- A truthful "I have built this" for jobs like the AccountMate posting

**Honest label:** reference implementation on synthetic data. Not a production deployment. Say "working build" in proposals.

## 2. Scope

**In**

- SQL Server in Docker with a synthetic ERP-style database
- A read-only MCP server exposing approved views
- Database-enforced permissions plus server-side query guards
- Automated tests that prove denial of unapproved reads and all writes
- Five business questions reconciled against reference report queries
- README with architecture diagram, security model, and limits

**Out**

- Any real client, employer, or FDOT data, schema, or code
- AccountMate's actual schema (proprietary). Use an invented company and tables.
- Write access, auth providers, cloud hosting, UI

**Timebox:** one weekend, 8 to 12 hours. Stop at Phase 4 if time runs out; Phase 5 is a stretch.

## 3. Decisions (made, so Claude Code does not have to ask)

| Decision | Choice | Why |
|---|---|---|
| Language | C# / .NET 8 or later | Matches your Microsoft-stack positioning; most MCP demos are Python or TypeScript |
| MCP library | Official MCP C# SDK | Have Claude Code read the current SDK docs first; the API has changed across releases |
| Transport | stdio | Works with Claude Desktop and Claude Code with no network setup |
| Database | SQL Server 2022 Developer in Docker, database compatibility level 110 | SQL Server 2012 has no container image. Level 110 approximates 2012 T-SQL behavior. |
| T-SQL rule | 2012-safe syntax only | No STRING_AGG, no JSON functions, no TRIM, no DROP IF EXISTS |
| Data | Fictional distributor "Harbor Supply Co." with an ERP-style schema | Customers, items, invoices, invoice lines, vendors, bills, payments. About 2 years of seeded data. |
| Repo name | `sqlserver-readonly-mcp` | Says what it is; searchable |
| License | MIT | Standard for a portfolio reference |

## 4. Architecture

```
Claude (Desktop or Code)
        |  MCP over stdio
        v
MCP server (.NET)          guards: SELECT only, single statement,
        |                  view allowlist, row cap, timeout, audit log
        v  SQL login: mcp_reader
SQL Server (Docker)
   reporting.*  views      GRANT SELECT
   dbo.*        base tables no access (DENY)
```

Two layers on purpose: the database is the real control, the server guards are defense in depth. The README should say so.

## 5. Security model (the part clients care about)

**Database**

1. Base tables live in `dbo`. Approved data is exposed only through views in a `reporting` schema.
2. Login and user `mcp_reader`: `GRANT SELECT ON SCHEMA::reporting`, explicit `DENY` on `dbo`, no other permissions, not a member of any role beyond `public`.
3. At least one sensitive column (for example customer tax ID, credit limit) exists in a base table and is deliberately left out of every view.

**MCP server**

1. Accept only a single `SELECT` statement. Reject anything containing a second statement, DML, DDL, `EXEC`, or `INTO`.
2. Referenced objects must be on the `reporting` view allowlist.
3. Row cap (default 500) and command timeout (default 15 seconds).
4. Every call written to an audit log: timestamp, tool, SQL text, row count, duration, outcome.
5. Connection string from environment only. No secrets in the repo. Ship `.env.example`.

## 6. MCP tools

| Tool | Input | Returns |
|---|---|---|
| `list_views` | none | Approved views with a one-line description each |
| `describe_view` | view name | Columns, types, and a plain-English description |
| `run_query` | SQL string | Rows as JSON, row count, truncated flag |
| `sample_rows` | view name, n (max 20) | First n rows, to help Claude learn the shape |

Tool descriptions matter: write them so Claude knows to call `list_views` and `describe_view` before `run_query`.

## 7. Phases and acceptance criteria

| # | Phase | Done when |
|---|---|---|
| 1 | Database | `docker compose up` starts SQL Server; one script creates schema, seeds data, sets compatibility level 110, creates `reporting` views and `mcp_reader` |
| 2 | MCP server | Server starts over stdio; all four tools work from Claude Code against the container |
| 3 | Permission proof | Tests pass for: SELECT on a `dbo` table fails as `mcp_reader`; INSERT, UPDATE, DELETE fail; multi-statement and `EXEC` are rejected by the server; excluded sensitive column is unreachable; row cap and timeout hold |
| 4 | Reconciliation and docs | Five business questions each have a reference SQL "report" with a known answer; a transcript shows Claude's answers matching; README complete; demo clip recorded |
| 5 | Stretch: Data API Builder variant | Same views exposed through Data API Builder on the 2022 container, with a README note that Microsoft documents SQL Server 2016 as DAB's minimum, so a true 2012 server needs the direct approach from Phases 1 to 4. Check whether the current DAB release includes its own MCP endpoint and note what you find. |

**Five business questions (edit freely)**

1. What were total sales by month for the last 12 months?
2. Who are the top 10 customers by revenue this year, and how does that compare with last year?
3. Which invoices are more than 60 days past due, and what is the total?
4. Which 5 items had the largest drop in units sold versus the prior quarter?
5. What do we owe each vendor right now, aged in 30-day buckets?

## 8. README outline

1. One-paragraph summary and a 60-second demo GIF or video link
2. Architecture diagram (Mermaid)
3. Quick start: three commands
4. Security model, with the permission tests as evidence
5. Reconciliation table: question, reference answer, Claude's answer
6. SQL Server 2012 notes: compatibility level, syntax limits, Data API Builder minimum version
7. What production would add: Windows auth or managed identity, network placement, TLS, secret storage, log shipping, change control for the view allowlist
8. Limits and honest scope statement

## 9. Publish checklist

- [ ] No secrets in history (`.env` in `.gitignore` from the first commit)
- [ ] All data synthetic; no employer or client names anywhere
- [ ] Tests run in GitHub Actions and the badge is green
- [ ] Repo description and topics: `mcp`, `claude`, `sql-server`, `dotnet`, `read-only`, `erp`
- [ ] Pinned on your GitHub profile
- [ ] Added to Upwork portfolio with the diagram and clip, and linked from LinkedIn
- [ ] Built on your own equipment and time

## 10. Kickoff prompt for Claude Code

```
Read BRIEF.md in this repo. It is the full specification.

Before writing code:
1. Read the current documentation for the official MCP C# SDK and tell me which package and version you will use.
2. Propose the folder structure and the database schema (tables, reporting views, seed volumes) and wait for my approval.

Then build Phase 1 only. Stop when its acceptance criteria pass and show me how you verified them. We will do one phase per session.

Rules: T-SQL must be valid on SQL Server 2012. No secrets in the repo. All data is synthetic. Write tests alongside each phase, not at the end.
```
