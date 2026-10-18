namespace VsrSwitcher.Tray;

/// <summary>
/// Lets the user choose the hotkey by pressing it. Each combination is checked as it is pressed —
/// against the rules in <see cref="HotkeySetting"/>, and against other programs by briefly
/// registering it — so OK is only enabled for a hotkey that will work.
/// </summary>
internal sealed class HotkeyDialog : Form
{
    private static readonly Color Good = Color.ForestGreen;
    private static readonly Color Warning = Color.DarkGoldenrod;
    private static readonly Color Bad = Color.Firebrick;

    private readonly TextBox _capture;
    private readonly Label _status;
    private readonly Button _ok;

    /// <summary>The chosen hotkey; <see cref="Keys.None"/> for no hotkey.</summary>
    public Keys Hotkey { get; private set; }

    public HotkeyDialog(Keys current)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = "Super Resolution hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        var prompt = new Label
        {
            Text = "Press the keys that should turn Super Resolution on and off.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8),
        };

        _capture = new TextBox
        {
            ReadOnly = true,
            ShortcutsEnabled = false,
            TabStop = true,
            TextAlign = HorizontalAlignment.Center,
            BackColor = SystemColors.Window,
            Font = new Font(Font.FontFamily, Font.Size * 1.5f),
            Dock = DockStyle.Fill,
            MinimumSize = new Size(360, 0),
        };
        _capture.KeyUp += (_, _) => ShowCandidateWhenReleased();

        _status = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Margin = new Padding(0, 6, 0, 14),
        };

        var useDefault = new Button { Text = $"Default ({HotkeySetting.Format(HotkeySetting.Default)})", AutoSize = true };
        useDefault.Click += (_, _) => Choose(HotkeySetting.Default);

        var none = new Button { Text = "None", AutoSize = true };
        none.Click += (_, _) => Choose(Keys.None);

        _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(80, 0) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(80, 0) };
        AcceptButton = _ok;
        CancelButton = cancel;

        var buttons = new TableLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 5, Margin = Padding.Empty };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(useDefault, 0, 0);
        buttons.Controls.Add(none, 1, 0);
        buttons.Controls.Add(_ok, 3, 0);
        buttons.Controls.Add(cancel, 4, 0);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(prompt);
        layout.Controls.Add(_capture);
        layout.Controls.Add(_status);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        Shown += (_, _) =>
        {
            Activate();
            _capture.Focus();
        };

        Choose(current);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Plain Tab, Enter and Esc keep their dialog meanings; anything else pressed while the box
        // has focus is a candidate hotkey (none of those three is a valid hotkey on its own).
        if (!_capture.Focused || keyData is Keys.Tab or (Keys.Tab | Keys.Shift) or Keys.Enter or Keys.Escape)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        var code = keyData & Keys.KeyCode;
        if (HotkeySetting.IsModifierKey(code))
        {
            // Only modifiers so far: show them, and wait for the key.
            _capture.Text = HotkeySetting.FormatModifiers(keyData) + "…";
            return true;
        }

        Choose(keyData & (Keys.KeyCode | Keys.Control | Keys.Alt | Keys.Shift));
        return true;
    }

    private void ShowCandidateWhenReleased()
    {
        // Modifiers pressed and released without a key: go back to showing the candidate.
        if (ModifierKeys == Keys.None)
        {
            _capture.Text = Hotkey == Keys.None ? "None" : HotkeySetting.Format(Hotkey);
        }
    }

    private void Choose(Keys hotkey)
    {
        Hotkey = hotkey;
        _capture.Text = hotkey == Keys.None ? "None" : HotkeySetting.Format(hotkey);

        if (hotkey == Keys.None)
        {
            Report("Super Resolution will have no hotkey.", SystemColors.GrayText, usable: true);
        }
        else if (HotkeySetting.Problem(hotkey) is { } problem)
        {
            Report(problem, Bad, usable: false);
        }
        else if (!GlobalHotkey.IsAvailable(hotkey))
        {
            Report("Another program already uses this combination.", Bad, usable: false);
        }
        else if (HotkeySetting.Caution(hotkey) is { } caution)
        {
            Report("Available. " + caution, Warning, usable: true);
        }
        else
        {
            Report("Available.", Good, usable: true);
        }

        _capture.Focus();
    }

    private void Report(string message, Color colour, bool usable)
    {
        _status.Text = message;
        _status.ForeColor = colour;
        _ok.Enabled = usable;
    }
}
