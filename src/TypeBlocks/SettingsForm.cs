using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class SettingsForm : Form
{
    private bool _changingPassword;
    public event Action? PasswordChanged;
    public SettingsForm(AppSettings settings, Func<RecordingHotkey, Action, string?>? saveWithHotkey = null)
    {
        Ui.Style(this, "TypeBlocks settings", new(580, 682));
        AutoScroll = true;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        FormClosing += (_, e) => { if (_changingPassword && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; };
        var startup = new CheckBox { Text = "Start when I sign into Windows", Checked = StartupRegistration.Enabled, Location = new(28, 80), Size = new(520, 30) };
        var control = new CheckBox { Text = "Ctrl", Location = new(28, 162), Size = new(72, 30), Checked = settings.ToggleHotkey.Control };
        var alt = new CheckBox { Text = "Alt", Location = new(104, 162), Size = new(64, 30), Checked = settings.ToggleHotkey.Alt };
        var shift = new CheckBox { Text = "Shift", Location = new(172, 162), Size = new(80, 30), Checked = settings.ToggleHotkey.Shift };
        var key = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new(264, 162), Size = new(150, 30), AccessibleName = "Recording shortcut key" };
        foreach (Keys available in RecordingHotkey.AvailableKeys) key.Items.Add(new HotkeyChoice(available));
        void SelectKey(Keys selected) => key.SelectedItem = key.Items.Cast<HotkeyChoice>().First(choice => choice.Key == selected);
        SelectKey(settings.ToggleHotkey.Key);
        var reset = Ui.Button("Reset", 432, 160, 116);
        reset.Click += (_, _) =>
        {
            control.Checked = RecordingHotkey.Default.Control; alt.Checked = RecordingHotkey.Default.Alt;
            shift.Checked = RecordingHotkey.Default.Shift; SelectKey(RecordingHotkey.Default.Key);
        };
        var exclusions = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new(28, 314), Size = new(520, 72), Text = string.Join(Environment.NewLine, settings.ExcludedApplications) };
        var current = Ui.Password(28, 471, 250);
        var next = Ui.Password(298, 471, 250);
        var confirm = Ui.Password(298, 541, 250);
        Controls.AddRange([
            Ui.Label("Settings", 28, 24, 520, 40, 22, true), startup,
            Ui.Label("Recording hotkey", 28, 127, 520, 30, 12, true), control, alt, shift, key, reset,
            Ui.Label("Works while other apps are open. Include Ctrl or Alt.\nWait for “Typeblocks: Off” before typing sensitive information.", 28, 203, 520, 42),
            Ui.Label("Excluded apps", 28, 251, 520, 30, 12, true),
            Ui.Label("One process name per line, without .exe (for example: chrome).", 28, 282, 520, 28), exclusions,
            Ui.Label("Change history password", 28, 408, 520, 28, 12, true),
            Ui.Label("Current password", 28, 443, 250, 25), current,
            Ui.Label("New password · 12+ characters", 298, 443, 250, 25), next,
            Ui.Label("Repeat new password", 298, 513, 250, 25), confirm
        ]);
        var change = Ui.Button("Change password", 28, 533, 250);
        change.Click += async (_, _) =>
        {
            if (next.Text.Length < 12 || next.Text != confirm.Text) { Ui.Error(this, new ArgumentException("Use at least 12 characters and enter the same new password twice.")); return; }
            _changingPassword = true; Enabled = false; ControlBox = false;
            string oldPassword = current.Text, newPassword = next.Text;
            try
            {
                await Task.Run(() => VaultStore.ChangePassword(AppSettings.VaultDirectory, oldPassword, newPassword));
                PasswordChanged?.Invoke();
                if (IsDisposed) return;
                current.Clear(); next.Clear(); confirm.Clear();
                MessageBox.Show(this, "Your password has been changed. Existing history uses the new password.", "TypeBlocks");
            }
            catch (Exception error) { if (!IsDisposed) Ui.Error(this, error); }
            finally { _changingPassword = false; if (!IsDisposed) { Enabled = true; ControlBox = true; } }
        };
        var save = Ui.Button("Save settings", 368, 616, 180, true);
        save.Click += (_, _) =>
        {
            try
            {
                var names = exclusions.Lines.Select(x => x.Trim()).Where(x => x.Length > 0).Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var binding = new RecordingHotkey { Control = control.Checked, Alt = alt.Checked, Shift = shift.Checked, Key = ((HotkeyChoice)key.SelectedItem!).Key };
                if (!binding.IsValid(out string? validationError)) throw new ArgumentException(validationError);
                void Persist()
                {
                    var previousNames = settings.ExcludedApplications;
                    var previousBinding = settings.ToggleHotkey;
                    bool previousStartup = StartupRegistration.Enabled;
                    try
                    {
                        if (previousStartup != startup.Checked) StartupRegistration.SetEnabled(startup.Checked);
                        settings.ExcludedApplications = names;
                        settings.ToggleHotkey = binding;
                        settings.Save();
                    }
                    catch
                    {
                        settings.ExcludedApplications = previousNames;
                        settings.ToggleHotkey = previousBinding;
                        if (previousStartup != StartupRegistration.Enabled) StartupRegistration.SetEnabled(previousStartup);
                        throw;
                    }
                }
                if (saveWithHotkey is null) Persist();
                else if (saveWithHotkey(binding, Persist) is string error) throw new InvalidOperationException(error);
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception error) { Ui.Error(this, error); }
        };
        var close = Ui.Button("Close", 28, 616, 116);
        close.DialogResult = DialogResult.Cancel;
        Controls.AddRange([change, save, close]); CancelButton = close;
        FormClosed += (_, _) => { current.Clear(); next.Clear(); confirm.Clear(); };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToWorkingArea(Screen.FromControl(this).WorkingArea.Size);
    }

    internal void FitToWorkingArea(Size available)
    {
        int margin = (int)Math.Round(28 * DeviceDpi / 96f);
        AutoScrollMinSize = new Size(0, Controls.Cast<Control>().Max(control => control.Bottom) + margin);
        Size = new Size(Math.Min(Width, Math.Max(1, available.Width - margin)),
            Math.Min(Height, Math.Max(1, available.Height - margin)));
    }

    private sealed record HotkeyChoice(Keys Key)
    {
        public override string ToString() => RecordingHotkey.KeyName(Key);
    }
}
