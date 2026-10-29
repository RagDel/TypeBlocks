using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class PasswordForm : Form
{
    private readonly TextBox _password = Ui.Password(28, 111, 384);
    private readonly Button _unlock = Ui.Button("Unlock", 272, 170, 140, true);
    private readonly Button _cancel = Ui.Button("Cancel", 28, 170, 110);
    private readonly Func<string, Task> _verifyPassword;
    private string? _verifiedPassword;
    private bool _closed;
    private bool _verifying;
    public string Password => _verifiedPassword ?? throw new InvalidOperationException("History has not been unlocked.");

    public PasswordForm(Func<string, Task>? verifyPassword = null)
    {
        _verifyPassword = verifyPassword ?? (password => Task.Run(() => VaultStore.VerifyPassword(AppSettings.VaultDirectory, password)));
        Ui.Style(this, "Unlock TypeBlocks", new(440, 226));
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        Controls.AddRange([Ui.Label("Open your history", 28, 25, 384, 36, 20, true), Ui.Label("Enter the password you chose in TypeBlocks.", 28, 73, 384, 30), _password]);
        _cancel.DialogResult = DialogResult.Cancel;
        _unlock.Click += async (_, _) => await VerifyPassword();
        Controls.AddRange([_unlock, _cancel]); AcceptButton = _unlock; CancelButton = _cancel;
        FormClosed += (_, _) => { _closed = true; _password.Clear(); };
    }

    private async Task VerifyPassword()
    {
        if (_closed || _verifying) return;
        _verifying = true;
        _unlock.Enabled = false; _cancel.Enabled = false; ControlBox = false;
        string password = _password.Text;
        try
        {
            await _verifyPassword(password);
            if (_closed || IsDisposed) return;
            _verifiedPassword = password;
            _password.Clear();
            DialogResult = DialogResult.OK; Close();
        }
        catch
        {
            if (_closed || IsDisposed) return;
            Ui.Error(this, new InvalidOperationException("The password is incorrect, or the encrypted vault is damaged."));
            if (!_closed && !IsDisposed) { _password.SelectAll(); _password.Focus(); }
        }
        finally
        {
            _verifying = false;
            if (!_closed && !IsDisposed) { _unlock.Enabled = true; _cancel.Enabled = true; ControlBox = true; }
        }
    }

    internal void CancelUnlock()
    {
        _closed = true;
        _verifiedPassword = null;
        if (IsDisposed) return;
        _password.Clear();
        DialogResult = DialogResult.Cancel;
        Close();
    }

    internal Task VerifyForTest(string password) { _password.Text = password; return VerifyPassword(); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _closed = true; _verifiedPassword = null; _password.Clear(); }
        base.Dispose(disposing);
    }
}
