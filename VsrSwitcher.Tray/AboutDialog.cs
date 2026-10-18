using System.Diagnostics;
using System.Reflection;

namespace VsrSwitcher.Tray;

/// <summary>
/// Name, version, copyright and the author's GitHub page. Everything but the link is read from the
/// assembly, so it stays in step with Directory.Build.props and the git tag the build came from.
/// </summary>
internal sealed class AboutDialog : Form
{
    private const string AuthorUrl = "https://github.com/Alex-D-Green";

    private readonly PictureBox _icon;

    public AboutDialog()
    {
        var assembly = typeof(AboutDialog).Assembly;
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "VSR Switcher";
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? string.Empty;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"About {product}";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        // The 64 px frame, scaled to fit: font-drawn at that size, so it stays clean when scaled.
        using (var appIcon = IconResources.Load("on.ico", new Size(64, 64)))
        {
            _icon = new PictureBox
            {
                Image = appIcon.ToBitmap(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Size = new Size(48, 48),
                Margin = new Padding(0, 4, 16, 0),
            };
        }

        var title = new Label
        {
            Text = product,
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.6f, FontStyle.Bold),
            Margin = Padding.Empty,
        };

        var versionLabel = new Label { Text = $"Version {version}", AutoSize = true, Margin = new Padding(0, 2, 0, 10) };

        var description = new Label
        {
            Text = "Turns NVIDIA RTX Video Super Resolution on and off from the tray.",
            AutoSize = true,
            MaximumSize = new Size(340, 0),
            Margin = new Padding(0, 0, 0, 10),
        };

        var copyrightLabel = new Label { Text = copyright, AutoSize = true, Margin = Padding.Empty };

        var link = new LinkLabel { Text = "github.com/Alex-D-Green", AutoSize = true, Margin = new Padding(0, 2, 0, 0) };
        link.LinkClicked += (_, _) => Open(AuthorUrl);

        var legal = new Label
        {
            Text = "MIT License. Not affiliated with NVIDIA.",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 10, 0, 0),
        };

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            MinimumSize = new Size(80, 0),
            Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 16, 0, 0),
        };
        AcceptButton = ok;
        CancelButton = ok;

        var text = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Margin = Padding.Empty,
        };
        text.Controls.AddRange(new Control[] { title, versionLabel, description, copyrightLabel, link, legal });

        // Docked, so the form's padding applies: a form's Padding affects only docked controls.
        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Dock = DockStyle.Fill };
        layout.Controls.Add(_icon, 0, 0);
        layout.Controls.Add(text, 1, 0);
        layout.Controls.Add(ok, 1, 1);
        Controls.Add(layout);

        Shown += (_, _) => Activate();
    }

    private void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open {url}: {ex.Message}", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // PictureBox does not dispose the image it shows.
            _icon.Image?.Dispose();
        }

        base.Dispose(disposing);
    }
}
