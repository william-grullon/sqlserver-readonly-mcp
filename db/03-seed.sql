-- 03: deterministic synthetic data for Harbor Supply Co.
-- Window 2024-11-01 .. 2026-10-31. No NEWID()/RAND()/GETDATE(): all "randomness" is
-- dbo.fnSeedHash(a, b, c) % k, an MD5 of integer inputs, so it is stable across runs and machines.
-- (T-SQL CHECKSUM was tried first and rejected: with small integer inputs its distinct salts
-- correlate, which left some customers and items with no rows at all.)
-- The helper function exists only while this script runs; it is dropped at the end.
--
-- Deliberate shapes (so the five business questions have interesting answers):
--   Q2  customers 2,3,5,7,9 (top-volume) lose ~55% of invoices from 2026-01-01; 16..20 absorb them
--   Q3  ~4% of invoices never paid and ~2% only half paid -> a long past-due tail
--   Q4  items 3,9,14,22,31 sell 3x in Jan-Jun 2026, then 80% of their lines move to other
--       items from 2026-07-01 -> largest unit drop Q2 2026 -> Q3 2026 (last full quarter)
--   Q5  open bills spread across not-yet-due, 1-30, 31-60, 61-90 and 90+ days past due
--
-- One batch on purpose: the whole seed is guarded by a single existence check.
USE HarborSupply;
GO

IF OBJECT_ID(N'dbo.fnSeedHash', N'FN') IS NOT NULL DROP FUNCTION dbo.fnSeedHash;
GO
CREATE FUNCTION dbo.fnSeedHash (@a INT, @b INT, @c INT)
RETURNS INT
AS
BEGIN
    -- first 4 bytes of MD5(a|b|c), masked non-negative
    RETURN CAST(CAST(HASHBYTES('MD5', CAST(@a AS BINARY(4)) + CAST(@b AS BINARY(4)) + CAST(@c AS BINARY(4))) AS BINARY(4)) AS INT)
           & 2147483647;
END;
GO

SET NOCOUNT ON;

IF EXISTS (SELECT 1 FROM dbo.Customer)
BEGIN
    PRINT N'Seed data already present - skipping.';
END
ELSE
BEGIN
    DECLARE @Start DATE = '2024-11-01';
    DECLARE @AsOf  DATE = '2026-10-31';

    ----------------------------------------------------------------------
    -- Numbers helper (1..10000)
    ----------------------------------------------------------------------
    CREATE TABLE #Nums (n INT NOT NULL PRIMARY KEY);
    ;WITH E1 AS (
        SELECT 1 AS x UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1
        UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1 UNION ALL SELECT 1
    ), E2 AS (SELECT 1 AS x FROM E1 a CROSS JOIN E1 b),
       E4 AS (SELECT 1 AS x FROM E2 a CROSS JOIN E2 b)
    INSERT #Nums (n)
    SELECT TOP (10000) CAST(ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS INT) FROM E4;

    ----------------------------------------------------------------------
    -- Customer (150)
    ----------------------------------------------------------------------
    INSERT dbo.Customer (CustomerId, CustomerCode, Name, City, State, PaymentTermsDays, CreatedDate, TaxId, CreditLimit)
    SELECT nm.n,
           'C' + RIGHT('0000' + CAST(nm.n AS VARCHAR(10)), 4),
           p.Word + ' ' + s.Word,
           c.City, c.State,
           CASE dbo.fnSeedHash(nm.n, 1, 0) % 10
                WHEN 0 THEN 15 WHEN 1 THEN 45 WHEN 2 THEN 45 WHEN 3 THEN 60 ELSE 30 END,
           DATEADD(DAY, -(30 + dbo.fnSeedHash(nm.n, 2, 0) % 1400), @Start),
           '9' + RIGHT('0' + CAST(nm.n % 10 AS VARCHAR(2)), 1) + '-' +
               RIGHT('0000000' + CAST(dbo.fnSeedHash(nm.n, 3, 0) % 10000000 AS VARCHAR(10)), 7),
           CAST((5 + dbo.fnSeedHash(nm.n, 4, 0) % 46) * 5000 AS DECIMAL(19,4))
    FROM #Nums nm
    JOIN (VALUES (0,'Anchor'),(1,'Bayview'),(2,'Cedar'),(3,'Driftwood'),(4,'Eastport'),
                 (5,'Fairwind'),(6,'Granite'),(7,'Harborlight'),(8,'Ironwood'),(9,'Juniper'),
                 (10,'Keystone'),(11,'Lakeshore'),(12,'Mariner'),(13,'Northgate'),(14,'Oakridge')
         ) p (i, Word) ON p.i = (nm.n - 1) % 15
    JOIN (VALUES (0,'Marine'),(1,'Builders'),(2,'Hardware'),(3,'Contractors'),(4,'Industrial'),
                 (5,'Mechanical'),(6,'Electric'),(7,'Fabrication'),(8,'Maintenance'),(9,'Facilities')
         ) s (i, Word) ON s.i = (nm.n - 1) / 15
    JOIN (VALUES (0,'Tampa','FL'),(1,'Orlando','FL'),(2,'Jacksonville','FL'),(3,'Miami','FL'),
                 (4,'Savannah','GA'),(5,'Atlanta','GA'),(6,'Charleston','SC'),(7,'Mobile','AL'),
                 (8,'Birmingham','AL'),(9,'New Orleans','LA'),(10,'Houston','TX'),(11,'Raleigh','NC')
         ) c (i, City, State) ON c.i = dbo.fnSeedHash(nm.n, 5, 0) % 12
    WHERE nm.n <= 150;

    ----------------------------------------------------------------------
    -- Vendor (40)
    ----------------------------------------------------------------------
    INSERT dbo.Vendor (VendorId, VendorCode, Name, City, State, PaymentTermsDays, TaxId, RemitBankAccount)
    SELECT nm.n,
           'V' + RIGHT('0000' + CAST(nm.n AS VARCHAR(10)), 4),
           p.Word + ' ' + s.Word,
           c.City, c.State,
           CASE dbo.fnSeedHash(nm.n, 31, 0) % 3 WHEN 0 THEN 30 WHEN 1 THEN 45 ELSE 60 END,
           '8' + RIGHT('0' + CAST(nm.n % 10 AS VARCHAR(2)), 1) + '-' +
               RIGHT('0000000' + CAST(dbo.fnSeedHash(nm.n, 32, 0) % 10000000 AS VARCHAR(10)), 7),
           'SYN-' + RIGHT('0000000000' + CAST(dbo.fnSeedHash(nm.n, 33, 0) % 1000000000 AS VARCHAR(12)), 10)
    FROM #Nums nm
    JOIN (VALUES (0,'Apex'),(1,'Beacon'),(2,'Continental'),(3,'Delta'),
                 (4,'Everline'),(5,'Frontier'),(6,'Global'),(7,'Highland')
         ) p (i, Word) ON p.i = (nm.n - 1) % 8
    JOIN (VALUES (0,'Wholesale'),(1,'Manufacturing'),(2,'Distribution'),(3,'Imports'),(4,'Supply Partners')
         ) s (i, Word) ON s.i = (nm.n - 1) / 8
    JOIN (VALUES (0,'Charlotte','NC'),(1,'Memphis','TN'),(2,'Dallas','TX'),(3,'Nashville','TN'),
                 (4,'Louisville','KY'),(5,'Columbus','OH'),(6,'Pittsburgh','PA'),(7,'Newark','NJ')
         ) c (i, City, State) ON c.i = dbo.fnSeedHash(nm.n, 34, 0) % 8
    WHERE nm.n <= 40;

    ----------------------------------------------------------------------
    -- Item (300; 8 categories x 5 nouns x grades)
    ----------------------------------------------------------------------
    INSERT dbo.Item (ItemId, Sku, Description, Category, ListPrice, UnitCost)
    SELECT nm.n,
           'HS-' + nn.CatCode + '-' + RIGHT('0000' + CAST(nm.n AS VARCHAR(10)), 4),
           g.Grade + ' ' + nn.Noun,
           nn.Category,
           pr.ListPrice,
           CAST(ROUND(pr.ListPrice * (55 + dbo.fnSeedHash(nm.n, 6, 0) % 26) / 100.0, 2) AS DECIMAL(19,4))
    FROM #Nums nm
    CROSS APPLY (SELECT CAST(5 + (dbo.fnSeedHash(nm.n, 7, 0) % 19500) / 100.0 AS DECIMAL(19,4)) AS ListPrice) pr
    JOIN (VALUES
        (0,0,'Fasteners','FAS','Hex Bolt'),(0,1,'Fasteners','FAS','Lag Screw'),(0,2,'Fasteners','FAS','Machine Screw'),
        (0,3,'Fasteners','FAS','Flat Washer'),(0,4,'Fasteners','FAS','Lock Nut'),
        (1,0,'Fittings','FIT','Elbow Fitting'),(1,1,'Fittings','FIT','Tee Fitting'),(1,2,'Fittings','FIT','Coupling'),
        (1,3,'Fittings','FIT','Reducer'),(1,4,'Fittings','FIT','Adapter'),
        (2,0,'Hand Tools','HND','Claw Hammer'),(2,1,'Hand Tools','HND','Adjustable Wrench'),(2,2,'Hand Tools','HND','Pliers'),
        (2,3,'Hand Tools','HND','Utility Knife'),(2,4,'Hand Tools','HND','Tape Measure'),
        (3,0,'Power Tools','PWR','Cordless Drill'),(3,1,'Power Tools','PWR','Circular Saw'),(3,2,'Power Tools','PWR','Angle Grinder'),
        (3,3,'Power Tools','PWR','Orbital Sander'),(3,4,'Power Tools','PWR','Impact Driver'),
        (4,0,'Safety Gear','SAF','Safety Glasses'),(4,1,'Safety Gear','SAF','Work Gloves'),(4,2,'Safety Gear','SAF','Hard Hat'),
        (4,3,'Safety Gear','SAF','Ear Protection'),(4,4,'Safety Gear','SAF','Respirator'),
        (5,0,'Electrical','ELE','Wire Spool'),(5,1,'Electrical','ELE','Outlet Box'),(5,2,'Electrical','ELE','Circuit Breaker'),
        (5,3,'Electrical','ELE','Conduit'),(5,4,'Electrical','ELE','Wire Connector'),
        (6,0,'Plumbing','PLM','Ball Valve'),(6,1,'Plumbing','PLM','PVC Pipe'),(6,2,'Plumbing','PLM','Faucet'),
        (6,3,'Plumbing','PLM','P-Trap'),(6,4,'Plumbing','PLM','Pipe Sealant'),
        (7,0,'Cleaning Supplies','CLN','Degreaser'),(7,1,'Cleaning Supplies','CLN','Shop Towels'),
        (7,2,'Cleaning Supplies','CLN','Floor Cleaner'),(7,3,'Cleaning Supplies','CLN','Trash Liner'),
        (7,4,'Cleaning Supplies','CLN','Disinfectant')
    ) nn (CatIdx, NounIdx, Category, CatCode, Noun)
        ON nn.CatIdx = (nm.n - 1) % 8 AND nn.NounIdx = ((nm.n - 1) / 8) % 5
    JOIN (VALUES (0,'Standard'),(1,'Pro'),(2,'Heavy Duty'),(3,'Compact'),
                 (4,'XL'),(5,'Bulk Pack'),(6,'Marine Grade'),(7,'Economy')
         ) g (i, Grade) ON g.i = (nm.n - 1) / 40
    WHERE nm.n <= 300;

    ----------------------------------------------------------------------
    -- Invoice plan: counts per month (growth + Q4 lift, soft Jan/Feb)
    ----------------------------------------------------------------------
    CREATE TABLE #Months (
        m INT NOT NULL PRIMARY KEY, MonthStart DATE NOT NULL, DaysInMonth INT NOT NULL,
        Cnt INT NOT NULL, CumStart INT NULL);
    INSERT #Months (m, MonthStart, DaysInMonth, Cnt)
    SELECT nm.n - 1,
           DATEADD(MONTH, nm.n - 1, @Start),
           DAY(EOMONTH(DATEADD(MONTH, nm.n - 1, @Start))),
           ((205 + 3 * (nm.n - 1)) *
            CASE MONTH(DATEADD(MONTH, nm.n - 1, @Start))
                 WHEN 10 THEN 130 WHEN 11 THEN 130 WHEN 12 THEN 130
                 WHEN 1 THEN 90 WHEN 2 THEN 90 ELSE 100 END) / 100
    FROM #Nums nm WHERE nm.n <= 24;

    UPDATE mo SET CumStart = (SELECT ISNULL(SUM(p.Cnt), 0) FROM #Months p WHERE p.m < mo.m)
    FROM #Months mo;

    CREATE TABLE #Inv (
        InvoiceId INT NOT NULL PRIMARY KEY, InvoiceDate DATE NOT NULL, CustomerId INT NOT NULL);
    INSERT #Inv (InvoiceId, InvoiceDate, CustomerId)
    SELECT CAST(ROW_NUMBER() OVER (ORDER BY d.InvoiceDate, d.Seq) AS INT), d.InvoiceDate, 0
    FROM (SELECT nm.n AS Seq,
                 DATEADD(DAY, dbo.fnSeedHash(nm.n, 8, 0) % mo.DaysInMonth, mo.MonthStart) AS InvoiceDate
          FROM #Nums nm
          JOIN #Months mo ON nm.n > mo.CumStart AND nm.n <= mo.CumStart + mo.Cnt) d;

    -- customer pick: min of three uniforms => a few big customers, long tail
    UPDATE i SET CustomerId = 1 + CASE WHEN r.a <= r.b AND r.a <= r.c THEN r.a
                                       WHEN r.b <= r.c THEN r.b ELSE r.c END
    FROM #Inv i
    CROSS APPLY (SELECT dbo.fnSeedHash(i.InvoiceId, 81, 0) % 150 AS a,
                        dbo.fnSeedHash(i.InvoiceId, 82, 0) % 150 AS b,
                        dbo.fnSeedHash(i.InvoiceId, 83, 0) % 150 AS c) r;

    -- shape Q2: shrinking customers hand invoices to rising customers 16..20
    UPDATE #Inv
    SET CustomerId = 16 + dbo.fnSeedHash(InvoiceId, 10, 0) % 5
    WHERE InvoiceDate >= '2026-01-01'
      AND CustomerId IN (2, 3, 5, 7, 9)
      AND dbo.fnSeedHash(InvoiceId, 9, 0) % 100 < 55;

    ----------------------------------------------------------------------
    -- Invoice lines (1..7 per invoice)
    ----------------------------------------------------------------------
    CREATE TABLE #Line (
        InvoiceId INT NOT NULL, [LineNo] INT NOT NULL, ItemId INT NOT NULL,
        Quantity INT NULL, UnitPrice DECIMAL(19,4) NULL, ExtendedAmount DECIMAL(19,4) NULL,
        PRIMARY KEY (InvoiceId, [LineNo]));
    INSERT #Line (InvoiceId, [LineNo], ItemId)
    SELECT i.InvoiceId, nm.n,
           1 + CASE WHEN r.a <= r.b THEN r.a ELSE r.b END
    FROM #Inv i
    JOIN #Nums nm ON nm.n <= 1 + (dbo.fnSeedHash(i.InvoiceId, 11, 0) % 7)
    CROSS APPLY (SELECT dbo.fnSeedHash(i.InvoiceId, nm.n, 84) % 300 AS a,
                        dbo.fnSeedHash(i.InvoiceId, nm.n, 85) % 300 AS b) r;

    -- shape Q4: from 2026-07-01, 80% of lines for items 3,9,14,22,31 move to items 50..300
    UPDATE l SET ItemId = 50 + dbo.fnSeedHash(l.InvoiceId, l.[LineNo], 86) % 251
    FROM #Line l
    JOIN #Inv i ON i.InvoiceId = l.InvoiceId
    WHERE i.InvoiceDate >= '2026-07-01'
      AND l.ItemId IN (3, 9, 14, 22, 31)
      AND dbo.fnSeedHash(l.InvoiceId, l.[LineNo], 87) % 100 < 80;

    -- quantity (1..20; x3 for the Q4-shape items during Jan-Jun 2026) and price (90..100% of list)
    UPDATE l SET Quantity = x.Qty, UnitPrice = x.Price, ExtendedAmount = x.Qty * x.Price
    FROM #Line l
    JOIN #Inv i ON i.InvoiceId = l.InvoiceId
    JOIN dbo.Item it ON it.ItemId = l.ItemId
    CROSS APPLY (SELECT
        (1 + dbo.fnSeedHash(l.InvoiceId, l.[LineNo], 13) % 20) *
            CASE WHEN l.ItemId IN (3, 9, 14, 22, 31)
                  AND i.InvoiceDate >= '2026-01-01' AND i.InvoiceDate < '2026-07-01' THEN 3 ELSE 1 END AS Qty,
        CAST(ROUND(it.ListPrice * (90 + dbo.fnSeedHash(l.InvoiceId, l.[LineNo], 12) % 11) / 100.0, 2)
             AS DECIMAL(19,4)) AS Price) x;

    ----------------------------------------------------------------------
    -- Customer payment plan
    ----------------------------------------------------------------------
    CREATE TABLE #Pay (
        InvoiceId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL, Total DECIMAL(19,4) NOT NULL,
        DueDate DATE NOT NULL, IsVoid BIT NOT NULL, PayDate DATE NULL, PayAmt DECIMAL(19,4) NULL);
    INSERT #Pay (InvoiceId, CustomerId, Total, DueDate, IsVoid)
    SELECT i.InvoiceId, i.CustomerId, t.Total,
           DATEADD(DAY, c.PaymentTermsDays, i.InvoiceDate),
           CASE WHEN dbo.fnSeedHash(i.InvoiceId, 14, 0) % 100 = 0 THEN 1 ELSE 0 END
    FROM #Inv i
    JOIN (SELECT InvoiceId, SUM(ExtendedAmount) AS Total FROM #Line GROUP BY InvoiceId) t ON t.InvoiceId = i.InvoiceId
    JOIN dbo.Customer c ON c.CustomerId = i.CustomerId;

    -- r < 4: never paid; 4..5: half paid; otherwise paid in full around the due date
    UPDATE p SET PayDate = DATEADD(DAY, (dbo.fnSeedHash(p.InvoiceId, 16, 0) % 26) - 8, p.DueDate),
                 PayAmt  = CASE WHEN x.r < 6 THEN ROUND(p.Total * 0.5, 2) ELSE p.Total END
    FROM #Pay p
    CROSS APPLY (SELECT dbo.fnSeedHash(p.InvoiceId, 15, 0) % 100 AS r) x
    WHERE p.IsVoid = 0 AND x.r >= 4;

    -- anything that would be paid after "today" is still open
    UPDATE #Pay SET PayDate = NULL, PayAmt = NULL WHERE PayDate > @AsOf;

    -- a payment = one customer paying on one date (may cover several invoices)
    CREATE TABLE #PayGroup (
        PaymentId INT NOT NULL PRIMARY KEY, CustomerId INT NOT NULL, PaymentDate DATE NOT NULL, Amount DECIMAL(19,4) NOT NULL);
    INSERT #PayGroup (PaymentId, CustomerId, PaymentDate, Amount)
    SELECT CAST(ROW_NUMBER() OVER (ORDER BY PayDate, CustomerId) AS INT), CustomerId, PayDate, SUM(PayAmt)
    FROM #Pay WHERE PayDate IS NOT NULL
    GROUP BY CustomerId, PayDate;

    ----------------------------------------------------------------------
    -- Bills (1,200 = 50 per month)
    ----------------------------------------------------------------------
    CREATE TABLE #Bill (
        BillId INT NOT NULL PRIMARY KEY, VendorId INT NOT NULL, BillDate DATE NOT NULL,
        DueDate DATE NULL, Amount DECIMAL(19,4) NULL, PayDate DATE NULL, PayAmt DECIMAL(19,4) NULL);
    INSERT #Bill (BillId, VendorId, BillDate)
    SELECT CAST(ROW_NUMBER() OVER (ORDER BY d.BillDate, d.Seq) AS INT), 0, d.BillDate
    FROM (SELECT nm.n AS Seq,
                 DATEADD(DAY, dbo.fnSeedHash(nm.n, 20, 0) % mo.DaysInMonth, mo.MonthStart) AS BillDate
          FROM #Nums nm
          JOIN #Months mo ON mo.m = (nm.n - 1) / 50
          WHERE nm.n <= 1200) d;

    UPDATE b SET VendorId = 1 + CASE WHEN r.a <= r.b THEN r.a ELSE r.b END
    FROM #Bill b
    CROSS APPLY (SELECT dbo.fnSeedHash(b.BillId, 88, 0) % 40 AS a,
                        dbo.fnSeedHash(b.BillId, 89, 0) % 40 AS b) r;

    UPDATE b SET DueDate = DATEADD(DAY, v.PaymentTermsDays, b.BillDate),
                 Amount  = CAST(((50000 + dbo.fnSeedHash(b.BillId, 23, 0) % 950000) / 100.0)
                                * (1 + b.VendorId % 4) AS DECIMAL(19,4))
    FROM #Bill b JOIN dbo.Vendor v ON v.VendorId = b.VendorId;

    -- normal case: paid in full within -10..+4 days of the due date
    UPDATE #Bill
    SET PayDate = DATEADD(DAY, (dbo.fnSeedHash(BillId, 22, 0) % 15) - 10, DueDate),
        PayAmt  = Amount;

    -- shape Q5: 40% of bills dated 35..170 days before today are left unpaid (ages the AP)
    UPDATE #Bill SET PayDate = NULL, PayAmt = NULL
    WHERE BillDate BETWEEN DATEADD(DAY, -170, @AsOf) AND DATEADD(DAY, -35, @AsOf)
      AND dbo.fnSeedHash(BillId, 21, 0) % 100 < 40;

    -- ...and a quarter of those have a half payment on file
    UPDATE #Bill SET PayDate = DATEADD(DAY, 5, DueDate), PayAmt = ROUND(Amount * 0.5, 2)
    WHERE PayDate IS NULL
      AND BillDate BETWEEN DATEADD(DAY, -170, @AsOf) AND DATEADD(DAY, -35, @AsOf)
      AND dbo.fnSeedHash(BillId, 21, 0) % 100 < 10
      AND DATEADD(DAY, 5, DueDate) <= @AsOf;

    UPDATE #Bill SET PayDate = NULL, PayAmt = NULL WHERE PayDate > @AsOf;

    ----------------------------------------------------------------------
    -- Final inserts in FK order
    ----------------------------------------------------------------------
    INSERT dbo.Invoice (InvoiceId, InvoiceNo, CustomerId, InvoiceDate, DueDate, Status)
    SELECT i.InvoiceId, 'INV-' + RIGHT('000000' + CAST(i.InvoiceId AS VARCHAR(10)), 6),
           i.CustomerId, i.InvoiceDate, p.DueDate,
           CASE WHEN p.IsVoid = 1 THEN 'Void' WHEN p.PayAmt = p.Total THEN 'Paid' ELSE 'Open' END
    FROM #Inv i JOIN #Pay p ON p.InvoiceId = i.InvoiceId;

    INSERT dbo.InvoiceLine (InvoiceLineId, InvoiceId, [LineNo], ItemId, Quantity, UnitPrice, ExtendedAmount)
    SELECT CAST(ROW_NUMBER() OVER (ORDER BY InvoiceId, [LineNo]) AS INT),
           InvoiceId, [LineNo], ItemId, Quantity, UnitPrice, ExtendedAmount
    FROM #Line;

    INSERT dbo.CustomerPayment (PaymentId, CustomerId, PaymentDate, Amount, Method)
    SELECT PaymentId, CustomerId, PaymentDate, Amount,
           CASE dbo.fnSeedHash(PaymentId, 30, 0) % 10
                WHEN 8 THEN 'Wire' WHEN 9 THEN 'Card' WHEN 4 THEN 'ACH' WHEN 5 THEN 'ACH'
                WHEN 6 THEN 'ACH' WHEN 7 THEN 'ACH' ELSE 'Check' END
    FROM #PayGroup;

    INSERT dbo.PaymentApplication (PaymentId, InvoiceId, AppliedAmount)
    SELECT g.PaymentId, p.InvoiceId, p.PayAmt
    FROM #Pay p JOIN #PayGroup g ON g.CustomerId = p.CustomerId AND g.PaymentDate = p.PayDate
    WHERE p.PayDate IS NOT NULL;

    INSERT dbo.Bill (BillId, BillNo, VendorId, BillDate, DueDate, Amount, Status)
    SELECT BillId, 'BILL-' + RIGHT('000000' + CAST(BillId AS VARCHAR(10)), 6),
           VendorId, BillDate, DueDate, Amount,
           CASE WHEN PayAmt = Amount THEN 'Paid' ELSE 'Open' END
    FROM #Bill;

    INSERT dbo.BillPayment (BillPaymentId, BillId, PaymentDate, Amount)
    SELECT CAST(ROW_NUMBER() OVER (ORDER BY PayDate, BillId) AS INT), BillId, PayDate, PayAmt
    FROM #Bill WHERE PayDate IS NOT NULL;

    DROP TABLE #Bill;
    DROP TABLE #PayGroup;
    DROP TABLE #Pay;
    DROP TABLE #Line;
    DROP TABLE #Inv;
    DROP TABLE #Months;
    DROP TABLE #Nums;
END
GO

DROP FUNCTION dbo.fnSeedHash;
GO
