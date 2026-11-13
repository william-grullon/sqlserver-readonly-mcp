using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HarborSupply.Db.Tests;

public class McpTests
{
    [Theory]
    [InlineData("DELETE FROM reporting.vCustomers")]
    [InlineData("UPDATE reporting.vCustomers SET Name = 'x'")]
    [InlineData("INSERT INTO reporting.vCustomers (CustomerId) VALUES (1)")]
    [InlineData("MERGE INTO reporting.vCustomers AS t USING reporting.vCustomers AS s ON 1=0 WHEN NOT MATCHED THEN INSERT (CustomerId) VALUES (1);")]
    [InlineData("CREATE TABLE dbo.Evil (Id INT)")]
    [InlineData("DROP VIEW reporting.vCustomers")]
    [InlineData("ALTER VIEW reporting.vCustomers AS SELECT 1 AS A")]
    [InlineData("TRUNCATE TABLE dbo.Customer")]
    [InlineData("GRANT SELECT ON SCHEMA::dbo TO mcp_reader")]
    [InlineData("SELECT * FROM dbo.Customer")]
    [InlineData("SELECT * FROM sys.tables")]
    [InlineData("SELECT * FROM INFORMATION_SCHEMA.TABLES")]
    [InlineData("SELECT * FROM reporting.vNonExistent")]
    [InlineData("SELECT * FROM vCustomers")]
    [InlineData("SELECT * FROM HarborSupply.reporting.vCustomers")]
    [InlineData("SELECT * FROM Server.HarborSupply.reporting.vCustomers")]
    [InlineData("WITH c AS (SELECT * FROM dbo.Customer) SELECT * FROM c")]
    [InlineData("SELECT * FROM (SELECT * FROM dbo.Vendor) d")]
    [InlineData("SELECT * FROM reporting.vCustomers WHERE CustomerId IN (SELECT CustomerId FROM dbo.Customer)")]
    [InlineData("SELECT (SELECT TOP 1 TaxId FROM dbo.Customer) FROM reporting.vReportingCalendar")]
    [InlineData("SELECT * FROM reporting.vCustomers UNION ALL SELECT * FROM dbo.Customer")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT 1; EXEC sp_who")]
    [InlineData("SELECT 1\nGO\nSELECT 2")]
    [InlineData("SELECT * INTO #x FROM reporting.vCustomers")]
    [InlineData("SELECT * INTO dbo.Leaked FROM reporting.vCustomers")]
    [InlineData("EXEC sp_who")]
    [InlineData("EXECUTE ('SELECT 1')")]
    [InlineData("WAITFOR DELAY '00:00:05'")]
    [InlineData("DECLARE @x INT = 1; SELECT @x")]
    [InlineData("SELECT * FROM OPENROWSET('SQLNCLI', 'Server=localhost;Trusted_Connection=yes;', 'SELECT 1')")]
    [InlineData("SELECT * FROM fn_my_permissions(NULL, 'DATABASE')")]
    [InlineData("SELECT * FROM reporting.vCustomers WITH (NOLOCK)")]
    [InlineData("SELECT * FROM reporting.vCustomers FOR XML RAW")]
    [InlineData("SELECT dbo.fnEvil(1) FROM reporting.vReportingCalendar")]
    [InlineData("SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM reporting.vCustomers")] // post-2012 syntax
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SELECT FROM")]
    public void GuardRejects(string sql) =>
        Assert.Throws<ArgumentException>(() => ReportingTools.Validate(sql));

    [Theory]
    [InlineData("SELECT AsOfDate FROM reporting.vReportingCalendar")]
    [InlineData("SELECT TOP 10 CustomerId, Name FROM reporting.vCustomers ORDER BY CustomerId")]
    [InlineData("WITH c AS (SELECT CustomerId, Name FROM reporting.vCustomers) SELECT * FROM c")]
    [InlineData("SELECT c.Name, i.InvoiceTotal FROM reporting.vCustomers c JOIN reporting.vSalesInvoices i ON i.CustomerId = c.CustomerId")]
    [InlineData("SELECT * FROM (SELECT CustomerId FROM reporting.vCustomers) AS d")]
    [InlineData("SELECT l.[LineNo], l.ExtendedAmount FROM reporting.vSalesInvoiceLines l")]
    public void GuardAcceptsValidReportingQueries(string sql) =>
        ReportingTools.Validate(sql);

    [Fact]
    public async Task AllToolsWorkOverStdio_AndEnforceGuardsAndAudit()
    {
        var auditPath = GetAuditLogPath("test-audit.jsonl");
        if (File.Exists(auditPath)) File.Delete(auditPath);

        await using var client = await CreateStdioClientAsync(auditPath);
        Assert.Equal(4, (await client.ListToolsAsync()).Count);

        foreach (var call in new[]
        {
            ("list_views", new Dictionary<string, object?>()),
            ("describe_view", new Dictionary<string, object?> { ["view"] = "reporting.vCustomers" }),
            ("sample_rows", new Dictionary<string, object?> { ["view"] = "reporting.vCustomers", ["n"] = 2 }),
            ("run_query", new Dictionary<string, object?> { ["sql"] = "SELECT AsOfDate FROM reporting.vReportingCalendar" })
        })
        {
            var result = await client.CallToolAsync(call.Item1, call.Item2);
            Assert.NotEqual(true, result.IsError);
            Assert.NotEmpty(result.Content);
        }

        // Unapproved base table, sensitive column, unapproved view, and invalid sample_rows n are all rejected over MCP
        foreach (var rejected in new[]
        {
            ("run_query", new Dictionary<string, object?> { ["sql"] = "SELECT * FROM dbo.Customer" }),
            ("run_query", new Dictionary<string, object?> { ["sql"] = "SELECT TaxId FROM reporting.vCustomers" }),
            ("run_query", new Dictionary<string, object?> { ["sql"] = "SELECT UnitCost FROM reporting.vItems" }),
            ("run_query", new Dictionary<string, object?> { ["sql"] = "SELECT 1; SELECT 2" }),
            ("run_query", new Dictionary<string, object?> { ["sql"] = "EXEC sp_who" }),
            ("describe_view", new Dictionary<string, object?> { ["view"] = "dbo.Customer" }),
            ("sample_rows", new Dictionary<string, object?> { ["view"] = "dbo.Customer", ["n"] = 5 }),
            ("sample_rows", new Dictionary<string, object?> { ["view"] = "reporting.vCustomers", ["n"] = 0 }),
            ("sample_rows", new Dictionary<string, object?> { ["view"] = "reporting.vCustomers", ["n"] = 21 })
        })
        {
            var bad = await client.CallToolAsync(rejected.Item1, rejected.Item2);
            Assert.True(bad.IsError, $"Expected tool {rejected.Item1} to fail for {JsonSerializer.Serialize(rejected.Item2)}");
        }

        // Verify JSONL audit log wrote both ok and error outcomes
        var lines = await File.ReadAllLinesAsync(auditPath);
        Assert.Equal(13, lines.Length);
        var entries = lines.Select(l => JsonDocument.Parse(l).RootElement).ToArray();
        Assert.Equal(4, entries.Count(e => e.GetProperty("outcome").GetString() == "ok"));
        Assert.Equal(9, entries.Count(e => e.GetProperty("outcome").GetString() == "error"));
    }

    [Fact]
    public async Task RowCap_TruncatesLargeResults_AndLeavesSmallResultsUntruncated()
    {
        var auditPath = GetAuditLogPath("test-rowcap-audit.jsonl");
        if (File.Exists(auditPath)) File.Delete(auditPath);

        await using var client = await CreateStdioClientAsync(auditPath, new Dictionary<string, string?>
        {
            ["HARBOR_MCP_ROW_CAP"] = "10"
        });

        // 150 customers > cap of 10 -> 10 rows returned, truncated = true
        var capped = await client.CallToolAsync("run_query", new Dictionary<string, object?>
        {
            ["sql"] = "SELECT CustomerId, Name FROM reporting.vCustomers ORDER BY CustomerId"
        });
        Assert.NotEqual(true, capped.IsError);
        var cappedJson = JsonDocument.Parse(((TextContentBlock)capped.Content[0]).Text).RootElement;
        Assert.Equal(10, cappedJson.GetProperty("rowCount").GetInt32());
        Assert.Equal(10, cappedJson.GetProperty("rows").GetArrayLength());
        Assert.True(cappedJson.GetProperty("truncated").GetBoolean());

        // 1 row <= cap of 10 -> 1 row returned, truncated = false
        var exact = await client.CallToolAsync("run_query", new Dictionary<string, object?>
        {
            ["sql"] = "SELECT AsOfDate FROM reporting.vReportingCalendar"
        });
        Assert.NotEqual(true, exact.IsError);
        var exactJson = JsonDocument.Parse(((TextContentBlock)exact.Content[0]).Text).RootElement;
        Assert.Equal(1, exactJson.GetProperty("rowCount").GetInt32());
        Assert.False(exactJson.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task CommandTimeout_AbortsSlowQuery()
    {
        var auditPath = GetAuditLogPath("test-timeout-audit.jsonl");
        if (File.Exists(auditPath)) File.Delete(auditPath);

        await using var client = await CreateStdioClientAsync(auditPath, new Dictionary<string, string?>
        {
            ["HARBOR_MCP_TIMEOUT_SECONDS"] = "1"
        });

        // Heavy cross join of 24k-row view against itself exceeds the 1s command timeout
        var slow = await client.CallToolAsync("run_query", new Dictionary<string, object?>
        {
            ["sql"] = "SELECT COUNT(*) FROM reporting.vSalesInvoiceLines a CROSS JOIN reporting.vSalesInvoiceLines b CROSS JOIN reporting.vSalesInvoiceLines c"
        });
        Assert.True(slow.IsError);

        var lines = await File.ReadAllLinesAsync(auditPath);
        var entry = JsonDocument.Parse(lines.Single()).RootElement;
        Assert.Equal("error", entry.GetProperty("outcome").GetString());
        Assert.True(entry.GetProperty("durationMs").GetInt64() >= 500);
    }

    private static string GetAuditLogPath(string fileName)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "HarborSupply.sln")))
        {
            root = root.Parent;
        }
        return Path.Combine(root!.FullName, "logs", fileName);
    }

    private static Task<McpClient> CreateStdioClientAsync(string auditPath, IDictionary<string, string?>? extraEnv = null)
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "HarborSupply.Mcp.dll");
        var env = new Dictionary<string, string?>
        {
            ["HARBOR_MCP_CONNECTION"] = Db.ReaderConnectionString,
            ["HARBOR_MCP_AUDIT_LOG"] = auditPath
        };
        if (extraEnv != null)
        {
            foreach (var kv in extraEnv)
            {
                env[kv.Key] = kv.Value;
            }
        }

        return McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = "harbor",
            Command = "dotnet",
            Arguments = [dll],
            EnvironmentVariables = env
        }));
    }
}
