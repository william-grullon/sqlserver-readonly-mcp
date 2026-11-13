namespace HarborSupply.Db.Tests;

public class SeedTests
{
    [Fact]
    public void CompatibilityLevel_Is110()
    {
        using var cn = Db.OpenAdmin();
        Assert.Equal(110, Db.Scalar<int>(cn, "SELECT compatibility_level FROM sys.databases WHERE name = N'HarborSupply'"));
    }

    [Theory]
    [InlineData("dbo.Customer", 150, 150)]
    [InlineData("dbo.Vendor", 40, 40)]
    [InlineData("dbo.Item", 300, 300)]
    [InlineData("dbo.Invoice", 5500, 6700)]
    [InlineData("dbo.InvoiceLine", 20000, 28000)]
    [InlineData("dbo.CustomerPayment", 4500, 6000)]
    [InlineData("dbo.PaymentApplication", 5000, 6300)]
    [InlineData("dbo.Bill", 1200, 1200)]
    [InlineData("dbo.BillPayment", 950, 1150)]
    public void RowCounts_AreInExpectedRange(string table, int min, int max)
    {
        using var cn = Db.OpenAdmin();
        var n = Db.Scalar<int>(cn, $"SELECT COUNT(*) FROM {table}");
        Assert.InRange(n, min, max);
    }

    [Fact]
    public void Data_StaysInsideTheSeedWindow()
    {
        using var cn = Db.OpenAdmin();
        Assert.Equal("2024-11-01", Db.Scalar<DateTime>(cn, "SELECT MIN(InvoiceDate) FROM dbo.Invoice").ToString("yyyy-MM-dd"));
        Assert.Equal("2026-10-31", Db.Scalar<DateTime>(cn, "SELECT MAX(InvoiceDate) FROM dbo.Invoice").ToString("yyyy-MM-dd"));
        Assert.Equal(0, Db.Scalar<int>(cn, "SELECT COUNT(*) FROM dbo.CustomerPayment WHERE PaymentDate > '2026-10-31'"));
        Assert.Equal(0, Db.Scalar<int>(cn, "SELECT COUNT(*) FROM dbo.BillPayment WHERE PaymentDate > '2026-10-31'"));
        Assert.Equal("2026-10-31", Db.Scalar<DateTime>(cn, "SELECT AsOfDate FROM reporting.vReportingCalendar").ToString("yyyy-MM-dd"));
    }

    /// <summary>
    /// The seed uses only CHECKSUM-based pseudo-randomness, so a fresh volume, a second run of
    /// db-init and another machine must all produce exactly these fixed values.
    /// </summary>
    [Fact]
    public void Seed_IsDeterministic_FixedFingerprint()
    {
        using var cn = Db.OpenAdmin();
        Assert.Equal(SeedFingerprint.InvoiceCount, Db.Scalar<int>(cn, "SELECT COUNT(*) FROM dbo.Invoice"));
        Assert.Equal(SeedFingerprint.InvoiceLineTotal,
            Db.Scalar<decimal>(cn, "SELECT SUM(ExtendedAmount) FROM dbo.InvoiceLine"));
        Assert.Equal(SeedFingerprint.InvoiceChecksum,
            Db.Scalar<int>(cn, "SELECT CHECKSUM_AGG(CHECKSUM(InvoiceId, CustomerId, DATEDIFF(DAY, '2000-01-01', InvoiceDate))) FROM dbo.Invoice"));
        Assert.Equal(SeedFingerprint.BillTotal, Db.Scalar<decimal>(cn, "SELECT SUM(Amount) FROM dbo.Bill"));
    }

    [Fact]
    public void Seed_ReportingTotalsMatchBaseTables()
    {
        using var admin = Db.OpenAdmin();
        using var reader = Db.OpenReader();
        var viaView = Db.Scalar<decimal>(reader, "SELECT SUM(InvoiceTotal) FROM reporting.vSalesInvoices");
        var viaBase = Db.Scalar<decimal>(admin,
            "SELECT SUM(l.ExtendedAmount) FROM dbo.InvoiceLine l JOIN dbo.Invoice i ON i.InvoiceId = l.InvoiceId WHERE i.Status <> 'Void'");
        Assert.Equal(viaBase, viaView);
        Assert.Equal(SeedFingerprint.SalesInvoiceTotal, viaView);
    }
}

/// <summary>Reference values captured from the first verified build of db/03-seed.sql.</summary>
internal static class SeedFingerprint
{
    public const int InvoiceCount = 6075;
    public const decimal InvoiceLineTotal = 23902023.4000m;
    public const int InvoiceChecksum = 10729;
    public const decimal BillTotal = 15633431.9100m;
    public const decimal SalesInvoiceTotal = 23669600.1700m;
}
