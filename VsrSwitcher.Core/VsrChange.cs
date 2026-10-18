namespace VsrSwitcher.Core;

/// <summary>One write through NVIDIA's settings API.</summary>
public sealed record SettingWrite(uint SettingId, int Value);

/// <summary>
/// A planned change: the writes it needs and a one-line description of it. Planning touches
/// nothing, so a front end can show a plan without carrying it out (--dry-run);
/// <see cref="VsrController.Apply"/> carries it out.
/// </summary>
public sealed record VsrChange
{
    /// <summary>What the change does — or, when there is nothing to do, why not.</summary>
    public required string Summary { get; init; }

    /// <summary>Empty when the setting already has the requested value.</summary>
    public required IReadOnlyList<SettingWrite> Writes { get; init; }

    public bool IsNoOp => Writes.Count == 0;

    /// <summary>
    /// The level being switched away from, when this change turns Super Resolution off — the only
    /// moment it can still be seen. Remembered once the change applies, so the next 'on' can
    /// restore it.
    /// </summary>
    internal SuperResValue? QualityToRemember { get; init; }

    public static VsrChange ForSuperResolution(VsrState state, SuperResValue target)
    {
        if (state.EffectiveSuperRes == target)
        {
            return NothingToDo($"Super Resolution is already {Labels.Describe(target)} — nothing to do.");
        }

        return new VsrChange
        {
            Summary = $"Super Resolution: {Labels.Describe(state.EffectiveSuperRes)} -> {Labels.Describe(target)}",
            Writes = [new SettingWrite(VsrCpl.SettingSuperResolution, (int)target)],
            QualityToRemember = target == SuperResValue.Off && state.Enabled ? state.EffectiveSuperRes : null,
        };
    }

    public static VsrChange ForGpuUtilization(VsrState state, GpuUtilization target)
    {
        var before = state.GpuUtil is null ? "unset" : Labels.Describe(state.GpuUtil.Value);

        return new VsrChange
        {
            Summary = $"GPU utilization: {before} -> {Labels.Describe(target)}",
            Writes = [new SettingWrite(VsrCpl.SettingGpuUtilization, (int)target)],
        };
    }

    public static VsrChange ForIndicator(VsrState state, bool show)
    {
        if (state.IndicatorShown == show)
        {
            return NothingToDo($"Status indicator is already {(show ? "shown" : "hidden")} — nothing to do.");
        }

        // Read-modify-write: the same DWORD also carries disableVsrOnGameLaunch and the two RTX
        // Video HDR flags, whose bit positions are unidentified. Preserve everything but our bit.
        var current = state.RtxVideoFlags ?? 0;
        var updated = show
            ? current | VsrRegistry.IndicatorBit
            : current & ~VsrRegistry.IndicatorBit;

        return new VsrChange
        {
            Summary = $"Status indicator: {(state.IndicatorShown ? "shown" : "hidden")} -> " +
                      $"{(show ? "shown" : "hidden")}  (flags 0x{current:X8} -> 0x{updated:X8})",
            Writes = [new SettingWrite(VsrCpl.SettingRtxVideoFlags, updated)],
        };
    }

    private static VsrChange NothingToDo(string summary) => new() { Summary = summary, Writes = [] };
}
