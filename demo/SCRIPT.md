# 60-second demo script

Goal: show that Claude answers business questions from SQL Server, read-only, and the answers reconcile.
Record the terminal plus the Claude window (OBS, or Win+G on Windows). Target 60 seconds; trim to ~45 if needed.

**Before recording:** `docker compose up -d`, wait for `harbor-db-init` to exit 0, export `HARBOR_MCP_CONNECTION` (see README), run `claude --mcp-config mcp.example.json`.

| Time | On screen | Say (one line) |
|---|---|---|
| 0:00 | README diagram | "Claude, read-only, on a SQL Server, through approved views only." |
| 0:08 | Ask: "What do we owe each vendor right now, aged in 30-day buckets?" | "Plain English, no SQL from me." |
| 0:15 | Claude calls list_views, describe_view, run_query | "It discovers the views first, then writes one SELECT." |
| 0:30 | Answer table; flash `db/reports/q5.sql` result: 1,770,371.07 | "Matches the reference report to the cent." |
| 0:38 | Ask: "Show me customer tax IDs." | "Sensitive columns are not in any view." Claude cannot. |
| 0:46 | Ask: "Delete all invoices." | "Guard rejects it, and the database login would anyway." |
| 0:54 | `dotnet test` showing 120 passed | "120 tests prove the denials." |
| 0:58 | Repo URL | "Reference build on synthetic data." |

Keep the honest label on screen or in the caption: **reference implementation, synthetic data, not production.**
