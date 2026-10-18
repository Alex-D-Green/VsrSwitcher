using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using VsrSwitcher.Core;

namespace VsrSwitcher.Cli;

/// <summary>
/// The command-line front end. Everything that reads or changes a setting lives in
/// VsrSwitcher.Core; this file only parses arguments and presents results.
/// </summary>
internal static class Program
{
    /// <summary>Stamped by the build from the git tag it was made from (see Directory.Build.targets).</summary>
    private static readonly string Version =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

    private static readonly string Copyright =
        typeof(Program).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitNotFound = 2;
    private const int ExitAccessDenied = 3;
    private const int ExitError = 4;
    private const int ExitIncompatible = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private static TextWriter _out = Console.Out;
    private static TextWriter _err = Console.Error;

    /// <summary>
    /// Set by --json. Every command then writes exactly one JSON object to stdout and nothing
    /// else, whatever the outcome — failures included, carrying an "error" member — so a script
    /// always has one document to parse. Exit codes are the same either way.
    /// </summary>
    private static bool _json;

    internal static int Main(string[] args)
    {
        // Looked for before parsing, so that even a malformed command line answers in JSON.
        _json = args.Contains("--json");

        StreamWriter? redirect = null;
        try
        {
            var options = Options.Parse(args, out var positional);

            if (options.OutputFile is not null)
            {
                redirect = new StreamWriter(options.OutputFile, append: false) { AutoFlush = true };
                _out = redirect;
                _err = redirect;
            }

            if (options.Version)
            {
                if (_json)
                {
                    WriteJson(new { version = Version });
                }
                else
                {
                    _out.WriteLine($"vsrswitch {Version} — VSR Switcher command line");
                    _out.WriteLine($"{Copyright}. MIT License.");
                }

                return ExitOk;
            }

            var command = positional.Count > 0 ? positional[0].ToLowerInvariant() : "status";
            if (options.Help || command is "help")
            {
                PrintHelp();
                return ExitOk;
            }

            if (options.Elevate && !IsElevated())
            {
                return Relaunch(args);
            }

            return Dispatch(command, positional, options);
        }
        catch (UsageException ex)
        {
            return Failure(ExitUsage, "usage", ex.Message, "Run 'vsrswitch --help' for usage.");
        }
        catch (AdapterNotFoundException ex)
        {
            return Failure(ExitNotFound, "adapterNotFound", ex.Message);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Not a cue to elevate: no change needs it, and refresh checks for itself. This is a
            // file or key the process could not open, such as an unwritable --output-file path.
            return Failure(ExitAccessDenied, "accessDenied", ex.Message);
        }
        catch (Exception ex)
        {
            return Failure(ExitError, "error", ex.Message);
        }
        finally
        {
            redirect?.Dispose();
        }
    }

    private static int Dispatch(string command, List<string> positional, Options options)
    {
        if (command is "refresh")
        {
            return Refresh();
        }

        var controller = VsrController.Open(options.Adapter);
        var state = controller.ReadState();

        // Every command that changes a setting goes through Change(), which is where the
        // compatibility gate runs. 'status', 'doctor' and 'refresh' are deliberately exempt: the
        // first two are read-only, and refresh is how you recover.
        switch (command)
        {
            case "status" or "get" or "show":
                Report(controller, state);
                return ExitOk;

            case "doctor" or "selftest" or "check":
                return Doctor(controller.CheckCompatibility());

            case "on":
                return Change(controller, state, options,
                    VsrChange.ForSuperResolution(state, ResolveOn(positional)));

            case "off":
                return Change(controller, state, options,
                    VsrChange.ForSuperResolution(state, SuperResValue.Off));

            case "toggle":
                // Refused rather than ignored: 'toggle high' would otherwise look like it worked.
                if (positional.Count > 1)
                {
                    throw new UsageException(
                        "'toggle' takes no value — to turn on at a specific level, use 'vsrswitch on <level>'");
                }

                return Change(controller, state, options,
                    VsrChange.ForSuperResolution(
                        state, state.Enabled ? SuperResValue.Off : VsrController.DefaultOnLevel()));

            case "quality":
                return Change(controller, state, options,
                    VsrChange.ForSuperResolution(state, ParseQualityArg(positional)));

            case "gpu-util" or "gpuutil":
                return Change(controller, state, options,
                    VsrChange.ForGpuUtilization(state, ParseGpuUtilArg(positional)));

            case "indicator":
                return Change(controller, state, options,
                    VsrChange.ForIndicator(state, ParseIndicatorArg(positional, state)));

            default:
                throw new UsageException($"unknown command '{command}'");
        }
    }

    // ---- commands -----------------------------------------------------------------------------

    /// <summary>
    /// Carries out one planned change. The compatibility check runs first either way: inside
    /// <see cref="VsrController.Apply"/> for a real run, and explicitly for --dry-run, whose whole
    /// point is to predict what would happen — and what would actually happen on an unverified
    /// mapping is that the change is refused.
    /// </summary>
    private static int Change(VsrController controller, VsrState state, Options options, VsrChange change)
    {
        // In JSON the same fact is the state's "supported" member.
        if (!_json && !change.IsNoOp && !state.LooksSupported)
        {
            _err.WriteLine("warning: this adapter carries no Super Resolution values, so RTX Video");
            _err.WriteLine("         may be unsupported by this GPU or driver. Continuing anyway.");
        }

        if (options.DryRun)
        {
            var report = controller.CheckCompatibility();
            if (!report.IsCompatible)
            {
                return Refuse(report);
            }

            if (_json)
            {
                WriteChangeJson(controller, state, change, applied: false, dryRun: true);
                return ExitOk;
            }

            if (change.IsNoOp)
            {
                _out.WriteLine(change.Summary);
                return ExitOk;
            }

            _out.WriteLine($"[dry run] {change.Summary}");
            _out.WriteLine("[dry run] would apply through NVIDIA's settings API (instant, no elevation):");
            foreach (var write in change.Writes)
            {
                _out.WriteLine($"[dry run]   setting 0x{write.SettingId:X3} = {write.Value}");
            }

            return ExitOk;
        }

        var result = controller.Apply(change);
        switch (result.Status)
        {
            case VsrApplyStatus.Incompatible:
                return Refuse(result.Compatibility);

            case VsrApplyStatus.NothingToDo:
                if (_json)
                {
                    WriteChangeJson(controller, state, change, applied: false, dryRun: false);
                }
                else
                {
                    _out.WriteLine(change.Summary);
                }

                return ExitOk;

            case VsrApplyStatus.ApiUnavailable:
                return Failure(ExitIncompatible, "apiUnavailable",
                    $"NVIDIA's settings API is unavailable — {result.Reason}",
                    "Nothing was changed. Run 'vsrswitch doctor' for the full diagnostic.");

            case VsrApplyStatus.WriteNotConfirmed:
                var failed = result.FailedWrite!;
                return Failure(ExitIncompatible, "writeNotConfirmed",
                    $"writing setting 0x{failed.SettingId:X3} = {failed.Value} was not confirmed.",
                    result.AppliedCount == 0
                        ? "Nothing was changed."
                        : $"{result.AppliedCount} earlier change(s) in this command did apply; run 'vsrswitch status'.",
                    payload =>
                    {
                        payload["failedWrite"] = ToJsonObject(new { id = failed.SettingId, value = failed.Value });
                        payload["appliedCount"] = result.AppliedCount;
                    });
        }

        // Read back rather than trusting the write, then show the committed state.
        var after = controller.ReadState();

        if (_json)
        {
            WriteChangeJson(controller, after, change, applied: true, dryRun: false);
            return ExitOk;
        }

        _out.WriteLine(change.Summary);
        Report(controller, after);
        return ExitOk;
    }

    /// <summary>Prints the compatibility check in full. Exits 5 when writes would be refused.</summary>
    private static int Doctor(CompatibilityReport report)
    {
        if (!_json)
        {
            WriteCompatibilityText(report);
            return report.IsCompatible ? ExitOk : ExitIncompatible;
        }

        // An incompatible API is a failure like any other, so it carries "error" too: a script can
        // rely on that member being present exactly when the exit code is non-zero.
        var payload = CompatibilityJson(report);
        if (!report.IsCompatible)
        {
            payload["error"] = ErrorJson(ExitIncompatible, "incompatible", IncompatibleMessage(report), null);
        }

        WriteJson(payload);
        return report.IsCompatible ? ExitOk : ExitIncompatible;
    }

    /// <summary>
    /// What a refused change reports: the full diagnostic, then why nothing happened — the danger
    /// being a setting id that has come to mean something else, where writing would change the
    /// wrong setting.
    /// </summary>
    private static int Refuse(CompatibilityReport report)
    {
        if (_json)
        {
            return Failure(ExitIncompatible, "incompatible", IncompatibleMessage(report),
                "Nothing was changed. Settings remain changeable in NVIDIA App itself.",
                payload => payload["compatibility"] = CompatibilityJson(report));
        }

        WriteCompatibilityText(report);
        _out.WriteLine();
        _out.WriteLine("  Nothing was changed. NVIDIA's settings API did not verify, so applying the");
        _out.WriteLine("  requested change could have written the wrong setting.");
        _out.WriteLine();
        _out.WriteLine("  vsrswitch writes only through that API, so there is nothing to fall back to.");
        _out.WriteLine("  Settings remain changeable in NVIDIA App itself.");
        return ExitIncompatible;
    }

    /// <summary>
    /// Restarts the display adapter so the driver re-reads settings from the registry.
    /// Takes no arguments: there is exactly one thing worth doing here.
    /// </summary>
    private static int Refresh()
    {
        if (!IsElevated())
        {
            return Failure(ExitAccessDenied, "accessDenied",
                "restarting the display adapter needs an elevated process.",
                "Re-run from an Administrator prompt, or add --elevate.");
        }

        if (!_json)
        {
            _out.WriteLine("Restarting the display adapter — the screen will blank for a few seconds.");
        }

        var devices = DisplayDevice.FindNvidia();
        if (devices.Count == 0)
        {
            return Failure(ExitNotFound, "adapterNotFound",
                "could not find an NVIDIA display device to restart.",
                "Check 'pnputil /enum-devices /class Display' manually.");
        }

        var results = new List<(string InstanceId, int ExitCode)>();
        foreach (var instanceId in devices)
        {
            if (!_json)
            {
                _out.WriteLine($"restarting device {instanceId} ...");
            }

            var exit = DisplayDevice.Restart(instanceId);
            results.Add((instanceId, exit));

            if (!_json && exit == -1)
            {
                _err.WriteLine("could not run pnputil.exe");
            }

            if (!_json && exit != 0)
            {
                _err.WriteLine($"pnputil exited {exit} for {instanceId}.");
            }
        }

        var restarted = results.Select(r => new { instanceId = r.InstanceId, exitCode = r.ExitCode }).ToList();

        if (results.Any(r => r.ExitCode != 0))
        {
            // Text mode has already said which device failed, as it happened.
            return _json
                ? Failure(ExitError, "restartFailed", "pnputil could not restart every NVIDIA display device.",
                    details: payload => payload["devices"] = JsonSerializer.SerializeToNode(restarted, JsonOptions))
                : ExitError;
        }

        if (_json)
        {
            WriteJson(new { restarted = true, devices = restarted });
        }
        else
        {
            _out.WriteLine("display adapter restarted — re-run 'vsrswitch status' and check NVIDIA App.");
        }

        return ExitOk;
    }

    // ---- argument resolution ------------------------------------------------------------------

    /// <summary>
    /// Picks the level to turn on: an explicit argument wins, else the level remembered from the
    /// last 'off', else Auto.
    /// </summary>
    private static SuperResValue ResolveOn(List<string> positional)
    {
        if (positional.Count > 1)
        {
            if (!Labels.TryParseSuperRes(positional[1], out var explicitLevel))
            {
                throw new UsageException(
                    $"'{positional[1]}' is not a quality level " +
                    "(expected auto, 1-4, verylow, low, medium or high)");
            }

            // The parser shares its vocabulary with 'quality', which does take off.
            if (explicitLevel == SuperResValue.Off)
            {
                throw new UsageException($"'{positional[1]}' is not a level to turn on at — use 'vsrswitch off'");
            }

            return explicitLevel;
        }

        return VsrController.DefaultOnLevel();
    }

    private static SuperResValue ParseQualityArg(List<string> positional)
    {
        if (positional.Count < 2)
        {
            throw new UsageException(
                "'quality' needs a level: off, auto, 1-4, verylow, low, medium or high");
        }

        if (!Labels.TryParseSuperRes(positional[1], out var value))
        {
            throw new UsageException(
                $"'{positional[1]}' is not a quality level " +
                "(expected off, auto, 1-4, verylow, low, medium or high)");
        }

        return value;
    }

    private static GpuUtilization ParseGpuUtilArg(List<string> positional)
    {
        if (positional.Count < 2)
        {
            throw new UsageException("'gpu-util' needs a value: low, medium or high");
        }

        if (!Labels.TryParseGpuUtil(positional[1], out var value))
        {
            throw new UsageException($"'{positional[1]}' is not a GPU utilization (expected low, medium or high)");
        }

        return value;
    }

    private static bool ParseIndicatorArg(List<string> positional, VsrState state)
    {
        if (positional.Count < 2)
        {
            throw new UsageException("'indicator' needs a value: on, off or toggle");
        }

        if (positional[1].Equals("toggle", StringComparison.OrdinalIgnoreCase))
        {
            return !state.IndicatorShown;
        }

        if (!Labels.TryParseSwitch(positional[1], out var value))
        {
            throw new UsageException($"'{positional[1]}' is not on, off or toggle");
        }

        return value;
    }

    // ---- text output ----------------------------------------------------------------------------

    private static void Report(VsrController controller, VsrState state)
    {
        var live = controller.ReadLive();

        if (_json)
        {
            WriteJson(StateJson(state, live));
            return;
        }

        var flags = state.RtxVideoFlags ?? 0;
        var gpuUtil = state.GpuUtil is null ? "unset" : Labels.Describe(state.GpuUtil.Value);
        var autoNote = state.EffectiveSuperRes == SuperResValue.Auto ? "" : "  (auto mode only)";

        _out.WriteLine();
        _out.WriteLine($"  Adapter           {state.Adapter}  {state.DriverDesc ?? "(unnamed)"}");
        _out.WriteLine();
        _out.WriteLine($"  Super Resolution  {Labels.Describe(state.EffectiveSuperRes)}");
        _out.WriteLine($"                    {VsrRegistry.ValSuperRes} = {(int)state.EffectiveSuperRes}");
        _out.WriteLine($"  GPU utilization   {gpuUtil}{autoNote}");
        _out.WriteLine($"  Status indicator  {(state.IndicatorShown ? "shown" : "hidden")}");
        _out.WriteLine($"                    {VsrRegistry.ValFlags} = 0x{flags:X8}");
        _out.WriteLine();

        if (live is not null)
        {
            _out.WriteLine($"  Driver (live)     {Labels.Describe((SuperResValue)live.SuperRes)}" +
                           $", gpu util {live.GpuUtil}, flags 0x{live.RtxVideoFlags:X8}");
            _out.WriteLine(live.Agrees(state)
                ? "                    agrees with the registry"
                : "                    DISAGREES with the registry — something changed the registry"
                  + Environment.NewLine
                  + "                    without the driver picking it up; 'vsrswitch refresh' applies it");
            _out.WriteLine();
        }
        else
        {
            _out.WriteLine("  Driver (live)     unavailable — NVIDIA's settings API could not be used,");
            _out.WriteLine("                    so vsrswitch cannot change settings. Run 'vsrswitch doctor'.");
            _out.WriteLine();
        }

        if (!state.LooksSupported)
        {
            _out.WriteLine("  Note: no Super Resolution values present — RTX Video may be");
            _out.WriteLine("        unsupported by this GPU or driver.");
            _out.WriteLine();
        }
    }

    private static void WriteCompatibilityText(CompatibilityReport report)
    {
        var state = report.State;

        _out.WriteLine();
        _out.WriteLine("  NVIDIA settings API — compatibility check (read-only)");
        _out.WriteLine();
        _out.WriteLine($"  adapter   {state.Adapter}  {state.DriverDesc ?? "(unnamed)"}");
        _out.WriteLine($"  registry  {state.RegistryPath}");
        _out.WriteLine();

        foreach (var check in report.Checks)
        {
            var verdict = check.Outcome switch
            {
                CheckOutcome.Pass => " ok ",
                CheckOutcome.Warn => "warn",
                _ => "FAIL",
            };

            _out.WriteLine($"  [{verdict}] {check.What}{(check.Detail.Length > 0 ? "  —  " + check.Detail : "")}");
        }

        _out.WriteLine();
        switch (report.Verdict)
        {
            case CompatibilityVerdict.Unavailable:
                _out.WriteLine("  VERDICT: settings API unavailable — vsrswitch cannot change settings.");
                _out.WriteLine("           Reading still works. Use NVIDIA App to change them.");
                break;

            case CompatibilityVerdict.Unusable:
                _out.WriteLine("  VERDICT: settings API unusable — vsrswitch cannot change settings.");
                break;

            case CompatibilityVerdict.NotCompatible:
                _out.WriteLine($"  VERDICT: NOT COMPATIBLE — {report.Failures} failure(s). vsrswitch will refuse to");
                _out.WriteLine("           change settings. Reading still works; use NVIDIA App to change them.");
                break;

            case CompatibilityVerdict.CompatibleWithWarnings:
                _out.WriteLine($"  VERDICT: usable, with {report.Warnings} warning(s) above.");
                break;

            default:
                _out.WriteLine("  VERDICT: compatible — writes will apply instantly and need no elevation.");
                break;
        }
    }

    // ---- JSON output ----------------------------------------------------------------------------

    private static void WriteJson(object payload) =>
        _out.WriteLine(JsonSerializer.Serialize(payload, JsonOptions));

    private static JsonObject ToJsonObject(object payload) =>
        JsonSerializer.SerializeToNode(payload, JsonOptions)!.AsObject();

    /// <summary>The state as JSON — what 'status' prints, and what every change prints after it.</summary>
    private static JsonObject StateJson(VsrState state, VsrLiveState? live) => ToJsonObject(new
    {
        adapter = state.Adapter,
        driverDesc = state.DriverDesc,
        registryKey = state.RegistryPath,
        supported = state.LooksSupported,
        enabled = state.Enabled,
        mode = state.EffectiveSuperRes switch
        {
            SuperResValue.Off => "off",
            SuperResValue.Auto => "auto",
            _ => "manual",
        },
        quality = (int)state.EffectiveSuperRes,
        qualityLabel = Labels.Short(state.EffectiveSuperRes),
        gpuUtilization = state.GpuUtil is null ? null : Labels.Describe(state.GpuUtil.Value),
        gpuUtilizationValue = state.GpuUtil is null ? null : (int?)state.GpuUtil.Value,
        indicator = state.IndicatorShown,
        rtxVideoFlags = state.RtxVideoFlags ?? 0,
        elevated = IsElevated(),
        settingsApiAvailable = live is not null,
        settingsApiPath = live?.LoadedFrom,
        live = live is null
            ? null
            : new
            {
                quality = live.SuperRes,
                gpuUtilizationValue = live.GpuUtil,
                rtxVideoFlags = live.RtxVideoFlags,
                matchesRegistry = live.Agrees(state),
            },
    });

    /// <summary>
    /// A change as JSON: the state it left behind, plus a "change" member saying what was done —
    /// so a script reads the outcome of 'on' exactly the way it reads 'status'. "applied" is true
    /// only when something was written; a dry run or a no-op leaves it false.
    /// </summary>
    private static void WriteChangeJson(
        VsrController controller, VsrState state, VsrChange change, bool applied, bool dryRun)
    {
        var payload = StateJson(state, controller.ReadLive());
        payload["change"] = ToJsonObject(new
        {
            summary = change.Summary,
            applied,
            dryRun,
            writes = change.Writes.Select(w => new { id = w.SettingId, value = w.Value }),
        });

        WriteJson(payload);
    }

    private static JsonObject CompatibilityJson(CompatibilityReport report) => ToJsonObject(new
    {
        adapter = report.State.Adapter,
        driverDesc = report.State.DriverDesc,
        registryKey = report.State.RegistryPath,
        compatible = report.IsCompatible,
        verdict = report.Verdict,
        failures = report.Failures,
        warnings = report.Warnings,
        checks = report.Checks.Select(c => new { outcome = c.Outcome, check = c.What, detail = c.Detail }),
    });

    private static string IncompatibleMessage(CompatibilityReport report) => report.Verdict switch
    {
        CompatibilityVerdict.Unavailable =>
            "NVIDIA's settings API is unavailable, so vsrswitch cannot change settings.",
        CompatibilityVerdict.Unusable =>
            "NVIDIA's settings API could not be initialised, so vsrswitch cannot change settings.",
        _ => $"NVIDIA's settings API failed {report.Failures} compatibility check(s), "
             + "so vsrswitch refuses to change settings.",
    };

    private static JsonObject ErrorJson(int exitCode, string code, string message, string? hint) =>
        ToJsonObject(new { code, exitCode, message, hint = hint?.Replace('\n', ' ') });

    /// <summary>
    /// Reports a failure and returns its exit code. Normally that is an "error:" line on stderr,
    /// followed by any hint lines; with --json it is an object on stdout whose "error" member
    /// carries the same text, plus whatever supporting data <paramref name="details"/> adds.
    /// </summary>
    private static int Failure(
        int exitCode, string code, string message, string? hint = null, Action<JsonObject>? details = null)
    {
        if (_json)
        {
            var payload = new JsonObject { ["error"] = ErrorJson(exitCode, code, message, hint) };
            details?.Invoke(payload);
            WriteJson(payload);
            return exitCode;
        }

        _err.WriteLine($"error: {message}");
        foreach (var line in (hint ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            _err.WriteLine(line);
        }

        return exitCode;
    }

    // ---- elevation ------------------------------------------------------------------------------

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static int Relaunch(string[] args)
    {
        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            return Failure(ExitError, "error", "cannot determine own executable path to elevate.");
        }

        // The elevated child gets a fresh console we never see, so have it tee output to a file.
        var transcript = Path.Combine(Path.GetTempPath(), $"vsrswitch-{Guid.NewGuid():N}.txt");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = true,
            Verb = "runas",
        };

        foreach (var arg in args.Where(a => a != "--elevate"))
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.ArgumentList.Add("--output-file");
        startInfo.ArgumentList.Add(transcript);

        try
        {
            using var child = Process.Start(startInfo);
            if (child is null)
            {
                return Failure(ExitError, "error", "could not start elevated process.");
            }

            child.WaitForExit();

            // With --json the child wrote its one JSON object here, so passing it through keeps
            // the contract.
            if (File.Exists(transcript))
            {
                _out.Write(File.ReadAllText(transcript));
                File.Delete(transcript);
            }

            return child.ExitCode;
        }
        catch (Win32Exception)
        {
            return Failure(ExitAccessDenied, "accessDenied", "elevation was declined.");
        }
    }

    private static void PrintHelp()
    {
        _out.WriteLine("""
            vsrswitch — VSR Switcher's command line, for NVIDIA RTX Video Super Resolution

            USAGE
              vsrswitch [command] [value] [options]

            COMMANDS
              status                  Show current state (default when no command is given)
              on [level]              Turn Super Resolution on at a level (see LEVELS).
                                      Without one: the level remembered from the
                                      last 'off', otherwise auto.
              off                     Turn Super Resolution off
              toggle                  Flip on/off, restoring the remembered level
              quality <level|off>     Set the level directly, or turn off
              gpu-util <low|medium|high>
                                      GPU budget used in auto mode
              indicator <on|off|toggle>
                                      Show or hide the on-video status indicator
              doctor                  Read-only check that NVIDIA's settings API is still
                                      compatible — run it after an NVIDIA App update
              refresh                 Restart the display adapter so the driver re-reads
                                      the registry (screen blanks briefly). Only needed
                                      when the settings API is unavailable.

            LEVELS
              auto                    The driver chooses (what NVIDIA App's On selects)
              1  or  verylow          Lowest quality, least GPU load
              2  or  low
              3  or  medium
              4  or  high             Highest quality, most GPU load
              NVIDIA App's slider names only its ends, Very low and High; 'low' and
              'medium' are vsrswitch's names for the two middle stops.

            OPTIONS
              -a, --adapter <NNNN>    Target a specific adapter key (default: auto-detect)
                  --json              One JSON object on stdout for every command (see JSON)
              -n, --dry-run           Print the writes without performing them
                  --elevate           Request elevation, then re-run the same command
              -h, --help              This help
              -v, --version           Version

            EXAMPLES
              vsrswitch                           # what is it doing right now
              vsrswitch on high                   # enable at quality 4, applies instantly
              vsrswitch quality auto
              vsrswitch indicator off
              vsrswitch toggle                    # bind this to a hotkey
              vsrswitch status --json

            NOTES
              All changes go through NVIDIA's own settings API — the same channel
              NVIDIA App uses, from its copy of NvCpl.dll. They apply to the running
              driver immediately, need NO elevation, and cause no screen blank. The
              driver updates the registry itself. Every write is verified by reading
              it back, and vsrswitch never writes the registry directly: doing so does
              not reach the running driver, so it would leave what is stored and what
              is in effect disagreeing until a device restart.

              Before any command that changes a setting, vsrswitch runs the same check
              as 'doctor'. It stays silent when everything verifies. If it does not,
              the diagnostic is printed, NOTHING is changed, and the exit code is 5 —
              because an unverified setting id could mean writing the wrong setting.
              'status', 'doctor' and 'refresh' are never blocked.

              Super Resolution is a global setting — there are no per-application profiles.

            JSON
              With --json every command except --help prints exactly one JSON object
              to stdout and nothing else, whatever the outcome. Exit codes are the same.
                status                  the state
                on, off, toggle, ...    the state after the change, plus "change":
                                        {summary, applied, dryRun, writes}
                doctor                  {compatible, verdict, failures, warnings, checks}
                refresh                 {restarted, devices}
              The object has an "error" member exactly when the exit code is non-zero:
                {"error": {"code", "exitCode", "message", "hint"}}
              A refused change adds the full check as "compatibility".

            EXIT CODES
              0 ok   1 usage   2 adapter not found   3 access denied   4 error
              5 settings API not compatible, or a write was not confirmed
            """);
    }
}
