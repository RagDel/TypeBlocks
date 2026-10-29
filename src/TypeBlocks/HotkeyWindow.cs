using System.ComponentModel;
using System.Runtime.InteropServices;

namespace TypeBlocks;

/// <summary>Owns a background Windows hotkey without a visible or focused window.</summary>
internal sealed class HotkeyWindow : NativeWindow, IDisposable
{
    private const uint NoRepeat = 0x4000;
    private const int HotkeyMessage = 0x0312;
    private readonly Action _callback;
    private int _activeId;
    private int _nextId = 1;
    private bool _disposed;

    public RecordingHotkey? CurrentBinding { get; private set; }
    public bool Registered => !_disposed && CurrentBinding is not null;
    internal int RegistrationId => _activeId;

    public HotkeyWindow(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        _callback = callback;
        CreateHandle(new CreateParams { Caption = "TypeBlocks controls", Parent = new IntPtr(-3) });
    }

    public bool TrySetBinding(RecordingHotkey binding, out string? error) => TrySetBinding(binding, null, out error);

    /// <summary>
    /// Registers the candidate before saving settings or releasing the existing shortcut.
    /// A registration conflict or a failed save therefore leaves the existing shortcut usable.
    /// The commit callback must not pump the message loop or alter this registration.
    /// </summary>
    public bool TrySetBinding(RecordingHotkey binding, Action? commit, out string? error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(binding);
        if (!binding.IsValid(out error)) return false;
        if (binding == CurrentBinding)
        {
            commit?.Invoke();
            return true;
        }

        int candidateId;
        do
        {
            candidateId = _nextId++;
            if (_nextId > 0xBFFF) _nextId = 1;
        } while (candidateId == _activeId);
        if (!RegisterHotKey(Handle, candidateId, binding.Modifiers | NoRepeat, (uint)binding.Key))
        {
            int nativeError = Marshal.GetLastWin32Error();
            error = nativeError == 1409
                ? $"{binding} is already in use. Choose another shortcut."
                : $"Windows could not register {binding}: {new Win32Exception(nativeError).Message}";
            return false;
        }

        try { commit?.Invoke(); }
        catch
        {
            UnregisterHotKey(Handle, candidateId);
            throw;
        }

        int previousId = _activeId;
        _activeId = candidateId;
        CurrentBinding = binding;
        if (previousId != 0) UnregisterHotKey(Handle, previousId);
        error = null;
        return true;
    }

    protected override void WndProc(ref Message message)
    {
        // Retired registrations may still have queued messages. Only the current binding acts.
        if (message.Msg == HotkeyMessage && Registered && message.WParam == _activeId)
        {
            uint details = unchecked((uint)message.LParam.ToInt64());
            if ((details & 0x000Fu) == CurrentBinding!.Modifiers && (details >> 16) == (uint)CurrentBinding.Key)
                _callback();
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (Handle != IntPtr.Zero)
        {
            if (_activeId != 0) UnregisterHotKey(Handle, _activeId);
            DestroyHandle();
        }
        _activeId = 0;
        CurrentBinding = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
