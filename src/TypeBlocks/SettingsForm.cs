using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class SettingsForm : Form
{
    public event Action? PasswordChanged;
    private Dictionary<Control, Rectangle>? _baseLayout;

    public SettingsForm(AppSettings settings, Func<RecordingHotkey, Action, string?>? saveWithHotkey = null,
        string? vaultDirectory = null)
    {
        string directory = vaultDirectory ?? AppSettings.VaultDirectory;
        AppTheme originalTheme = settings.Theme;
        Ui.Style(this, "TypeBlocks · Settings", new(620, 854));
        AutoScroll = true;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var theme = new ThemeToggle { Text = "Dark theme", AccessibleName = "Dark theme",
            Location = new(460, 86), Size = new(128, 48), Checked = Ui.Theme == AppTheme.Dark };
        theme.CheckedChanged += (_, _) => Ui.SetTheme(theme.Checked ? AppTheme.Dark : AppTheme.Light);

        var startup = new ModernCheckBox { Text = "Start when I sign into Windows", Checked = StartupRegistration.Enabled, Location = new(32, 180), Size = new(556, 32) };
        var control = new ModernCheckBox { Text = "Ctrl", Location = new(32, 252), Size = new(74, 42), Checked = settings.ToggleHotkey.Control };
        var alt = new ModernCheckBox { Text = "Alt", Location = new(112, 252), Size = new(66, 42), Checked = settings.ToggleHotkey.Alt };
        var shift = new ModernCheckBox { Text = "Shift", Location = new(184, 252), Size = new(84, 42), Checked = settings.ToggleHotkey.Shift };
        var key = new ModernComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new(284, 252), Size = new(172, 42), AccessibleName = "Recording shortcut key" };
        foreach (Keys available in RecordingHotkey.AvailableKeys) key.Items.Add(new HotkeyChoice(available));
        void SelectKey(Keys selected) => key.SelectedItem = key.Items.Cast<HotkeyChoice>().First(choice => choice.Key == selected);
        SelectKey(settings.ToggleHotkey.Key);
        var reset = Ui.Button("Reset", 472, 252, 116);
        reset.Click += (_, _) =>
        {
            control.Checked = RecordingHotkey.Default.Control; alt.Checked = RecordingHotkey.Default.Alt;
            shift.Checked = RecordingHotkey.Default.Shift; SelectKey(RecordingHotkey.Default.Key);
        };
        var exclusions = new ModernTextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new(32, 430), Size = new(556, 112), Text = string.Join(Environment.NewLine, settings.ExcludedApplications), AccessibleName = "Excluded applications" };
        var lockMinutes = new ModernTextBox { Location = new(460, 146), Size = new(128, 44), MaxLength = 4,
            Text = settings.AutoLockMinutes.ToString(), AccessibleName = "Lock after (minutes)" };
        var accessStatus = Ui.Label("", 32, 620, 556, 34);
        var change = Ui.Button("Change password", 32, 566, 270, icon: UiIcon.Key);
        change.Click += (_, _) =>
        {
            using var form = new PasswordChangeForm(directory);
            if (form.ShowDialog(this) != DialogResult.OK) return;
            PasswordChanged?.Invoke();
            accessStatus.Text = "Password changed. History is locked.";
        };
        var recovery = Ui.Button("Recovery code", 318, 566, 270, icon: UiIcon.Shield);
        recovery.Click += (_, _) =>
        {
            try
            {
                using var form = new RecoveryForm(directory, resetPassword: false);
                if (form.ShowDialog(this) == DialogResult.OK) accessStatus.Text = "Recovery code is set. Keep your copy somewhere private.";
            }
            catch (Exception error) { Ui.Error(this, error); }
        };
        Controls.AddRange([
            Ui.Label("Settings", 32, 24, 556, 42, 22, true),
            Ui.Label("Dark theme", 32, 98, 370, 28, 11), theme,
            Ui.Label("Recording", 32, 148, 556, 28, 12, true), startup,
            Ui.Label("Global hotkey", 32, 224, 556, 24), control, alt, shift, key, reset,
            Ui.Label("Wait for “Typeblocks: Off” before typing sensitive information.", 32, 310, 556, 50),
            Ui.Label("Excluded apps", 32, 374, 556, 24, 11, true),
            Ui.Label("Process names, one per line. Example: chrome", 32, 402, 556, 24), exclusions,
            Ui.Label("History access", 32, 530, 556, 26, 12, true), accessStatus, change, recovery
        ]);
        var save = Ui.Button("Save settings", 388, 676, 200, true, icon: UiIcon.Check);
        save.Click += (_, _) =>
        {
            try
            {
                var names = exclusions.Lines.Select(x => x.Trim()).Where(x => x.Length > 0)
                    .Select(x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? x[..^4] : x)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var binding = new RecordingHotkey { Control = control.Checked, Alt = alt.Checked, Shift = shift.Checked, Key = ((HotkeyChoice)key.SelectedItem!).Key };
                if (!binding.IsValid(out string? validationError)) throw new ArgumentException(validationError);
                if (!int.TryParse(lockMinutes.Text, out int duration) || duration < 1 || duration > 1440)
                    throw new ArgumentException("Choose a lock duration from 1 to 1440 minutes.");
                void Persist()
                {
                    var previousNames = settings.ExcludedApplications;
                    var previousBinding = settings.ToggleHotkey;
                    var previousTheme = settings.Theme;
                    int previousDuration = settings.AutoLockMinutes;
                    bool previousStartup = StartupRegistration.Enabled;
                    try
                    {
                        if (previousStartup != startup.Checked) StartupRegistration.SetEnabled(startup.Checked);
                        settings.ExcludedApplications = names; settings.ToggleHotkey = binding; settings.Theme = Ui.Theme;
                        settings.AutoLockMinutes = duration;
                        settings.Save();
                    }
                    catch
                    {
                        settings.ExcludedApplications = previousNames; settings.ToggleHotkey = previousBinding; settings.Theme = previousTheme;
                        settings.AutoLockMinutes = previousDuration;
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
        var close = Ui.Button("Close", 32, 676, 116);
        close.DialogResult = DialogResult.Cancel;
        Controls.AddRange([save, close]); CancelButton = close;
        foreach (Control controlToMove in Controls)
        {
            if (controlToMove.Top >= 530) controlToMove.Top += 40;
            if (controlToMove.Top >= 148) controlToMove.Top += 64;
        }
        Controls.AddRange([Ui.Label("Lock after (minutes)", 32, 154, 370, 30, 11), lockMinutes]);
        FormClosed += (_, _) => { if (DialogResult != DialogResult.OK) Ui.SetTheme(originalTheme); };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        FitToWorkingArea(Screen.FromControl(this).WorkingArea.Size);
    }

    internal void FitToWorkingArea(Size available)
    {
        _baseLayout ??= Controls.Cast<Control>().ToDictionary(control => control, control => control.Bounds);
        int outerMargin = (int)Math.Round(16 * DeviceDpi / 96f);
        int margin = _baseLayout.Values.Min(bounds => bounds.Left);
        int contentHeight = _baseLayout.Values.Max(bounds => bounds.Bottom) + margin;
        int originalWidth = _baseLayout.Values.Max(bounds => bounds.Right) - margin;
        SuspendLayout();
        AutoScrollPosition = Point.Empty;
        Size = new Size(Math.Min(Width, Math.Max(1, available.Width - outerMargin)), Math.Min(Height, Math.Max(1, available.Height - outerMargin)));
        int scrollbar = contentHeight > ClientSize.Height ? SystemInformation.VerticalScrollBarWidth : 0;
        float ratio = Math.Min(1f, Math.Max(1, ClientSize.Width - margin * 2 - scrollbar) / (float)originalWidth);
        foreach (var (control, bounds) in _baseLayout)
            control.SetBounds(margin + (int)Math.Round((bounds.Left - margin) * ratio), bounds.Top,
                (int)Math.Round(bounds.Width * ratio), bounds.Height);
        // Shrink flexible fields, preserving readable modifier labels and action buttons.
        int right = margin + (int)Math.Round(originalWidth * ratio);
        int Scale(int value) => (int)Math.Round(value * margin / 32f);
        var modifiers = Controls.OfType<ModernCheckBox>().Where(control => control.Text is "Ctrl" or "Alt" or "Shift")
            .OrderBy(control => _baseLayout[control].Left).ToArray();
        int cursor = margin;
        foreach (var modifier in modifiers)
        {
            modifier.SetBounds(cursor, modifier.Top, _baseLayout[modifier].Width, modifier.Height);
            cursor = modifier.Right + Scale(6);
        }
        var key = Controls.OfType<ModernComboBox>().Single();
        var reset = Controls.OfType<Button>().Single(button => button.Text == "Reset");
        reset.Width = _baseLayout[reset].Width;
        reset.Left = right - reset.Width;
        key.Left = cursor + Scale(10);
        int keyWidth = reset.Left - Scale(16) - key.Left;
        if (keyWidth < Scale(140))
        {
            // Put the key choice on its own row when the window cannot fit the complete shortcut.
            int extraRow = key.Height + Scale(12);
            foreach (var (control, bounds) in _baseLayout)
                if (bounds.Top > _baseLayout[key].Top) control.Top += extraRow;
            key.Top += extraRow;
            reset.Top = key.Top;
            key.Left = margin;
            keyWidth = reset.Left - Scale(16) - margin;
            contentHeight += extraRow;
        }
        key.Width = Math.Max(1, keyWidth);
        var save = Controls.OfType<Button>().Single(button => button.Text == "Save settings");
        save.Width = _baseLayout[save].Width;
        save.Left = right - save.Width;
        AutoScrollMinSize = new Size(0, contentHeight);
        ResumeLayout(true);
    }

    private sealed record HotkeyChoice(Keys Key)
    {
        public override string ToString() => RecordingHotkey.KeyName(Key);
    }
}
