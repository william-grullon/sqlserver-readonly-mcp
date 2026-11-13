using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
namespace HarborSupply.Db.Tests;
public class McpTests
{
    [Theory]
    [InlineData("DELETE FROM reporting.vCustomers")]
    [InlineData("SELECT * FROM dbo.Customer")]
    [InlineData("SELECT 1; SELECT 2")]
    [InlineData("SELECT * INTO #x FROM reporting.vCustomers")]
    [InlineData("EXEC sp_who")]
    public void GuardRejects(string sql) => Assert.Throws<ArgumentException>(()=>ReportingTools.Validate(sql));

    [Fact]
    public async Task AllToolsWorkOverStdio()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "HarborSupply.sln")))
        {
            root = root.Parent;
        }

        var dll = Path.Combine(AppContext.BaseDirectory, "HarborSupply.Mcp.dll");
        await using var client=await McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions {
            Name="harbor",Command="dotnet",Arguments=[dll],
            EnvironmentVariables = new Dictionary<string, string?>
            {
                ["HARBOR_MCP_CONNECTION"] = Db.ReaderConnectionString,
                ["HARBOR_MCP_AUDIT_LOG"] = Path.Combine(root!.FullName, "logs", "test-audit.jsonl")
            }
        }));
        Assert.Equal(4,(await client.ListToolsAsync()).Count);
        foreach(var call in new[] {
            ("list_views",new Dictionary<string,object?>()),
            ("describe_view",new Dictionary<string,object?> {["view"]="reporting.vCustomers"}),
            ("sample_rows",new Dictionary<string,object?> {["view"]="reporting.vCustomers",["n"]=2}),
            ("run_query",new Dictionary<string,object?> {["sql"]="SELECT AsOfDate FROM reporting.vReportingCalendar"})
        }) {
            var result=await client.CallToolAsync(call.Item1,call.Item2);
            Assert.NotEqual(true,result.IsError);
            Assert.NotEmpty(result.Content);
        }
        var bad=await client.CallToolAsync("run_query",new Dictionary<string,object?> {["sql"]="SELECT * FROM dbo.Customer"});
        Assert.True(bad.IsError);
    }
}

