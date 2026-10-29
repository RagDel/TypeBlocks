using TypeBlocks.Core;

namespace TypeBlocks;

/// <summary>Developer-only interactive review. No recorder, real vault, or persisted settings.</summary>
internal static class InteractivePreview
{
    private const string Password = "Synthetic preview password 42!";

    public static int Run(bool openHistory = false)
    {
        string root = Path.Combine(Path.GetTempPath(), "TypeBlocks-ui-preview-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var gallery = new Gallery(root);
            if (openHistory)
            {
                // The review starts on the actual history surface; the gallery is never shown.
                using var history = gallery.CreateHistory();
                Application.Run(history);
            }
            else Application.Run(gallery);
            return 0;
        }
        finally
        {
            // This path is created exclusively above; it never points at the owner's data.
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class Gallery : Form
    {
        private readonly string _root;
        private readonly string _historyDirectory;
        private readonly RecordingStatusPopup _popup = new();
        private readonly List<HistoryUnlockSession> _sessions = [];

        public Gallery(string root)
        {
            _root = root;
            _historyDirectory = CreateVault("history", addHistory: true);
            Text = "TypeBlocks · Synthetic UI review";
            Font = SystemFonts.MessageBoxFont;
            ClientSize = new Size(560, 460);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            var description = new Label
            {
                Text = "Synthetic records only. Recording is not running.\n" +
                    "Settings saves and setup creation are disabled.\n" +
                    "Test password: " + Password,
                Dock = DockStyle.Top, Height = 92, Padding = new Padding(14)
            };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), AutoScroll = true };
            Controls.Add(buttons);
            Controls.Add(description);
            void Add(string text, Action action)
            {
                var button = new Button { Text = text, Size = new(250, 42), Margin = new Padding(6) };
                button.Click += (_, _) =>
                {
                    try { action(); }
                    catch (Exception error) { MessageBox.Show(this, error.Message, "Synthetic UI review"); }
                };
                buttons.Controls.Add(button);
            }
            Add("History / search", OpenHistory);
            Add("Settings", () => OpenSettings());
            Add("Setup (creation disabled)", () =>
            {
                using var form = new SetupForm();
                form.AcceptButton = null;
                form.Controls.OfType<Button>().Single(button => button.Text == "Create encrypted history").Enabled = false;
                ShowPreviewDialog(form);
            });
            Add("Unlock", () =>
            {
                using var form = new PasswordForm(value => Task.Run(() => VaultStore.VerifyPassword(_historyDirectory, value)),
                    owner => { using var recovery = new RecoveryForm(CreateVault("forgot-password"), true); ShowPreviewDialog(recovery, owner); });
                ShowPreviewDialog(form);
            });
            Add("Change password", () =>
            {
                using var form = new PasswordChangeForm(CreateVault("change-password"));
                ShowPreviewDialog(form);
            });
            Add("Recovery · generate", () => OpenRecovery(generated: false));
            Add("Recovery · prepared code", () => OpenRecovery(generated: true));
            Add("Recovery · reset password", () =>
            {
                string directory = CreateVault("reset-password");
                string code = VaultStore.CreateRecoveryCode(directory, Password);
                using var form = new RecoveryForm(directory, true);
                form.Controls.OfType<ModernTextBox>().Single(input => input.AccessibleName == "Recovery code").Text = code;
                ShowPreviewDialog(form);
            });
            Add("Recovery · unavailable", () =>
            {
                using var form = new RecoveryForm(CreateVault("no-recovery"), true);
                ShowPreviewDialog(form);
            });
            Add("Popup On / Off", () => { _popup.ShowStatus(_popup.StatusText != "Typeblocks: On"); });
            Add("Light theme", () => Ui.SetTheme(AppTheme.Light));
            Add("Dark theme", () => Ui.SetTheme(AppTheme.Dark));
        }

        private string CreateVault(string name, bool addHistory = false)
        {
            string directory = Path.Combine(_root, name + "-" + Guid.NewGuid().ToString("N"));
            using var vault = VaultStore.Create(directory, Password);
            if (addHistory) vault.Append(SampleEntries());
            return directory;
        }

        private void OpenHistory() => CreateHistory().Show(this);

        public HistoryForm CreateHistory()
        {
            var session = new HistoryUnlockSession();
            session.Unlock(Password);
            _sessions.Add(session);
            var form = new HistoryForm(session, _historyDirectory);
            form.SettingsRequested += () =>
            {
                OpenSettings(session);
                if (!form.IsDisposed) Ui.BringForward(form);
            };
            form.FormClosed += (_, _) => { session.Lock(); _sessions.Remove(session); form.Dispose(); };
            MarkPreview(form);
            return form;
        }

        private void OpenSettings(HistoryUnlockSession? session = null)
        {
            using var form = new SettingsForm(new AppSettings { Theme = Ui.Theme, Paused = true },
                // Deliberately do not invoke persist: no settings file or startup mutation.
                saveWithHotkey: (_, _) => null, vaultDirectory: CreateVault("settings"));
            if (session is not null) form.PasswordChanged += session.Lock;
            ShowPreviewDialog(form);
        }

        private void OpenRecovery(bool generated)
        {
            using var form = new RecoveryForm(CreateVault("recovery"), false);
            // Clipboard tests use an isolated payload elsewhere; don't replace the owner's clipboard.
            form.Controls.OfType<Button>().Single(button => button.Text == "Copy code").Enabled = false;
            if (generated)
                form.Shown += async (_, _) =>
                {
                    try { await form.GenerateForTest(Password); }
                    catch (Exception error) { if (!form.IsDisposed) MessageBox.Show(form, error.Message, "Synthetic UI review"); }
                };
            ShowPreviewDialog(form);
        }

        private void ShowPreviewDialog(Form form, IWin32Window? owner = null)
        {
            MarkPreview(form);
            form.ShowDialog(owner ?? this);
        }

        private static void MarkPreview(Form form)
        {
            form.Text += " · Synthetic preview";
            form.StartPosition = FormStartPosition.CenterScreen;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var session in _sessions.ToArray()) session.Lock();
                _popup.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    private static IReadOnlyList<KeystrokeEntry> SampleEntries()
    {
        var result = new List<KeystrokeEntry>();
        var start = DateTimeOffset.Now.AddMinutes(-2);
        void Add(string app, string text)
        {
            foreach (char character in text)
                result.Add(new(start.AddMilliseconds(result.Count * 20), app,
                    character switch { '\n' => "Enter", ' ' => "Space", _ => character.ToString().ToUpperInvariant() },
                    character == '\n' ? null : character.ToString(), false));
        }
        Add("notepad", "Hello, this is synthetic history.\nFind this word: hello.\nHello appears three times for search testing.\n");
        Add("notepad", "Numbers: 12999");
        for (int i = 0; i < 3; i++) result.Add(new(start.AddMilliseconds(result.Count * 20), "notepad", "Backspace", null, false));
        Add("notepad", "345\n");
        Add("browser", "This screen contains no real recorded typing.\nReview text, controls, themes, and the event table.");
        return result;
    }
}
