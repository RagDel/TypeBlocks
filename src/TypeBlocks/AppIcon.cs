namespace TypeBlocks;

internal static class AppIcon
{
    internal const string ResourceName = "TypeBlocks.Assets.TypeBlocks.ico";

    // Process-wide icon shared by forms and the tray; its embedded data also works in a single-file build.
    public static Icon Application { get; } = Load();

    private static Icon Load()
    {
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException("The TypeBlocks application icon is missing.");
        using var icon = new Icon(stream, new Size(32, 32));
        return (Icon)icon.Clone();
    }
}
