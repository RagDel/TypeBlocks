using TypeBlocks.Core;

namespace TypeBlocks;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Length == 2 && args[0] == "--smoke-test") return SmokeTest.Run(args[1]);
        using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceActivation.EventName);
        using var instance = new Mutex(true, @"Local\TypeBlocks.Desktop", out bool firstInstance);
        if (!firstInstance)
        {
            if (!args.Contains("--background")) InstanceActivation.RequestShow(showSignal);
            return 0;
        }
        // Hold the legacy name too, so an old executable cannot start recording alongside this one.
        using var legacyInstance = new Mutex(true, @"Local\TypeMemory.Desktop", out bool legacyAvailable);
        if (!legacyAvailable)
        {
            MessageBox.Show("The previous Type Memory app is still running. Choose Exit from its tray menu, then open TypeBlocks again. Your password and encrypted history will be kept.",
                "TypeBlocks", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
        try
        {
            AppSettings.MigrateLegacyDataDirectory();
            StartupRegistration.MigrateLegacyRegistration();
            using var activation = new InstanceActivation(showSignal);
            bool background = args.Contains("--background");
            if (!VaultStore.Exists(AppSettings.VaultDirectory))
            {
                // Never initialize or start recording silently before the owner chooses a password.
                if (background) return 0;
                using var setup = new SetupForm();
                activation.SetHandler(() => Ui.BringForward(setup));
                if (setup.ShowDialog() != DialogResult.OK) return 0;
                activation.SetHandler(null);
                try { StartupRegistration.SetEnabled(setup.StartAtSignIn); }
                catch (Exception error) { Ui.Error(null, new IOException("History is ready, but startup could not be configured: " + error.Message)); }
            }
            var settings = AppSettings.Load();
            using var app = new TrayApplication(settings, background);
            activation.SetHandler(app.ShowWindow);
            Application.Run(app);
            return 0;
        }
        catch (Exception error) { Ui.Error(null, error); return 1; }
    }
}
