-- Q1: What were total sales by month for the last 12 months?
-- "Sales" = invoice total (sum of line amounts) of non-void invoices, by invoice date.
-- Window = the 12 full calendar months ending at reporting.vReportingCalendar.AsOfDate (2026-10-31):
-- 2025-11 .. 2026-10. Void invoices are already excluded by reporting.vSalesInvoices.
SELECT DATEFROMPARTS(YEAR(i.InvoiceDate), MONTH(i.InvoiceDate), 1) AS SalesMonth,
       COUNT(*)            AS InvoiceCount,
       SUM(i.InvoiceTotal) AS TotalSales
FROM reporting.vSalesInvoices i
CROSS JOIN reporting.vReportingCalendar cal
WHERE i.InvoiceDate >= DATEADD(MONTH, -11, DATEFROMPARTS(YEAR(cal.AsOfDate), MONTH(cal.AsOfDate), 1))
  AND i.InvoiceDate <= cal.AsOfDate
GROUP BY DATEFROMPARTS(YEAR(i.InvoiceDate), MONTH(i.InvoiceDate), 1)
ORDER BY SalesMonth;
