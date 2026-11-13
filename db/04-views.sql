-- 04: reporting schema + views (the allowlist). Plain views only; 2012-safe (no CREATE OR ALTER).
-- Sensitive columns (TaxId, CreditLimit, UnitCost, RemitBankAccount) are never selected here.
USE HarborSupply;
GO

IF SCHEMA_ID(N'reporting') IS NULL
    EXEC (N'CREATE SCHEMA reporting AUTHORIZATION dbo');
GO

-- Drop in reverse dependency order so the script can be re-run
IF OBJECT_ID(N'reporting.vSalesInvoiceLines', N'V') IS NOT NULL DROP VIEW reporting.vSalesInvoiceLines;
IF OBJECT_ID(N'reporting.vSalesInvoices',     N'V') IS NOT NULL DROP VIEW reporting.vSalesInvoices;
IF OBJECT_ID(N'reporting.vCustomerPayments',  N'V') IS NOT NULL DROP VIEW reporting.vCustomerPayments;
IF OBJECT_ID(N'reporting.vVendorBills',       N'V') IS NOT NULL DROP VIEW reporting.vVendorBills;
IF OBJECT_ID(N'reporting.vVendors',           N'V') IS NOT NULL DROP VIEW reporting.vVendors;
IF OBJECT_ID(N'reporting.vItems',             N'V') IS NOT NULL DROP VIEW reporting.vItems;
IF OBJECT_ID(N'reporting.vCustomers',         N'V') IS NOT NULL DROP VIEW reporting.vCustomers;
IF OBJECT_ID(N'reporting.vReportingCalendar', N'V') IS NOT NULL DROP VIEW reporting.vReportingCalendar;
GO

CREATE VIEW reporting.vReportingCalendar AS
SELECT CAST('2026-10-31' AS DATE) AS AsOfDate;
GO

CREATE VIEW reporting.vCustomers AS
SELECT CustomerId, CustomerCode, Name, City, State, PaymentTermsDays, CreatedDate
FROM dbo.Customer;
GO

CREATE VIEW reporting.vItems AS
SELECT ItemId, Sku, Description, Category, ListPrice
FROM dbo.Item;
GO

CREATE VIEW reporting.vVendors AS
SELECT VendorId, VendorCode, Name, City, State, PaymentTermsDays
FROM dbo.Vendor;
GO

CREATE VIEW reporting.vSalesInvoices AS
SELECT i.InvoiceId, i.InvoiceNo, i.CustomerId, i.InvoiceDate, i.DueDate, i.Status,
       t.InvoiceTotal,
       ISNULL(p.AmountPaid, 0) AS AmountPaid,
       t.InvoiceTotal - ISNULL(p.AmountPaid, 0) AS BalanceDue,
       CASE WHEN t.InvoiceTotal - ISNULL(p.AmountPaid, 0) > 0 AND cal.AsOfDate > i.DueDate
            THEN DATEDIFF(DAY, i.DueDate, cal.AsOfDate) ELSE 0 END AS DaysPastDue
FROM dbo.Invoice i
JOIN (SELECT InvoiceId, SUM(ExtendedAmount) AS InvoiceTotal
      FROM dbo.InvoiceLine GROUP BY InvoiceId) t ON t.InvoiceId = i.InvoiceId
LEFT JOIN (SELECT InvoiceId, SUM(AppliedAmount) AS AmountPaid
           FROM dbo.PaymentApplication GROUP BY InvoiceId) p ON p.InvoiceId = i.InvoiceId
CROSS JOIN reporting.vReportingCalendar cal
WHERE i.Status <> 'Void';
GO

CREATE VIEW reporting.vSalesInvoiceLines AS
SELECT l.InvoiceLineId, l.InvoiceId, i.InvoiceNo, i.InvoiceDate,
       i.CustomerId, c.CustomerCode, c.Name AS CustomerName,
       l.[LineNo], l.ItemId, it.Sku, it.Category,
       l.Quantity, l.UnitPrice, l.ExtendedAmount
FROM dbo.InvoiceLine l
JOIN dbo.Invoice i ON i.InvoiceId = l.InvoiceId
JOIN dbo.Customer c ON c.CustomerId = i.CustomerId
JOIN dbo.Item it ON it.ItemId = l.ItemId
WHERE i.Status <> 'Void';
GO

CREATE VIEW reporting.vCustomerPayments AS
SELECT PaymentId, CustomerId, PaymentDate, Amount, Method
FROM dbo.CustomerPayment;
GO

CREATE VIEW reporting.vVendorBills AS
SELECT b.BillId, b.BillNo, b.VendorId, b.BillDate, b.DueDate, b.Amount, b.Status,
       ISNULL(p.AmountPaid, 0) AS AmountPaid,
       b.Amount - ISNULL(p.AmountPaid, 0) AS BalanceDue,
       CASE WHEN b.Amount - ISNULL(p.AmountPaid, 0) > 0 AND cal.AsOfDate > b.DueDate
            THEN DATEDIFF(DAY, b.DueDate, cal.AsOfDate) ELSE 0 END AS DaysPastDue
FROM dbo.Bill b
LEFT JOIN (SELECT BillId, SUM(Amount) AS AmountPaid
           FROM dbo.BillPayment GROUP BY BillId) p ON p.BillId = b.BillId
CROSS JOIN reporting.vReportingCalendar cal;
GO
