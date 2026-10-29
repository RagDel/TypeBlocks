using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TypeBlocks;

// This local signal contains no password or history. Only the owner process listens.
internal sealed class InstanceActivation : IDisposable
{
    public const string EventName = @"Local\TypeBlocks.ShowWindow";
    private readonly EventWaitHandle _signal;
    private readonly Control _dispatcher = new();
    private readonly RegisteredWaitHandle _wait;
    private Action? _show;
    private bool _pending;
    private volatile bool _disposed;

    public InstanceActivation(EventWaitHandle signal)
    {
        _signal = signal;
        _ = _dispatcher.Handle;
        _wait = ThreadPool.RegisterWaitForSingleObject(signal, (_, _) =>
        {
            if (_disposed) return;
            try { _dispatcher.BeginInvoke(Dispatch); }
            catch (InvalidOperationException) { /* The UI is closing. */ }
        }, null, Timeout.Infinite, executeOnlyOnce: false);
    }

    public void SetHandler(Action? show)
    {
        _show = show;
        if (_pending && show is not null) _dispatcher.BeginInvoke(Dispatch);
    }

    private void Dispatch()
    {
        if (_disposed) return;
        if (_show is null) { _pending = true; return; }
        _pending = false;
        _show();
    }

    public static void RequestShow(EventWaitHandle signal)
    {
        // A user-launched second process may grant foreground permission to the owner.
        foreach (var process in Process.GetProcessesByName("TypeBlocks"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId) continue;
                try { AllowSetForegroundWindow((uint)process.Id); }
                catch (InvalidOperationException) { }
            }
        }
        signal.Set();
    }

    public void Dispose()
    {
        _disposed = true;
        _wait.Unregister(null);
        _dispatcher.Dispose();
        GC.KeepAlive(_signal);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);
}
