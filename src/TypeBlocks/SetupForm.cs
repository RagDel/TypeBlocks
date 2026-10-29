using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class SetupForm : Form
{
    private readonly ModernTextBox _password = Ui.Password(32, 168, 528);
    private readonly ModernTextBox _confirm = Ui.Password(32, 264, 528);
    private readonly ModernCheckBox _startup = new() { Text = "Start recording when I sign into Windows", Checked = true, Location = new(32, 330), Size = new(528, 32) };
    public bool StartAtSignIn => _startup.Checked;

    public SetupForm()
    {
        Ui.Style(this, "Set up TypeBlocks", new(592, 574));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        _password.AccessibleName = "New history password";
        _confirm.AccessibleName = "Repeat history password";
        Controls.AddRange([
            Ui.Label("Set up TypeBlocks", 32, 28, 528, 42, 22, true),
            Ui.Label("Create a password for your encrypted history.", 32, 86, 528, 32),
            Ui.Label("Password · at least 12 characters", 32, 136, 528, 26, bold: true), _password,
            Ui.Label("Repeat password", 32, 232, 528, 26, bold: true), _confirm, _startup,
            Ui.Label($"Records typing in other apps, including sensitive text.\nPause with {RecordingHotkey.Default} before typing secrets.", 32, 384, 528, 52),
            Ui.Label("History stays on this PC. Keep your password safe.", 32, 448, 528, 28)
        ]);
        var create = Ui.Button("Create encrypted history", 276, 500, 284, true, icon: UiIcon.Shield);
        var cancel = Ui.Button("Cancel", 32, 500, 116);
        cancel.DialogResult = DialogResult.Cancel;
        create.Click += async (_, _) =>
        {
            if (_password.Text.Length < 12) { Ui.Error(this, new ArgumentException("Use a password or passphrase with at least 12 characters.")); return; }
            if (_password.Text != _confirm.Text) { Ui.Error(this, new ArgumentException("The passwords do not match.")); return; }
            create.Enabled = false; cancel.Enabled = false; ControlBox = false;
            string password = _password.Text;
            try
            {
                await Task.Run(() => { using var vault = VaultStore.Create(AppSettings.VaultDirectory, password); });
                _password.Clear(); _confirm.Clear();
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception error) { Ui.Error(this, error); }
            finally { create.Enabled = true; cancel.Enabled = true; ControlBox = true; }
        };
        Controls.AddRange([create, cancel]);
        AcceptButton = create; CancelButton = cancel;
        FormClosed += (_, _) => { _password.Clear(); _confirm.Clear(); };
        Ui.ApplyTheme(this);
    }
}
