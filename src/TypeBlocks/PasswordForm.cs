using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class PasswordForm : Form
{
    private readonly ModernTextBox _password = Ui.Password(32, 130, 416);
    private readonly Button _unlock = Ui.Button("Unlock", 288, 238, 160, true, icon: UiIcon.Lock);
    private readonly Button _cancel = Ui.Button("Cancel", 32, 238, 110);
    private readonly LinkLabel _recover = new() { Text = "Forgot password?", Location = new(32, 188), Size = new(250, 28), TabStop = true };
    private readonly Func<string, Task> _verifyPassword;
    private string? _verifiedPassword;
    private bool _closed;
    private bool _verifying;
    public string Password => _verifiedPassword ?? throw new InvalidOperationException("History has not been unlocked.");

    public PasswordForm(Func<string, Task>? verifyPassword = null, Action<IWin32Window>? recoverPassword = null)
    {
        _verifyPassword = verifyPassword ?? (password => Task.Run(() => VaultStore.VerifyPassword(AppSettings.VaultDirectory, password)));
        Ui.Style(this, "Unlock TypeBlocks", new(480, recoverPassword is null ? 270 : 310));
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        _password.AccessibleName = "History password";
        _recover.Visible = recoverPassword is not null;
        if (recoverPassword is null) { _unlock.Top = 196; _cancel.Top = 196; }
        Controls.AddRange([
            Ui.Label("Unlock your history", 32, 28, 416, 42, 20, true),
            Ui.Label("Password", 32, 98, 416, 26, bold: true), _password, _recover
        ]);
        _recover.LinkClicked += (_, _) =>
        {
            if (_closed || _verifying || recoverPassword is null) return;
            _password.Clear();
            try { recoverPassword(this); }
            catch (Exception error) { if (!_closed && !IsDisposed) Ui.Error(this, error); }
            if (!_closed && !IsDisposed) _password.Focus();
        };
        _cancel.DialogResult = DialogResult.Cancel;
        _unlock.Click += async (_, _) => await VerifyPassword();
        Controls.AddRange([_unlock, _cancel]); AcceptButton = _unlock; CancelButton = _cancel;
        FormClosed += (_, _) => { _closed = true; _password.Clear(); };
        Ui.ApplyTheme(this);
    }

    private async Task VerifyPassword()
    {
        if (_closed || _verifying) return;
        _verifying = true;
        _unlock.Enabled = false; _cancel.Enabled = false; _recover.Enabled = false; ControlBox = false;
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
            if (!_closed && !IsDisposed) { _unlock.Enabled = true; _cancel.Enabled = true; _recover.Enabled = true; ControlBox = true; }
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
