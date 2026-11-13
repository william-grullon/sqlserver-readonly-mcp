using Microsoft.Data.SqlClient;

namespace HarborSupply.Db.Tests;

/// <summary>
/// Connection helpers. Values come from the environment; if a variable is not set, the repo-root
/// .env file (gitignored) is read as a convenience for local runs. Nothing is hard-coded here.
///   HARBOR_MCP_CONNECTION    mcp_reader (the login under test)
///   HARBOR_ADMIN_CONNECTION  sa (setup/inspection only, never used to prove permissions)
/// </summary>
public static class Db
{
    private static readonly Lazy<Dictionary<string, string>> DotEnv = new(LoadDotEnv);

    public static string ReaderConnectionString => Require("HARBOR_MCP_CONNECTION");
    public static string AdminConnectionString => Require("HARBOR_ADMIN_CONNECTION");

    public static SqlConnection OpenReader() => Open(ReaderConnectionString);
    public static SqlConnection OpenAdmin() => Open(AdminConnectionString);

    public static T Scalar<T>(SqlConnection cn, string sql)
    {
        using var cmd = new SqlCommand(sql, cn);
        var value = cmd.ExecuteScalar();
        return (T)Convert.ChangeType(value!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    public static List<string> Strings(SqlConnection cn, string sql)
    {
        using var cmd = new SqlCommand(sql, cn);
        using var rd = cmd.ExecuteReader();
        var list = new List<string>();
        while (rd.Read()) list.Add(rd.GetValue(0).ToString()!);
        return list;
    }

    /// <summary>Runs the statement and returns the SqlException it must raise. Fails the test if it succeeds.</summary>
    public static SqlException Denied(SqlConnection cn, string sql)
    {
        using var tx = cn.BeginTransaction();      // never leave a change behind if a guard were broken
        try
        {
            using var cmd = new SqlCommand(sql, cn, tx);
            cmd.ExecuteNonQuery();
        }
        catch (SqlException ex)
        {
            return ex;
        }
        finally
        {
            try { tx.Rollback(); } catch { /* connection may already have rolled back */ }
        }
        throw new Xunit.Sdk.XunitException($"Statement unexpectedly succeeded as mcp_reader: {sql}");
    }

    private static SqlConnection Open(string cs)
    {
        var cn = new SqlConnection(cs);
        cn.Open();
        return cn;
    }

    private static string Require(string name)
    {
        var v = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(v) && DotEnv.Value.TryGetValue(name, out var fromFile)) v = fromFile;
        if (string.IsNullOrWhiteSpace(v))
            throw new InvalidOperationException($"{name} is not set (environment or repo-root .env). See .env.example.");
        return v;
    }

    private static Dictionary<string, string> LoadDotEnv()
    {
        var map = new Dictionary<string, string>();
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, ".env")))
            dir = dir.Parent;
        if (dir == null) return map;

        foreach (var raw in File.ReadAllLines(Path.Combine(dir.FullName, ".env")))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[line[..eq].Trim()] = line[(eq + 1)..].Trim();
        }
        return map;
    }
}
