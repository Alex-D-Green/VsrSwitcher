using System.Runtime.InteropServices;

namespace VsrSwitcher.Tray;

/// <summary>
/// A system-wide hotkey, registered with Windows for a hidden message-only window. Windows refuses
/// a combination that another program already holds, so registering doubles as the check for
/// whether a combination is free.
/// </summary>
internal sealed class GlobalHotkey : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;
    private const int ProbeId = 0x7000;

    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;

    /// <summary>Holding the keys down fires once, rather than repeating.</summary>
    private const uint ModNoRepeat = 0x4000;

    /// <summary>HWND_MESSAGE: the window exists only to receive WM_HOTKEY.</summary>
    private static readonly IntPtr MessageOnlyParent = new(-3);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    private bool _registered;

    public event Action? Pressed;

    public GlobalHotkey() => CreateHandle(new CreateParams { Parent = MessageOnlyParent });

    /// <summary>
    /// Registers <paramref name="hotkey"/> in place of any previous one; <see cref="Keys.None"/>
    /// just releases it. Returns false if another program holds the combination.
    /// </summary>
    public bool Register(Keys hotkey)
    {
        Unregister();
        if (hotkey == Keys.None)
        {
            return true;
        }

        _registered = RegisterHotKey(Handle, HotkeyId, NativeModifiers(hotkey), (uint)(hotkey & Keys.KeyCode));
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    /// <summary>Whether no other program holds <paramref name="hotkey"/>: claims it for a moment, then lets go.</summary>
    public static bool IsAvailable(Keys hotkey)
    {
        if (!RegisterHotKey(IntPtr.Zero, ProbeId, NativeModifiers(hotkey), (uint)(hotkey & Keys.KeyCode)))
        {
            return false;
        }

        UnregisterHotKey(IntPtr.Zero, ProbeId);
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == HotkeyId)
        {
            Pressed?.Invoke();
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }

    private static uint NativeModifiers(Keys hotkey) =>
        (hotkey.HasFlag(Keys.Alt) ? ModAlt : 0) |
        (hotkey.HasFlag(Keys.Control) ? ModControl : 0) |
        (hotkey.HasFlag(Keys.Shift) ? ModShift : 0) |
        ModNoRepeat;
}
