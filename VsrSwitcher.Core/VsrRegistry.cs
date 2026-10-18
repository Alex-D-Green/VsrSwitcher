using Microsoft.Win32;

namespace VsrSwitcher.Core;

/// <summary>Thrown when no NVIDIA display adapter key can be found or opened.</summary>
public sealed class AdapterNotFoundException(string message) : Exception(message);

/// <summary>
/// Reads the RTX Video Super Resolution settings that the NVIDIA App persists through its
/// UXDriver settings layer. They live as <c>_User_*</c> values directly on the display adapter's
/// driver key:
/// <c>HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\NNNN</c>
///
/// READ-ONLY by design. Writing these values directly does not reach the running driver — it only
/// changes what the driver would load at its next initialisation — so all writes go through
/// NVIDIA's settings API (see <see cref="VsrCpl"/>) and the driver updates the registry itself.
/// The registry is still read here, as an independent witness used for status and by the
/// compatibility check.
/// </summary>
public sealed class VsrRegistry
{
    internal const string DisplayClassKey =
        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";

    private const string NvidiaVendorId = @"pci\ven_10de";

    public const string ValSuperRes = "_User_Global_VAL_SuperResolution";
    public const string ValGpuUtil = "_User_Global_VAL_SuperResolution_Auto";
    public const string ValFlags = "_User_Global_VAL_RTXVideoFlags";
    private const string DatSuperRes = "_User_Global_DAT_SuperResolution";

    /// <summary>
    /// Bit of <c>_User_Global_VAL_RTXVideoFlags</c> that shows the VSR status indicator.
    /// Confirmed in both directions by toggling "Show status indicator on video" in NVIDIA App.
    /// The rest of the mask carries disableVsrOnGameLaunch and the two RTX Video HDR flags, whose
    /// positions are not identified — so never write this value without preserving them.
    /// </summary>
    public const int IndicatorBit = 0x100;

    private readonly string _subKey;

    public string Adapter { get; }

    public string RegistryPath => $@"HKLM\{_subKey}";

    /// <summary>The adapter key's path under HKLM.</summary>
    internal string SubKey => _subKey;

    private VsrRegistry(string adapter)
    {
        Adapter = adapter;
        _subKey = $@"{DisplayClassKey}\{adapter}";
    }

    /// <summary>
    /// Locates the NVIDIA adapter's driver key. Pass <paramref name="explicitAdapter"/> (e.g.
    /// "0001") to skip discovery on multi-GPU machines.
    /// </summary>
    internal static VsrRegistry Open(string? explicitAdapter)
    {
        if (!string.IsNullOrWhiteSpace(explicitAdapter))
        {
            var name = explicitAdapter.Trim();
            using var probe = Registry.LocalMachine.OpenSubKey($@"{DisplayClassKey}\{name}");
            if (probe is null)
            {
                throw new AdapterNotFoundException(
                    $@"no such adapter key: HKLM\{DisplayClassKey}\{name}");
            }

            return new VsrRegistry(name);
        }

        using var classKey = Registry.LocalMachine.OpenSubKey(DisplayClassKey)
            ?? throw new AdapterNotFoundException(
                $@"display adapter class key is missing: HKLM\{DisplayClassKey}");

        var nvidia = new List<string>();
        foreach (var name in classKey.GetSubKeyNames())
        {
            if (name.Length != 4 || !name.All(char.IsAsciiDigit))
            {
                continue;
            }

            using var adapter = classKey.OpenSubKey(name);
            if (adapter is not null && IsNvidia(adapter))
            {
                nvidia.Add(name);
            }
        }

        if (nvidia.Count == 0)
        {
            throw new AdapterNotFoundException(
                "no NVIDIA display adapter found under " + $@"HKLM\{DisplayClassKey}");
        }

        // Prefer an adapter that already carries VSR values — on a hybrid laptop/desktop there can
        // be several NVIDIA entries, only one of which the driver actually manages.
        foreach (var name in nvidia)
        {
            using var adapter = classKey.OpenSubKey(name);
            if (adapter?.GetValue(ValSuperRes) is not null || adapter?.GetValue(DatSuperRes) is not null)
            {
                return new VsrRegistry(name);
            }
        }

        return new VsrRegistry(nvidia[0]);
    }

    private static bool IsNvidia(RegistryKey adapter)
    {
        if (adapter.GetValue("MatchingDeviceId") is string id &&
            id.StartsWith(NvidiaVendorId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return adapter.GetValue("ProviderName") is string provider &&
               provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase);
    }

    internal VsrState Read()
    {
        using var key = Registry.LocalMachine.OpenSubKey(_subKey)
            ?? throw new AdapterNotFoundException($"cannot open {RegistryPath}");

        var superRes = ReadDword(key, ValSuperRes);
        var gpuUtil = ReadDword(key, ValGpuUtil);
        var flags = ReadDword(key, ValFlags);

        return new VsrState
        {
            Adapter = Adapter,
            RegistryPath = RegistryPath,
            DriverDesc = key.GetValue("DriverDesc") as string,
            SuperRes = superRes is null ? null : (SuperResValue)superRes.Value,
            GpuUtil = gpuUtil is null ? null : (GpuUtilization)gpuUtil.Value,
            RtxVideoFlags = flags,
            LooksSupported = superRes is not null
                             || flags is not null
                             || key.GetValue(DatSuperRes) is not null,
        };
    }

    private static int? ReadDword(RegistryKey key, string name) =>
        key.GetValue(name) is int value ? value : null;
}
