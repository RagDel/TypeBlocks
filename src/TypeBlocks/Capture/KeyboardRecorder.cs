using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using TypeBlocks.Core;

namespace TypeBlocks.Capture;

/// <summary>
/// Observes keyboard events on the current user's desktop without intercepting or suppressing them.
/// Construct, use, and dispose on the WinForms UI thread, with a running message loop.
/// </summary>
public sealed class KeyboardRecorder : NativeWindow, IDisposable
{
    private const int WmInput = 0x00ff;
    private const uint RidInput = 0x10000003;
    private const uint RidevInputSink = 0x00000100;
    private const uint RidevRemove = 0x00000001;
    private const ushort RiKeyBreak = 0x0001;
    private const ushort RiKeyE0 = 0x0002;
    private const int BufferSize = 256;
    private const int EsPassword = 0x0020;
    private static readonly int[] ModifierKeys =
    {
        (int)Keys.LShiftKey, (int)Keys.RShiftKey, (int)Keys.LControlKey, (int)Keys.RControlKey,
        (int)Keys.LMenu, (int)Keys.RMenu, (int)Keys.LWin, (int)Keys.RWin
    };
    private static readonly int[] ToggleKeys = { (int)Keys.CapsLock, (int)Keys.NumLock, (int)Keys.Scroll };
    private readonly uint _headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
    private readonly int _ownProcessId = Environment.ProcessId;
    private readonly int? _captureOnlyProcessId;
    private readonly HashSet<(nint Device, int Key)> _pressedKeys = new();
    private readonly byte[] _keyboardState = new byte[256];
    private readonly char[] _translation = new char[16];
    private readonly StringBuilder _className = new(256);
    private nint _rawBuffer;
    private nint _stateForeground;
    private nint _lastForeground;
    private uint _lastProcessId;
    private string? _lastApplication;
    private bool _registered;
    private bool _disposed;
    private bool _paused = true;
    private long _resumedAt;

    /// <summary>Raised on the UI thread. Handlers should enqueue work and return promptly.</summary>
    public event Action<KeystrokeEntry>? KeyRecorded;

    /// <summary>Reports a capture or event-handler failure without including typed content.</summary>
    public event Action<string>? Error;

    /// <summary>Capture starts paused. The owner should unpause only after configuring a history vault.</summary>
    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused && !value) _resumedAt = (long)GetTickCount64();
            _paused = value;
        }
    }

    /// <summary>Process names without .exe; modify only on the UI thread.</summary>
    public HashSet<string> ExcludedApplications { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        "1password", "bitwarden", "keepass", "keepassxc"
    };

    /// <param name="captureOnlyProcessId">Optional dedicated target for a controlled integration test.</param>
    public KeyboardRecorder(int? captureOnlyProcessId = null)
    {
        if (captureOnlyProcessId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(captureOnlyProcessId));
        _captureOnlyProcessId = captureOnlyProcessId;
        try
        {
            // A message-only window receives Raw Input while the visible tray UI is elsewhere.
            CreateHandle(new CreateParams
            {
                Caption = "TypeBlocks Input",
                Parent = new nint(-3) // HWND_MESSAGE
            });
            _rawBuffer = Marshal.AllocHGlobal(BufferSize);
            ResynchronizeKeyboardState();
            var devices = new[]
            {
                new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevInputSink, Target = Handle }
            };
            if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>()))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not register keyboard input.");
            _registered = true;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmInput && !_disposed)
        {
            try
            {
                ProcessInput(message.LParam, GetMessageTime());
            }
            catch
            {
                Paused = true;
                ReportError("Recording paused because Windows keyboard input could not be processed.");
            }
        }

        // Let DefWindowProc perform Windows' required WM_INPUT cleanup.
        base.WndProc(ref message);
    }

    private void ProcessInput(nint input, int messageTime)
    {
        uint length = BufferSize;
        uint read = GetRawInputData(input, RidInput, _rawBuffer, ref length, _headerSize);
        if (read == uint.MaxValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());
        if (read < _headerSize + Marshal.SizeOf<RawKeyboard>())
            return;

        var header = Marshal.PtrToStructure<RawInputHeader>(_rawBuffer);
        if (header.Type != 1) // RIM_TYPEKEYBOARD
            return;
        var raw = Marshal.PtrToStructure<RawKeyboard>(_rawBuffer + (int)_headerSize);
        if (raw.VirtualKey == 0 || raw.VirtualKey >= 255 || raw.MakeCode == 0xff)
            return;

        int key = ResolveVirtualKey(raw);
        bool released = (raw.Flags & RiKeyBreak) != 0;
        nint foreground = GetForegroundWindow();
        bool resynchronized = foreground != _stateForeground;
        if (resynchronized)
            ResynchronizeKeyboardState(foreground);
        else
            RefreshModifiersAndToggles(key);

        // Apply this raw event AFTER the physical-state snapshot: asynchronous state can already
        // reflect a later queued event. In particular, keep this event's own modifier transition.
        var physicalKey = (header.Device, key);
        bool repeat = false;
        if (released)
        {
            _pressedKeys.Remove(physicalKey);
            bool stillDown = _pressedKeys.Any(pressed => pressed.Key == key);
            SetDown(key, stillDown);
        }
        else
        {
            // Pause has no ordinary break event on some keyboards and cannot auto-repeat.
            repeat = key != (int)Keys.Pause && !_pressedKeys.Add(physicalKey);
            SetDown(key, true);
            // A fresh snapshot already includes the physical toggle transition.
            if (!resynchronized && !repeat && key is (int)Keys.CapsLock or (int)Keys.NumLock or (int)Keys.Scroll)
                _keyboardState[key] ^= 1;
        }
        UpdateAggregateModifiers();

        if (released || Paused || KeyRecorded is null
            || !IsMessageAfterResume(messageTime, (long)GetTickCount64(), _resumedAt))
            return;

        // Fail closed when the foreground process or focused control cannot be established.
        if (foreground == 0)
            return;
        uint threadId = GetWindowThreadProcessId(foreground, out uint processId);
        if (threadId == 0 || processId == 0 || processId == _ownProcessId)
            return;
        if (_captureOnlyProcessId is int onlyProcess && processId != onlyProcess)
            return;
        string? application = GetApplication(foreground, processId);
        if (application is null || ExcludedApplications.Contains(application))
            return;
        if (!TryCheckPasswordControl(threadId, out bool passwordControl) || passwordControl)
            return;

        bool control = IsDown((int)Keys.ControlKey);
        bool alt = IsDown((int)Keys.Menu);
        bool shift = IsDown((int)Keys.ShiftKey);
        bool windows = IsDown((int)Keys.LWin) || IsDown((int)Keys.RWin);
        string label = FormatKey((Keys)key, control, alt, shift, windows, (raw.Flags & RiKeyE0) != 0);
        string? text = TranslateKey(raw, key, threadId, control, alt, windows);

        // Focus may change while inspecting another process. Do not assign that event to stale context.
        if (GetForegroundWindow() != foreground)
            return;
        var entry = new KeystrokeEntry(DateTimeOffset.UtcNow, application, label, text, repeat);
        var handlers = KeyRecorded;
        if (handlers is null)
            return;
        foreach (Action<KeystrokeEntry> handler in handlers.GetInvocationList())
        {
            try
            {
                handler(entry);
            }
            catch
            {
                Paused = true;
                ReportError("Recording paused because the history handler could not accept a key event.");
                break;
            }
        }
    }

    internal static bool IsMessageAfterResume(int messageTime, long now, long resumedAt)
    {
        // WM_HOTKEY can overtake queued WM_INPUT. Never save keys queued while paused,
        // including the resume chord. Reconstruct the 64-bit time across DWORD wraparound.
        long age = unchecked((uint)((uint)now - (uint)messageTime));
        return now - age > resumedAt;
    }

    private string? GetApplication(nint foreground, uint processId)
    {
        if (_lastForeground == foreground && _lastProcessId == processId && _lastApplication is not null)
            return _lastApplication;
        _lastForeground = foreground;
        _lastProcessId = processId;
        _lastApplication = null;
        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            _lastApplication = process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or Win32Exception or OverflowException)
        {
            // Foreground processes can exit between obtaining their ID and reading their name.
        }
        return _lastApplication;
    }

    private bool TryCheckPasswordControl(uint foregroundThread, out bool isPassword)
    {
        isPassword = false;
        var information = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(foregroundThread, ref information))
            return false;
        if (information.Focus == 0)
            return false;
        _className.Clear();
        if (GetClassName(information.Focus, _className, _className.Capacity) == 0)
            return false;
        string className = _className.ToString();
        bool editControl = className.Equals("Edit", StringComparison.OrdinalIgnoreCase)
            || className.StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)
            || className.Contains(".EDIT.", StringComparison.OrdinalIgnoreCase);
        if (editControl)
        {
            int style = GetWindowLong(information.Focus, -16); // GWL_STYLE
            if (style == 0 && Marshal.GetLastWin32Error() != 0)
                return false;
            isPassword = (style & EsPassword) != 0;
        }
        return true;
    }

    private string? TranslateKey(RawKeyboard raw, int key, uint foregroundThread, bool control, bool alt, bool windows)
    {
        // Ctrl/Win shortcuts and ordinary Alt shortcuts are key events, not typed text.
        // Ctrl+Alt is retained for AltGr layouts. IME composition and dead-key sequences are best effort.
        if (windows || (control && !alt) || (alt && !control))
            return null;
        Array.Clear(_translation);
        int count = ToUnicodeEx((uint)key, raw.MakeCode, _keyboardState,
            _translation, _translation.Length, 4, GetKeyboardLayout(foregroundThread));
        // Flag 4 prevents this observer from modifying the application's dead-key state.
        if (count <= 0)
        {
            Array.Clear(_translation);
            return null;
        }
        string text = new(_translation, 0, Math.Min(count, _translation.Length));
        Array.Clear(_translation);
        return text.Length == 0 || text.Any(char.IsControl) ? null : text;
    }

    private static int ResolveVirtualKey(RawKeyboard raw)
    {
        bool extended = (raw.Flags & RiKeyE0) != 0;
        return (Keys)raw.VirtualKey switch
        {
            Keys.ShiftKey => raw.MakeCode == 0x36 ? (int)Keys.RShiftKey : (int)Keys.LShiftKey,
            Keys.ControlKey => extended ? (int)Keys.RControlKey : (int)Keys.LControlKey,
            Keys.Menu => extended ? (int)Keys.RMenu : (int)Keys.LMenu,
            _ => raw.VirtualKey
        };
    }

    /// <summary>Formats a physical key plus held modifiers; does not require input registration.</summary>
    public static string FormatKey(Keys key, bool control = false, bool alt = false,
        bool shift = false, bool windows = false, bool isExtended = false)
    {
        var parts = new List<string>(5);
        if (control && key is not (Keys.ControlKey or Keys.LControlKey or Keys.RControlKey)) parts.Add("Ctrl");
        if (alt && key is not (Keys.Menu or Keys.LMenu or Keys.RMenu)) parts.Add("Alt");
        if (shift && key is not (Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey)) parts.Add("Shift");
        if (windows && key is not (Keys.LWin or Keys.RWin)) parts.Add("Win");
        parts.Add(key switch
        {
            Keys.Return => isExtended ? "Num Enter" : "Enter",
            Keys.Back => "Backspace",
            Keys.Capital => "Caps Lock",
            Keys.LShiftKey => "Left Shift",
            Keys.RShiftKey => "Right Shift",
            Keys.LControlKey => "Left Ctrl",
            Keys.RControlKey => "Right Ctrl",
            Keys.LMenu => "Left Alt",
            Keys.RMenu => "Right Alt",
            Keys.LWin => "Left Win",
            Keys.RWin => "Right Win",
            >= Keys.D0 and <= Keys.D9 => ((int)key - (int)Keys.D0).ToString(),
            _ => Enum.IsDefined(key) ? key.ToString() : $"VK_{(int)key:X2}"
        });
        return string.Join("+", parts);
    }

    /// <summary>
    /// Forget missed transitions and snapshot current physical keys and toggles. Call on the UI
    /// thread after session unlock/resume; this does not change whether recording is paused.
    /// </summary>
    public void ResynchronizeKeyboardState()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ResynchronizeKeyboardState(GetForegroundWindow());
    }

    private void ResynchronizeKeyboardState(nint foreground)
    {
        _pressedKeys.Clear();
        _stateForeground = foreground;
        _lastForeground = 0;
        _lastProcessId = 0;
        _lastApplication = null;
        for (int key = 0; key < _keyboardState.Length; key++)
            _keyboardState[key] = (byte)((GetAsyncKeyState(key) & 0x8000) != 0 ? 0x80 : 0);
        foreach (int key in ToggleKeys)
            _keyboardState[key] |= (byte)(GetKeyState(key) & 1);
        UpdateAggregateModifiers();
    }

    private void RefreshModifiersAndToggles(int currentKey)
    {
        // UAC can switch away and return to the same foreground HWND without delivering key-up
        // events here. Reconcile only when input arrives; there is no timer or idle polling.
        foreach (int key in ModifierKeys)
            SetDown(key, (GetAsyncKeyState(key) & 0x8000) != 0);
        _pressedKeys.RemoveWhere(pressed => IsModifier(pressed.Key) && !IsDown(pressed.Key));
        foreach (int key in ToggleKeys)
        {
            // Preserve the current raw toggle event's transition, which is applied below.
            if (key != currentKey)
                _keyboardState[key] = (byte)((_keyboardState[key] & 0x80) | (GetKeyState(key) & 1));
        }
    }

    private static bool IsModifier(int key) => key is
        (int)Keys.LShiftKey or (int)Keys.RShiftKey or (int)Keys.LControlKey or (int)Keys.RControlKey
        or (int)Keys.LMenu or (int)Keys.RMenu or (int)Keys.LWin or (int)Keys.RWin;

    private bool IsDown(int key) => (_keyboardState[key] & 0x80) != 0;

    private void SetDown(int key, bool down) =>
        _keyboardState[key] = (byte)((_keyboardState[key] & 1) | (down ? 0x80 : 0));

    private void UpdateAggregateModifiers()
    {
        SetDown((int)Keys.ShiftKey, IsDown((int)Keys.LShiftKey) || IsDown((int)Keys.RShiftKey));
        SetDown((int)Keys.ControlKey, IsDown((int)Keys.LControlKey) || IsDown((int)Keys.RControlKey));
        SetDown((int)Keys.Menu, IsDown((int)Keys.LMenu) || IsDown((int)Keys.RMenu));
    }

    private void ReportError(string message)
    {
        var handlers = Error;
        if (handlers is null)
            return;
        foreach (Action<string> handler in handlers.GetInvocationList())
        {
            try { handler(message); }
            catch { /* Never let consumer failures escape the Windows message callback. */ }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Paused = true;
        if (_registered)
        {
            var devices = new[] { new RawInputDevice { UsagePage = 1, Usage = 6, Flags = RidevRemove } };
            RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf<RawInputDevice>());
            _registered = false;
        }
        if (Handle != 0)
            DestroyHandle();
        if (_rawBuffer != 0)
        {
            Marshal.FreeHGlobal(_rawBuffer);
            _rawBuffer = 0;
        }
        Array.Clear(_keyboardState);
        Array.Clear(_translation);
        _pressedKeys.Clear();
        KeyRecorded = null;
        Error = null;
        GC.SuppressFinalize(this);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public nint Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public uint Type;
        public uint Size;
        public nint Device;
        public nuint WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VirtualKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        public uint Size;
        public uint Flags;
        public nint Active;
        public nint Focus;
        public nint Capture;
        public nint MenuOwner;
        public nint MoveSize;
        public nint Caret;
        public int CaretLeft;
        public int CaretTop;
        public int CaretRight;
        public int CaretBottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices([In] RawInputDevice[] devices, uint number, uint size);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(nint input, uint command, nint data, ref uint size, uint headerSize);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern int GetMessageTime();

    [DllImport("kernel32.dll")]
    private static extern ulong GetTickCount64();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo information);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, StringBuilder className, int count);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern nint GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ToUnicodeEx(uint virtualKey, uint scanCode, [In] byte[] state,
        [Out] char[] buffer, int bufferLength, uint flags, nint keyboardLayout);
}
