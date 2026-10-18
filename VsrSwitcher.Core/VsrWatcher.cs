using System.Runtime.InteropServices;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace VsrSwitcher.Core;

/// <summary>
/// Reports every change to the persisted VSR settings, whoever made it — NVIDIA App, VSR Switcher, or
/// anything else that goes through the driver, since the driver writes the registry itself. It
/// waits on registry change notifications rather than polling, so it costs nothing while nothing
/// changes: measured unelevated on 2026-10-17, the adapter key stayed silent for 15 s at rest and
/// signalled as soon as a setting changed.
///
/// NVIDIA App writes while its dialog is still open, before OK, and reverts on Cancel — so what is
/// reported follows the dialog's live preview, which is what the driver is actually doing.
/// </summary>
public sealed class VsrWatcher : IDisposable
{
    /// <summary>
    /// How often to re-read even without a notification. A safety net only — for instance if a
    /// driver reinstall recreates the key and the old handle goes quiet.
    /// </summary>
    private static readonly TimeSpan SafetyNet = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>REG_NOTIFY_CHANGE_LAST_SET: a value was added, changed or deleted.</summary>
    private const int NotifyChangeLastSet = 0x4;

    [DllImport("advapi32.dll")]
    private static extern int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        int filter,
        SafeWaitHandle notifyEvent,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);

    private readonly VsrController _controller;
    private readonly SynchronizationContext? _context;
    private readonly ManualResetEvent _stop = new(false);
    private readonly Thread _thread;
    private VsrState? _last;

    /// <summary>
    /// Raised with the new state whenever a VSR value changes — on the synchronization context the
    /// watcher was created on (the UI thread, in a tray app), else on the watcher's own thread.
    /// </summary>
    public event Action<VsrState>? Changed;

    public VsrWatcher(VsrController controller)
    {
        _controller = controller;
        _context = SynchronizationContext.Current;
        _thread = new Thread(Run) { IsBackground = true, Name = nameof(VsrWatcher) };
    }

    /// <summary>Starts watching. Changes are reported relative to the state at this moment.</summary>
    public void Start()
    {
        _last = _controller.ReadState();
        _thread.Start();
    }

    private void Run()
    {
        using var changed = new AutoResetEvent(false);
        RegistryKey? key = null;

        // A notification is one-shot: armed, it fires once. Re-arming only after it has fired
        // (rather than after every safety-net timeout) keeps registrations from piling up.
        var armed = false;

        while (!_stop.WaitOne(0))
        {
            try
            {
                if (key is null)
                {
                    key = Registry.LocalMachine.OpenSubKey(_controller.RegistrySubKey);
                    armed = false;
                }

                if (key is null)
                {
                    throw new AdapterNotFoundException($"cannot open {_controller.RegistryPath}");
                }

                if (!armed)
                {
                    var result = RegNotifyChangeKeyValue(
                        key.Handle, false, NotifyChangeLastSet, changed.SafeWaitHandle, true);
                    if (result != 0)
                    {
                        throw new IOException($"RegNotifyChangeKeyValue failed with error {result}");
                    }

                    armed = true;
                }

                var signalled = WaitHandle.WaitAny([_stop, changed], SafetyNet);
                if (signalled == 0)
                {
                    break;
                }

                if (signalled == 1)
                {
                    armed = false;
                }

                Report(_controller.ReadState());
            }
            catch (Exception)
            {
                // The key vanished or its handle went stale — reopen after a pause.
                key?.Dispose();
                key = null;

                if (_stop.WaitOne(RetryDelay))
                {
                    break;
                }
            }
        }

        key?.Dispose();
    }

    private void Report(VsrState state)
    {
        // The adapter key holds plenty of values that are not ours; ignore changes to those.
        if (state.Equals(_last))
        {
            return;
        }

        _last = state;

        if (_context is null)
        {
            Changed?.Invoke(state);
        }
        else
        {
            _context.Post(_ => Changed?.Invoke(state), null);
        }
    }

    public void Dispose()
    {
        _stop.Set();

        // Only once the thread has ended: it still waits on this event until then.
        if (!_thread.IsAlive || _thread.Join(TimeSpan.FromSeconds(2)))
        {
            _stop.Dispose();
        }
    }
}
