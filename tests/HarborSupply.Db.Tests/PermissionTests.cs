namespace HarborSupply.Db.Tests;

/// <summary>
/// Proves the database is the real control. Every denial is asserted as SQL error 229
/// (permission denied) so a syntax error can never pass for a security success.
/// </summary>
public class PermissionTests
{
    private const int PermissionDenied = 229;

    public static readonly string[] DboTables =
    {
        "Customer", "Vendor", "Item", "Invoice", "InvoiceLine",
        "CustomerPayment", "PaymentApplication", "Bill", "BillPayment"
    };

    public static IEnumerable<object[]> DboTableData() => DboTables.Select(t => new object[] { t });

    [Fact]
    public void DboTableList_CoversEveryBaseTable()
    {
        using var cn = Db.OpenAdmin();
        var actual = Db.Strings(cn, "SELECT t.name FROM sys.tables t JOIN sys.schemas s ON s.schema_id = t.schema_id WHERE s.name = N'dbo'");
        Assert.Equal(DboTables.OrderBy(x => x), actual.OrderBy(x => x));
    }

    [Theory]
    [MemberData(nameof(DboTableData))]
    public void Select_OnDboTable_IsDenied(string table)
    {
        using var cn = Db.OpenReader();
        var ex = Db.Denied(cn, $"SELECT TOP 1 * FROM dbo.{table}");
        Assert.Equal(PermissionDenied, ex.Number);
    }

    [Theory]
    [InlineData("INSERT INTO dbo.Customer (CustomerId, CustomerCode, Name, City, State, PaymentTermsDays, CreatedDate, TaxId, CreditLimit) VALUES (9999, 'X9999', 'x', 'x', 'FL', 30, '2024-01-01', 'x', 0)")]
    [InlineData("UPDATE dbo.Customer SET Name = 'x' WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.Customer WHERE 1 = 0")]
    [InlineData("INSERT INTO dbo.Bill (BillId, BillNo, VendorId, BillDate, DueDate, Amount, Status) VALUES (999999, 'x', 1, '2024-01-01', '2024-01-01', 1, 'Open')")]
    [InlineData("UPDATE dbo.Invoice SET Status = 'Paid' WHERE 1 = 0")]
    [InlineData("DELETE FROM dbo.InvoiceLine WHERE 1 = 0")]
    public void Writes_OnDbo_AreDenied(string sql)
    {
        using var cn = Db.OpenReader();
        Assert.Equal(PermissionDenied, Db.Denied(cn, sql).Number);
    }

    // vCustomers is a simple single-table view (updatable in principle), so it is the strictest case.
    [Theory]
    [InlineData("INSERT INTO reporting.vCustomers (CustomerId, CustomerCode, Name, City, State, PaymentTermsDays, CreatedDate) VALUES (9999, 'X9999', 'x', 'x', 'FL', 30, '2024-01-01')")]
    [InlineData("UPDATE reporting.vCustomers SET Name = 'x' WHERE 1 = 0")]
    [InlineData("DELETE FROM reporting.vCustomers WHERE 1 = 0")]
    [InlineData("UPDATE reporting.vItems SET ListPrice = 0 WHERE 1 = 0")]
    [InlineData("DELETE FROM reporting.vCustomerPayments WHERE 1 = 0")]   // single-table, so it reaches the permission check
    public void Writes_OnReportingView_AreDenied(string sql)
    {
        using var cn = Db.OpenReader();
        Assert.Equal(PermissionDenied, Db.Denied(cn, sql).Number);
    }

    [Theory]
    [InlineData("CREATE TABLE dbo.Evil (id INT)")]
    [InlineData("CREATE VIEW reporting.vEvil AS SELECT 1 AS x")]
    [InlineData("DROP VIEW reporting.vCustomers")]
    [InlineData("TRUNCATE TABLE dbo.BillPayment")]
    public void Ddl_IsDenied(string sql)
    {
        using var cn = Db.OpenReader();
        var ex = Db.Denied(cn, sql);
        // Every one of these is a genuine denial; syntax/binding errors (102, 156, 4405 ...) are not in the list.
        //   229  permission denied on object        262  CREATE permission denied in database
        //   1088 cannot find object / no permission 3701 cannot drop, does not exist or no permission
        Assert.Contains(ex.Number, new[] { PermissionDenied, 262, 1088, 3701 });
    }

    [Fact]
    public void ReportingViews_AreReadableThroughOwnershipChaining()
    {
        using var cn = Db.OpenReader();
        // joins dbo tables underneath; works only because owner (dbo) is the same all the way down
        Assert.True(Db.Scalar<int>(cn, "SELECT COUNT(*) FROM reporting.vSalesInvoiceLines") > 0);
    }

    [Fact]
    public void McpReader_IsInNoRoleBeyondPublic()
    {
        using var cn = Db.OpenAdmin();
        Assert.Equal(0, Db.Scalar<int>(cn,
            @"SELECT COUNT(*) FROM sys.database_role_members rm
              JOIN sys.database_principals m ON m.principal_id = rm.member_principal_id
              WHERE m.name = N'mcp_reader'"));
        Assert.Equal(0, Db.Scalar<int>(cn,
            @"SELECT COUNT(*) FROM sys.server_role_members rm
              JOIN sys.server_principals m ON m.principal_id = rm.member_principal_id
              WHERE m.name = N'mcp_reader'"));
    }

}
