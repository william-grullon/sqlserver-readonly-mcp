-- Q2: Who are the top 10 customers by revenue this year, and how does that compare with last year?
-- "This year" = 2026 year-to-date (2026-01-01 .. AsOfDate). "Last year" = the SAME period of 2025
-- (2025-01-01 .. 2025-10-31) so the comparison is like for like. Revenue = non-void invoice totals.
SELECT TOP (10)
       c.CustomerCode, c.Name,
       t.RevenueThisYear,
       t.RevenueLastYear,
       t.RevenueThisYear - t.RevenueLastYear AS Change,
       CASE WHEN t.RevenueLastYear = 0 THEN NULL
            ELSE CAST(100.0 * (t.RevenueThisYear - t.RevenueLastYear) / t.RevenueLastYear AS DECIMAL(9,1)) END AS ChangePct
FROM (
    SELECT i.CustomerId,
           SUM(CASE WHEN i.InvoiceDate >= DATEFROMPARTS(YEAR(cal.AsOfDate), 1, 1) AND i.InvoiceDate <= cal.AsOfDate
                    THEN i.InvoiceTotal ELSE 0 END) AS RevenueThisYear,
           SUM(CASE WHEN i.InvoiceDate >= DATEFROMPARTS(YEAR(cal.AsOfDate) - 1, 1, 1)
                     AND i.InvoiceDate <= DATEADD(YEAR, -1, cal.AsOfDate)
                    THEN i.InvoiceTotal ELSE 0 END) AS RevenueLastYear
    FROM reporting.vSalesInvoices i
    CROSS JOIN reporting.vReportingCalendar cal
    GROUP BY i.CustomerId
) t
JOIN reporting.vCustomers c ON c.CustomerId = t.CustomerId
ORDER BY t.RevenueThisYear DESC, c.CustomerCode;
