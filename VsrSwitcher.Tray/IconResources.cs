namespace VsrSwitcher.Tray;

/// <summary>The icons embedded in the tray app.</summary>
internal static class IconResources
{
    /// <summary>Loads an embedded .ico, at the frame closest to <paramref name="size"/>.</summary>
    public static Icon Load(string name, Size size)
    {
        using var stream = typeof(IconResources).Assembly.GetManifestResourceStream($"VsrSwitcher.Tray.Resources.{name}")
            ?? throw new InvalidOperationException($"missing icon resource {name}");
        return new Icon(stream, size);
    }
}
