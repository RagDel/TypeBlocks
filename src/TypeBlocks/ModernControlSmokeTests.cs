using System.Runtime.InteropServices;

namespace TypeBlocks;

// Delivers messages only to synthetic controls in this process. No global input or clipboard use.
internal static class ModernControlSmokeTests
{
    public static void Verify(Action<Form> prepareForm)
    {
        using var host = new Form { ClientSize = new Size(420, 280), Font = Ui.Font(10.5f) };
        var choice = new ModernComboBox { Location = new(16, 16), AccessibleName = "Synthetic choice" };
        var input = new ModernTextBox
        {
            Location = new(16, 80), Size = new(380, 160), Multiline = true,
            ScrollBars = ScrollBars.Vertical, AccessibleName = "Synthetic editor", Text = "Original text"
        };
        host.Controls.AddRange([choice, input]);
        for (int index = 0; index < 12; index++) choice.Items.Add("Choice " + index);
        choice.SelectedIndex = 1;
        prepareForm(host);

        VerifyChoice(host, choice);
        VerifyEditor(choice, input);
    }

    private static void VerifyChoice(Form host, ModernComboBox choice)
    {
        int changes = 0;
        choice.SelectedIndexChanged += (_, _) =>
        {
            changes++;
            _ = choice.SelectedItem; // Clearing an expanded list must also be safe inside observers.
            _ = choice.AccessibilityObject.Value;
        };
        choice.Focus();
        Key(choice, Keys.Down);
        Assert(choice.SelectedIndex == 2 && changes == 1, "Collapsed dropdown arrow selection did not update once.");
        Key(choice, Keys.End);
        Key(choice, Keys.PageUp);
        Assert(choice.SelectedIndex == 3, "Collapsed dropdown page navigation failed.");
        Key(choice, Keys.Home);
        Assert(choice.SelectedIndex == 0, "Collapsed dropdown Home navigation failed.");

        choice.SelectedIndex = 2;
        changes = 0;
        Key(choice, Keys.Space);
        Assert(choice.DroppedDown, "Space did not expand the dropdown.");
        Form popup = host.OwnedForms.Single();
        Key(popup, Keys.Down);
        Assert(choice.SelectedIndex == 2 && changes == 0, "Moving the dropdown highlight committed before confirmation.");
        Key(popup, Keys.Enter);
        Assert(!choice.DroppedDown && choice.SelectedIndex == 3 && changes == 1 && choice.Focused,
            "Dropdown confirmation did not select once, close, and restore focus.");

        Key(choice, Keys.Space);
        popup = host.OwnedForms.Single();
        Key(popup, Keys.End);
        Key(popup, Keys.Escape);
        Assert(!choice.DroppedDown && choice.SelectedIndex == 3 && changes == 1 && choice.Focused,
            "Canceling the dropdown changed its selection or lost focus.");

        Key(choice, Keys.Space);
        choice.Items.Clear();
        Assert(!choice.DroppedDown && choice.Items.Count == 0 && choice.SelectedIndex == -1 &&
            choice.SelectedItem is null && choice.Text.Length == 0 && changes == 2,
            "Clearing an expanded dropdown did not close and clear its selection once.");
        Key(choice, Keys.Space);
        Assert(!choice.DroppedDown, "An empty dropdown opened a popup.");
        choice.Items.Add("Replacement");
        Key(choice, Keys.Down);
        Assert(choice.SelectedIndex == 0 && choice.Text == "Replacement", "Dropdown selection failed after repopulating items.");
    }

    private static void VerifyEditor(Control otherControl, ModernTextBox input)
    {
        var editor = input.Controls.OfType<NativeTextEditor>().Single();
        var rail = input.Controls.OfType<EditorScrollBar>().Single();
        int viewChanges = 0, railInvalidations = 0;
        editor.ViewChanged += () => viewChanges++;
        rail.Invalidated += (_, _) => railInvalidations++;
        editor.SelectAll();
        editor.SelectedText = "Replaced\r\nwithout a keyboard event";
        Assert(viewChanges > 0 && railInvalidations > 0,
            "Replacing selected text did not refresh the custom scrollbar.");
        // WinForms SelectedText deliberately disables Undo; add a separate undoable native edit.
        string beforeUndoableEdit = editor.Text;
        editor.SelectAll();
        SendTextMessage(editor.Handle, 0x00C2, 1, "Undoable\r\nsynthetic replacement"); // EM_REPLACESEL, allow Undo.
        Assert(editor.CanUndo, "The synthetic text replacement did not enter the native undo history.");
        viewChanges = railInvalidations = 0;
        editor.Undo();
        Assert(editor.Text == beforeUndoableEdit && viewChanges > 0 && railInvalidations > 0,
            "Native Undo did not restore the text and refresh the scrollbar.");

        editor.Text = string.Join("\r\n", Enumerable.Range(1, 40).Select(index => "Synthetic line " + index));
        editor.Select(0, 0);
        editor.ScrollToLine(0);
        otherControl.Focus();
        Assert(rail.Visible && !rail.CanSelect && !rail.TabStop, "The editor scrollbar is missing or takes keyboard focus.");
        nint start = (nint)(2 << 16 | rail.Width / 2);
        nint point = (nint)((rail.Height - 2) << 16 | rail.Width / 2);
        SendMessage(rail.Handle, 0x0201, 1, start); // WM_LBUTTONDOWN, only this synthetic rail.
        SendMessage(rail.Handle, 0x0200, 1, point); // WM_MOUSEMOVE
        SendMessage(rail.Handle, 0x0202, 0, point); // WM_LBUTTONUP
        Assert(editor.Focused && editor.FirstLine > 0 && !rail.Capture,
            "Dragging the editor scrollbar did not scroll, restore editor focus, and release capture.");
        editor.ScrollToLine(int.MaxValue);
        Assert(editor.FirstLine <= Math.Max(0, editor.LineCount - editor.VisibleLines), "Editor scrolling exceeded its last page.");
        editor.ScrollToLine(-1);
        Assert(editor.FirstLine == 0, "Editor scrolling did not clamp to the first line.");
    }

    private static void Key(Control control, Keys key)
    {
        var message = Message.Create(control.Handle, 0x0100, (nint)key, 0);
        if (!control.PreProcessMessage(ref message)) SendMessage(control.Handle, message.Msg, message.WParam, message.LParam);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, int message, nint wParam, nint lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern nint SendTextMessage(nint window, int message, nint wParam, string text);
}
