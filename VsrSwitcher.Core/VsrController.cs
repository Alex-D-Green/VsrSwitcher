namespace VsrSwitcher.Core;

public enum VsrApplyStatus
{
    /// <summary>Every write applied and was confirmed by reading it back.</summary>
    Applied,

    /// <summary>The setting already had the requested value, so nothing was written.</summary>
    NothingToDo,

    /// <summary>The compatibility check failed, so nothing was written.</summary>
    Incompatible,

    /// <summary>NVIDIA's settings API could not be opened. See <see cref="VsrApplyResult.Reason"/>.</summary>
    ApiUnavailable,

    /// <summary>A write was not confirmed by reading back. Earlier writes in the same change may
    /// have applied — see <see cref="VsrApplyResult.AppliedCount"/>.</summary>
    WriteNotConfirmed,
}

public sealed record VsrApplyResult
{
    public required VsrApplyStatus Status { get; init; }

    /// <summary>The check that ran before anything was written — present whatever the outcome.</summary>
    public required CompatibilityReport Compatibility { get; init; }

    public string? Reason { get; init; }

    public SettingWrite? FailedWrite { get; init; }

    public int AppliedCount { get; init; }

    public bool Succeeded => Status is VsrApplyStatus.Applied or VsrApplyStatus.NothingToDo;
}

/// <summary>
/// The entry point for front ends: reads the current state, checks compatibility, and applies
/// changes. The compatibility check is built into <see cref="Apply"/>, so no front end can write
/// to a mapping that has not been verified.
/// </summary>
public sealed class VsrController
{
    private readonly VsrRegistry _registry;

    private VsrController(VsrRegistry registry) => _registry = registry;

    /// <summary>
    /// Finds the NVIDIA adapter. Pass <paramref name="adapter"/> (e.g. "0001") to skip discovery
    /// on multi-GPU machines.
    /// </summary>
    /// <exception cref="AdapterNotFoundException">No NVIDIA display adapter key was found.</exception>
    public static VsrController Open(string? adapter = null) => new(VsrRegistry.Open(adapter));

    public string Adapter => _registry.Adapter;

    public string RegistryPath => _registry.RegistryPath;

    internal string RegistrySubKey => _registry.SubKey;

    /// <summary>The persisted settings, from the registry.</summary>
    public VsrState ReadState() => _registry.Read();

    /// <summary>The running driver's values, or null when NVIDIA's settings API cannot be used.</summary>
    public VsrLiveState? ReadLive()
    {
        if (VsrCpl.TryOpen(out var cpl, out _) &&
            cpl.TryGet(VsrCpl.SettingSuperResolution, out var superRes) &&
            cpl.TryGet(VsrCpl.SettingGpuUtilization, out var gpuUtil) &&
            cpl.TryGet(VsrCpl.SettingRtxVideoFlags, out var flags))
        {
            return new VsrLiveState(superRes, gpuUtil, flags, cpl.LoadedFrom);
        }

        return null;
    }

    /// <summary>Read-only check that NVIDIA's settings API still means what this tool expects.</summary>
    public CompatibilityReport CheckCompatibility() => CompatibilityProbe.Run(ReadState());

    /// <summary>
    /// The level to turn on at when none is given: the one remembered from the last time Super
    /// Resolution was turned off, else Auto — which is what NVIDIA App itself selects when you
    /// click On.
    /// </summary>
    public static SuperResValue DefaultOnLevel() => QualityMemory.Recall() ?? SuperResValue.Auto;

    /// <summary>
    /// Applies a change through NVIDIA's settings API, which is the only write path. The driver
    /// updates the registry itself as a consequence, so nothing here touches it directly.
    ///
    /// The compatibility check runs first, every time, and a failure refuses the change — the
    /// danger being a setting id that has come to mean something else, where writing would change
    /// the wrong setting. It runs even when there turns out to be nothing to do, so an incompatible
    /// API is reported by the first command that asks for a change, not the first that writes.
    ///
    /// There is deliberately no registry fallback. Writing the registry does not reach the running
    /// driver, so it would silently leave the machine in a state where what is stored and what is
    /// in effect disagree until a device restart — a worse outcome than refusing to write.
    /// </summary>
    public VsrApplyResult Apply(VsrChange change)
    {
        var report = CheckCompatibility();
        if (!report.IsCompatible)
        {
            return new VsrApplyResult { Status = VsrApplyStatus.Incompatible, Compatibility = report };
        }

        if (change.IsNoOp)
        {
            return new VsrApplyResult { Status = VsrApplyStatus.NothingToDo, Compatibility = report };
        }

        if (!VsrCpl.TryOpen(out var cpl, out var reason))
        {
            return new VsrApplyResult
            {
                Status = VsrApplyStatus.ApiUnavailable,
                Compatibility = report,
                Reason = reason,
            };
        }

        var done = 0;
        foreach (var write in change.Writes)
        {
            // TrySet verifies by reading back, so an unconfirmed write is reported rather than
            // assumed.
            if (!cpl.TrySet(write.SettingId, write.Value))
            {
                return new VsrApplyResult
                {
                    Status = VsrApplyStatus.WriteNotConfirmed,
                    Compatibility = report,
                    FailedWrite = write,
                    AppliedCount = done,
                };
            }

            done++;
        }

        // Only once the change has applied, so a refused or failed change leaves the remembered
        // level alone.
        if (change.QualityToRemember is { } quality)
        {
            QualityMemory.Remember(quality);
        }

        return new VsrApplyResult
        {
            Status = VsrApplyStatus.Applied,
            Compatibility = report,
            AppliedCount = done,
        };
    }
}
