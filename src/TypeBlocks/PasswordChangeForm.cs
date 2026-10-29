using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class PasswordChangeForm : Form
{
    public PasswordChangeForm(string directory)
    {
        Ui.Style(this, "TypeBlocks · Change password", new(560, 464));
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var current = Ui.Password(32, 160, 496);
        var next = Ui.Password(32, 248, 240);
        var confirm = Ui.Password(288, 248, 240);
        current.AccessibleName = "Current history password";
        next.AccessibleName = "New history password";
        confirm.AccessibleName = "Repeat new history password";
        var error = Ui.Label("", 32, 314, 496, 64);
        var save = Ui.Button("Change password", 300, 396, 228, true, icon: UiIcon.Key);
        var cancel = Ui.Button("Cancel", 32, 396, 112);
        cancel.DialogResult = DialogResult.Cancel;
        Controls.AddRange([
            Ui.Label("Change password", 32, 24, 496, 42, 22, true),
            Ui.Label("Your existing history will use the new password.", 32, 82, 496, 30),
            Ui.Label("Current password", 32, 130, 496, 24), current,
            Ui.Label("New · 12+ characters", 32, 218, 240, 24), next,
            Ui.Label("Repeat new password", 288, 218, 240, 24), confirm, error, save, cancel
        ]);
        save.Click += async (_, _) =>
        {
            if (next.Text.Length < VaultStore.MinimumPasswordLength || next.Text != confirm.Text)
            {
                error.Text = "Use at least 12 characters and enter the same new password twice.";
                return;
            }
            Enabled = false; ControlBox = false;
            string oldPassword = current.Text, newPassword = next.Text;
            try
            {
                await Task.Run(() => VaultStore.ChangePassword(directory, oldPassword, newPassword));
                if (IsDisposed) return;
                DialogResult = DialogResult.OK; Close();
            }
            catch (Exception failure) { if (!IsDisposed) error.Text = failure.Message; }
            finally { if (!IsDisposed) { Enabled = true; ControlBox = true; } }
        };
        FormClosed += (_, _) => { current.Clear(); next.Clear(); confirm.Clear(); };
        AcceptButton = save; CancelButton = cancel;
    }
}
