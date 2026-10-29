using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TypeBlocks;

/// <summary>Copies an explicitly requested recovery code, with bounded, ownership-checked cleanup.</summary>
internal static class PrivateClipboard
{
    private static System.Windows.Forms.Timer? _timer;
    private static byte[]? _expectedHash;
    private static int _expectedByteLength;
    private static uint _sequence;
    private static bool _retried;

    public static void CopyRecoveryCode(string code)
    {
        var data = BuildDataObject(code);
        byte[] bytes = Encoding.Unicode.GetBytes(code);
        byte[] hash;
        try { hash = SHA256.HashData(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
        try { Clipboard.SetDataObject(data, copy: true, retryTimes: 2, retryDelay: 50); }
        catch { CryptographicOperations.ZeroMemory(hash); throw; }

        Forget();
        _expectedHash = hash;
        _expectedByteLength = code.Length * sizeof(char);
        _sequence = GetClipboardSequenceNumber();
        if (_timer is null)
        {
            _timer = new System.Windows.Forms.Timer();
            _timer.Tick += (_, _) => ClearIfOwned();
        }
        _timer.Interval = 45_000;
        _timer.Start();
    }

    /// <summary>Builds a synthetic-testable payload without reading or writing the system clipboard.</summary>
    internal static DataObject BuildDataObject(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);
        if (code.Length > 256 || code.Contains('\0')) throw new ArgumentException("The recovery code is invalid.", nameof(code));
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, autoConvert: false, code);
        // Windows specifies serialized DWORD zeros. Streams are transferred as raw HGLOBAL bytes.
        // https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
        data.SetData("CanIncludeInClipboardHistory", autoConvert: false, new MemoryStream(new byte[4], writable: false));
        data.SetData("CanUploadToCloudClipboard", autoConvert: false, new MemoryStream(new byte[4], writable: false));
        return data;
    }

    /// <summary>UI thread only. Also called before exit; never clears a replaced clipboard item.</summary>
    public static void ClearIfOwned()
    {
        _timer?.Stop();
        if (_expectedHash is null) return;
        bool complete;
        try { complete = TryClear(); }
        catch { complete = true; } // Clipboard cleanup must never crash or interrupt the app.
        if (!complete && !_retried && _timer is not null)
        {
            _retried = true;
            _timer.Interval = 1000;
            _timer.Start();
            return;
        }
        Forget();
    }

    private static bool TryClear()
    {
        if (_sequence == 0 || GetClipboardSequenceNumber() != _sequence) return true;
        if (!OpenClipboard(IntPtr.Zero)) return false;
        try
        {
            // Keep comparison and clearing within one clipboard lock so a new copy cannot race us.
            if (GetClipboardSequenceNumber() != _sequence) return true;
            nint handle = GetClipboardData(13); // CF_UNICODETEXT
            if (handle == 0) return false;
            if (GlobalSize(handle).ToUInt64() < (ulong)(_expectedByteLength + sizeof(char))) return true;
            nint pointer = GlobalLock(handle);
            if (pointer == 0) return false;
            bool matches;
            try
            {
                if (Marshal.ReadInt16(pointer, _expectedByteLength) != 0) return true;
                byte[] bytes = new byte[_expectedByteLength];
                byte[]? hash = null;
                try
                {
                    Marshal.Copy(pointer, bytes, 0, bytes.Length);
                    hash = SHA256.HashData(bytes);
                    matches = CryptographicOperations.FixedTimeEquals(hash, _expectedHash!);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(bytes);
                    if (hash is not null) CryptographicOperations.ZeroMemory(hash);
                }
            }
            finally { GlobalUnlock(handle); }
            return !matches || GetClipboardSequenceNumber() != _sequence || EmptyClipboard();
        }
        finally { CloseClipboard(); }
    }

    private static void Forget()
    {
        _timer?.Stop();
        if (_expectedHash is not null) CryptographicOperations.ZeroMemory(_expectedHash);
        _expectedHash = null;
        _expectedByteLength = 0;
        _sequence = 0;
        _retried = false;
    }

    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenClipboard(nint window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern nint GetClipboardData(uint format);
    [DllImport("kernel32.dll")] private static extern nuint GlobalSize(nint memory);
    [DllImport("kernel32.dll")] private static extern nint GlobalLock(nint memory);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalUnlock(nint memory);
}
