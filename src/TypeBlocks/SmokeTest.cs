using System.Drawing.Imaging;
using TypeBlocks.Capture;
using TypeBlocks.Core;

namespace TypeBlocks;

// Explicit developer-only path. Uses synthetic content and a temporary vault;
// never enables recording or changes the owner's vault, settings, or startup entry.
internal static class SmokeTest
{
    public static int Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        string progressFile = Path.Combine(outputDirectory, "progress.txt");
        File.WriteAllText(progressFile, "Starting synthetic smoke test\n");
        File.WriteAllText(Path.Combine(outputDirectory, "result.txt"), "INCOMPLETE: smoke test started but has not finished.");
        void Progress(string message) => File.AppendAllText(progressFile, message + "\n");
        string temporaryVault = Path.Combine(Path.GetTempPath(), "TypeBlocks-smoke-" + Guid.NewGuid().ToString("N"));
        const string password = "Synthetic test passphrase 42!";
        int result = 1;
        using var dispatcher = new Control();
        _ = dispatcher.Handle;
        using var context = new ApplicationContext();
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
                Progress("Rendering setup");
                using (var setup = new SetupForm()) Render(setup, Path.Combine(outputDirectory, "setup.png"));
                Progress("Rendering unlock");
                using (var unlock = new PasswordForm()) Render(unlock, Path.Combine(outputDirectory, "unlock.png"));
                Progress("Rendering settings");
                using (var settings = new SettingsForm(new AppSettings())) Render(settings, Path.Combine(outputDirectory, "settings.png"));
                using (var settings = new SettingsForm(new AppSettings()))
                {
                    PrepareForm(settings);
                    settings.FitToWorkingArea(new Size(600, 560));
                    var save = settings.Controls.OfType<Button>().Single(button => button.Text == "Save settings");
                    settings.ScrollControlIntoView(save);
                    if (!settings.ClientRectangle.Contains(save.Bounds)) throw new Exception("Settings save button is inaccessible in a small working area.");
                    Render(settings, Path.Combine(outputDirectory, "settings-small.png"));
                }
                Progress("Rendering history");
                var clock = new UnlockTestClock();
                var session = new HistoryUnlockSession(clock);
                session.Unlock(VerifySuccessfulUnlock(password, temporaryVault));
                using (var history = new HistoryForm(session, temporaryVault))
                {
                    PrepareForm(history);
                    await history.PreparePreviewForTest();
                    await history.VerifyTextViewForTest();
                    Render(history, Path.Combine(outputDirectory, "history.png"));
                    var originalSize = history.Size;
                    history.Size = history.MinimumSize;
                    Render(history, Path.Combine(outputDirectory, "history-small.png"));
                    history.Size = originalSize;
                    history.SelectTableForPreview();
                    Render(history, Path.Combine(outputDirectory, "history-table.png"));
                    history.Close();
                }
                Progress("Verifying one-hour unlock lifecycle and canceled authentication");
                await VerifyUnlockLifecycle(session, clock, password, temporaryVault);
                await VerifyCanceledUnlock();
                File.WriteAllText(Path.Combine(outputDirectory, "result.txt"), "PASS: Raw Input register/dispose while paused; legacy data migration and collision safety; shortcut formatting; global shortcut registration, conflict/save-failure rollback, rebind/disposal release and synthetic message dispatch; shortcut settings compatibility; On/Off popup rendering, no-activation styles and timer refresh/dismissal; synthetic encrypted history load; pure text replay, search, layout and table sorting; shared one-hour unlock across viewer reopen, expiry closes and clears an open viewer, explicit lock, stale decryption rejection and canceled authentication; all four UI forms rendered, including history at minimum size. No real input recorded, no physical shortcut injection, no startup changes.");
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
        Application.Run(context);
        return result;
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
        form.Show();
        Application.DoEvents();
    }
}
