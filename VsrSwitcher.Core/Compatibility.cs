using System.Diagnostics;

namespace VsrSwitcher.Core;

public enum CheckOutcome
{
    Pass,
    Warn,
    Fail,
}

/// <summary>One line of the compatibility check.</summary>
public sealed record CompatibilityCheck(CheckOutcome Outcome, string What, string Detail = "");

public enum CompatibilityVerdict
{
    /// <summary>Every check passed.</summary>
    Compatible,

    /// <summary>No failures, but something looked unusual. Writes are allowed.</summary>
    CompatibleWithWarnings,

    /// <summary>The API loads, but a check failed — most importantly, an id that no longer agrees
    /// with the registry.</summary>
    NotCompatible,

    /// <summary>NvCpl.dll was found but would not load or initialise.</summary>
    Unusable,

    /// <summary>NvCpl.dll could not be found.</summary>
    Unavailable,
}

/// <summary>The outcome of the compatibility check, with the state it was checked against.</summary>
public sealed record CompatibilityReport(
    VsrState State,
    IReadOnlyList<CompatibilityCheck> Checks,
    CompatibilityVerdict Verdict)
{
    /// <summary>Whether changes may be written. Warnings do not block.</summary>
    public bool IsCompatible =>
        Verdict is CompatibilityVerdict.Compatible or CompatibilityVerdict.CompatibleWithWarnings;

    public int Failures => Checks.Count(c => c.Outcome == CheckOutcome.Fail);

    public int Warnings => Checks.Count(c => c.Outcome == CheckOutcome.Warn);
}

internal static class CompatibilityProbe
{
    /// <summary>
    /// Read-only compatibility check for the settings API. Worth running after an NVIDIA App
    /// update, because the danger is not that the API stops working — that is obvious and handled
    /// — but that a setting id silently comes to mean something else, so a write would change the
    /// wrong setting.
    ///
    /// The check leans on an independent source of truth: the driver keeps the registry in step,
    /// so reading each id through the API and comparing it with its registry value proves the
    /// mapping still holds. Nothing is written.
    /// </summary>
    internal static CompatibilityReport Run(VsrState state)
    {
        var checks = new List<CompatibilityCheck>();

        void Pass(string what, string detail = "") => checks.Add(new(CheckOutcome.Pass, what, detail));
        void Warn(string what, string detail = "") => checks.Add(new(CheckOutcome.Warn, what, detail));
        void Fail(string what, string detail = "") => checks.Add(new(CheckOutcome.Fail, what, detail));

        CompatibilityReport Finish(CompatibilityVerdict verdict) => new(state, checks, verdict);

        // 1. Can we find it at all?
        var path = VsrCpl.ResolveNvCplPath(out var tried);
        if (path is null)
        {
            Fail("locate NvCpl.dll", "tried " + string.Join("; ", tried));
            return Finish(CompatibilityVerdict.Unavailable);
        }

        Pass("locate NvCpl.dll", path);

        // 2. Is it the right one of the two files with this name? The driver's trimmed build
        //    carries a different product name and does not implement these settings.
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var product = (info.ProductName ?? string.Empty).Trim();
            var identity = $"{(product.Length > 0 ? product : "(no product name)")}, version {info.FileVersion}";

            if (product.Equals("NVIDIA App", StringComparison.OrdinalIgnoreCase))
            {
                Pass("it is NVIDIA App's copy, not the driver's trimmed build", identity);
            }
            else
            {
                Warn("unexpected product name — this may be the wrong NvCpl.dll", identity);
            }
        }
        catch (Exception ex)
        {
            Warn("read version info", ex.GetType().Name);
        }

        // 3. Does it load and initialise?
        if (!VsrCpl.TryOpen(out var cpl, out var reason))
        {
            Fail("load and initialise", reason);
            return Finish(CompatibilityVerdict.Unusable);
        }

        Pass("load and initialise", "NvCplApiInit returned 0");

        // 4. Does it discriminate between ids at all? If a nonsense id also succeeds, the reads
        //    below would be meaningless.
        if (cpl.TryGet(0xFFFF, out _))
        {
            Fail("reject an invalid setting id", "0xFFFF was accepted — reads cannot be trusted");
        }
        else
        {
            Pass("reject an invalid setting id", "0xFFFF refused, so ids are discriminated");
        }

        // 5. The mapping itself: each id must agree with the registry value it drives.
        var mapping = new (uint Id, string Name, int Registry, int Min, int Max)[]
        {
            (VsrCpl.SettingSuperResolution, "Super Resolution", (int)state.EffectiveSuperRes, 0, 5),
            (VsrCpl.SettingGpuUtilization, "GPU utilization", state.GpuUtil is null ? -1 : (int)state.GpuUtil.Value, 0, 2),
            (VsrCpl.SettingRtxVideoFlags, "RTX Video flags", state.RtxVideoFlags ?? 0, 0, int.MaxValue),
        };

        foreach (var (id, name, registryValue, min, max) in mapping)
        {
            if (!cpl.TryGet(id, out var live))
            {
                Fail($"read 0x{id:X3} ({name})", "the call failed");
                continue;
            }

            if (registryValue >= 0 && live != registryValue)
            {
                Fail($"0x{id:X3} ({name}) matches the registry",
                    $"API says {live}, registry says {registryValue}");
            }
            else if (live < min || live > max)
            {
                Warn($"0x{id:X3} ({name}) is in range", $"value {live} outside {min}..{max}");
            }
            else
            {
                Pass($"0x{id:X3} ({name}) matches the registry", $"both {live}");
            }
        }

        // 6. How strong is that evidence? Identical values cannot detect a permutation.
        var values = mapping.Select(m => m.Registry).ToArray();

        if (values.Distinct().Count() == values.Length)
        {
            Pass("the three values are distinct", "so a shuffled mapping would have been caught");
        }
        else
        {
            Warn("the three values are not distinct",
                "a shuffled mapping could go unnoticed — re-run with Super Resolution set to a "
                + "quality other than the GPU utilization value");
        }

        if (checks.Any(c => c.Outcome == CheckOutcome.Fail))
        {
            return Finish(CompatibilityVerdict.NotCompatible);
        }

        return Finish(checks.Any(c => c.Outcome == CheckOutcome.Warn)
            ? CompatibilityVerdict.CompatibleWithWarnings
            : CompatibilityVerdict.Compatible);
    }
}
