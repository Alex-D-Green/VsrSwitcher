using System.Diagnostics;
using System.Text.Json;

namespace VsrSwitcher.Tray;

/// <summary>The outcome of one vsrswitch.exe run: success, or the error it reported.</summary>
internal sealed record CliResult(bool Succeeded, string Message);

/// <summary>
/// Makes every change by running vsrswitch.exe. NVIDIA App's NvCpl.dll cannot be unloaded once used,
/// so keeping it out of this long-running process means an NVIDIA App update never finds it
/// locked, and each change uses whichever copy is installed at that moment. The cost is about
/// 150 ms per change.
/// </summary>
internal sealed class VsrSwitchCli
{
    /// <summary>A change takes well under a second; this only guards against a hang in NVIDIA's DLL.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private readonly string _path = Path.Combine(AppContext.BaseDirectory, "vsrswitch.exe");

    internal async Task<CliResult> RunAsync(params string[] args)
    {
        if (!File.Exists(_path))
        {
            return new CliResult(false, $"vsrswitch.exe was not found beside the tray app ({_path}).");
        }

        var start = new ProcessStartInfo(_path)
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

        start.ArgumentList.Add("--json");

        try
        {
            using var process = Process.Start(start)
                ?? throw new InvalidOperationException("the process did not start");

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();

            using var timeout = new CancellationTokenSource(Timeout);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                return new CliResult(false, "vsrswitch.exe did not respond and was stopped.");
            }

            if (process.ExitCode == 0)
            {
                return new CliResult(true, string.Empty);
            }

            // With --json the reason is in the "error" member on stdout; stderr is only a fallback
            // for a failure before vsrswitch got that far.
            var message = ErrorMessage(await stdout);
            if (message is null)
            {
                var text = (await stderr).Trim();
                message = text.Length > 0 ? text : $"vsrswitch.exe exited with code {process.ExitCode}.";
            }

            return new CliResult(false, message);
        }
        catch (Exception ex)
        {
            return new CliResult(false, $"could not run vsrswitch.exe: {ex.Message}");
        }
    }

    /// <summary>The "error" member of vsrswitch's JSON, as its message followed by its hint.</summary>
    private static string? ErrorMessage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("error", out var error))
            {
                return null;
            }

            var message = error.GetProperty("message").GetString();
            var hint = error.TryGetProperty("hint", out var h) && h.ValueKind == JsonValueKind.String
                ? h.GetString()
                : null;

            return hint is null ? message : $"{message} {hint}";
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}
