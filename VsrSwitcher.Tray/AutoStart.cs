using Microsoft.Win32;

namespace VsrSwitcher.Tray;

/// <summary>
/// "Start with Windows", through the per-user Run key: no elevation needed, and it is what Task
/// Manager's Startup apps list shows. That list can also switch an entry off without removing it,
/// by marking it under StartupApproved, so both places are consulted.
/// </summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "VsrSwitcher";

    private static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>
    /// Whether this copy of the tray app starts with Windows: the Run entry names this executable
    /// (not, say, a Debug build elsewhere), and Task Manager has not switched it off.
    /// </summary>
    public static bool IsEnabled()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string command || !NamesThisExecutable(command))
        {
            return false;
        }

        // Task Manager records 12 bytes: an odd first byte means switched off (the rest is when).
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return approved?.GetValue(ValueName) is not byte[] { Length: > 0 } state || (state[0] & 1) == 0;
    }

    public static void Enable()
    {
        using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            run.SetValue(ValueName, $"\"{ExecutablePath}\"", RegistryValueKind.String);
        }

        // A "switched off" mark left by Task Manager would keep the entry from starting.
        ClearApproval();
    }

    public static void Disable()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            run?.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        ClearApproval();
    }

    private static void ClearApproval()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
        approved?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static bool NamesThisExecutable(string command) =>
        string.Equals(command.Trim().Trim('"'), ExecutablePath, StringComparison.OrdinalIgnoreCase);
}
