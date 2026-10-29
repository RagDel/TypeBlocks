using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.Json;
using TypeBlocks.Capture;

namespace TypeBlocks;

// Tests only synthetic messages and temporary native registrations; never sends keys or captures input.
internal static class HotkeySmokeTests
{
    public static async Task Verify(string outputDirectory)
    {
        VerifySettings();
        VerifyPrivateClipboardPayload();
        VerifyResumeBoundary();
        VerifyRegistration();
        await VerifyPopup(outputDirectory);
    }

    private static void VerifyPrivateClipboardPayload()
    {
        var data = PrivateClipboard.BuildDataObject("SYNTHETIC-RECOVERY-CODE");
        foreach (string format in new[] { "CanIncludeInClipboardHistory", "CanUploadToCloudClipboard" })
        {
            if (data.GetData(format, autoConvert: false) is not MemoryStream stream || !stream.ToArray().SequenceEqual(new byte[4]))
                throw new Exception("Recovery clipboard data permits history or cloud upload.");
        }
    }

    private static void VerifyResumeBoundary()
    {
        // Absolute synthetic event times are reduced to the DWORD supplied by GetMessageTime.
        // No recorder is created or enabled to exercise these queue-ordering boundaries.
        (long Message, long Now, long Resumed, bool Expected, string Name)[] cases =
        [
            (950, 1100, 1000, false, "input queued during pause"),
            (1000, 1100, 1000, false, "the resume chord's exact tick"),
            (1001, 1100, 1000, true, "input after resuming"),
            (1100, 1300, 1200, false, "a later resume invalidates older queued input"),
            (1201, 1300, 1200, true, "fresh input after the later resume"),
            (0x7FFFFFFF, 0x80000020, 0x80000000, false, "paused input across signed DWORD wrap"),
            (0x80000000, 0x80000020, 0x80000000, false, "same tick at signed DWORD wrap"),
            (0x80000001, 0x80000020, 0x80000000, true, "new input with a negative signed message time"),
            (0xFFFFFFF0, 0x100000020, 0x100000000, false, "paused input across unsigned DWORD wrap"),
            (0x100000000, 0x100000020, 0x100000000, false, "same tick at unsigned DWORD wrap"),
            (0x100000001, 0x100000020, 0x100000000, true, "new input after unsigned DWORD wrap"),
            (0x2FFFFFFF0, 0x300000020, 0x300000000, false, "paused input after multiple uptime epochs"),
            (0x300000000, 0x300000020, 0x300000000, false, "same tick after multiple uptime epochs"),
            (0x300000001, 0x300000020, 0x300000000, true, "new input after multiple uptime epochs")
        ];
        foreach (var test in cases)
            Assert(KeyboardRecorder.IsMessageAfterResume(unchecked((int)test.Message), test.Now, test.Resumed) == test.Expected,
                "Recording resume boundary failed for " + test.Name + ".");
    }

    private static void VerifySettings()
    {
        Assert(RecordingHotkey.Default.ToString() == "Ctrl+Alt+Shift+F11", "Unexpected default recording shortcut.");
        foreach (string json in new[]
        {
            "{\"Paused\":true}",
            "{\"ToggleHotkey\":null}",
            "{\"ToggleHotkey\":{\"Key\":123}}", // F12 is reserved by Windows.
            "{\"ToggleHotkey\":{\"Control\":false,\"Alt\":false}}"
        })
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
            settings.Normalize();
            Assert(settings.ToggleHotkey == RecordingHotkey.Default, "Missing or invalid shortcuts must use the safe default.");
        }

        var custom = new RecordingHotkey { Key = Keys.D7, Control = true, Alt = false, Shift = true };
        foreach (string json in new[] { "{}", "{\"AutoLockMinutes\":0}", "{\"AutoLockMinutes\":-1}", "{\"AutoLockMinutes\":1441}" })
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(json)!;
            settings.Normalize();
            Assert(settings.AutoLockMinutes == 60, "Missing or invalid lock durations must default to one hour.");
        }
        var source = new AppSettings { Paused = true, ToggleHotkey = custom, Theme = AppTheme.Dark, AutoLockMinutes = 15 };
        var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(source))!;
        restored.Normalize();
        Assert(restored.Paused && restored.Theme == AppTheme.Dark && restored.AutoLockMinutes == 15 && restored.ToggleHotkey == custom && custom.ToString() == "Ctrl+Shift+7",
            "Custom shortcut or pause state failed the settings round trip.");
    }

    private static void VerifyRegistration()
    {
        int callbacks = 0;
        using var active = new HotkeyWindow(() => callbacks++);
        using var blocker = new HotkeyWindow(() => { });
        RecordingHotkey first = RegisterFree(active);
        int firstId = active.RegistrationId;
        RecordingHotkey occupied = RegisterFree(blocker, first);
        bool committed = false;
        Assert(!active.TrySetBinding(occupied, () => committed = true, out string? conflict) && !string.IsNullOrWhiteSpace(conflict),
            "A shortcut owned by another window must be rejected with an explanation.");
        Assert(!committed && active.CurrentBinding == first && active.RegistrationId == firstId,
            "A registration conflict changed the previous working shortcut or saved settings.");
        Dispatch(active, firstId, first);
        Assert(callbacks == 1, "The previous shortcut stopped responding after a registration conflict.");
        Dispatch(active, firstId + 1000, first);
        Dispatch(active, firstId, first with { Shift = !first.Shift });
        Assert(callbacks == 1, "A stale registration id or mismatched chord invoked the shortcut.");

        RecordingHotkey replacement = FindFree(first, occupied);
        Assert(active.TrySetBinding(replacement, () =>
        {
            Assert(!blocker.TrySetBinding(first, out _), "The old shortcut was released before settings were saved.");
            Assert(!blocker.TrySetBinding(replacement, out _), "The new shortcut was not reserved during settings save.");
            committed = true;
        }, out string? error), error ?? "Could not change the global shortcut.");
        Assert(committed && active.CurrentBinding == replacement, "A successful shortcut change did not commit.");
        Assert(blocker.TrySetBinding(first, out error), error ?? "The retired shortcut was not released.");
        Dispatch(active, firstId, first);
        Assert(callbacks == 1, "A message queued for the retired shortcut toggled recording.");
        Dispatch(active, active.RegistrationId, replacement);
        Assert(callbacks == 2, "The new global shortcut did not dispatch.");

        RecordingHotkey failedSave = FindFree(first, replacement);
        int replacementId = active.RegistrationId;
        bool saveFailed = false;
        try
        {
            active.TrySetBinding(failedSave, () => throw new IOException("Synthetic settings save failure."), out _);
        }
        catch (IOException) { saveFailed = true; }
        Assert(saveFailed && active.CurrentBinding == replacement && active.RegistrationId == replacementId,
            "A settings save failure changed the working shortcut.");
        Assert(blocker.TrySetBinding(failedSave, out error), error ?? "A failed save left its candidate shortcut reserved.");
        Dispatch(active, replacementId, replacement);
        Assert(callbacks == 3, "The previous shortcut stopped responding after a settings save failure.");

        Assert(!active.TrySetBinding(new RecordingHotkey { Key = Keys.F12 }, out _)
            && active.CurrentBinding == replacement, "An invalid shortcut changed the active registration.");
        active.Dispose();
        active.Dispose();
        Assert(!active.Registered && active.Handle == IntPtr.Zero, "Shortcut disposal left a live native window.");
        Assert(blocker.TrySetBinding(replacement, out error), error ?? "Disposing the shortcut window did not release its registration.");
    }

    private static RecordingHotkey RegisterFree(HotkeyWindow window, params RecordingHotkey[] excluded)
    {
        // Unusual test-only chords avoid taking the default shortcut from a running owner app.
        foreach (Keys key in Enumerable.Range((int)Keys.F13, 12).Select(value => (Keys)value))
        {
            var candidate = new RecordingHotkey { Key = key };
            if (!excluded.Contains(candidate) && window.TrySetBinding(candidate, out _)) return candidate;
        }
        throw new Exception("No spare Ctrl+Alt+Shift+F13–F24 shortcut was available for the synthetic test.");
    }

    private static RecordingHotkey FindFree(params RecordingHotkey[] excluded)
    {
        using var probe = new HotkeyWindow(() => { });
        return RegisterFree(probe, excluded);
    }

    private static void Dispatch(HotkeyWindow window, int registrationId, RecordingHotkey binding)
    {
        uint details = ((uint)binding.Key << 16) | binding.Modifiers;
        SendMessage(window.Handle, 0x0312, registrationId, unchecked((nint)details));
    }

    private static async Task VerifyPopup(string outputDirectory)
    {
        using var popup = new RecordingStatusPopup();
        _ = popup.Handle;
        int extendedStyle = GetWindowLong(popup.Handle, -20);
        Assert((extendedStyle & 0x08000000) != 0 && (extendedStyle & 0x00000080) != 0 && !popup.ShowInTaskbar,
            "The recording popup must be a nonactivating tool window without a taskbar entry.");
        Assert(SendMessage(popup.Handle, 0x0021, IntPtr.Zero, IntPtr.Zero) == 4,
            "Clicking the recording popup could activate it.");
        foreach (bool recording in new[] { true, false })
        {
            popup.SetStatusForTest(recording);
            Assert(popup.StatusText == (recording ? "Typeblocks: On" : "Typeblocks: Off"), "The recording popup label is incorrect.");
            using var bitmap = new Bitmap(popup.Width, popup.Height);
            popup.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(outputDirectory, recording ? "recording-on.png" : "recording-off.png"), ImageFormat.Png);
        }

        popup.ShowStatus(true);
        Assert(popup.Visible && GetForegroundWindow() != popup.Handle, "The recording popup took keyboard focus.");
        await Task.Delay(1100);
        popup.ShowStatus(false);
        Assert(popup.StatusText == "Typeblocks: Off" && GetForegroundWindow() != popup.Handle,
            "A repeated toggle did not update the existing popup without taking focus.");
        await Task.Delay(1100);
        Assert(popup.Visible, "A previous popup timeout hid the latest recording state too early.");
        await Task.Delay(1500);
        Assert(!popup.Visible, "The recording popup did not dismiss automatically.");
        popup.ShowStatus(true);
        popup.Dismiss();
        Assert(!popup.Visible, "The popup was not dismissed when recording UI is shut down.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
