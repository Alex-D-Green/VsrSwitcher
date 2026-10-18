using VsrSwitcher.Core;

namespace VsrSwitcher.Tray;

/// <summary>
/// The tray app: two notification icons, one per state, with only the one matching the current
/// state present. Windows remembers "show on the taskbar / hide in the overflow" per icon, so
/// presenting ON and OFF as separate icons lets the user show one and hide the other.
///
/// State comes from the registry and is followed through <see cref="VsrWatcher"/>, so changes made
/// in NVIDIA App or with vsrswitch flip the icon too. Every change is made by running vsrswitch.exe:
/// NVIDIA App's NvCpl.dll is never loaded into this process.
/// </summary>
internal sealed class TrayContext : ApplicationContext
{
    private const string Title = "RTX Super Resolution";

    private static readonly (SuperResValue Level, string Label)[] Levels =
    [
        (SuperResValue.Auto, "Auto"),
        (SuperResValue.Level1, "1 — Very low"),
        (SuperResValue.Level2, "2 — Low"),
        (SuperResValue.Level3, "3 — Medium"),
        (SuperResValue.Level4, "4 — High"),
    ];

    private static readonly (GpuUtilization Level, string Label)[] GpuLevels =
    [
        (GpuUtilization.Low, "Low"),
        (GpuUtilization.Medium, "Medium"),
        (GpuUtilization.High, "High"),
    ];

    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _qualityMenu;
    private readonly ToolStripMenuItem _gpuMenu;
    private readonly ToolStripMenuItem _indicatorItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly GlobalHotkey _hotkey = new();
    private bool _choosingHotkey;
    private bool _showingAbout;
    private readonly NotifyIcon _offIcon;
    private readonly NotifyIcon _onIcon;
    private readonly VsrSwitchCli _cli = new();
    private readonly VsrController? _controller;
    private readonly VsrWatcher? _watcher;
    private VsrState? _state;
    private bool _busy;
    private readonly SynchronizationContext _context;
    private readonly RegisteredWaitHandle _exitWait;

    /// <param name="exitRequested">Signalled by another copy started with <c>--exit</c>.</param>
    public TrayContext(EventWaitHandle exitRequested)
    {
        _context = SynchronizationContext.Current
            ?? throw new InvalidOperationException("the tray must be created on the UI thread");
        _exitWait = ThreadPool.RegisterWaitForSingleObject(
            exitRequested, (_, _) => _context.Post(_ => ExitWhenDialogsClosed(), null), null,
            Timeout.Infinite, executeOnlyOnce: true);

        _enabledItem = new ToolStripMenuItem("Super Resolution", null, (_, _) => Change("toggle"));
        _qualityMenu = new ToolStripMenuItem("Quality");
        foreach (var (level, label) in Levels)
        {
            _qualityMenu.DropDownItems.Add(
                new ToolStripMenuItem(label, null, (_, _) => Change("on", Labels.Short(level))) { Tag = level });
        }

        // NVIDIA App's label for what its internals (and vsrswitch gpu-util) call GPU utilization.
        _gpuMenu = new ToolStripMenuItem("GPU priority (Auto only)");
        foreach (var (level, label) in GpuLevels)
        {
            _gpuMenu.DropDownItems.Add(
                new ToolStripMenuItem(label, null, (_, _) => Change("gpu-util", Labels.Describe(level))) { Tag = level });
        }

        _indicatorItem = new ToolStripMenuItem("Show status indicator on video", null,
            (_, _) => Change("indicator", _state?.IndicatorShown == true ? "off" : "on"));

        _menu.Items.AddRange(new ToolStripItem[]
        {
            _enabledItem,
            new ToolStripSeparator(),
            _qualityMenu,
            _gpuMenu,
            _indicatorItem,
            new ToolStripSeparator(),
            _autoStartItem = new ToolStripMenuItem("Start with Windows", null, (_, _) => ToggleAutoStart()),
            new ToolStripMenuItem("Hotkey…", null, (_, _) => ChooseHotkey()),
            new ToolStripMenuItem("About VSR Switcher", null, (_, _) => ShowAbout()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => ExitThread()),
        });

        // The creation order IS the identity Windows remembers each icon by. WinForms numbers
        // NotifyIcons 1, 2, ... as they are created, and Windows keys its show/hide setting on the
        // executable's path plus that number. OFF must stay first and ON second, or every user's
        // settings would swap.
        _offIcon = CreateIcon("off.ico", $"{Title}: OFF");
        _onIcon = CreateIcon("on.ico", $"{Title}: ON");

        try
        {
            _controller = VsrController.Open();
            _watcher = new VsrWatcher(_controller);
            _watcher.Changed += Show;
            Show(_controller.ReadState());
            _watcher.Start();
        }
        catch (Exception ex)
        {
            Show(null);
            Notify(ex.Message, ToolTipIcon.Error);
        }

        _hotkey.Pressed += () => Change("toggle");
        ApplyHotkey(HotkeySetting.Load());

        // Read each time the menu opens: Task Manager can switch start-up off behind our back.
        _menu.Opening += (_, _) => _autoStartItem.Checked = ReadAutoStart();
    }

    private void ToggleAutoStart()
    {
        try
        {
            if (AutoStart.IsEnabled())
            {
                AutoStart.Disable();
            }
            else
            {
                AutoStart.Enable();
            }
        }
        catch (Exception ex)
        {
            Notify($"Start with Windows could not be changed: {ex.Message}", ToolTipIcon.Error);
        }

        _autoStartItem.Checked = ReadAutoStart();
    }

    private static bool ReadAutoStart()
    {
        try
        {
            return AutoStart.IsEnabled();
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Registers the hotkey and shows it beside the menu's Super Resolution item. A combination
    /// another program already holds is reported, and Super Resolution is left without a hotkey.
    /// </summary>
    private void ApplyHotkey(Keys hotkey)
    {
        var registered = _hotkey.Register(hotkey);
        _enabledItem.ShortcutKeyDisplayString = registered && hotkey != Keys.None ? HotkeySetting.Format(hotkey) : null;

        if (!registered)
        {
            Notify($"{HotkeySetting.Format(hotkey)} is already used by another program, so Super Resolution " +
                   "has no hotkey. Choose another under Hotkey….", ToolTipIcon.Warning);
        }
    }

    private void ChooseHotkey()
    {
        if (_choosingHotkey)
        {
            return;
        }

        _choosingHotkey = true;
        var hotkey = HotkeySetting.Load();

        // Released while the dialog is open, so pressing the current combination reaches the
        // dialog instead of toggling Super Resolution — and shows as available, which it is.
        _hotkey.Unregister();

        try
        {
            using var dialog = new HotkeyDialog(hotkey);
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                hotkey = dialog.Hotkey;
                HotkeySetting.Save(hotkey);
            }
        }
        catch (Exception ex)
        {
            Notify($"The hotkey could not be saved: {ex.Message}", ToolTipIcon.Error);
        }
        finally
        {
            _choosingHotkey = false;
            ApplyHotkey(hotkey);
        }
    }

    private void ShowAbout()
    {
        // One at a time: the menu stays usable while a dialog is open.
        if (_showingAbout)
        {
            return;
        }

        _showingAbout = true;
        try
        {
            using var dialog = new AboutDialog();
            dialog.ShowDialog();
        }
        finally
        {
            _showingAbout = false;
        }
    }

    private NotifyIcon CreateIcon(string resource, string tooltip)
    {
        var icon = new NotifyIcon
        {
            Icon = IconResources.Load(resource, SystemInformation.SmallIconSize),
            Text = tooltip,
            ContextMenuStrip = _menu,
            Visible = false,
        };

        icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Change("toggle");
            }
        };

        return icon;
    }

    /// <summary>Shows the icon for <paramref name="state"/> and brings the menu up to date.
    /// Null means the state is unknown: the OFF icon, with every change disabled.</summary>
    private void Show(VsrState? state)
    {
        _state = state;
        var on = state?.Enabled == true;

        // Add the new icon before removing the old one, so the tray never has neither.
        (on ? _onIcon : _offIcon).Visible = true;
        (on ? _offIcon : _onIcon).Visible = false;

        UpdateMenu();
    }

    private void UpdateMenu()
    {
        var usable = _state is not null && !_busy;
        _enabledItem.Enabled = _qualityMenu.Enabled = _gpuMenu.Enabled = _indicatorItem.Enabled = usable;

        _enabledItem.Checked = _state?.Enabled == true;
        _indicatorItem.Checked = _state?.IndicatorShown == true;

        foreach (ToolStripMenuItem item in _qualityMenu.DropDownItems)
        {
            item.Checked = _state?.Enabled == true && _state.EffectiveSuperRes == (SuperResValue)item.Tag!;
        }

        foreach (ToolStripMenuItem item in _gpuMenu.DropDownItems)
        {
            item.Checked = _state?.GpuUtil == (GpuUtilization)item.Tag!;
        }
    }

    /// <summary>
    /// Runs one vsrswitch command. One at a time: a click while a change is still running is
    /// ignored rather than queued, so a double-click cannot flip the state twice.
    /// </summary>
    private async void Change(params string[] args)
    {
        if (_busy || _state is null)
        {
            return;
        }

        _busy = true;
        UpdateMenu();

        try
        {
            var result = await _cli.RunAsync(args);
            if (!result.Succeeded)
            {
                Notify(result.Message, ToolTipIcon.Warning);
            }
        }
        finally
        {
            _busy = false;

            // The watcher reports the change as well; reading here too means the icon is current
            // even if a notification were missed.
            Show(ReadStateOrKeep());
        }
    }

    private VsrState? ReadStateOrKeep()
    {
        try
        {
            return _controller?.ReadState() ?? _state;
        }
        catch (Exception)
        {
            return _state;
        }
    }

    private void Notify(string message, ToolTipIcon kind)
    {
        // Balloons appear as Windows notifications even when the icon is in the overflow.
        var visible = _onIcon.Visible ? _onIcon : _offIcon;
        visible.ShowBalloonTip(10_000, Title, message.Length > 250 ? message[..250] + "…" : message, kind);
    }

    /// <summary>
    /// Exits as the Exit menu item does, for <c>--exit</c>. An open dialog runs its own message
    /// loop, so it is closed first and the exit retried once that loop has returned.
    /// </summary>
    private void ExitWhenDialogsClosed()
    {
        var open = Application.OpenForms.Cast<Form>().ToList();
        if (open.Count > 0)
        {
            foreach (var form in open)
            {
                form.Close();
            }

            _context.Post(_ => ExitWhenDialogsClosed(), null);
            return;
        }

        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _exitWait.Unregister(null);
        _watcher?.Dispose();
        _hotkey.Dispose();

        _onIcon.Visible = false;
        _offIcon.Visible = false;
        _onIcon.Dispose();
        _offIcon.Dispose();
        _menu.Dispose();

        base.ExitThreadCore();
    }
}
