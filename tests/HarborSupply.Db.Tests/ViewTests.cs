namespace HarborSupply.Db.Tests;

public class ViewTests
{
    public static readonly string[] ReportingViews =
    {
        "vReportingCalendar", "vCustomers", "vItems", "vVendors",
        "vSalesInvoices", "vSalesInvoiceLines", "vCustomerPayments", "vVendorBills"
    };

    public static IEnumerable<object[]> ViewData() => ReportingViews.Select(v => new object[] { v });

    private static readonly string[] SensitiveColumns = { "TaxId", "CreditLimit", "UnitCost", "RemitBankAccount" };

    [Fact]
    public void ReportingSchema_ContainsExactlyTheAllowlistedViews()
    {
        using var cn = Db.OpenAdmin();
        var actual = Db.Strings(cn,
            "SELECT v.name FROM sys.views v JOIN sys.schemas s ON s.schema_id = v.schema_id WHERE s.name = N'reporting'");
        Assert.Equal(ReportingViews.OrderBy(x => x), actual.OrderBy(x => x));
        // and nothing but views lives there
        Assert.Equal(0, Db.Scalar<int>(cn,
            "SELECT COUNT(*) FROM sys.objects o JOIN sys.schemas s ON s.schema_id = o.schema_id WHERE s.name = N'reporting' AND o.type <> 'V'"));
    }

    [Theory]
    [MemberData(nameof(ViewData))]
    public void EveryReportingView_ReturnsRows_AsMcpReader(string view)
    {
        using var cn = Db.OpenReader();
        Assert.True(Db.Scalar<int>(cn, $"SELECT COUNT(*) FROM reporting.{view}") > 0, $"{view} returned no rows");
    }

    [Fact]
    public void SensitiveColumns_ExistInBaseTables_SoTheNextTestIsNotVacuous()
    {
        using var cn = Db.OpenAdmin();
        var found = Db.Strings(cn,
            @"SELECT DISTINCT c.name FROM sys.columns c JOIN sys.tables t ON t.object_id = c.object_id
              JOIN sys.schemas s ON s.schema_id = t.schema_id
              WHERE s.name = N'dbo' AND c.name IN (N'TaxId', N'CreditLimit', N'UnitCost', N'RemitBankAccount')");
        Assert.Equal(SensitiveColumns.OrderBy(x => x), found.OrderBy(x => x));
    }

    [Fact]
    public void SensitiveColumns_AppearInNoReportingView()
    {
        using var cn = Db.OpenAdmin();
        var leaked = Db.Strings(cn,
            @"SELECT v.name + N'.' + c.name FROM sys.columns c
              JOIN sys.views v ON v.object_id = c.object_id
              JOIN sys.schemas s ON s.schema_id = v.schema_id
              WHERE s.name = N'reporting' AND c.name IN (N'TaxId', N'CreditLimit', N'UnitCost', N'RemitBankAccount')");
        Assert.Empty(leaked);
    }

    [Theory]
    [InlineData("TaxId", "reporting.vCustomers")]
    [InlineData("CreditLimit", "reporting.vCustomers")]
    [InlineData("UnitCost", "reporting.vItems")]
    [InlineData("TaxId", "reporting.vVendors")]
    [InlineData("RemitBankAccount", "reporting.vVendors")]
    public void SensitiveColumn_IsUnreachable_AsMcpReader(string column, string view)
    {
        using var cn = Db.OpenReader();
        var ex = Db.Denied(cn, $"SELECT TOP 1 {column} FROM {view}");
        Assert.Equal(207, ex.Number); // invalid column name
    }

    // ---- the seed really contains the shapes the five business questions rely on ----

    [Fact]
    public void Shape_SalesExistForEveryMonthOfTheLast12()
    {
        using var cn = Db.OpenReader();
        var months = Db.Scalar<int>(cn,
            @"SELECT COUNT(DISTINCT DATEFROMPARTS(YEAR(InvoiceDate), MONTH(InvoiceDate), 1))
              FROM reporting.vSalesInvoices WHERE InvoiceDate >= '2025-11-01'");
        Assert.Equal(12, months);
    }

    [Fact]
    public void Shape_SomeTopCustomersShrinkYearOverYear()
    {
        using var cn = Db.OpenReader();
        var shrunk = Db.Scalar<int>(cn,
            @"WITH r AS (
                SELECT CustomerId,
                       SUM(CASE WHEN InvoiceDate >= '2026-01-01' THEN InvoiceTotal ELSE 0 END) AS ThisYtd,
                       SUM(CASE WHEN InvoiceDate >= '2025-01-01' AND InvoiceDate <= '2025-10-31' THEN InvoiceTotal ELSE 0 END) AS LastYtd
                FROM reporting.vSalesInvoices GROUP BY CustomerId)
              SELECT COUNT(*) FROM r WHERE CustomerId IN (2,3,5,7,9) AND ThisYtd < 0.75 * LastYtd");
        Assert.True(shrunk >= 3, $"expected several shrinking top customers, got {shrunk}");
    }

    [Fact]
    public void Shape_PastDueTailExists()
    {
        using var cn = Db.OpenReader();
        var n = Db.Scalar<int>(cn, "SELECT COUNT(*) FROM reporting.vSalesInvoices WHERE DaysPastDue > 60");
        Assert.InRange(n, 100, 500);   // visible, but under the default 500-row cap
    }

    [Fact]
    public void Shape_FiveItemsHaveTheLargestUnitDrop_Q2To_Q3_2026()
    {
        using var cn = Db.OpenReader();
        var top = Db.Strings(cn,
            @"WITH q AS (
                SELECT ItemId,
                       SUM(CASE WHEN InvoiceDate >= '2026-04-01' AND InvoiceDate < '2026-07-01' THEN Quantity ELSE 0 END) AS Q2,
                       SUM(CASE WHEN InvoiceDate >= '2026-07-01' AND InvoiceDate < '2026-10-01' THEN Quantity ELSE 0 END) AS Q3
                FROM reporting.vSalesInvoiceLines GROUP BY ItemId)
              SELECT TOP 5 CAST(ItemId AS VARCHAR(10)) FROM q ORDER BY Q3 - Q2, ItemId");
        Assert.Equal(new[] { "14", "22", "3", "31", "9" }, top.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void Shape_OpenBillsSpanEveryAgingBucket()
    {
        using var cn = Db.OpenReader();
        using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
            @"SELECT CASE WHEN DaysPastDue = 0 THEN '0 current'
                          WHEN DaysPastDue <= 30 THEN '1 1-30'
                          WHEN DaysPastDue <= 60 THEN '2 31-60'
                          WHEN DaysPastDue <= 90 THEN '3 61-90'
                          ELSE '4 90+' END AS Bucket, COUNT(*) AS N
              FROM reporting.vVendorBills WHERE BalanceDue > 0 GROUP BY
                   CASE WHEN DaysPastDue = 0 THEN '0 current'
                          WHEN DaysPastDue <= 30 THEN '1 1-30'
                          WHEN DaysPastDue <= 60 THEN '2 31-60'
                          WHEN DaysPastDue <= 90 THEN '3 61-90'
                          ELSE '4 90+' END", cn);
        using var rd = cmd.ExecuteReader();
        var buckets = new Dictionary<string, int>();
        while (rd.Read()) buckets[rd.GetString(0)] = rd.GetInt32(1);
        Assert.Equal(5, buckets.Count);
        Assert.All(buckets.Values, n => Assert.True(n >= 5, "each bucket should have a meaningful number of bills"));
    }
}
