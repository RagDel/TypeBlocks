using System.Text.Json;
using Microsoft.Win32;

namespace TypeBlocks;

internal sealed class AppSettings
{
    public bool Paused { get; set; }
    public RecordingHotkey ToggleHotkey { get; set; } = RecordingHotkey.Default;
    public AppTheme Theme { get; set; } = AppTheme.Light;
    public int AutoLockMinutes { get; set; } = 60;
    public string[] ExcludedApplications { get; set; } = ["1password", "bitwarden", "keepass", "keepassxc"];

    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TypeBlocks");
    public static string VaultDirectory => Path.Combine(DataDirectory, "History");

    public static void MigrateLegacyDataDirectory(string? localDataRoot = null)
    {
        localDataRoot ??= Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string legacy = Path.Combine(localDataRoot, "TypeMemory");
        string current = Path.Combine(localDataRoot, "TypeBlocks");
        if (!Directory.Exists(legacy)) return;
        if (Directory.Exists(current))
            throw new IOException("Two app data folders exist, so TypeBlocks has stopped without changing either one. " +
                "Back up both folders, then keep the history you want to use in TypeBlocks and move the other folder elsewhere before restarting.\n\n" +
                legacy + "\n" + current);
        // A same-volume directory rename preserves the encrypted vault and settings byte for byte.
        Directory.Move(legacy, current);
    }

    public static AppSettings Load()
    {
        string file = Path.Combine(DataDirectory, "settings.json");
        if (!File.Exists(file)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? throw new InvalidDataException("Settings could not be read.");
        settings.Normalize();
        return settings;
    }

    internal void Normalize()
    {
        ExcludedApplications ??= [];
        if (ToggleHotkey is null || !ToggleHotkey.IsValid(out _)) ToggleHotkey = RecordingHotkey.Default;
        if (!Enum.IsDefined(Theme)) Theme = AppTheme.Light;
        if (AutoLockMinutes is < 1 or > 1440) AutoLockMinutes = 60;
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);
        string file = Path.Combine(DataDirectory, "settings.json");
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
}

internal static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string CurrentName = "TypeBlocks";
    private const string LegacyName = "TypeMemory";
    public static bool Enabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(CurrentName) is string || key?.GetValue(LegacyName) is string;
        }
    }

    public static void MigrateLegacyRegistration()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key?.GetValue(LegacyName) is not string) return;

        // Preserve Windows' own enabled/disabled metadata without interpreting its format.
        using var approved = Registry.CurrentUser.OpenSubKey(StartupApprovedKey, writable: true);
        if (approved?.GetValue(LegacyName) is { } state && approved.GetValue(CurrentName) is null)
            approved.SetValue(CurrentName, state, approved.GetValueKind(LegacyName));

        key.SetValue(CurrentName, StartupCommand());
        key.DeleteValue(LegacyName, false);
        approved?.DeleteValue(LegacyName, false);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            key.SetValue(CurrentName, StartupCommand());
        }
        else key.DeleteValue(CurrentName, false);
        key.DeleteValue(LegacyName, false);
    }

    private static string StartupCommand()
    {
        string path = Environment.ProcessPath ?? throw new InvalidOperationException("The executable location is unavailable.");
        return $"\"{path}\" --background";
    }
}
