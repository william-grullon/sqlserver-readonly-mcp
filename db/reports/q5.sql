-- Q5: What do we owe each vendor right now, aged in 30-day buckets?
-- Owed = unpaid bill balance as of AsOfDate (2026-10-31). Aged by days past DUE date:
-- Current (not yet due), 1-30, 31-60, 61-90, 90+. Vendors with nothing owed are omitted.
SELECT v.VendorCode, v.Name,
       SUM(CASE WHEN b.DaysPastDue = 0                  THEN b.BalanceDue ELSE 0 END) AS Current_,
       SUM(CASE WHEN b.DaysPastDue BETWEEN 1 AND 30     THEN b.BalanceDue ELSE 0 END) AS Days1To30,
       SUM(CASE WHEN b.DaysPastDue BETWEEN 31 AND 60    THEN b.BalanceDue ELSE 0 END) AS Days31To60,
       SUM(CASE WHEN b.DaysPastDue BETWEEN 61 AND 90    THEN b.BalanceDue ELSE 0 END) AS Days61To90,
       SUM(CASE WHEN b.DaysPastDue > 90                 THEN b.BalanceDue ELSE 0 END) AS Over90,
       SUM(b.BalanceDue) AS TotalOwed
FROM reporting.vVendorBills b
JOIN reporting.vVendors v ON v.VendorId = b.VendorId
WHERE b.BalanceDue > 0
GROUP BY v.VendorCode, v.Name
ORDER BY TotalOwed DESC, v.VendorCode;
