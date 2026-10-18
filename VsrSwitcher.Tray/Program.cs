namespace VsrSwitcher.Tray;

internal static class Program
{
    /// <summary>Held by the running copy for its whole life: one copy per user session.</summary>
    private const string InstanceName = @"Local\VsrSwitcher.Tray";

    /// <summary>Signalled by <c>--exit</c> to ask the running copy to close.</summary>
    private const string ExitEventName = @"Local\VsrSwitcher.Tray.Exit";

    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(10);

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase)))
        {
            return RequestExit();
        }

        // A second copy would put a duplicate pair of icons in the tray.
        using var instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            return 0;
        }

        using var exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName);

        ApplicationConfiguration.Initialize();

        // Installed before the tray is built, so the state watcher it creates reports changes on
        // this (the UI) thread.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        Application.Run(new TrayContext(exitRequested));

        // Released explicitly, so a waiting --exit sees a clean exit rather than an abandoned mutex.
        instance.ReleaseMutex();
        return 0;
    }

    /// <summary>
    /// <c>--exit</c>: asks the running copy to close, the way its Exit menu item does, and waits
    /// until it has — so an installer can replace or remove the files, and no stale icon is left
    /// behind as a killed process would leave. Exit code 0 when it has closed or was not running,
    /// 1 when it is still running after <see cref="ExitTimeout"/>.
    /// </summary>
    private static int RequestExit()
    {
        if (!EventWaitHandle.TryOpenExisting(ExitEventName, out var exitRequested))
        {
            return 0;
        }

        using (exitRequested)
        {
            exitRequested.Set();
        }

        // The running copy holds the instance mutex until it ends, so being able to take it means
        // the copy is gone.
        using var instance = new Mutex(initiallyOwned: false, InstanceName);
        try
        {
            if (!instance.WaitOne(ExitTimeout))
            {
                return 1;
            }
        }
        catch (AbandonedMutexException)
        {
            // It ended without releasing the mutex (killed, say): gone all the same, and the
            // mutex is now ours.
        }

        instance.ReleaseMutex();
        return 0;
    }
}
