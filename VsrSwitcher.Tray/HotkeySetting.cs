using Microsoft.Win32;

namespace VsrSwitcher.Tray;

/// <summary>
/// The global hotkey that toggles Super Resolution: its default, where it is kept, how it is
/// written, and which combinations are acceptable. A hotkey is a <see cref="Keys"/> value — a key
/// code combined with Control, Alt and Shift — and <see cref="Keys.None"/> means switched off.
/// </summary>
internal static class HotkeySetting
{
    /// <summary>
    /// Ctrl+Alt+Shift+V. Windows keeps Win-key shortcuts for itself and uses none with all of Ctrl,
    /// Alt and Shift, and few programs do. That matters because a global hotkey takes precedence
    /// over a program's own shortcut for the same keys.
    /// </summary>
    public static readonly Keys Default = Keys.Control | Keys.Alt | Keys.Shift | Keys.V;

    // Beside the remembered quality level that VsrSwitcher.Core keeps there.
    private const string StateKey = @"Software\VsrSwitcher";
    private const string ValueName = "Hotkey";

    /// <summary>The saved hotkey: the default when none is saved, <see cref="Keys.None"/> when switched off.</summary>
    public static Keys Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StateKey);
            if (key?.GetValue(ValueName) is string text)
            {
                if (text.Length == 0)
                {
                    return Keys.None;
                }

                if (TryParse(text, out var saved) && Problem(saved) is null)
                {
                    return saved;
                }
            }
        }
        catch (Exception)
        {
            // Unreadable — fall back to the default.
        }

        return Default;
    }

    /// <summary>Saves the hotkey; <see cref="Keys.None"/> is saved as an empty string, meaning off.</summary>
    public static void Save(Keys hotkey)
    {
        using var key = Registry.CurrentUser.CreateSubKey(StateKey);
        key.SetValue(ValueName, hotkey == Keys.None ? string.Empty : Format(hotkey), RegistryValueKind.String);
    }

    /// <summary>"Ctrl+Alt+Shift+V" — both what the user sees and what is saved.</summary>
    public static string Format(Keys hotkey) =>
        hotkey == Keys.None ? "None" : FormatModifiers(hotkey) + KeyName(hotkey & Keys.KeyCode);

    /// <summary>The modifier part alone, with a trailing '+': "Ctrl+Alt+".</summary>
    public static string FormatModifiers(Keys hotkey) =>
        (hotkey.HasFlag(Keys.Control) ? "Ctrl+" : "") +
        (hotkey.HasFlag(Keys.Alt) ? "Alt+" : "") +
        (hotkey.HasFlag(Keys.Shift) ? "Shift+" : "");

    public static bool TryParse(string text, out Keys hotkey)
    {
        hotkey = Keys.None;
        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        foreach (var part in parts[..^1])
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": hotkey |= Keys.Control; break;
                case "alt": hotkey |= Keys.Alt; break;
                case "shift": hotkey |= Keys.Shift; break;
                default: return false;
            }
        }

        var last = parts[^1];
        if (last.Length == 1 && char.IsAsciiDigit(last[0]))
        {
            hotkey |= Keys.D0 + (last[0] - '0');
            return true;
        }

        // Only a plain key code may come last — not a modifier flag such as "Control".
        if (!Enum.TryParse<Keys>(last, ignoreCase: true, out var code) || (code & ~Keys.KeyCode) != 0)
        {
            return false;
        }

        hotkey |= code;
        return true;
    }

    /// <summary>
    /// Why <paramref name="hotkey"/> cannot be used, or null if it can. Whether another program
    /// already holds it is a separate question, answered by trying to register it.
    /// </summary>
    public static string? Problem(Keys hotkey)
    {
        var code = hotkey & Keys.KeyCode;
        if (IsModifierKey(code))
        {
            return "Press a key together with the modifiers.";
        }

        if (!hotkey.HasFlag(Keys.Control) && !hotkey.HasFlag(Keys.Alt))
        {
            return "Include Ctrl or Alt — otherwise the hotkey would take that key away from typing.";
        }

        if (code == Keys.F12)
        {
            return "Windows reserves F12 for debuggers.";
        }

        return null;
    }

    /// <summary>Something worth knowing about an otherwise usable hotkey, or null.</summary>
    public static string? Caution(Keys hotkey) =>
        hotkey.HasFlag(Keys.Control) && hotkey.HasFlag(Keys.Alt) && !hotkey.HasFlag(Keys.Shift)
            ? "On keyboard layouts with an AltGr key, Ctrl+Alt+key can be a typed character, which the hotkey would then block."
            : null;

    public static bool IsModifierKey(Keys code) =>
        code is Keys.None
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;

    private static string KeyName(Keys code) =>
        code is >= Keys.D0 and <= Keys.D9 ? ((char)('0' + (code - Keys.D0))).ToString() : code.ToString();
}
