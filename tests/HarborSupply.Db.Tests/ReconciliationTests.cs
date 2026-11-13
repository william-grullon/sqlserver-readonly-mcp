using System.Globalization;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Client;

namespace HarborSupply.Db.Tests;

/// <summary>
/// Phase 4. Each of the five business questions has a reference report (db/reports/qN.sql, written against the
/// reporting views). These tests prove the report is right by comparing it, cell for cell, with an independent
/// calculation written directly against the dbo base tables (as sa, with literal dates, no views, no calendar view).
/// The report is executed through the real MCP server over stdio, so it also proves the guard accepts it and that
/// the run_query tool returns exactly the reference answer.
/// </summary>
public class ReconciliationTests
{
    // Independent references: base tables only, literal dates (AsOfDate = 2026-10-31), different query shapes.
    private const string Ref1 = @"
SELECT CONVERT(CHAR(10), DATEFROMPARTS(YEAR(i.InvoiceDate), MONTH(i.InvoiceDate), 1), 23) AS SalesMonth,
       COUNT(DISTINCT i.InvoiceId) AS InvoiceCount, SUM(l.ExtendedAmount) AS TotalSales
FROM dbo.Invoice i JOIN dbo.InvoiceLine l ON l.InvoiceId = i.InvoiceId
WHERE i.Status IN ('Open','Paid') AND i.InvoiceDate BETWEEN '2025-11-01' AND '2026-10-31'
GROUP BY YEAR(i.InvoiceDate), MONTH(i.InvoiceDate)
ORDER BY 1";

    private const string Ref2 = @"
SELECT TOP (10) c.CustomerCode, c.Name,
       SUM(CASE WHEN i.InvoiceDate BETWEEN '2026-01-01' AND '2026-10-31' THEN l.ExtendedAmount ELSE 0 END) AS RevenueThisYear,
       SUM(CASE WHEN i.InvoiceDate BETWEEN '2025-01-01' AND '2025-10-31' THEN l.ExtendedAmount ELSE 0 END) AS RevenueLastYear
FROM dbo.Customer c JOIN dbo.Invoice i ON i.CustomerId = c.CustomerId JOIN dbo.InvoiceLine l ON l.InvoiceId = i.InvoiceId
WHERE i.Status <> 'Void'
GROUP BY c.CustomerCode, c.Name
ORDER BY 3 DESC, c.CustomerCode";

    private const string Ref3 = @"
SELECT i.InvoiceNo,
       t.Total - ISNULL((SELECT SUM(a.AppliedAmount) FROM dbo.PaymentApplication a WHERE a.InvoiceId = i.InvoiceId), 0) AS BalanceDue
FROM dbo.Invoice i
CROSS APPLY (SELECT SUM(ExtendedAmount) AS Total FROM dbo.InvoiceLine WHERE InvoiceId = i.InvoiceId) t
WHERE i.Status <> 'Void'
  AND i.DueDate < '2026-09-01'
  AND t.Total - ISNULL((SELECT SUM(a.AppliedAmount) FROM dbo.PaymentApplication a WHERE a.InvoiceId = i.InvoiceId), 0) > 0
ORDER BY i.InvoiceNo";

    private const string Ref4 = @"
SELECT TOP (5) it.Sku,
       SUM(CASE WHEN i.InvoiceDate BETWEEN '2026-04-01' AND '2026-06-30' THEN l.Quantity ELSE 0 END) AS UnitsPriorQuarter,
       SUM(CASE WHEN i.InvoiceDate BETWEEN '2026-07-01' AND '2026-09-30' THEN l.Quantity ELSE 0 END) AS UnitsLastQuarter
FROM dbo.Item it JOIN dbo.InvoiceLine l ON l.ItemId = it.ItemId JOIN dbo.Invoice i ON i.InvoiceId = l.InvoiceId
WHERE i.Status <> 'Void' AND i.InvoiceDate BETWEEN '2026-04-01' AND '2026-09-30'
GROUP BY it.Sku
ORDER BY SUM(CASE WHEN i.InvoiceDate BETWEEN '2026-07-01' AND '2026-09-30' THEN l.Quantity ELSE 0 END)
       - SUM(CASE WHEN i.InvoiceDate BETWEEN '2026-04-01' AND '2026-06-30' THEN l.Quantity ELSE 0 END), it.Sku";

    private const string Ref5 = @"
SELECT v.VendorCode,
       SUM(CASE WHEN b.DueDate >= '2026-10-31' THEN b.Owed ELSE 0 END)                               AS Current_,
       SUM(CASE WHEN b.DueDate <  '2026-10-31' AND b.DueDate >= '2026-10-01' THEN b.Owed ELSE 0 END) AS Days1To30,
       SUM(CASE WHEN b.DueDate <  '2026-10-01' AND b.DueDate >= '2026-09-01' THEN b.Owed ELSE 0 END) AS Days31To60,
       SUM(CASE WHEN b.DueDate <  '2026-09-01' AND b.DueDate >= '2026-08-02' THEN b.Owed ELSE 0 END) AS Days61To90,
       SUM(CASE WHEN b.DueDate <  '2026-08-02' THEN b.Owed ELSE 0 END)                               AS Over90,
       SUM(b.Owed) AS TotalOwed
FROM (SELECT bl.VendorId, bl.DueDate,
             bl.Amount - ISNULL((SELECT SUM(p.Amount) FROM dbo.BillPayment p WHERE p.BillId = bl.BillId), 0) AS Owed
      FROM dbo.Bill bl) b
JOIN dbo.Vendor v ON v.VendorId = b.VendorId
WHERE b.Owed > 0
GROUP BY v.VendorCode
ORDER BY SUM(b.Owed) DESC, v.VendorCode";

    // question number, reference SQL, report columns to compare, whether row order is significant
    public static IEnumerable<object[]> Questions() =>
    [
        [1, Ref1, new[] { "SalesMonth", "InvoiceCount", "TotalSales" }, true],
        [2, Ref2, new[] { "CustomerCode", "Name", "RevenueThisYear", "RevenueLastYear" }, true],
        [3, Ref3, new[] { "InvoiceNo", "BalanceDue" }, false],
        [4, Ref4, new[] { "Sku", "UnitsPriorQuarter", "UnitsLastQuarter" }, true],
        [5, Ref5, new[] { "VendorCode", "Current_", "Days1To30", "Days31To60", "Days61To90", "Over90", "TotalOwed" }, true],
    ];

    [Theory]
    [MemberData(nameof(Questions))]
    public async Task ReportViaMcp_MatchesIndependentBaseTableCalculation(int q, string referenceSql, string[] columns, bool ordered)
    {
        var (names, rows) = await RunReportViaMcpAsync(q);
        var idx = columns.Select(c => Array.IndexOf(names, c)).ToArray();
        Assert.DoesNotContain(-1, idx);
        var actual = rows.Select(r => idx.Select(i => r[i]).ToArray()).ToList();

        using var cn = Db.OpenAdmin();
        using var cmd = new SqlCommand(referenceSql, cn);
        using var rd = cmd.ExecuteReader();
        var expected = new List<string[]>();
        while (rd.Read())
            expected.Add(Enumerable.Range(0, rd.FieldCount).Select(i => Norm(rd.GetValue(i))).ToArray());

        Assert.NotEmpty(expected);
        if (!ordered)
        {
            actual = actual.OrderBy(r => r[0], StringComparer.Ordinal).ToList();
            expected = expected.OrderBy(r => r[0], StringComparer.Ordinal).ToList();
        }
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
            Assert.True(expected[i].SequenceEqual(actual[i]),
                $"Q{q} row {i}: reference [{string.Join(", ", expected[i])}] vs report [{string.Join(", ", actual[i])}]");
    }

    // Headline answers, pinned. These are the numbers quoted in the README reconciliation table.
    [Fact]
    public async Task HeadlineAnswers_AreKnownConstants()
    {
        var (n1, r1) = await RunReportViaMcpAsync(1);
        Assert.Equal(12, r1.Count);
        Assert.Equal("2025-11-01", r1[0][Array.IndexOf(n1, "SalesMonth")]);
        Assert.Equal("2026-10-01", r1[^1][Array.IndexOf(n1, "SalesMonth")]);
        Assert.Equal(Headline.Q1Total, Sum(r1, n1, "TotalSales"));

        var (n2, r2) = await RunReportViaMcpAsync(2);
        Assert.Equal(10, r2.Count);
        Assert.Equal("C0020", r2[0][Array.IndexOf(n2, "CustomerCode")]);

        var (n3, r3) = await RunReportViaMcpAsync(3);
        Assert.Equal(Headline.Q3Count, r3.Count);
        Assert.Equal(Headline.Q3Count.ToString(), r3[0][Array.IndexOf(n3, "InvoiceCountOver60")]);
        Assert.Equal(Headline.Q3Total, decimal.Parse(r3[0][Array.IndexOf(n3, "TotalBalanceDue")], CultureInfo.InvariantCulture));
        Assert.All(r3, r => Assert.True(int.Parse(r[Array.IndexOf(n3, "DaysPastDue")]) > 60));

        var (n4, r4) = await RunReportViaMcpAsync(4);
        Assert.Equal(Headline.Q4Skus, r4.Select(r => r[Array.IndexOf(n4, "Sku")]).ToArray());

        var (n5, r5) = await RunReportViaMcpAsync(5);
        Assert.Equal(Headline.Q5Total, Sum(r5, n5, "TotalOwed"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ReportFiles_AreSingleGuardApprovedSelects_WithNoNonDeterministicFunctions(int q)
    {
        var sql = File.ReadAllText(ReportPath(q));
        ReportingTools.Validate(sql);
        foreach (var banned in new[] { "GETDATE", "SYSDATETIME", "CURRENT_TIMESTAMP", "NEWID", "RAND(" })
            Assert.DoesNotContain(banned, sql, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers ----------------------------------------------------------------------------------------------

    private static decimal Sum(List<string[]> rows, string[] names, string col) =>
        rows.Sum(r => decimal.Parse(r[Array.IndexOf(names, col)], CultureInfo.InvariantCulture));

    private static async Task<(string[] Columns, List<string[]> Rows)> RunReportViaMcpAsync(int q)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "HarborSupply.Mcp.dll");
        var audit = Path.Combine(RepoRoot(), "logs", $"test-recon-q{q}.jsonl");
        if (File.Exists(audit)) File.Delete(audit);
        await using var client = await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "harbor",
            Command = "dotnet",
            Arguments = [dll],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HARBOR_MCP_CONNECTION"] = Db.ReaderConnectionString,
                ["HARBOR_MCP_AUDIT_LOG"] = audit
            }
        }));
        var result = await client.CallToolAsync("run_query", new Dictionary<string, object?> { ["sql"] = File.ReadAllText(ReportPath(q)) });
        Assert.NotEqual(true, result.IsError);
        var text = string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        Assert.False(root.GetProperty("truncated").GetBoolean());
        var cols = root.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToArray();
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(r => r.EnumerateArray().Select(CellToString).ToArray()).ToList();
        return (cols, rows);
    }

    private static string CellToString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l.ToString(CultureInfo.InvariantCulture) : Norm(e.GetDecimal()),
        JsonValueKind.String => NormString(e.GetString()!),
        JsonValueKind.Null => "NULL",
        _ => e.ToString()
    };

    // Money (decimal) -> 2 dp; dates -> yyyy-MM-dd; whole numbers stay as they are.
    private static string Norm(object v) => v switch
    {
        decimal d => d.ToString("F2", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DBNull => "NULL",
        string s => NormString(s),
        _ => Convert.ToString(v, CultureInfo.InvariantCulture)!
    };

    private static string NormString(string s) => s.Length >= 10 && s[4] == '-' && s[7] == '-' ? s[..10] : s;

    private static string ReportPath(int q) => Path.Combine(RepoRoot(), "db", "reports", $"q{q}.sql");

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "HarborSupply.sln"))) d = d.Parent;
        return d!.FullName;
    }

    private static class Headline
    {
        public const decimal Q1Total = 12888296.32m;
        public const int Q3Count = 312;
        public const decimal Q3Total = 1012732.00m;
        public static readonly string[] Q4Skus = ["HS-PLM-0031", "HS-FAS-0009", "HS-ELE-0022", "HS-HND-0003", "HS-ELE-0014"];
        public const decimal Q5Total = 1770371.07m;
    }
}
