using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class HistoryForm : Form
{
    public event Action? SettingsRequested;

    private readonly HistoryUnlockSession _unlockSession;
    private readonly string _directory;
    private readonly ModernComboBox _dates = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new(28, 124), Size = new(180, 44), AccessibleName = "History date" };
    private readonly ModernTextBox _search = new() { PlaceholderText = "Search history", Icon = UiIcon.Search, Location = new(220, 124), Size = new(412, 44), AccessibleName = "Search history" };
    private readonly Button _findNext = Ui.Button("Find next", 644, 124, 118);
    private readonly Button _refresh;
    private readonly RoundedPanel _views = new() { Radius = 20, Location = new(28, 252), Size = new(884, 380), Padding = new Padding(20), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
    private readonly RoundedPanel _viewSwitch = new() { Radius = 16, Location = new(28, 184), Size = new(270, 52), AccessibleName = "History view" };
    private readonly ModernRadioButton _textView = new() { Text = "Text view", Appearance = Appearance.Button, Checked = true, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Location = new(6, 6), Size = new(126, 40), Cursor = Cursors.Hand };
    private readonly ModernRadioButton _eventView = new() { Text = "Event table", Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Location = new(132, 6), Size = new(132, 40), Cursor = Cursors.Hand };
    private readonly Panel _textPage = new() { Dock = DockStyle.Fill };
    private readonly Panel _tablePage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Panel _textToolbar = new() { Dock = DockStyle.Top, Height = 72 };
    private readonly RoundedPanel _modeSwitch = new() { Radius = 16, Size = new(430, 54), AccessibleName = "Text mode" };
    private readonly RichTextBox _text = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = true, HideSelection = false, BorderStyle = BorderStyle.None, Font = Ui.Font(11.5f), ScrollBars = RichTextBoxScrollBars.Vertical, AccessibleName = "History transcript" };
    private readonly ModernRadioButton _allKeys = new() { Text = "All keys", Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Location = new(6, 6), Size = new(112, 42), Cursor = Cursors.Hand, TabIndex = 0 };
    private readonly ModernRadioButton _pureText = new() { Text = "Pure Text", Appearance = Appearance.Button, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Location = new(118, 6), Size = new(128, 42), Cursor = Cursors.Hand, TabIndex = 1 };
    private readonly ModernRadioButton _intelligentPureText = new() { Text = "Intelligent Text", Appearance = Appearance.Button, Checked = true, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Location = new(246, 6), Size = new(178, 42), Cursor = Cursors.Hand, TabIndex = 2 };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoGenerateColumns = false, BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal, ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToResizeRows = false, AccessibleName = "Recorded key events" };
    private readonly Label _count = Ui.Label("", 28, 650, 884, 26);
    private readonly Label _unlockStatus = Ui.Label("", 28, 90, 560, 26);
    private IReadOnlyList<KeystrokeEntry> _entries = [];
    private readonly System.Windows.Forms.Timer _lockTimer = new() { Interval = 1000 };
    private int _loadVersion;
    private bool _populatingDates;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private int _nextSearchStart;
    private int _sortColumn;
    private bool _sortAscending;
    private bool IsTextView => _textView.Checked;
    private HistoryTextMode TextMode => _allKeys.Checked ? HistoryTextMode.AllKeys : _pureText.Checked ? HistoryTextMode.PureText : HistoryTextMode.IntelligentPureText;

    public HistoryForm(HistoryUnlockSession unlockSession, string? directory = null)
    {
        _unlockSession = unlockSession;
        _directory = directory ?? AppSettings.VaultDirectory;
        Ui.Style(this, "TypeBlocks · History", new(940, 690));
        MinimumSize = new(860, 620);
        KeyPreview = true;
        _allKeys.CheckedChanged += (_, _) => { if (_allKeys.Checked) RenderTextView(); };
        _pureText.CheckedChanged += (_, _) => { if (_pureText.Checked) RenderTextView(); };
        _intelligentPureText.CheckedChanged += (_, _) => { if (_intelligentPureText.Checked) RenderTextView(); };
        _modeSwitch.Controls.AddRange([_allKeys, _pureText, _intelligentPureText]);
        _textToolbar.Controls.Add(_modeSwitch);
        _textPage.Controls.Add(_text);
        _textPage.Controls.Add(_textToolbar);
        _tablePage.Controls.Add(_grid);
        _views.Controls.AddRange([_textPage, _tablePage]);
        _viewSwitch.Controls.AddRange([_textView, _eventView]);
        _textView.CheckedChanged += (_, _) => { if (_textView.Checked) UpdateSelectedView(); };
        _eventView.CheckedChanged += (_, _) => { if (_eventView.Checked) UpdateSelectedView(); };
        Controls.AddRange([
            Ui.Label("Your history", 28, 26, 400, 44, 24, true),
            _unlockStatus,
            _dates, _search, _viewSwitch, _views, _count
        ]);
        var lockButton = Ui.Button("Lock && close", 730, 32, 182, icon: UiIcon.Lock);
        lockButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lockButton.Click += (_, _) => _unlockSession.Lock();
        var settingsButton = Ui.Button("Settings", 580, 32, 138, icon: UiIcon.Settings);
        settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        _refresh = Ui.Button("Refresh", 774, 124, 138, icon: UiIcon.Refresh);
        _refresh.Click += async (_, _) => { PopulateDates(_dates.SelectedItem as DateOnly?); await LoadDay(); };
        _findNext.Click += (_, _) =>
        {
            if (_search.Text.Length == 0) { _search.Focus(); return; }
            FindText(restart: false);
        };
        Controls.AddRange([settingsButton, lockButton, _refresh, _findNext]);
        _count.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time", DataPropertyName = "Time", Width = 155 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Application", DataPropertyName = "Application", Width = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", DataPropertyName = "Key", Width = 210 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text (best effort)", DataPropertyName = "Text", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.ColumnHeaderMouseClick += (_, e) => { if (e.ColumnIndex >= 0) SortByColumn(e.ColumnIndex); };
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersHeight = 44;
        _grid.RowTemplate.Height = 38;
        _dates.SelectedIndexChanged += async (_, _) => { if (!_populatingDates) await LoadDay(); };
        _search.TextChanged += (_, _) => UpdateSelectedView();
        _search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && IsTextView) { e.SuppressKeyPress = true; FindText(restart: false); } };
        KeyDown += (_, e) =>
        {
            if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; _search.Focus(); _search.SelectAll(); }
            else if (e.Control && e.KeyCode == Keys.Tab) { e.SuppressKeyPress = true; if (IsTextView) _eventView.Checked = true; else _textView.Checked = true; }
        };
        _unlockSession.Locked += CloseForLock;
        _lockTimer.Tick += (_, _) => UpdateUnlockStatus();
        ClientSizeChanged += (_, _) => LayoutToolbar();
        Shown += async (_, _) => { UpdateUnlockStatus(); if (IsDisposed) return; LayoutToolbar(); PopulateDates(); _lockTimer.Start(); await LoadDay(); };
        FormClosed += (_, _) => { _loadVersion++; _lockTimer.Stop(); _unlockSession.Locked -= CloseForLock; Ui.ThemeChanged -= ApplyHistoryTheme; ClearHistory(); _search.Clear(); };
        HandleCreated += (_, _) => ApplyHistoryTheme();
        Ui.ThemeChanged += ApplyHistoryTheme;
        Ui.ApplyTheme(this);
        ApplyHistoryTheme();
        LayoutToolbar();
    }

    private void ApplyHistoryTheme()
    {
        if (IsDisposed) return;
        _views.BackColor = _textPage.BackColor = _tablePage.BackColor = _textToolbar.BackColor = Ui.Surface;
        _viewSwitch.BackColor = _modeSwitch.BackColor = Ui.Input;
        _text.BackColor = Ui.Surface;
        _text.ForeColor = Ui.Ink;
        _grid.GridColor = Ui.Border;
    }

    private void CloseForLock() => Close();

    private void UpdateUnlockStatus()
    {
        TimeSpan remaining = _unlockSession.Remaining;
        if (remaining <= TimeSpan.Zero) { if (!IsDisposed) Close(); return; }
        if (IsDisposed) return;
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        _unlockStatus.Text = $"Locks in {seconds / 60:00}:{seconds % 60:00}";
    }

    private void LayoutToolbar()
    {
        if (_refresh is null) return;
        // Use the geometry WinForms has actually scaled, including during offscreen rendering.
        // DeviceDpi can already describe a new monitor before sibling controls finish scaling.
        float layoutScale = _dates.Width / 180f;
        int Scale(int value) => (int)Math.Round(value * layoutScale);
        int gap = Scale(12), buttonHeight = Scale(44);
        int buttonTop = _dates.Top + (_dates.Height - buttonHeight) / 2;
        int refreshWidth = Scale(138), findWidth = Scale(118);
        _refresh.SetBounds(ClientSize.Width - _dates.Left - refreshWidth, buttonTop, refreshWidth, buttonHeight);
        _findNext.SetBounds(_refresh.Left - gap - findWidth, buttonTop, findWidth, buttonHeight);
        int searchLeft = _dates.Right + gap;
        int searchRight = (IsTextView ? _findNext.Left : _refresh.Left) - gap;
        _search.SetBounds(searchLeft, buttonTop, Math.Max(Scale(100), searchRight - searchLeft), buttonHeight);
    }

    private void PopulateDates(DateOnly? selected = null)
    {
        try
        {
            _populatingDates = true;
            _dates.Items.Clear();
            foreach (var date in VaultStore.ListDates(_directory).OrderDescending()) _dates.Items.Add(date);
            if (selected.HasValue && _dates.Items.Contains(selected.Value)) _dates.SelectedItem = selected.Value;
            else if (_dates.Items.Count > 0) _dates.SelectedIndex = 0;
            else { _loadVersion++; ClearHistory(); _count.Text = "No history yet."; }
        }
        catch (Exception error) { Ui.Error(this, error); }
        finally { _populatingDates = false; }
    }

    internal async Task PreparePreviewForTest() { PopulateDates(); await LoadDay(); }

    internal async Task LoadDay()
    {
        if (!_unlockSession.TryGetPassword(out string? password)) { if (!IsDisposed) Close(); return; }
        if (IsDisposed || _dates.SelectedItem is not DateOnly date) return;
        long unlockVersion = _unlockSession.Version;
        if (IsDisposed || !_unlockSession.IsUnlocked || IsDisposed) return;
        int version = ++_loadVersion;
        _count.Text = "Decrypting history…";
        ClearHistory();
        await _loadGate.WaitAsync();
        try
        {
            if (!CanShowHistory(version, unlockVersion)) return;
            var entries = await Task.Run(() => VaultStore.ReadHistory(_directory, password, date));
            if (!CanShowHistory(version, unlockVersion)) return;
            _entries = entries;
            RenderTextView();
            if (IsTextView && _search.Text.Length == 0) { _text.Select(0, 0); _text.ScrollToCaret(); }
        }
        catch (Exception error) { if (CanShowHistory(version, unlockVersion)) { _count.Text = "History could not be opened."; Ui.Error(this, error); } }
        finally { _loadGate.Release(); }
    }

    private bool CanShowHistory(int loadVersion, long unlockVersion) =>
        !IsDisposed && loadVersion == _loadVersion && _unlockSession.IsUnlocked &&
        _unlockSession.Version == unlockVersion && !IsDisposed;

    private void ClearHistory()
    {
        _entries = [];
        _grid.DataSource = null;
        _text.Clear();
        _nextSearchStart = 0;
        _findNext.Enabled = true;
    }

    private void RenderTextView()
    {
        _text.Text = HistoryTextFormatter.Format(_entries, TextMode, newestFirst: true);
        UpdateSelectedView();
    }

    private void UpdateSelectedView()
    {
        _textPage.Visible = IsTextView;
        _tablePage.Visible = !IsTextView;
        _search.PlaceholderText = IsTextView ? "Search history" : "Filter events";
        _findNext.Visible = IsTextView;
        LayoutToolbar();
        if (IsTextView) FindText(restart: true);
        else ApplyTableFilter();
    }

    private void FindText(bool restart)
    {
        if (!IsTextView) return;
        string query = _search.Text;
        _findNext.Enabled = true;
        if (restart) _nextSearchStart = 0;
        if (query.Length == 0)
        {
            _text.Select(0, 0);
            _count.Text = "";
            return;
        }
        int index = _text.Find(query, Math.Min(_nextSearchStart, _text.TextLength), RichTextBoxFinds.None);
        if (index < 0 && _nextSearchStart > 0) index = _text.Find(query, 0, RichTextBoxFinds.None);
        if (index >= 0)
        {
            _text.Select(index, query.Length);
            _text.ScrollToCaret();
            _nextSearchStart = index + query.Length;
            _count.Text = "Match found";
        }
        else
        {
            _nextSearchStart = 0;
            _text.Select(0, 0);
            _count.Text = "No matches";
        }
    }

    private void ApplyTableFilter()
    {
        string query = _search.Text;
        var matching = _entries.Where(e => query.Length == 0 || e.Application.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Key.Contains(query, StringComparison.OrdinalIgnoreCase) || (e.Text?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
        // Keep the native grid responsive even when a full day's history is large.
        var rows = matching.TakeLast(25000).Select(e => new EventRow(e.Timestamp, e.Application, e.Key + (e.IsRepeat ? " (repeat)" : ""), e.Text ?? ""));
        IOrderedEnumerable<EventRow> sorted = _sortColumn switch
        {
            1 => _sortAscending ? rows.OrderBy(e => e.Application, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(e => e.Application, StringComparer.OrdinalIgnoreCase),
            2 => _sortAscending ? rows.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(e => e.Key, StringComparer.OrdinalIgnoreCase),
            3 => _sortAscending ? rows.OrderBy(e => e.Text, StringComparer.OrdinalIgnoreCase) : rows.OrderByDescending(e => e.Text, StringComparer.OrdinalIgnoreCase),
            _ => _sortAscending ? rows.OrderBy(e => e.Timestamp) : rows.OrderByDescending(e => e.Timestamp)
        };
        _grid.DataSource = sorted.ThenBy(e => e.Timestamp).ToList();
        foreach (DataGridViewColumn column in _grid.Columns) column.HeaderCell.SortGlyphDirection = column.Index == _sortColumn ? (_sortAscending ? SortOrder.Ascending : SortOrder.Descending) : SortOrder.None;
        _count.Text = matching.Count > 25000 ? $"Newest 25,000 of {matching.Count:N0} events" : $"{matching.Count:N0} events";
    }

    private void SortByColumn(int index)
    {
        _sortAscending = index == _sortColumn ? !_sortAscending : index != 0;
        _sortColumn = index;
        ApplyTableFilter();
    }

    private sealed record EventRow(DateTimeOffset Timestamp, string Application, string Key, string Text)
    {
        public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
    }

    internal async Task VerifyTextViewForTest()
    {
        if (!IsTextView || TextMode != HistoryTextMode.IntelligentPureText || !_allKeys.Visible || !_pureText.Visible || !_intelligentPureText.Visible || !_text.Text.Contains("Hello", StringComparison.Ordinal)) throw new Exception("The default intelligent pure text view did not join typed characters.");
        VerifyToolbarBounds();
        Size originalSize = Size;
        try { Size = MinimumSize; LayoutToolbar(); VerifyToolbarBounds(); }
        finally { Size = originalSize; LayoutToolbar(); }
        foreach (string marker in new[] { "[Backspace]", "[Left]", "[Ctrl+V]", "[Ctrl+S]" })
            if (_text.Text.Contains(marker, StringComparison.Ordinal)) throw new Exception("Pure text displayed a special-key marker.");
        _allKeys.PerformClick();
        if (!_allKeys.Checked || _pureText.Checked || _intelligentPureText.Checked || TextMode != HistoryTextMode.AllKeys)
            throw new Exception("All keys did not become the only selected text mode.");
        foreach (string marker in new[] { "[Backspace]", "[Left]", "[Ctrl+V]", "[Ctrl+S]" })
            if (!_text.Text.Contains(marker, StringComparison.Ordinal)) throw new Exception("All keys omitted a recorded key marker.");
        if (!_text.Text.Contains("12999[Backspace][Backspace][Backspace]345", StringComparison.Ordinal))
            throw new Exception("All keys applied edits instead of retaining the recorded sequence.");
        _search.Text = "[Backspace]";
        await LoadDay();
        if (TextMode != HistoryTextMode.AllKeys || _text.SelectedText != "[Backspace]")
            throw new Exception("Reloading lost All keys or its key-marker search.");
        _allKeys.PerformClick();
        if (!_allKeys.Checked || _pureText.Checked || _intelligentPureText.Checked)
            throw new Exception("Clicking the selected All keys segment changed its selection.");
        _eventView.PerformClick();
        if (_allKeys.Visible || _pureText.Visible || _intelligentPureText.Visible)
            throw new Exception("Text-mode buttons are visible outside Text view.");
        _textView.PerformClick();
        if (!_allKeys.Visible || TextMode != HistoryTextMode.AllKeys || _text.SelectedText != "[Backspace]")
            throw new Exception("Switching views lost All keys or its search.");
        _intelligentPureText.PerformClick();
        if (_allKeys.Checked) throw new Exception("Intelligent Text left All keys selected.");
        _search.Clear();
        _findNext.PerformClick();
        if (!_findNext.Enabled || !_search.ContainsFocus) throw new Exception("Find next did not focus the empty search field.");
        var searchEditor = _search.Controls.OfType<TextBox>().Single();
        if (searchEditor.AutoSize || searchEditor.Height < Ui.NativeLineHeight(searchEditor)) throw new Exception("The native search editor clips the font's line height.");
        searchEditor.Text = "Hello";
        if (_text.SelectedText != "Hello") throw new Exception("Text search did not find a phrase across key events.");
        _search.Text = "12345";
        if (_text.SelectedText != "12345") throw new Exception("Pure text search did not find digits corrected with Backspace.");
        if (!_text.Text.Contains("Word: cat", StringComparison.Ordinal)) throw new Exception("Pure text did not insert text at the recorded caret.");
        _pureText.Checked = true;
        if (_intelligentPureText.Checked || TextMode != HistoryTextMode.PureText) throw new Exception("The two pure text modes are not mutually exclusive.");
        if (_text.SelectedText.Length != 0 || _count.Text != "No matches") throw new Exception("Switching to pure text did not update the active search.");
        if (!_text.Text.Contains("12999345", StringComparison.Ordinal) || !_text.Text.Contains("Word: cta", StringComparison.Ordinal)) throw new Exception("Pure text applied edits instead of keeping typed characters.");
        foreach (string marker in new[] { "[Backspace]", "[Left]", "[Ctrl+V]", "[Ctrl+S]" })
            if (_text.Text.Contains(marker, StringComparison.Ordinal)) throw new Exception("Pure text displayed a special-key marker.");
        _search.Text = "12999345";
        if (_text.SelectedText != "12999345") throw new Exception("Pure text could not search the unedited characters.");
        await LoadDay();
        if (TextMode != HistoryTextMode.PureText || _text.SelectedText != "12999345") throw new Exception("Reloading lost the selected pure text mode or search.");
        _pureText.PerformClick();
        if (!_pureText.Checked || _intelligentPureText.Checked || TextMode != HistoryTextMode.PureText) throw new Exception("Clicking the selected Pure Text segment cleared the selection.");
        _search.Text = "[Ctrl+V]";
        if (_text.SelectedText.Length != 0 || _count.Text != "No matches") throw new Exception("Pure Text exposed a shortcut marker in search.");
        _intelligentPureText.PerformClick();
        if (_pureText.Checked || !_intelligentPureText.Checked || TextMode != HistoryTextMode.IntelligentPureText) throw new Exception("Choosing Intelligent Text did not switch the selected segment.");
        _search.Text = "12345";
        await LoadDay();
        if (TextMode != HistoryTextMode.IntelligentPureText || _text.SelectedText != "12345") throw new Exception("Reloading history lost Intelligent Text or search.");
        _intelligentPureText.PerformClick();
        if (!_intelligentPureText.Checked || _pureText.Checked) throw new Exception("Clicking the selected Intelligent Text segment cleared the selection.");
        _pureText.PerformClick();
        _eventView.Checked = true;
        _search.Clear();
        if (_pureText.Visible || _intelligentPureText.Visible) throw new Exception("A text-mode button is visible outside the text view.");
        if (_grid.Rows.Count != _entries.Count) throw new Exception("The optional event table is missing events.");
        if (!((List<EventRow>)_grid.DataSource!).Any(row => row.Key == "Ctrl+V")) throw new Exception("Pure text removed a shortcut from the event table.");
        for (int column = 0; column < _grid.Columns.Count; column++)
        {
            for (int direction = 0; direction < 2; direction++)
            {
                SortByColumn(column);
                var rows = ((List<EventRow>)_grid.DataSource!).ToArray();
                for (int i = 1; i < rows.Length; i++)
                {
                    int comparison = column switch
                    {
                        1 => StringComparer.OrdinalIgnoreCase.Compare(rows[i - 1].Application, rows[i].Application),
                        2 => StringComparer.OrdinalIgnoreCase.Compare(rows[i - 1].Key, rows[i].Key),
                        3 => StringComparer.OrdinalIgnoreCase.Compare(rows[i - 1].Text, rows[i].Text),
                        _ => rows[i - 1].Timestamp.CompareTo(rows[i].Timestamp)
                    };
                    if (_sortAscending ? comparison > 0 : comparison < 0) throw new Exception("Table column sort order is incorrect.");
                }
            }
        }
        _sortColumn = 0; _sortAscending = false; ApplyTableFilter();
        _textView.Checked = true;
        if (TextMode != HistoryTextMode.PureText || !_pureText.Checked || !_text.Text.Contains("12999345", StringComparison.Ordinal)) throw new Exception("Switching views lost the selected text mode.");
        _intelligentPureText.Checked = true;
        if (_pureText.Checked || TextMode != HistoryTextMode.IntelligentPureText) throw new Exception("Intelligent pure text did not turn off the plain pure text mode.");
        if (_count.Text.Length != 0) throw new Exception("The idle text view shows unnecessary status text.");
        _search.Text = "12345";
        AppTheme initialTheme = Ui.Theme;
        try
        {
            Ui.SetTheme(initialTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light);
            if (_text.BackColor != Ui.Surface || _text.ForeColor != Ui.Ink || _grid.GridColor != Ui.Border)
                throw new Exception("History did not apply the changed theme.");
            if (_text.SelectedText != "12345" || TextMode != HistoryTextMode.IntelligentPureText)
                throw new Exception("Changing the theme lost text mode or search state.");
        }
        finally { Ui.SetTheme(initialTheme); }
        _search.Clear();
    }

    private void VerifyToolbarBounds()
    {
        if (_textToolbar.Bottom > _text.Top || !_textToolbar.ClientRectangle.Contains(_modeSwitch.Bounds) ||
            !_modeSwitch.ClientRectangle.Contains(_allKeys.Bounds) || !_modeSwitch.ClientRectangle.Contains(_pureText.Bounds) ||
            !_modeSwitch.ClientRectangle.Contains(_intelligentPureText.Bounds) || _allKeys.Right > _pureText.Left || _pureText.Right > _intelligentPureText.Left)
            throw new Exception("The three text-mode segments overlap the transcript or are clipped.");
        if (_dates.Right >= _search.Left || _search.Right >= _findNext.Left || _findNext.Right >= _refresh.Left)
            throw new Exception("History toolbar controls overlap horizontally.");
        if (_search.Top != _refresh.Top || _search.Height != _refresh.Height || Math.Abs((_dates.Top * 2 + _dates.Height) - (_refresh.Top * 2 + _refresh.Height)) > 1)
            throw new Exception("History toolbar controls do not align with the date selector.");
        if (Math.Max(Math.Max(_dates.Bottom, _search.Bottom), Math.Max(_findNext.Bottom, _refresh.Bottom)) >= _viewSwitch.Top)
            throw new Exception("The history toolbar overlaps the view buttons vertically.");
        if (_refresh.Right > ClientSize.Width - _dates.Left)
            throw new Exception("The history toolbar extends past its right margin.");
    }

    internal void SelectTableForPreview() => _eventView.Checked = true;
    internal void SelectTextForPreview() => _textView.Checked = true;
    internal void SelectAllKeysForPreview() { _textView.Checked = true; _allKeys.Checked = true; }

    internal void CheckUnlockForTest() => UpdateUnlockStatus();
    internal bool IsHistoryClearedForTest => _entries.Count == 0 && _text.Text.Length == 0 && _grid.DataSource is null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _unlockSession.Locked -= CloseForLock; Ui.ThemeChanged -= ApplyHistoryTheme; _lockTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
