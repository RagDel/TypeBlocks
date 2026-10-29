using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class SetupForm : Form
{
    private readonly TextBox _password = Ui.Password(32, 162, 496);
    private readonly TextBox _confirm = Ui.Password(32, 234, 496);
    private readonly CheckBox _startup = new() { Text = "Start recording when I sign into Windows", Checked = true, Location = new(32, 289), Size = new(490, 32) };
    public bool StartAtSignIn => _startup.Checked;

    public SetupForm()
    {
        Ui.Style(this, "Set up TypeBlocks", new(560, 524));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        Controls.AddRange([
            Ui.Label("TYPEBLOCKS", 32, 24, 496, 24, 10, true),
            Ui.Label("Your typing, saved.", 32, 59, 496, 46, 23, true),
            Ui.Label("Choose a password to protect your local history.", 32, 108, 496, 28),
            Ui.Label("Password · at least 12 characters", 32, 137, 496, 25), _password,
            Ui.Label("Repeat password", 32, 206, 496, 25), _confirm, _startup,
            Ui.Label($"Records typing in other apps, including sensitive text.\nPause with {RecordingHotkey.Default} before typing secrets.", 32, 335, 496, 68),
            Ui.Label("There is no password recovery. Keep your password safe.\nHistory stays on this PC.", 32, 411, 496, 55)
        ]);
        var create = Ui.Button("Create encrypted history", 270, 475, 258, true);
        var cancel = Ui.Button("Cancel", 32, 475, 116);
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
    }
}
