# Reconciliation transcript

Real, unedited answers from Claude (Claude Code, headless `claude -p`) to the five business questions, using **only** the four `harbor` MCP tools (`--strict-mcp-config`, `--allowedTools mcp__harbor__*`) against the Docker container, as the read-only `mcp_reader` login. Each question was asked in plain English with the instruction "Use only the harbor MCP tools. Today is whatever reporting.vReportingCalendar says. Give a compact table and state any assumptions in one line."

Reference answers are the queries in `db/reports/q1..q5.sql`, which `tests/HarborSupply.Db.Tests/ReconciliationTests.cs` proves equal to an independent base-table calculation. "Today" is `2026-10-31`.

| # | Question | Result |
|---|---|---|
| 1 | Sales by month, last 12 months | Match (all 12 months and the 12,888,296.32 total) |
| 2 | Top 10 customers, this year vs last | Match **once the comparison basis is stated** (see note) |
| 3 | Invoices > 60 days past due, and total | Match (312 invoices, 1,012,732.00; oldest 10 identical) |
| 4 | 5 items with largest unit drop vs prior quarter | Match **once the quarter basis is stated** (see note) |
| 5 | Owed to each vendor, 30-day aging | Match (every vendor row and bucket totals; grand total 1,770,371.07) |

**Honest note on Q2 and Q4.** Asked with no further guidance, Claude chose different, clearly stated interpretations: for Q2 it compared 2026 year-to-date against *full-year* 2025, and for Q4 it compared the partial quarter Oct 2026 against Q3. Its 2026 figures for Q2 matched the reference exactly; the 2025 column and the Q4 items differ only because the basis differs. Re-asked with the basis spelled out (like-for-like 2026 YTD vs the same dates of 2025; last complete quarter vs the one before), the answers match the reference. The lesson for real deployments: put period conventions in the question or in the view descriptions. Both runs are shown below.

---

## Q1: What were total sales by month for the last 12 months?

> **Total Sales by Month — Last 12 Months**
> 
> | Month   |    Total Sales |
> |---------|---------------|
> | 2025-11 | $1,212,133.03 |
> | 2025-12 | $1,213,149.73 |
> | 2026-01 |   $985,216.16 |
> | 2026-02 |   $900,560.26 |
> | 2026-03 | $1,047,065.37 |
> | 2026-04 | $1,073,028.39 |
> | 2026-05 | $1,073,497.81 |
> | 2026-06 | $1,007,978.49 |
> | 2026-07 |   $998,117.76 |
> | 2026-08 | $1,035,781.61 |
> | 2026-09 | $1,030,768.41 |
> | 2026-10 | $1,310,999.30 |
> | **Total** | **$12,888,296.32** |
> 
> **Assumption:** "Last 12 months" = the 12 complete calendar months ending on `AsOfDate` (2026-10-31), i.e. Nov 2025 – Oct 2026; sales are summed from `vSalesInvoiceLines.ExtendedAmount` (non-void invoices only).


## Q2 (as first asked): Who are the top 10 customers by revenue this year, and how does that compare with last year?

> Here are the top 10 customers by 2026 revenue vs 2025:
> 
> | # | Customer | 2026 Revenue | 2025 Revenue | YoY Change | YoY % |
> |---|----------|-------------|-------------|-----------|-------|
> | 1 | Eastport Builders | $317,352 | $184,556 | +$132,796 | +72.0% |
> | 2 | Driftwood Builders | $308,507 | $129,887 | +$178,620 | +137.5% |
> | 3 | Anchor Builders | $276,298 | $174,136 | +$102,162 | +58.7% |
> | 4 | Driftwood Marine | $261,602 | $271,307 | -$9,705 | -3.6% |
> | 5 | Cedar Builders | $238,724 | $141,921 | +$96,802 | +68.2% |
> | 6 | Harborlight Marine | $223,098 | $190,947 | +$32,151 | +16.8% |
> | 7 | Lakeshore Marine | $217,034 | $202,137 | +$14,896 | +7.4% |
> | 8 | Anchor Marine | $213,045 | $194,782 | +$18,263 | +9.4% |
> | 9 | Juniper Builders | $194,776 | $147,676 | +$47,099 | +31.9% |
> | 10 | Northgate Marine | $188,352 | $201,754 | -$13,402 | -6.6% |
> 
> **Assumption:** 2026 revenue covers Jan 1–Oct 31 (10 months, per `AsOfDate = 2026-10-31`), while 2025 covers the full calendar year — so YoY comparisons are not period-matched; 2026 figures will grow further by year-end.
> 
> **Notable:** Driftwood Builders is the standout at +137.5% growth. Two customers (Driftwood Marine, Northgate Marine) are running below their 2025 pace despite the truncated comparison window.


## Q2 (basis stated: like for like, 2026 YTD vs the same dates of 2025)

> Here are the top 10 customers by 2026 YTD revenue vs the same Jan 1–Oct 31 window in 2025:
> 
> | # | Customer | 2026 YTD ($) | 2025 YTD ($) | YoY % |
> |---|---|---:|---:|---:|
> | 1 | Eastport Builders | 317,352 | 150,394 | +111.0% |
> | 2 | Driftwood Builders | 308,507 | 94,994 | +224.8% |
> | 3 | Anchor Builders | 276,298 | 129,556 | +113.3% |
> | 4 | Driftwood Marine | 261,602 | 209,875 | +24.6% |
> | 5 | Cedar Builders | 238,724 | 109,342 | +118.3% |
> | 6 | Harborlight Marine | 223,098 | 157,017 | +42.1% |
> | 7 | Lakeshore Marine | 217,034 | 139,540 | +55.5% |
> | 8 | Anchor Marine | 213,045 | 173,466 | +22.8% |
> | 9 | Juniper Builders | 194,776 | 129,979 | +49.9% |
> | 10 | Northgate Marine | 188,352 | 157,538 | +19.6% |
> 
> **Assumption:** Revenue = `InvoiceTotal` on non-void invoices (the view excludes voids); customers with zero 2026 YTD revenue are excluded from ranking.
> 
> **Standout:** Driftwood Builders is the biggest mover at +225% YoY despite ranking #2 — nearly tripled from $95K to $309K. The Marine segment (rows 4, 6, 7, 8, 10) is growing slower (20–55%) than the Builders segment (110–225%), which dominates the top 5.


## Q3: Which invoices are more than 60 days past due, and what is the total? (10 oldest plus count and total)

> **Assumption:** `DaysPastDue` is precomputed in `vSalesInvoices` using `vReportingCalendar.AsOfDate` (2026-10-31) as "today."
> 
> ---
> 
> **10 Oldest Invoices > 60 Days Past Due**
> 
> | InvoiceNo   | Customer               | Due Date   | Days Past Due | Balance Due  |
> |-------------|------------------------|------------|---------------|--------------|
> | INV-000029  | Eastport Builders      | 2024-12-03 | 697           | $1,237.86    |
> | INV-000027  | Juniper Mechanical     | 2024-12-03 | 697           | $3,116.56    |
> | INV-000061  | Bayview Marine         | 2024-12-05 | 695           | $3,746.02    |
> | INV-000106  | Driftwood Contractors  | 2024-12-11 | 689           | $685.79      |
> | INV-000104  | Eastport Mechanical    | 2024-12-11 | 689           | $1,114.18    |
> | INV-000159  | Eastport Builders      | 2024-12-17 | 683           | $3,007.31    |
> | INV-000023  | Mariner Hardware       | 2024-12-18 | 682           | $642.60      |
> | INV-000177  | Bayview Electric       | 2024-12-20 | 680           | $715.04      |
> | INV-000073  | Cedar Builders         | 2024-12-22 | 678           | $27.75       |
> | INV-000201  | Ironwood Hardware      | 2024-12-23 | 677           | $2,830.89    |
> 
> ---
> 
> **All Invoices > 60 Days Past Due**
> 
> | Count | Total Balance Due  |
> |-------|--------------------|
> | 312   | $1,012,732.00      |


## Q4 (as first asked): Which 5 items had the largest drop in units sold versus the prior quarter?

> **Assumption:** "Current quarter" = Q4 2026 (Oct 1–Oct 31, partial — only 1 month of data); "prior quarter" = Q3 2026 (Jul 1–Sep 30, full). The drop is partly structural because Q4 is incomplete.
> 
> | SKU | Description | Category | Q3 Units | Q4 Units (thru 10/31) | Drop |
> |---|---|---|---:|---:|---:|
> | HS-HND-0019 | Standard Pliers | Hand Tools | 319 | 50 | **269** |
> | HS-CLN-0016 | Standard Shop Towels | Cleaning Supplies | 361 | 107 | **254** |
> | HS-CLN-0040 | Standard Disinfectant | Cleaning Supplies | 320 | 97 | **223** |
> | HS-FIT-0066 | Pro Reducer | Fittings | 288 | 79 | **209** |
> | HS-SAF-0045 | Pro Safety Glasses | Safety Gear | 295 | 96 | **199** |
> 
> Since Q4 has only ~1 month of data vs. Q3's 3 months, all drops are expected — but Standard Pliers and both Cleaning items show the steepest absolute declines and may warrant a closer look even on a pace-adjusted basis.


## Q4 (basis stated: last complete quarter vs the one before)

> **Assumption:** "Last complete quarter" = Q3 2026 (Jul–Sep); "prior quarter" = Q2 2026 (Apr–Jun), anchored to `vReportingCalendar.AsOfDate` = 2026-10-31. Items with zero sales in one quarter are included.
> 
> | SKU | Description | Category | Q2 2026 | Q3 2026 | QoQ Change |
> |---|---|---|---:|---:|---:|
> | HS-PLM-0031 | Standard P-Trap | Plumbing | 888 | 94 | **−794** |
> | HS-FAS-0009 | Standard Lag Screw | Fasteners | 747 | 16 | **−731** |
> | HS-ELE-0022 | Standard Circuit Breaker | Electrical | 483 | 34 | **−449** |
> | HS-HND-0003 | Standard Claw Hammer | Hand Tools | 411 | 35 | **−376** |
> | HS-ELE-0014 | Standard Outlet Box | Electrical | 405 | 66 | **−339** |


## Q5: What do we owe each vendor right now, aged in 30-day buckets? (top 10 plus bucket totals)

> As of **2026-10-31** (from `vReportingCalendar.AsOfDate`):
> 
> | Vendor | Current | 1–30 | 31–60 | 61–90 | 90+ | **Total** |
> |---|---:|---:|---:|---:|---:|---:|
> | Continental Wholesale | 101,692 | 5,304 | 1,416 | — | 42,217 | **150,629** |
> | Frontier Manufacturing | 42,203 | 7,461 | 66,901 | — | 25,747 | **142,312** |
> | Global Manufacturing | 65,042 | 33,546 | — | — | 38,694 | **137,283** |
> | Beacon Manufacturing | 19,403 | 31,472 | — | 24,172 | 61,144 | **136,191** |
> | Global Wholesale | 76,947 | — | 37,846 | — | — | **114,792** |
> | Frontier Wholesale | 33,417 | — | — | 16,077 | 63,262 | **112,755** |
> | Apex Wholesale | 87,434 | — | 19,458 | — | — | **106,893** |
> | Beacon Wholesale | 26,628 | 28,095 | — | — | 32,231 | **86,955** |
> | Continental Supply Partners | 21,486 | 17,031 | — | — | 31,956 | **70,473** |
> | Frontier Distribution | 44,745 | — | — | — | 14,135 | **58,880** |
> | **GRAND TOTAL (all vendors)** | **799,173** | **181,396** | **281,867** | **133,298** | **374,636** | **1,770,371** |
> 
> *(All amounts in USD. Dashes = $0.)*
> 
> **Assumption:** "Current" = bills where `DaysPastDue ≤ 0` (due date not yet reached); only open bills (`BalanceDue > 0`) are included; grand total spans all vendors with a balance, not just the top 10.


_Audit: the 7 runs made 36 tool calls through the server (JSONL audit log); 2 errored (Claude guessed a column name, read the error and corrected itself). Every call stayed inside `reporting`._
