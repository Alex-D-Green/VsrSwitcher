using Microsoft.Win32;

namespace VsrSwitcher.Core;

/// <summary>
/// Remembers the quality level across an off/on cycle so turning back on can restore it. Kept per
/// user under HKCU, so it needs no elevation — and shared by every front end, so an 'off' from one
/// followed by an 'on' from another comes back at the same level.
/// </summary>
internal static class QualityMemory
{
    private const string StateKey = @"Software\VsrSwitcher";
    private const string LastQualityValue = "LastQuality";

    internal static void Remember(SuperResValue value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(StateKey);
            key?.SetValue(LastQualityValue, (int)value, RegistryValueKind.DWord);
        }
        catch (Exception)
        {
            // Losing the hint only costs us a fallback to Auto on the next 'on'.
        }
    }

    internal static SuperResValue? Recall()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKey);
            if (key?.GetValue(LastQualityValue) is int stored &&
                stored is > (int)SuperResValue.Off and <= (int)SuperResValue.Auto)
            {
                return (SuperResValue)stored;
            }
        }
        catch (Exception)
        {
            // Fall through to Auto.
        }

        return null;
    }
}
