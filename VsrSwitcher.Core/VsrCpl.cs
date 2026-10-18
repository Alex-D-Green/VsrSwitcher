using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace VsrSwitcher.Core;

/// <summary>
/// NVIDIA's own settings API — the only write path. It applies a change to the running driver
/// immediately: no elevation, no adapter restart, no screen blank.
///
/// Two things about this were hard-won and are easy to get wrong again:
///
/// 1. **It must be NVIDIA App's copy of NvCpl.dll, not the one in System32.** Two different
///    binaries share that name. The System32 copy answers NV_NOTIMPLEMENTED for every video
///    setting; only the copy in NVIDIA App's own folder implements them.
/// 2. **The setting id is the THIRD argument**, not the first:
///        NvCplApiGet/SetSetting(1, void* pTarget, DWORD settingId, DWORD* pValue)
///    Putting it first never reaches the id lookup and always answers NV_BADPARAMETER.
///
/// Both were established by hooking NVIDIA App and observing its own calls. `pTarget` only needs
/// to be a readable zeroed buffer — in NVIDIA App's own calls its contents were mostly
/// uninitialised stack, so the callee evidently does not read past the leading zero dwords.
///
/// This is the least durable component in the tool: it binds to a co-versioned file by absolute
/// path and to ids that are positions in an internal enum. Everything here is therefore written
/// to fail cleanly, so callers can refuse a change rather than risk a wrong write.
///
/// **One session per process.** NvCplApiInit starts a thread inside NvCpl.dll that NvCplApiClose
/// does not stop, and from then on the DLL cannot be unloaded: FreeLibrary returns, but the module
/// stays. Measured 2026-10-17: every Init/Close pair leaked one thread (50 cycles, 50 threads),
/// whereas loading and freeing without Init left nothing behind. So the API is initialised once,
/// on first use, and kept until the process exits — no Close, no re-Init, no FreeLibrary. A
/// long-running host therefore holds NvCpl.dll for its whole lifetime; in-process, that cannot be
/// avoided.
/// </summary>
internal sealed class VsrCpl
{
    /// <summary>
    /// Locates NVIDIA App's NvCpl.dll. The install location comes from the registry first, so a
    /// non-default install directory still works; the conventional paths are tried afterwards.
    ///
    /// Deliberately never bundled with this tool. Redistributing NVIDIA's binary is not permitted,
    /// and pinning a copy would be worse anyway: the installed file is guaranteed to match the
    /// machine's NVIDIA stack, whereas a frozen copy could half-work against a future driver.
    /// </summary>
    internal static string? ResolveNvCplPath(out IReadOnlyList<string> tried)
    {
        var candidates = new List<string>();

        // Explicit override, for an unusual install layout — and the only way to point the tool at
        // a different NvCpl.dll, which is how the compatibility gate gets tested.
        var overridePath = Environment.GetEnvironmentVariable("VSRSWITCH_NVCPL");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            candidates.Add(overridePath);
        }

        // NVIDIA App records its own executable here, as ...\NVIDIA App\CEF\NVIDIA App.exe.
        // NvCpl sits beside CEF, so climb two levels.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\NVIDIA Corporation\Global\NvApp");

            if (key?.GetValue("FullPath") is string executable && !string.IsNullOrWhiteSpace(executable))
            {
                var appRoot = Path.GetDirectoryName(Path.GetDirectoryName(executable));
                if (!string.IsNullOrEmpty(appRoot))
                {
                    candidates.Add(Path.Combine(appRoot, "NvCpl", "NvCpl.dll"));
                }
            }
        }
        catch (Exception)
        {
            // Registry unreadable — fall through to the conventional paths.
        }

        foreach (var root in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     @"C:\Program Files",
                 })
        {
            if (!string.IsNullOrEmpty(root))
            {
                candidates.Add(Path.Combine(root, "NVIDIA Corporation", "NVIDIA App", "NvCpl", "NvCpl.dll"));
            }
        }

        var distinct = candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        tried = distinct;
        return distinct.FirstOrDefault(File.Exists);
    }

    /// <summary>Setting ids, each confirmed by round-tripping a read against the registry.</summary>
    internal const uint SettingSuperResolution = 0x317;
    internal const uint SettingGpuUtilization = 0x32f;
    internal const uint SettingRtxVideoFlags = 0x330;

    private const int NvOk = 0;

    /// <summary>Argument 0. NVIDIA App passes 1 for these settings; 2 and above are rejected.</summary>
    private const int TargetCount = 1;

    private const int BufferBytes = 128;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NvCplCall(IntPtr a, IntPtr b, IntPtr c, IntPtr d);

    /// <summary>
    /// <see cref="LoadLibraryExW"/> flags: resolve the DLL's dependencies from its own folder, then
    /// from the default safe locations (the application folder and System32).
    /// </summary>
    private const uint LoadLibrarySearchDllLoadDir = 0x00000100;
    private const uint LoadLibrarySearchDefaultDirs = 0x00001000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryExW(string fileName, IntPtr reserved, uint flags);

    /// <summary>
    /// Serialises every call into the DLL: the buffers are shared, and nothing says NvCpl.dll is
    /// safe to call from several threads at once.
    /// </summary>
    private static readonly object Gate = new();

    private static VsrCpl? _session;

    private readonly NvCplCall _get;
    private readonly NvCplCall _set;

    // Allocated once and kept, like the session itself.
    private readonly IntPtr _target = Marshal.AllocHGlobal(BufferBytes);
    private readonly IntPtr _value = Marshal.AllocHGlobal(BufferBytes);

    /// <summary>Which NvCpl.dll this session loaded — worth reporting, given that picking the
    /// wrong one of two identically named files is the classic failure here.</summary>
    internal string LoadedFrom { get; }

    private VsrCpl(string loadedFrom, NvCplCall get, NvCplCall set)
    {
        LoadedFrom = loadedFrom;
        _get = get;
        _set = set;
    }

    /// <summary>
    /// Returns the process's session with NVIDIA App's NvCpl.dll, loading and initialising it on
    /// first use. Returns false with a reason rather than throwing: an unavailable API is an
    /// expected outcome, which callers report and refuse on. A failure is not remembered, so a
    /// long-running host picks the API up once it becomes available.
    /// </summary>
    internal static bool TryOpen([NotNullWhen(true)] out VsrCpl? cpl, out string reason)
    {
        lock (Gate)
        {
            if (_session is null)
            {
                if (!TryLoad(out var loaded, out reason))
                {
                    cpl = null;
                    return false;
                }

                _session = loaded;
            }

            cpl = _session;
            reason = string.Empty;
            return true;
        }
    }

    private static bool TryLoad([NotNullWhen(true)] out VsrCpl? cpl, out string reason)
    {
        cpl = null;

        var path = ResolveNvCplPath(out var tried);
        if (path is null)
        {
            reason = "NVIDIA App's NvCpl.dll not found — tried " + string.Join("; ", tried);
            return false;
        }

        // Any of NvCpl.dll's imports that live beside it resolve from its folder — for this one
        // load only, rather than for the whole process as SetDllDirectory would. (nvxdbat.dll and
        // nvxdplcy.dll, which it loads itself at run time, resolve either way.)
        var module = LoadLibraryExW(path, IntPtr.Zero, LoadLibrarySearchDllLoadDir | LoadLibrarySearchDefaultDirs);
        if (module == IntPtr.Zero)
        {
            reason = $"could not load NvCpl.dll: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
            return false;
        }

        if (!NativeLibrary.TryGetExport(module, "NvCplApiInit", out var initAddr) ||
            !NativeLibrary.TryGetExport(module, "NvCplApiGetSetting", out var getAddr) ||
            !NativeLibrary.TryGetExport(module, "NvCplApiSetSetting", out var setAddr))
        {
            // Nothing of NVIDIA's has run yet, so this unload is clean.
            NativeLibrary.Free(module);
            reason = "NvCpl.dll does not export the expected setting API";
            return false;
        }

        // From here on the module is never freed: once NvCplApiInit has been called — even
        // unsuccessfully — NVIDIA's thread may be running inside it.
        try
        {
            var init = Marshal.GetDelegateForFunctionPointer<NvCplCall>(initAddr);
            var result = init(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (result != NvOk)
            {
                reason = $"NvCplApiInit returned {result}";
                return false;
            }

            cpl = new VsrCpl(
                path,
                Marshal.GetDelegateForFunctionPointer<NvCplCall>(getAddr),
                Marshal.GetDelegateForFunctionPointer<NvCplCall>(setAddr));

            reason = string.Empty;
            return true;
        }
        catch (Exception ex)
        {
            reason = $"initialising NvCpl.dll failed: {ex.Message}";
            return false;
        }
    }

    /// <summary>Reads a setting's live value from the driver.</summary>
    internal bool TryGet(uint settingId, out int value)
    {
        lock (Gate)
        {
            try
            {
                Zero(_target);
                Zero(_value);
                var result = _get(new IntPtr(TargetCount), _target, new IntPtr(settingId), _value);
                value = Marshal.ReadInt32(_value);
                return result == NvOk;
            }
            catch (Exception)
            {
                value = 0;
                return false;
            }
        }
    }

    /// <summary>
    /// Writes a setting and verifies it by reading back, so a silent no-op is reported as failure
    /// rather than trusted.
    /// </summary>
    internal bool TrySet(uint settingId, int value)
    {
        lock (Gate)
        {
            try
            {
                Zero(_target);
                Zero(_value);
                Marshal.WriteInt32(_value, value);

                if (_set(new IntPtr(TargetCount), _target, new IntPtr(settingId), _value) != NvOk)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }

            return TryGet(settingId, out var readBack) && readBack == value;
        }
    }

    private static void Zero(IntPtr buffer)
    {
        for (var offset = 0; offset + 8 <= BufferBytes; offset += 8)
        {
            Marshal.WriteInt64(buffer, offset, 0);
        }
    }
}
