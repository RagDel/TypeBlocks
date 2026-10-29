using System.Collections.Concurrent;
using Microsoft.Win32;
using TypeBlocks.Capture;
using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class TrayApplication : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly VaultStore _vault;
    private readonly KeyboardRecorder _recorder;
    private readonly Control _dispatcher = new();
    private readonly BlockingCollection<KeystrokeEntry> _queue = new(8192);
    private readonly Task _writer;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _pause;
    private readonly HotkeyWindow _hotkey;
    private readonly RecordingStatusPopup _statusPopup = new();
    private readonly HistoryUnlockSession _unlockSession = new();
    private readonly System.Windows.Forms.Timer _unlockTimer = new() { Interval = 1000 };
    private int _unlockAttemptVersion;
    private HistoryForm? _history;
    private bool _faulted;
    private bool _exiting;
    private bool _sessionLocked;
    private bool _dialogOpen;
    private Form? _activeDialog;

    public TrayApplication(AppSettings settings, bool background)
    {
        _settings = settings;
        _ = _dispatcher.Handle;
        _vault = VaultStore.OpenForRecording(AppSettings.VaultDirectory);
        _recorder = new KeyboardRecorder();
        ApplyExclusions();
        _pause = new ToolStripMenuItem();
        _pause.Click += (_, _) => TogglePause();
        var menu = new ContextMenuStrip();
        menu.Items.Add("TypeBlocks", null, (_, _) => OpenHistory());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open encrypted history…", null, (_, _) => OpenHistory());
        menu.Items.Add(_pause);
        menu.Items.Add("Settings…", null, (_, _) => OpenSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        _tray = new NotifyIcon { Icon = AppIcon.Application, Visible = true, ContextMenuStrip = menu };
        _tray.DoubleClick += (_, _) => OpenHistory();
        _hotkey = new HotkeyWindow(TogglePause);
        bool shortcutReady = _hotkey.TrySetBinding(_settings.ToggleHotkey, out string? shortcutError);
        // Do not silently start capturing when the owner's privacy shortcut is unavailable.
        if (!shortcutReady)
        {
            _settings.Paused = true;
            try { _settings.Save(); }
            catch (Exception error) { shortcutError += " The paused state could not be saved: " + error.Message; }
        }
        _recorder.KeyRecorded += OnKey;
        _recorder.Error += message => StopWithError(message);
        _writer = Task.Factory.StartNew(WriteLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        SystemEvents.SessionSwitch += SessionSwitch;
        Application.ApplicationExit += OnApplicationExit;
        _unlockTimer.Tick += (_, _) => _unlockSession.CheckExpiry();
        _unlockTimer.Start();
        ApplyPauseState();
        _dispatcher.BeginInvoke(() => _statusPopup.ShowStatus(!_recorder.Paused));
        if (!shortcutReady)
            _tray.ShowBalloonTip(10000, "TypeBlocks · shortcut unavailable", shortcutError + " Recording is paused. Choose another recording hotkey in Settings, or use the tray menu.", ToolTipIcon.Warning);
        if (!background)
        {
            if (shortcutReady)
                _tray.ShowBalloonTip(5000, "TypeBlocks is ready", $"Use the tray icon to open history. {_settings.ToggleHotkey} toggles recording.", ToolTipIcon.Info);
            _dispatcher.BeginInvoke(ShowWindow);
        }
    }

    private void OnKey(KeystrokeEntry entry)
    {
        if (_exiting || _faulted || _recorder.Paused) return;
        if (!_queue.TryAdd(entry)) StopWithError("The write buffer filled up. Recording stopped to prevent silent loss. Restart TypeBlocks after checking available disk space.");
    }

    private void WriteLoop()
    {
        try
        {
            while (!_queue.IsCompleted)
            {
                KeystrokeEntry first;
                try { first = _queue.Take(); } catch (InvalidOperationException) { break; }
                var batch = new List<KeystrokeEntry>(256) { first };
                var deadline = Environment.TickCount64 + 2000;
                while (batch.Count < 256)
                {
                    int wait = (int)Math.Max(0, deadline - Environment.TickCount64);
                    if (!_queue.TryTake(out var entry, wait)) break;
                    batch.Add(entry);
                }
                _vault.Append(batch);
            }
        }
        catch (Exception error)
        {
            if (!_dispatcher.IsDisposed)
                _dispatcher.BeginInvoke(() => StopWithError("Encrypted history could not be saved. The last buffered events may be missing. Recording stopped. " + error.Message));
        }
    }

    private void StopWithError(string message)
    {
        if (_faulted || _exiting) return;
        _faulted = true;
        _settings.Paused = true;
        _recorder.Paused = true;
        try { _settings.Save(); } catch { /* Surface the original failure. */ }
        ApplyPauseState(showStatus: true);
        _tray.ShowBalloonTip(10000, "TypeBlocks stopped", message, ToolTipIcon.Error);
        MessageBox.Show(message + "\n\nRestart the app after resolving the problem.", "TypeBlocks", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void TogglePause()
    {
        if (_exiting || _sessionLocked) return;
        if (_faulted) { MessageBox.Show("Recording stopped after an error. Restart TypeBlocks to try again.", "TypeBlocks"); return; }
        _settings.Paused = !_settings.Paused;
        if (!_settings.Paused) _recorder.ResynchronizeKeyboardState();
        ApplyPauseState(showStatus: true);
        try { _settings.Save(); } catch (Exception error) { Ui.Error(null, error); }
    }

    private void ApplyPauseState(bool showStatus = false)
    {
        bool wasPaused = _recorder.Paused;
        _recorder.Paused = _settings.Paused || _faulted || _sessionLocked;
        _pause.Text = (_settings.Paused ? "Resume recording" : "Pause recording") + (_hotkey.Registered ? $"  ({_hotkey.CurrentBinding})" : "  (shortcut unavailable)");
        _tray.Text = _faulted ? "TypeBlocks · recording error" : _recorder.Paused ? "TypeBlocks · paused" : "TypeBlocks · recording";
        if (_sessionLocked) _statusPopup.Dismiss();
        else if (showStatus || wasPaused != _recorder.Paused) _statusPopup.ShowStatus(!_recorder.Paused);
    }

    private void ApplyExclusions()
    {
        _recorder.ExcludedApplications.Clear();
        foreach (string name in _settings.ExcludedApplications) _recorder.ExcludedApplications.Add(name);
    }

    public void ShowWindow() => OpenHistory();

    private void OpenHistory()
    {
        if (_exiting || _sessionLocked) return;
        _unlockSession.CheckExpiry();
        if (_activeDialog is { IsDisposed: false }) { Ui.BringForward(_activeDialog); return; }
        if (_history is { IsDisposed: false }) { Ui.BringForward(_history); return; }
        if (_dialogOpen || _sessionLocked) return;
        _dialogOpen = true;
        try
        {
            if (!_unlockSession.IsUnlocked)
            {
                int attemptVersion = _unlockAttemptVersion;
                using var unlock = new PasswordForm();
                _activeDialog = unlock;
                if (unlock.ShowDialog() != DialogResult.OK || _sessionLocked || _exiting || attemptVersion != _unlockAttemptVersion) return;
                _unlockSession.Unlock(unlock.Password);
                _activeDialog = null;
            }
            if (!_unlockSession.IsUnlocked) return;
            _history = new HistoryForm(_unlockSession);
            _history.FormClosed += (_, _) => _history = null;
            _history.Show();
        }
        finally { _activeDialog = null; _dialogOpen = false; }
    }

    private void OpenSettings()
    {
        if (_dialogOpen) return;
        _dialogOpen = true;
        _history?.Close();
        try
        {
            using var form = new SettingsForm(_settings, (binding, persist) =>
                _hotkey.TrySetBinding(binding, persist, out string? error) ? null : error);
            form.PasswordChanged += _unlockSession.Lock;
            _activeDialog = form;
            if (form.ShowDialog() == DialogResult.OK) { ApplyExclusions(); ApplyPauseState(); }
        }
        finally { _activeDialog = null; _dialogOpen = false; }
    }

    private void SessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (_exiting || _dispatcher.IsDisposed) return;
        _dispatcher.BeginInvoke(() =>
        {
            if (_exiting) return;
            if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            {
                _sessionLocked = true;
                _unlockAttemptVersion++;
                _unlockSession.Lock();
                _history?.Close();
                if (_activeDialog is PasswordForm unlock) unlock.CancelUnlock();
            }
            else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            { _sessionLocked = false; _recorder.ResynchronizeKeyboardState(); }
            ApplyPauseState();
        });
    }

    private void OnApplicationExit(object? sender, EventArgs e) => Stop();

    private void Stop()
    {
        if (_exiting) return;
        _exiting = true;
        _unlockAttemptVersion++;
        _unlockTimer.Stop();
        _unlockTimer.Dispose();
        _unlockSession.Lock();
        if (_activeDialog is PasswordForm unlock) unlock.CancelUnlock();
        SystemEvents.SessionSwitch -= SessionSwitch;
        Application.ApplicationExit -= OnApplicationExit;
        _recorder.Paused = true;
        _recorder.Dispose();
        _hotkey.Dispose();
        _statusPopup.Dispose();
        _history?.Close();
        _tray.Visible = false;
        _queue.CompleteAdding();
        // Normal exit drains pending events; Windows power loss can lose the last 2 seconds.
        _writer.GetAwaiter().GetResult();
        _vault.Dispose();
        _tray.ContextMenuStrip?.Dispose();
        _tray.Dispose();
        _queue.Dispose();
        _dispatcher.Dispose();
    }

    protected override void ExitThreadCore() { Stop(); base.ExitThreadCore(); }
    protected override void Dispose(bool disposing) { if (disposing) Stop(); base.Dispose(disposing); }
}
