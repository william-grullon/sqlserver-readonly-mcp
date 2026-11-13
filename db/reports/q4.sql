-- Q4: Which 5 items had the largest drop in units sold versus the prior quarter?
-- AsOfDate (2026-10-31) falls in a partial quarter (Q4 2026, one month old), so "the quarter" is the last
-- FULL quarter (Q3 2026: Jul-Sep) and the prior quarter is Q2 2026 (Apr-Jun). Units = line quantity on non-void invoices.
SELECT TOP (5)
       it.Sku, it.Description, it.Category,
       q.UnitsPriorQuarter, q.UnitsLastQuarter,
       q.UnitsLastQuarter - q.UnitsPriorQuarter AS UnitChange
FROM (
    SELECT l.ItemId,
           SUM(CASE WHEN l.InvoiceDate >= b.PriorStart AND l.InvoiceDate < b.LastStart THEN l.Quantity ELSE 0 END) AS UnitsPriorQuarter,
           SUM(CASE WHEN l.InvoiceDate >= b.LastStart  AND l.InvoiceDate < b.CurrStart THEN l.Quantity ELSE 0 END) AS UnitsLastQuarter
    FROM reporting.vSalesInvoiceLines l
    CROSS JOIN (SELECT DATEADD(QUARTER, -2, s.CurrStart) AS PriorStart,
                       DATEADD(QUARTER, -1, s.CurrStart) AS LastStart,
                       s.CurrStart
                FROM (SELECT DATEFROMPARTS(YEAR(AsOfDate), ((MONTH(AsOfDate) - 1) / 3) * 3 + 1, 1) AS CurrStart
                      FROM reporting.vReportingCalendar) s) b
    WHERE l.InvoiceDate >= b.PriorStart AND l.InvoiceDate < b.CurrStart
    GROUP BY l.ItemId
) q
JOIN reporting.vItems it ON it.ItemId = q.ItemId
ORDER BY q.UnitsLastQuarter - q.UnitsPriorQuarter ASC, it.Sku;
