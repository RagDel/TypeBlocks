using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class RecoveryForm : Form
{
    private readonly ModernTextBox _credential;
    private readonly ModernTextBox _next;
    private readonly ModernTextBox _confirm;
    private readonly ModernTextBox _code;
    private readonly Func<Task> _runAction;
    private VaultStore.PendingRecoveryCode? _pendingRecovery;

    public RecoveryForm(string directory, bool resetPassword)
    {
        Ui.Style(this, resetPassword ? "TypeBlocks · Reset password" : "TypeBlocks · Recovery code", new(620, 550));
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var heading = Ui.Label(resetPassword ? "Reset password" : "Recovery code", 32, 24, 556, 42, 22, true);
        var explanation = Ui.Label("", 32, 82, 556, 70);
        var prompt = Ui.Label(resetPassword ? "Your recovery code" : "Current password", 32, 170, 556, 26);
        _credential = resetPassword
            ? new ModernTextBox { Location = new(32, 202), Size = new(556, 78), Multiline = true, MaxLength = 160, AccessibleName = "Recovery code" }
            : Ui.Password(32, 202, 556);
        if (!resetPassword) _credential.AccessibleName = "Current history password";
        _next = Ui.Password(32, 334, 270);
        _confirm = Ui.Password(318, 334, 270);
        _next.AccessibleName = "New history password";
        _confirm.AccessibleName = "Repeat new history password";
        var nextLabel = Ui.Label("New · 12+ characters", 32, 304, 270, 24);
        var confirmLabel = Ui.Label("Repeat new password", 318, 304, 270, 24);
        _code = new ModernTextBox { Location = new(32, 202), Size = new(556, 104), Multiline = true, ReadOnly = true,
            Visible = false, Font = Ui.Font(11), AccessibleName = "Save this recovery code" };
        var status = Ui.Label("", 32, 398, 556, 66);
        var action = Ui.Button(resetPassword ? "Reset password" : "Generate code", 368, 478, 220, true, icon: UiIcon.Key);
        var close = Ui.Button("Cancel", 32, 478, 112);
        close.DialogResult = DialogResult.Cancel;
        var copy = Ui.Button("Copy code", 32, 326, 156, icon: UiIcon.Copy);
        copy.Visible = false;
        copy.Click += (_, _) =>
        {
            try { PrivateClipboard.CopyRecoveryCode(_code.Text); status.Text = "Copied. Save it now, then choose “I've saved it”."; }
            catch { status.Text = "The clipboard is busy. Select the code and copy it manually."; }
        };
        nextLabel.Visible = confirmLabel.Visible = _next.Visible = _confirm.Visible = resetPassword;
        Controls.AddRange([heading, explanation, prompt, _credential, nextLabel, confirmLabel, _next, _confirm, _code, copy, status, action, close]);
        bool hasCode = VaultStore.HasRecoveryCode(directory);
        explanation.Text = resetPassword
            ? "Use the code you saved earlier to choose a new password.\nYour encrypted history will be kept."
            : hasCode ? "Prepare a replacement code to reset a forgotten password.\nYour previous code stays active until you save the replacement."
                : "Save a recovery code now to reset a forgotten password later.\nKeep it somewhere private, outside TypeBlocks.";
        if (resetPassword && !hasCode)
        {
            explanation.Text = "Recovery was not set up for this history.\nUnlock with your password, then create a code in Settings.";
            explanation.Height = 96;
            prompt.Visible = _credential.Visible = nextLabel.Visible = confirmLabel.Visible = _next.Visible = _confirm.Visible = action.Visible = false;
            status.Text = "Without the password or a previously saved code, this encrypted history cannot be recovered.";
            close.Text = "Close";
        }
        bool generated = false;
        async Task RunAction()
        {
            if (generated)
            {
                try { _pendingRecovery!.Activate(); DialogResult = DialogResult.OK; Close(); }
                catch (Exception failure) { status.Text = failure.Message; }
                return;
            }
            if (resetPassword && (_next.Text.Length < VaultStore.MinimumPasswordLength || _next.Text != _confirm.Text))
            {
                status.Text = "Use at least 12 characters and enter the same new password twice.";
                return;
            }
            Enabled = false; ControlBox = false;
            string credential = _credential.Text, newPassword = _next.Text;
            try
            {
                if (resetPassword)
                {
                    await Task.Run(() => VaultStore.RecoverPassword(directory, credential, newPassword));
                    if (IsDisposed) return;
                    DialogResult = DialogResult.OK; Close();
                }
                else
                {
                    var pending = await Task.Run(() => VaultStore.PrepareRecoveryCode(directory, credential));
                    if (IsDisposed) { pending.Dispose(); return; }
                    _pendingRecovery = pending;
                    generated = true;
                    _credential.Clear(); _credential.Visible = prompt.Visible = false;
                    _code.Text = pending.Code; _code.Visible = copy.Visible = true;
                    heading.Text = "Save your recovery code";
                    explanation.Text = "Save it privately, then choose “I've saved it” to activate it.\nAnyone with an active code can reset your history password.";
                    status.Text = "Not active yet. Cancel leaves your existing recovery unchanged.";
                    action.Text = "I've saved it";
                }
            }
            catch (Exception failure) { if (!IsDisposed) status.Text = failure.Message; }
            finally { if (!IsDisposed) { Enabled = true; ControlBox = true; } }
        }
        _runAction = RunAction;
        action.Click += async (_, _) => await RunAction();
        FormClosed += (_, _) => { _credential.Clear(); _next.Clear(); _confirm.Clear(); _code.Clear(); _pendingRecovery?.Dispose(); _pendingRecovery = null; };
        AcceptButton = action; CancelButton = close;
    }

    internal Task GenerateForTest(string password) { _credential.Text = password; return _runAction(); }
    internal Task ResetForTest(string code, string password) { _credential.Text = code; _next.Text = password; _confirm.Text = password; return _runAction(); }
    internal Task ActivateForTest() => _runAction();
    internal string RecoveryCodeForTest => _code.Text;
}
