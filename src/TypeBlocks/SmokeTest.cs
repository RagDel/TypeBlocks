using System.Drawing.Imaging;
using TypeBlocks.Capture;
using TypeBlocks.Core;

namespace TypeBlocks;

// Explicit developer-only path. Uses synthetic content and a temporary vault;
// never enables recording or changes the owner's vault, settings, or startup entry.
internal static class SmokeTest
{
    private static Action<string>? _trace;
    public static int Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string progressFile = Path.Combine(outputDirectory, "progress.txt");
        File.WriteAllText(progressFile, "Starting synthetic smoke test\n");
        File.WriteAllText(Path.Combine(outputDirectory, "result.txt"), "INCOMPLETE: smoke test started but has not finished.");
        void Progress(string message) => File.AppendAllText(progressFile, message + "\n");
        _trace = Progress;
        string temporaryVault = Path.Combine(Path.GetTempPath(), "TypeBlocks-smoke-" + Guid.NewGuid().ToString("N"));
        const string password = "Synthetic test passphrase 42!";
        int result = 1;
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var context = new ApplicationContext();
        ThreadExceptionEventHandler reportUiFailure = (_, eventArgs) =>
        {
            File.WriteAllText(Path.Combine(outputDirectory, "result.txt"), eventArgs.Exception.ToString());
            Progress("Failed in a Windows UI callback");
            context.ExitThread();
        };
        Application.ThreadException += reportUiFailure;
        dispatcher.BeginInvoke(async () =>
        {
            try
            {
                Progress("Creating temporary encrypted vault");
                VerifyLegacyMigration(Path.Combine(temporaryVault, "migration"));
                using (var vault = VaultStore.Create(temporaryVault, password))
                    vault.Append(SampleEntries());
                Progress("Registering paused input window");
                using (var recorder = new KeyboardRecorder(captureOnlyProcessId: Environment.ProcessId))
                    if (!recorder.Paused || recorder.Handle == IntPtr.Zero) throw new Exception("Raw Input registration failed.");
                if (KeyboardRecorder.FormatKey(Keys.S, control: true) != "Ctrl+S") throw new Exception("Shortcut formatting failed.");
                if (KeyboardRecorder.FormatKey(Keys.Return, isExtended: true) != "Num Enter") throw new Exception("Keypad formatting failed.");
                Progress("Verifying global shortcut registration, settings and nonactivating status popup");
                await HotkeySmokeTests.Verify(outputDirectory);
                Progress("Verifying dropdown keyboard selection and multiline editor scrolling");
                ModernControlSmokeTests.Verify(PrepareForm);
                Progress("Rendering setup");
                AppIconSmokeTests.Verify(outputDirectory);
                Progress("Icon verification completed");
                using (var setup = new SetupForm()) { Progress("Setup constructed"); Render(setup, Path.Combine(outputDirectory, "setup.png")); }
                Progress("Rendering unlock");
                using (var unlock = new PasswordForm(recoverPassword: _ => { })) Render(unlock, Path.Combine(outputDirectory, "unlock.png"));
                Progress("Rendering settings");
                using (var settings = new SettingsForm(new AppSettings(), vaultDirectory: temporaryVault)) Render(settings, Path.Combine(outputDirectory, "settings.png"));
                using (var settings = new SettingsForm(new AppSettings(), vaultDirectory: temporaryVault))
                {
                    PrepareForm(settings);
                    settings.FitToWorkingArea(new Size(600, 560));
                    var save = settings.Controls.OfType<Button>().Single(button => button.Text == "Save settings");
                    settings.ActiveControl = save;
                    settings.ScrollControlIntoView(save);
                    if (!settings.ClientRectangle.Contains(save.Bounds)) throw new Exception("Settings save button is inaccessible in a small working area.");
                    Render(settings, Path.Combine(outputDirectory, "settings-small.png"));
                }
                Progress("Rendering history");
                var clock = new UnlockTestClock();
                var session = new HistoryUnlockSession(clock);
                Progress("Verifying synthetic modal unlock");
                session.Unlock(VerifySuccessfulUnlock(password, temporaryVault));
                using (var history = new HistoryForm(session, temporaryVault))
                {
                    Progress("Preparing synthetic history viewer");
                    PrepareForm(history);
                    await history.PreparePreviewForTest();
                    Progress("Verifying text modes and toolbar layout");
                    await history.VerifyTextViewForTest();
                    Render(history, Path.Combine(outputDirectory, "history.png"));
                    var originalSize = history.Size;
                    history.Size = history.MinimumSize;
                    Render(history, Path.Combine(outputDirectory, "history-small.png"));
                    history.Size = originalSize;
                    history.SelectTableForPreview();
                    Render(history, Path.Combine(outputDirectory, "history-table.png"));
                    Ui.SetTheme(AppTheme.Dark);
                    Render(history, Path.Combine(outputDirectory, "history-table-dark.png"));
                    history.SelectTextForPreview();
                    Render(history, Path.Combine(outputDirectory, "history-dark.png"));
                    history.SelectAllKeysForPreview();
                    Render(history, Path.Combine(outputDirectory, "history-all-keys-dark.png"));
                    Ui.SetTheme(AppTheme.Light);
                    Render(history, Path.Combine(outputDirectory, "history-all-keys.png"));
                    history.Close();
                }
                Progress("Verifying default one-hour unlock lifecycle and canceled authentication");
                await VerifyUnlockLifecycle(session, clock, password, temporaryVault);
                await VerifyCanceledUnlock();
                Progress("Verifying in-app settings navigation, theme preview and lock lifecycle");
                await VerifySettingsNavigation(password, temporaryVault, dispatcher.Handle);
                Progress("Verifying recovery dialogs and both themes with temporary credentials");
                await VerifyRecoveryUi(temporaryVault, password, outputDirectory);
                File.WriteAllText(Path.Combine(outputDirectory, "result.txt"), "PASS: paused Raw Input registration; legacy migration; global shortcut conflicts, rollback, rebinding and synthetic dispatch; resume queue boundaries; theme/settings persistence; nonactivating On/Off popup; embedded icon sizes/transparency; dropdown keyboard selection, cancellation and item clearing; multiline edit notifications, scroll bounds and editor focus; segmented views and text modes, search and sorting; configurable lock settings, default one-hour unlock and cancellation; in-app settings navigation, theme preview, cancel and lock lifecycle; staged recovery generation, confirmation and password reset; private clipboard payload flags; light/dark forms and constrained layouts rendered. No real input recorded, no physical shortcut injection, no system clipboard or startup changes.");
                result = 0;
                Progress("Completed");
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(outputDirectory, "result.txt"), error.ToString()); }
            finally
            {
                if (Directory.Exists(temporaryVault)) Directory.Delete(temporaryVault, true);
                context.ExitThread();
            }
        });
        try { Application.Run(context); }
        finally { Application.ThreadException -= reportUiFailure; _trace = null; }
        return result;
    }

    private static async Task VerifyRecoveryUi(string directory, string password, string outputDirectory)
    {
        using (var unavailable = new RecoveryForm(directory, resetPassword: true))
            Render(unavailable, Path.Combine(outputDirectory, "recovery-unconfigured.png"));
        string code;
        using (var generate = new RecoveryForm(directory, resetPassword: false))
        {
            PrepareForm(generate);
            await generate.GenerateForTest(password);
            code = generate.RecoveryCodeForTest;
            if (code.Length != 71) throw new Exception("The recovery dialog did not display its generated code.");
            Render(generate, Path.Combine(outputDirectory, "recovery-code.png"));
            Ui.SetTheme(AppTheme.Dark);
            Render(generate, Path.Combine(outputDirectory, "recovery-code-dark.png"));
            if (VaultStore.HasRecoveryCode(directory)) throw new Exception("A recovery code was activated before confirmation.");
            await generate.ActivateForTest();
            if (generate.DialogResult != DialogResult.OK || !VaultStore.HasRecoveryCode(directory)) throw new Exception("Recovery activation did not complete.");
            if (generate.RecoveryCodeForTest.Length != 0) throw new Exception("Closing recovery kept the displayed code.");
        }
        foreach (AppTheme theme in Enum.GetValues<AppTheme>())
        {
            Ui.SetTheme(theme);
            string suffix = theme == AppTheme.Dark ? "-dark" : "";
            using (var change = new PasswordChangeForm(directory)) Render(change, Path.Combine(outputDirectory, "change-password" + suffix + ".png"));
            using (var reset = new RecoveryForm(directory, resetPassword: true)) Render(reset, Path.Combine(outputDirectory, "recovery-reset" + suffix + ".png"));
            if (theme != AppTheme.Dark) continue;
            using (var setup = new SetupForm()) Render(setup, Path.Combine(outputDirectory, "setup-dark.png"));
            using (var unlock = new PasswordForm(recoverPassword: _ => { })) Render(unlock, Path.Combine(outputDirectory, "unlock-dark.png"));
            using (var settings = new SettingsForm(new AppSettings { Theme = theme }, vaultDirectory: directory))
                Render(settings, Path.Combine(outputDirectory, "settings-dark.png"));
        }
        const string replacement = "Synthetic recovered passphrase 43!";
        using (var reset = new RecoveryForm(directory, resetPassword: true))
        {
            PrepareForm(reset);
            await reset.ResetForTest(code, replacement);
            if (reset.DialogResult != DialogResult.OK) throw new Exception("Recovery reset dialog did not complete.");
            VaultStore.VerifyPassword(directory, replacement);
        }
        VaultStore.ChangePassword(directory, replacement, password);
        Ui.SetTheme(AppTheme.Light);
    }

    private static string VerifySuccessfulUnlock(string password, string directory)
    {
        using var unlock = new PasswordForm(value => Task.Run(() => VaultStore.VerifyPassword(directory, value)))
        {
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-32000, -32000)
        };
        unlock.Shown += async (_, _) => await unlock.VerifyForTest(password);
        if (unlock.ShowDialog() != DialogResult.OK || unlock.Password != password)
            throw new Exception("Successful modal verification did not return the verified password.");
        return unlock.Password;
    }

    private static async Task VerifyUnlockLifecycle(HistoryUnlockSession session, UnlockTestClock clock, string password, string directory)
    {
        if (!session.IsUnlocked) throw new Exception("Closing the viewer incorrectly locked the shared session.");
        var deadline = session.ExpiresAtUtc;
        clock.Advance(TimeSpan.FromMinutes(59));
        using (var reopened = new HistoryForm(session, directory))
        {
            PrepareForm(reopened);
            await reopened.PreparePreviewForTest();
            if (reopened.IsHistoryClearedForTest || session.ExpiresAtUtc != deadline) throw new Exception("Reopening history failed or extended the unlock deadline.");
            clock.Advance(TimeSpan.FromMinutes(1));
            reopened.CheckUnlockForTest();
            if (!reopened.IsDisposed || session.IsUnlocked || !reopened.IsHistoryClearedForTest) throw new Exception("The one-hour deadline did not lock and clear an open viewer.");
        }

        session.Unlock(password);
        using (var manual = new HistoryForm(session, directory))
        {
            PrepareForm(manual);
            await manual.PreparePreviewForTest();
            manual.Controls.OfType<Button>().Single(button => button.Text == "Lock && close").PerformClick();
            if (!manual.IsDisposed || session.IsUnlocked || !manual.IsHistoryClearedForTest) throw new Exception("Lock & close did not immediately revoke access.");
        }

        session.Unlock(password);
        using (var pending = new HistoryForm(session, directory))
        {
            PrepareForm(pending);
            await pending.PreparePreviewForTest();
            Task loading = pending.LoadDay();
            clock.Advance(TimeSpan.FromHours(1));
            session.CheckExpiry();
            await loading;
            if (!pending.IsDisposed || !pending.IsHistoryClearedForTest) throw new Exception("An expired decrypt repainted the closed viewer.");
        }

        session.Unlock(password);
        using (var changed = new HistoryForm(session, directory))
        {
            PrepareForm(changed);
            await changed.PreparePreviewForTest();
            Task loading = changed.LoadDay();
            session.Unlock(password);
            await loading;
            if (!changed.IsHistoryClearedForTest) throw new Exception("A decrypt from an older unlock session was displayed.");
            changed.Close();
        }
        session.Lock();
    }

    private static async Task VerifyCanceledUnlock()
    {
        foreach (bool verificationFails in new[] { false, true })
        {
            var verification = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var unlock = new PasswordForm(_ => verification.Task);
            PrepareForm(unlock);
            Task pending = unlock.VerifyForTest("Synthetic canceled password 42!");
            unlock.CancelUnlock();
            if (verificationFails) verification.SetException(new InvalidOperationException("Synthetic verification failure"));
            else verification.SetResult();
            await pending;
            if (unlock.DialogResult == DialogResult.OK) throw new Exception("Canceled verification unlocked history after closing.");
            bool passwordUnavailable = false;
            try { _ = unlock.Password; } catch (InvalidOperationException) { passwordUnavailable = true; }
            if (!passwordUnavailable) throw new Exception("Canceled verification retained a credential.");
        }
    }

    private static async Task VerifySettingsNavigation(string password, string directory, IntPtr ownerHandle)
    {
        AppTheme originalTheme = Ui.Theme;
        var session = new HistoryUnlockSession();
        session.Unlock(password);
        using var history = new HistoryForm(session, directory);
        PrepareForm(history);
        await history.PreparePreviewForTest();
        int requests = 0;
        history.SettingsRequested += () =>
        {
            requests++;
            using var settings = new SettingsForm(new AppSettings { Theme = originalTheme }, vaultDirectory: directory)
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000)
            };
            Exception? callbackError = null;
            settings.Shown += (_, _) =>
            {
                try
                {
                    var theme = Descendants(settings).OfType<ThemeToggle>().Single();
                    theme.Checked = originalTheme != AppTheme.Dark;
                    AppTheme preview = originalTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
                    if (Ui.Theme != preview || history.BackColor != Ui.Background || history.ForeColor != Ui.Ink)
                        throw new Exception("Settings theme selection did not preview in the open history window.");

                    // Expiry and successful password changes use this same lock signal.
                    session.Lock();
                    if (!history.IsDisposed || !history.IsHistoryClearedForTest)
                        throw new Exception("Opening settings prevented history from locking and clearing.");
                    if (settings.IsDisposed || !settings.Visible)
                        throw new Exception("Closing locked history also closed its settings dialog.");
                }
                catch (Exception error) { callbackError = error; }
                finally { if (!settings.IsDisposed) { settings.DialogResult = DialogResult.Cancel; settings.Close(); } }
            };
            settings.ShowDialog(new TrayApplication.SettingsOwner(ownerHandle));
            if (callbackError is not null) throw callbackError;
            if (Ui.Theme != originalTheme) throw new Exception("Canceling settings did not restore the original theme.");
        };
        try
        {
            Descendants(history).OfType<Button>().Single(button => button.Text == "Settings").PerformClick();
            if (requests != 1) throw new Exception("The in-app Settings button did not open settings once.");
        }
        finally { session.Lock(); Ui.SetTheme(originalTheme); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class UnlockTestClock : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2031, 1, 2, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        public override long GetTimestamp() => _timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Advance(TimeSpan amount) { _utcNow += amount; _timestamp += amount.Ticks; }
    }

    private static void Render(Form form, string path)
    {
        PrepareForm(form);
        _trace?.Invoke("Drawing " + Path.GetFileName(path));
        form.PerformLayout();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(path, ImageFormat.Png);
    }

    private static IReadOnlyList<KeystrokeEntry> SampleEntries()
    {
        var entries = new List<KeystrokeEntry>();
        var timestamp = DateTimeOffset.Now;
        void AddText(string application, string text)
        {
            foreach (char character in text)
            {
                entries.Add(new(timestamp.AddMilliseconds(entries.Count * 80), application, character switch { '\n' => "Enter", ' ' => "Space", _ => character.ToString().ToUpperInvariant() }, character == '\n' ? null : character.ToString(), false));
            }
        }
        AddText("notepad", "Hello, this is my typing history.\nA readable page with real line breaks.\n");
        AddText("notepad", "Numbers: 12999");
        foreach (string key in new[] { "Backspace", "Backspace", "Backspace" })
            entries.Add(new(timestamp.AddMilliseconds(entries.Count * 80), "notepad", key, null, false));
        AddText("notepad", "345\n");
        AddText("notepad", "Word: ct");
        entries.Add(new(timestamp.AddMilliseconds(entries.Count * 80), "notepad", "Left", null, false));
        AddText("notepad", "a");
        entries.Add(new(timestamp.AddMilliseconds(entries.Count * 80), "notepad", "End", null, false));
        AddText("notepad", "\n");
        entries.Add(new(timestamp.AddMilliseconds(entries.Count * 80), "notepad", "Ctrl+S", null, false));
        entries.Add(new(timestamp.AddMilliseconds(entries.Count * 80), "notepad", "Ctrl+V", null, false));
        AddText("browser", "Find a word or phrase in this view.\nUse the event table for individual keys and timestamps.");
        return entries;
    }

    private static void VerifyLegacyMigration(string root)
    {
        string oldFolder = Path.Combine(root, "TypeMemory");
        string newFolder = Path.Combine(root, "TypeBlocks");
        Directory.CreateDirectory(Path.Combine(oldFolder, "History"));
        byte[] fixture = [1, 4, 9, 16, 25];
        File.WriteAllBytes(Path.Combine(oldFolder, "History", "fixture.tmh"), fixture);
        File.WriteAllText(Path.Combine(oldFolder, "settings.json"), "{\"Paused\":true}");
        AppSettings.MigrateLegacyDataDirectory(root);
        if (Directory.Exists(oldFolder) || !File.ReadAllBytes(Path.Combine(newFolder, "History", "fixture.tmh")).SequenceEqual(fixture) || File.ReadAllText(Path.Combine(newFolder, "settings.json")) != "{\"Paused\":true}")
            throw new Exception("Legacy migration changed history or settings.");
        AppSettings.MigrateLegacyDataDirectory(root);
        Directory.CreateDirectory(oldFolder);
        bool rejected = false;
        try { AppSettings.MigrateLegacyDataDirectory(root); } catch (IOException) { rejected = true; }
        if (!rejected || !Directory.Exists(oldFolder) || !File.ReadAllBytes(Path.Combine(newFolder, "History", "fixture.tmh")).SequenceEqual(fixture))
            throw new Exception("Legacy folder collision did not preserve both locations.");
    }

    private static void PrepareForm(Form form)
    {
        if (form.Visible) return;
        form.ShowInTaskbar = false;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(-32000, -32000);
        _trace?.Invoke("Showing " + form.GetType().Name);
        form.Show();
        _trace?.Invoke("Shown " + form.GetType().Name);
        Application.DoEvents();
        _trace?.Invoke("Ready " + form.GetType().Name);
    }
}
