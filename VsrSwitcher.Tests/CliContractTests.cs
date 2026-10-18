using System.Diagnostics;
using System.Text.Json;

namespace VsrSwitcher.Tests;

/// <summary>
/// Runs the real vsrswitch.exe, but only on paths that answer the same on every machine — with or
/// without an NVIDIA GPU — so these hold on a build server too.
/// </summary>
public class CliContractTests
{
    private static readonly string Exe = Path.Combine(AppContext.BaseDirectory, "vsrswitch.exe");

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var start = new ProcessStartInfo(Exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(30_000), "vsrswitch did not exit");
        return (process.ExitCode, stdout.Result, stderr.Result);
    }

    /// <summary>The whole of stdout must be one JSON object, as the --json contract promises.</summary>
    private static JsonElement Json(string stdout) => JsonDocument.Parse(stdout).RootElement;

    [Fact]
    public void Version_names_the_tool()
    {
        var (exit, stdout, _) = Run("--version");

        Assert.Equal(0, exit);
        Assert.StartsWith("vsrswitch ", stdout);
    }

    [Fact]
    public void Version_in_json_is_one_object_without_an_error()
    {
        var (exit, stdout, stderr) = Run("--version", "--json");

        Assert.Equal(0, exit);
        Assert.Equal(string.Empty, stderr);
        var json = Json(stdout);
        Assert.False(string.IsNullOrEmpty(json.GetProperty("version").GetString()));
        Assert.False(json.TryGetProperty("error", out _));
    }

    [Fact]
    public void Help_lists_the_commands()
    {
        var (exit, stdout, _) = Run("--help");

        Assert.Equal(0, exit);
        Assert.Contains("USAGE", stdout);
        Assert.Contains("doctor", stdout);
    }

    [Fact]
    public void An_unknown_option_is_a_usage_error_in_text()
    {
        var (exit, stdout, stderr) = Run("--bogus");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, stdout);
        Assert.Contains("unknown option '--bogus'", stderr);
    }

    [Fact]
    public void An_unknown_option_is_a_usage_error_in_json()
    {
        var (exit, stdout, stderr) = Run("--bogus", "--json");

        Assert.Equal(1, exit);
        Assert.Equal(string.Empty, stderr);
        var error = Json(stdout).GetProperty("error");
        Assert.Equal("usage", error.GetProperty("code").GetString());
        Assert.Equal(1, error.GetProperty("exitCode").GetInt32());
    }

    [Fact]
    public void A_missing_adapter_is_reported_in_json()
    {
        var (exit, stdout, _) = Run("--adapter", "9999", "--json");

        Assert.Equal(2, exit);
        Assert.Equal("adapterNotFound", Json(stdout).GetProperty("error").GetProperty("code").GetString());
    }
}
