-- Q3: Which invoices are more than 60 days past due, and what is the total?
-- Past due = unpaid balance > 0 and DaysPastDue > 60 as of AsOfDate (2026-10-31). Void invoices are excluded by the view.
-- InvoiceCountOver60 and TotalBalanceDue repeat on every row so the total is part of the same single statement.
SELECT i.InvoiceNo, c.CustomerCode, c.Name AS CustomerName,
       i.InvoiceDate, i.DueDate, i.DaysPastDue, i.InvoiceTotal, i.AmountPaid, i.BalanceDue,
       COUNT(*)           OVER () AS InvoiceCountOver60,
       SUM(i.BalanceDue)  OVER () AS TotalBalanceDue
FROM reporting.vSalesInvoices i
JOIN reporting.vCustomers c ON c.CustomerId = i.CustomerId
WHERE i.DaysPastDue > 60
ORDER BY i.DaysPastDue DESC, i.InvoiceNo;
