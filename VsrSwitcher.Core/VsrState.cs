namespace VsrSwitcher.Core;

/// <summary>
/// The driver's <c>SuperResolutionValue</c> enum, stored verbatim in
/// <c>_User_Global_VAL_SuperResolution</c>. Names come from the JSON schema embedded in
/// NVIDIA App's NvCplDisplayPlugin.dll (SuperResValue_Off, _1 .. _4, _Auto).
/// </summary>
public enum SuperResValue
{
    Off = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Level4 = 4,
    Auto = 5,
}

/// <summary>
/// The driver's <c>SuperResolutionGpuUtilization</c> enum, stored in
/// <c>_User_Global_VAL_SuperResolution_Auto</c>. Only consulted while
/// <see cref="SuperResValue.Auto"/> is selected.
/// </summary>
public enum GpuUtilization
{
    Low = 0,
    Medium = 1,
    High = 2,
}

/// <summary>A snapshot of every VSR value on one adapter, as persisted in the registry.</summary>
public sealed record VsrState
{
    public required string Adapter { get; init; }
    public required string RegistryPath { get; init; }
    public string? DriverDesc { get; init; }

    /// <summary>Null when the value is absent, which the driver treats as Off.</summary>
    public SuperResValue? SuperRes { get; init; }

    /// <summary>Null when the value is absent.</summary>
    public GpuUtilization? GpuUtil { get; init; }

    /// <summary>Whole bitmask. Only bit <see cref="VsrRegistry.IndicatorBit"/> is identified.</summary>
    public int? RtxVideoFlags { get; init; }

    /// <summary>False when this adapter shows no sign of VSR support.</summary>
    public bool LooksSupported { get; init; }

    public SuperResValue EffectiveSuperRes => SuperRes ?? SuperResValue.Off;

    public bool Enabled => EffectiveSuperRes != SuperResValue.Off;

    public bool IndicatorShown => ((RtxVideoFlags ?? 0) & VsrRegistry.IndicatorBit) != 0;
}

/// <summary>
/// The running driver's values, read through NVIDIA's settings API. Worth having alongside
/// <see cref="VsrState"/>: the two can disagree, because the registry is only what the driver
/// persists and a direct registry write does not reach the running driver at all.
/// </summary>
/// <param name="LoadedFrom">The NvCpl.dll that answered.</param>
public sealed record VsrLiveState(int SuperRes, int GpuUtil, int RtxVideoFlags, string LoadedFrom)
{
    /// <summary>True when the driver's values match what the registry holds.</summary>
    public bool Agrees(VsrState state) =>
        SuperRes == (int)state.EffectiveSuperRes && RtxVideoFlags == (state.RtxVideoFlags ?? 0);
}

public static class Labels
{
    /// <summary>
    /// Renders a stored value the way NVIDIA App presents it. The slider in the app labels only
    /// its endpoints — "Very low" at stop 1 and "High" at stop 4 — so the two middle names are
    /// this tool's own aliases for stops 2 and 3.
    /// </summary>
    public static string Describe(SuperResValue value) => value switch
    {
        SuperResValue.Off => "off",
        SuperResValue.Auto => "on, auto",
        SuperResValue.Level1 => "on, quality 1 (very low)",
        SuperResValue.Level2 => "on, quality 2 (low)",
        SuperResValue.Level3 => "on, quality 3 (medium)",
        SuperResValue.Level4 => "on, quality 4 (high)",
        _ => $"unknown ({(int)value})",
    };

    public static string Short(SuperResValue value) => value switch
    {
        SuperResValue.Off => "off",
        SuperResValue.Auto => "auto",
        SuperResValue.Level1 => "verylow",
        SuperResValue.Level2 => "low",
        SuperResValue.Level3 => "medium",
        SuperResValue.Level4 => "high",
        _ => ((int)value).ToString(),
    };

    public static string Describe(GpuUtilization value) => value switch
    {
        GpuUtilization.Low => "low",
        GpuUtilization.Medium => "medium",
        GpuUtilization.High => "high",
        _ => $"unknown ({(int)value})",
    };

    /// <summary>Accepts off | auto | 1..4 | verylow | low | medium | high.</summary>
    public static bool TryParseSuperRes(string text, out SuperResValue value)
    {
        switch (Normalize(text))
        {
            case "off": case "0": value = SuperResValue.Off; return true;
            case "auto": case "5": value = SuperResValue.Auto; return true;
            case "1": case "verylow": case "vlow": value = SuperResValue.Level1; return true;
            case "2": case "low": value = SuperResValue.Level2; return true;
            case "3": case "medium": case "med": value = SuperResValue.Level3; return true;
            case "4": case "high": value = SuperResValue.Level4; return true;
            default: value = default; return false;
        }
    }

    /// <summary>Accepts low | medium | high (or 0 | 1 | 2).</summary>
    public static bool TryParseGpuUtil(string text, out GpuUtilization value)
    {
        switch (Normalize(text))
        {
            case "0": case "low": value = GpuUtilization.Low; return true;
            case "1": case "medium": case "med": value = GpuUtilization.Medium; return true;
            case "2": case "high": value = GpuUtilization.High; return true;
            default: value = default; return false;
        }
    }

    /// <summary>Accepts on | off | true | false | 1 | 0 | show | hide.</summary>
    public static bool TryParseSwitch(string text, out bool value)
    {
        switch (Normalize(text))
        {
            case "on": case "1": case "true": case "yes": case "show": case "shown":
                value = true; return true;
            case "off": case "0": case "false": case "no": case "hide": case "hidden":
                value = false; return true;
            default: value = default; return false;
        }
    }

    private static string Normalize(string text) =>
        text.Trim().Replace("-", "").Replace("_", "").Replace(" ", "").ToLowerInvariant();
}
