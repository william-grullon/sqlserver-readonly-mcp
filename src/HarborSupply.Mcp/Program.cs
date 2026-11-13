using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.SqlServer.TransactSql.ScriptDom;
using ModelContextProtocol.Server;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
// MCP uses stdout for JSON-RPC messages; route all host logs to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<ReportingTools>();
builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<ReportingTools>();
await builder.Build().RunAsync();

/// <summary>
/// Read-only MCP tools exposing approved Harbor Supply reporting views.
/// </summary>
[McpServerToolType]
public sealed class ReportingTools
{
    public static readonly Dictionary<string, string> Views = new(StringComparer.OrdinalIgnoreCase)
    {
        ["reporting.vReportingCalendar"] = "Fixed reporting date, 2026-10-31; use instead of GETDATE().",
        ["reporting.vCustomers"] = "Customer master with sensitive fields excluded.",
        ["reporting.vItems"] = "Item catalog without costs.",
        ["reporting.vVendors"] = "Vendor master without tax or bank details.",
        ["reporting.vSalesInvoices"] = "Non-void invoices with totals, payments, balances and days past due.",
        ["reporting.vSalesInvoiceLines"] = "Sales lines with invoice date, customer and item; bracket [LineNo].",
        ["reporting.vCustomerPayments"] = "Customer payments.",
        ["reporting.vVendorBills"] = "Vendor bills with payments, balances and days past due."
    };

    private static readonly string ViewsJson = JsonSerializer.Serialize(Views);
    private static readonly SemaphoreSlim AuditLock = new(1, 1);

    private readonly string _connection = Environment.GetEnvironmentVariable("HARBOR_MCP_CONNECTION")
        ?? throw new InvalidOperationException("Set HARBOR_MCP_CONNECTION.");
    private readonly int _cap = ParsePositiveLimit("HARBOR_MCP_ROW_CAP", 500);
    private readonly int _timeoutSeconds = ParsePositiveLimit("HARBOR_MCP_TIMEOUT_SECONDS", 15);
    private readonly string _auditLogPath = ResolveAuditLogPath();

    private static int ParsePositiveLimit(string name, int fallback) =>
        Environment.GetEnvironmentVariable(name) is not string value
            ? fallback
            : int.TryParse(value, out var parsed) && parsed > 0 && parsed < int.MaxValue
                ? parsed
                : throw new InvalidOperationException($"Invalid {name}.");

    private static string ResolveAuditLogPath()
    {
        var path = Path.GetFullPath(Environment.GetEnvironmentVariable("HARBOR_MCP_AUDIT_LOG") ?? "logs/audit.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private static string ResolveApprovedView(string name) =>
        Views.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase))
        ?? throw new ArgumentException("View is not approved.");

    [McpServerTool(Name = "list_views")]
    [Description("List approved reporting views. Call this and describe_view before run_query.")]
    public Task<string> ListViews() =>
        AuditedAsync("list_views", null, () => Task.FromResult((ViewsJson, Views.Count)));

    [McpServerTool(Name = "describe_view")]
    [Description("Describe approved view columns and SQL types before querying. Use reporting-qualified names; bracket [LineNo].")]
    public Task<string> DescribeView(string view, CancellationToken cancellationToken) =>
        AuditedAsync("describe_view", null, async () =>
        {
            var viewName = ResolveApprovedView(view);
            await using var conn = new SqlConnection(_connection);
            await conn.OpenAsync(cancellationToken);

            // SchemaOnly avoids executing underlying view joins when inspecting column metadata.
            await using var cmd = new SqlCommand($"SELECT TOP (0) * FROM {viewName}", conn) { CommandTimeout = _timeoutSeconds };
            await using var reader = await cmd.ExecuteReaderAsync(CommandBehavior.SchemaOnly, cancellationToken);

            var columns = new object[reader.FieldCount];
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns[i] = new { name = reader.GetName(i), type = reader.GetDataTypeName(i) };
            }

            var json = JsonSerializer.Serialize(new { view = viewName, description = Views[viewName], columns });
            return (json, columns.Length);
        });

    [McpServerTool(Name = "sample_rows")]
    [Description("Sample 1 to 20 rows of an approved view. Order is unspecified.")]
    public Task<string> SampleRows(string view, int n = 5, CancellationToken cancellationToken = default) =>
        AuditedAsync("sample_rows", null, async () =>
        {
            if (n is < 1 or > 20)
            {
                throw new ArgumentException("n must be 1 through 20.");
            }

            var viewName = ResolveApprovedView(view);
            return await ExecuteQueryAsync($"SELECT TOP ({n}) * FROM {viewName}", Math.Min(n, _cap), cancellationToken);
        });

    [McpServerTool(Name = "run_query")]
    [Description("Run one SQL Server 2012 SELECT against approved reporting views. First use list_views and describe_view. Use vReportingCalendar for today and bracket [LineNo]. Results are capped.")]
    public Task<string> RunQuery(string sql, CancellationToken cancellationToken) =>
        AuditedAsync("run_query", sql, async () =>
        {
            Validate(sql);
            return await ExecuteQueryAsync(sql, _cap, cancellationToken);
        });

    /// <summary>
    /// Parses T-SQL under SQL Server 2012 rules and ensures a single SELECT referencing only approved views or declared CTEs.
    /// </summary>
    public static void Validate(string sql)
    {
        var parser = new TSql110Parser(initialQuotedIdentifiers: true);
        var tree = parser.Parse(new StringReader(sql), out var errors);

        if (errors.Count > 0
            || tree is not TSqlScript script
            || script.Batches.Count != 1
            || script.Batches[0].Statements.Count != 1
            || script.Batches[0].Statements[0] is not SelectStatement select
            || select.Into != null)
        {
            throw new ArgumentException("Only one SELECT without INTO is allowed.");
        }

        var cteNames = select.WithCtesAndXmlNamespaces?.CommonTableExpressions.Select(c => c.ExpressionName.Value) ?? [];
        tree.Accept(new Guard(cteNames));
    }

    private sealed class Guard(IEnumerable<string> ctes) : TSqlFragmentVisitor
    {
        private readonly HashSet<string> _cteNames = new(ctes, StringComparer.OrdinalIgnoreCase);

        public override void Visit(NamedTableReference node)
        {
            var identifiers = node.SchemaObject.Identifiers;

            // Allow unqualified references only when they match a CTE declared in the same query.
            if (identifiers.Count == 1 && _cteNames.Contains(node.SchemaObject.BaseIdentifier.Value))
            {
                return;
            }

            if (identifiers.Count != 2)
            {
                throw new ArgumentException("Only approved reporting views are allowed.");
            }

            var qualifiedName = $"{node.SchemaObject.SchemaIdentifier.Value}.{node.SchemaObject.BaseIdentifier.Value}";
            if (!Views.ContainsKey(qualifiedName))
            {
                throw new ArgumentException("Only approved reporting views are allowed.");
            }
        }

        public override void Visit(TSqlFragment node)
        {
            // Block table-valued functions, OPENROWSET, PIVOT/UNPIVOT, and other non-view table sources.
            if (node is TableReference
                and not NamedTableReference
                and not QualifiedJoin
                and not UnqualifiedJoin
                and not QueryDerivedTable
                and not JoinParenthesisTableReference)
            {
                throw new ArgumentException("Unsupported table source.");
            }
        }
    }

    private async Task<(string Json, int RowCount)> ExecuteQueryAsync(string sql, int limit, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connection);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn) { CommandTimeout = _timeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var fieldCount = reader.FieldCount;
        var columns = new string[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            columns[i] = reader.GetName(i);
        }

        var rows = new List<object?[]>(Math.Min(limit, 64));
        while (rows.Count < limit && await reader.ReadAsync(ct))
        {
            var row = new object?[fieldCount];
            for (var i = 0; i < fieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        var truncated = await reader.ReadAsync(ct);
        var json = JsonSerializer.Serialize(new { columns, rows, rowCount = rows.Count, truncated });
        return (json, rows.Count);
    }

    private async Task<string> AuditedAsync(string tool, string? sql, Func<Task<(string Json, int RowCount)>> action)
    {
        var watch = Stopwatch.StartNew();
        var outcome = "error";
        var rowCount = 0;

        try
        {
            var result = await action();
            rowCount = result.RowCount;
            outcome = "ok";
            return result.Json;
        }
        finally
        {
            var entry = JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.UtcNow,
                tool,
                sql,
                rowCount,
                durationMs = watch.ElapsedMilliseconds,
                outcome
            });

            // Serialize appends so concurrent tool calls cannot interleave JSONL lines.
            await AuditLock.WaitAsync();
            try
            {
                await File.AppendAllTextAsync(_auditLogPath, entry + Environment.NewLine);
            }
            finally
            {
                AuditLock.Release();
            }
        }
    }
}
