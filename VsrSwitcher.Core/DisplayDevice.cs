using System.Diagnostics;

namespace VsrSwitcher.Core;

/// <summary>
/// Restarts the NVIDIA display adapter, forcing the kernel driver to re-initialise and re-read its
/// persisted <c>_User_*</c> values. This is the recovery route for when the registry and the
/// running driver disagree; settings changes never need it, because NVIDIA's settings API applies
/// them to the running driver directly.
///
/// Needs elevation. Expect the screen to blank for a few seconds, and windows may be rearranged.
///
/// Restarting only the NVIDIA display container service was tried first and does **not** apply a
/// Super Resolution change, so that lighter option is not offered — a fallback that silently fails
/// to apply is worse than no fallback.
/// </summary>
public static class DisplayDevice
{
    /// <summary>Instance ids of the NVIDIA display devices, as pnputil reports them.</summary>
    public static IReadOnlyList<string> FindNvidia()
    {
        var found = new List<string>();
        var output = Execute("pnputil.exe", "/enum-devices /class Display")?.Output;
        if (output is null)
        {
            return found;
        }

        foreach (var line in output.Split('\n'))
        {
            // pnputil prints "Instance ID:   PCI\VEN_10DE&DEV_2786&..." (label is localised, so
            // key off the PCI path rather than the prefix).
            var index = line.IndexOf(@"PCI\VEN_10DE", StringComparison.OrdinalIgnoreCase);
            if (index >= 0)
            {
                var id = line[index..].Trim();
                if (id.Length > 0 && !found.Contains(id, StringComparer.OrdinalIgnoreCase))
                {
                    found.Add(id);
                }
            }
        }

        return found;
    }

    /// <summary>Restarts one device. Returns pnputil's exit code, or -1 if it could not be run.</summary>
    public static int Restart(string instanceId) =>
        Execute("pnputil.exe", $"/restart-device \"{instanceId}\"")?.ExitCode ?? -1;

    private static (string Output, int ExitCode)? Execute(string executable, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            if (process is null)
            {
                return null;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (stdout + stderr, process.ExitCode);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
