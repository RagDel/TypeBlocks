using TypeBlocks.Core;

namespace TypeBlocks;

internal sealed class HistoryForm : Form
{
    private readonly HistoryUnlockSession _unlockSession;
    private readonly string _directory;
    private readonly ComboBox _dates = new() { DropDownStyle = ComboBoxStyle.DropDownList, Location = new(28, 136), Width = 166 };
    private readonly TextBox _search = new() { PlaceholderText = "Find a word or phrase in this day", Location = new(210, 136), Width = 356 };
    private readonly Button _findNext = Ui.Button("Find next", 580, 128, 124);
    private readonly Button _refresh;
    private readonly TabControl _views = new() { Location = new(28, 186), Size = new(844, 391), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
    private readonly RichTextBox _text = new() { Dock = DockStyle.Fill, ReadOnly = true, DetectUrls = false, WordWrap = true, HideSelection = false, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Ui.Ink, Font = new Font("Consolas", 11), ScrollBars = RichTextBoxScrollBars.Vertical };
    private readonly CheckBox _pureText = new() { Text = "Pure text", Appearance = Appearance.Button, Checked = true, FlatStyle = FlatStyle.Flat, TextAlign = ContentAlignment.MiddleCenter, Size = new(126, 34), Cursor = Cursors.Hand, BackColor = Color.White, ForeColor = Color.White };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoGenerateColumns = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AllowUserToResizeRows = false };
    private readonly Label _count = Ui.Label("", 28, 592, 844, 30);
    private readonly Label _unlockStatus = Ui.Label("", 28, 76, 690, 26);
    private IReadOnlyList<KeystrokeEntry> _entries = [];
    private readonly System.Windows.Forms.Timer _lockTimer = new() { Interval = 1000 };
    private int _loadVersion;
    private bool _populatingDates;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private int _nextSearchStart;
    private int _sortColumn;
    private bool _sortAscending;
    private bool IsTextView => _views.SelectedIndex == 0;

    public HistoryForm(HistoryUnlockSession unlockSession, string? directory = null)
    {
        _unlockSession = unlockSession;
        _directory = directory ?? AppSettings.VaultDirectory;
        Ui.Style(this, "TypeBlocks · History", new(900, 638));
        MinimumSize = new(830, 580);
        KeyPreview = true;
        var textPage = new TabPage("Text view") { BackColor = Color.White, Padding = new Padding(12) };
        var tablePage = new TabPage("Event table") { BackColor = Color.White };
        var textToolbar = new Panel { Dock = DockStyle.Top, Height = 46 };
        _pureText.FlatAppearance.CheckedBackColor = Ui.Accent;
        _pureText.FlatAppearance.BorderColor = Ui.Accent;
        _pureText.CheckedChanged += (_, _) =>
        {
            _pureText.ForeColor = _pureText.Checked ? Color.White : Ui.Ink;
            _pureText.FlatAppearance.BorderColor = _pureText.Checked ? Ui.Accent : Color.FromArgb(211, 219, 227);
            RenderTextView();
        };
        textToolbar.Controls.Add(_pureText);
        textToolbar.Controls.Add(Ui.Label("Applies recorded edits · best effort", 140, 5, 440, 26));
        textPage.Controls.Add(_text);
        textPage.Controls.Add(textToolbar);
        tablePage.Controls.Add(_grid);
        _views.TabPages.AddRange([textPage, tablePage]);
        _views.SelectedIndex = 0;
        Controls.AddRange([
            Ui.Label("Your history", 28, 25, 580, 42, 24, true),
            _unlockStatus,
            _dates, _search, _views, _count
        ]);
        var lockButton = Ui.Button("Lock && close", 722, 32, 150);
        lockButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        lockButton.Click += (_, _) => _unlockSession.Lock();
        _refresh = Ui.Button("Refresh", 722, 128, 150);
        _refresh.Click += async (_, _) => { PopulateDates(_dates.SelectedItem as DateOnly?); await LoadDay(); };
        _findNext.Enabled = false;
        _findNext.Click += (_, _) => FindText(restart: false);
        Controls.AddRange([lockButton, _refresh, _findNext]);
        _count.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Time", DataPropertyName = "Time", Width = 115 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Application", DataPropertyName = "Application", Width = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Key", DataPropertyName = "Key", Width = 210 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Text (best effort)", DataPropertyName = "Text", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        foreach (DataGridViewColumn column in _grid.Columns) column.SortMode = DataGridViewColumnSortMode.Programmatic;
        _grid.ColumnHeaderMouseClick += (_, e) => { if (e.ColumnIndex >= 0) SortByColumn(e.ColumnIndex); };
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(229, 235, 242);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Ui.Ink;
        _grid.ColumnHeadersHeight = 38;
        _grid.RowTemplate.Height = 30;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(247, 249, 252);
        _grid.DefaultCellStyle.SelectionBackColor = Ui.Accent;
        _dates.SelectedIndexChanged += async (_, _) => { if (!_populatingDates) await LoadDay(); };
        _search.TextChanged += (_, _) => UpdateSelectedView();
        _views.SelectedIndexChanged += (_, _) => UpdateSelectedView();
        _search.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter && IsTextView) { e.SuppressKeyPress = true; FindText(restart: false); } };
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.F) { e.SuppressKeyPress = true; _search.Focus(); _search.SelectAll(); } };
        _unlockSession.Locked += CloseForLock;
        _lockTimer.Tick += (_, _) => UpdateUnlockStatus();
        ClientSizeChanged += (_, _) => LayoutToolbar();
        Shown += async (_, _) => { UpdateUnlockStatus(); if (IsDisposed) return; LayoutToolbar(); PopulateDates(); _lockTimer.Start(); await LoadDay(); };
        FormClosed += (_, _) => { _loadVersion++; _lockTimer.Stop(); _unlockSession.Locked -= CloseForLock; ClearHistory(); _search.Clear(); };
        LayoutToolbar();
    }

    private void CloseForLock() => Close();

    private void UpdateUnlockStatus()
    {
        TimeSpan remaining = _unlockSession.Remaining;
        if (remaining <= TimeSpan.Zero) { if (!IsDisposed) Close(); return; }
        if (IsDisposed) return;
        int seconds = (int)Math.Ceiling(remaining.TotalSeconds);
        _unlockStatus.Text = $"Local and encrypted · auto-lock in {seconds / 60:00}:{seconds % 60:00}";
    }

    private void LayoutToolbar()
    {
        if (_refresh is null) return;
        _refresh.SetBounds(ClientSize.Width - 178, 128, 150, 38);
        _findNext.SetBounds(_refresh.Left - 142, 128, 124, 38);
        _search.SetBounds(210, 136, Math.Max(100, (IsTextView ? _findNext.Left : _refresh.Left) - 226), _search.Height);
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
            else { _loadVersion++; ClearHistory(); _count.Text = "No saved keystrokes yet. History is written in small batches."; }
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
            if (IsTextView && _search.Text.Length == 0) { _text.Select(_text.TextLength, 0); _text.ScrollToCaret(); }
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
        _findNext.Enabled = false;
    }

    private void RenderTextView()
    {
        _text.Text = HistoryTextFormatter.Format(_entries, pureText: _pureText.Checked);
        UpdateSelectedView();
    }

    private void UpdateSelectedView()
    {
        _search.PlaceholderText = IsTextView ? "Find a word or phrase in this day" : "Filter app, key, or text on this day";
        _findNext.Visible = IsTextView;
        LayoutToolbar();
        if (IsTextView) FindText(restart: true);
        else ApplyTableFilter();
    }

    private void FindText(bool restart)
    {
        if (!IsTextView) return;
        string query = _search.Text;
        _findNext.Enabled = query.Length > 0 && _text.TextLength > 0;
        if (restart) _nextSearchStart = 0;
        if (query.Length == 0)
        {
            _text.Select(0, 0);
            string mode = _pureText.Checked ? "Pure text: best-effort edits" : "[brackets] show special keys";
            _count.Text = $"{_entries.Count:N0} recorded key events · {mode} · Ctrl+F to find text";
            return;
        }
        int index = _text.Find(query, Math.Min(_nextSearchStart, _text.TextLength), RichTextBoxFinds.None);
        if (index < 0 && _nextSearchStart > 0) index = _text.Find(query, 0, RichTextBoxFinds.None);
        if (index >= 0)
        {
            _text.Select(index, query.Length);
            _text.ScrollToCaret();
            _nextSearchStart = index + query.Length;
            _count.Text = $"Match highlighted · Find next or Enter for another · {_entries.Count:N0} recorded key events";
        }
        else
        {
            _nextSearchStart = 0;
            _text.Select(0, 0);
            _count.Text = $"No text matches · {_entries.Count:N0} recorded key events";
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
        _count.Text = matching.Count > 25000 ? $"Showing newest 25,000 of {matching.Count:N0} matching events. Narrow your filter to see more." : $"{matching.Count:N0} events · {_entries.Count:N0} saved on this day";
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
        if (!IsTextView || !_pureText.Checked || !_pureText.Visible || !_text.Text.Contains("Hello", StringComparison.Ordinal)) throw new Exception("The default pure text view did not join typed characters.");
        if (_search.Right >= _findNext.Left || _findNext.Right >= _refresh.Left) throw new Exception("History toolbar controls overlap.");
        if (_pureText.Parent!.Bottom > _text.Top || !_pureText.Parent.ClientRectangle.Contains(_pureText.Bounds)) throw new Exception("The pure text toggle overlaps the transcript or is clipped.");
        foreach (string marker in new[] { "[Backspace]", "[Left]", "[Ctrl+V]", "[Ctrl+S]" })
            if (_text.Text.Contains(marker, StringComparison.Ordinal)) throw new Exception("Pure text displayed a special-key marker.");
        _search.Text = "Hello";
        if (_text.SelectedText != "Hello") throw new Exception("Text search did not find a phrase across key events.");
        _search.Text = "12345";
        if (_text.SelectedText != "12345") throw new Exception("Pure text search did not find digits corrected with Backspace.");
        if (!_text.Text.Contains("Word: cat", StringComparison.Ordinal)) throw new Exception("Pure text did not insert text at the recorded caret.");
        _pureText.Checked = false;
        if (_text.SelectedText.Length != 0 || !_count.Text.StartsWith("No text matches", StringComparison.Ordinal)) throw new Exception("Disabling pure text did not update the active search.");
        if (!_text.Text.Contains("12999[Backspace][Backspace][Backspace]345", StringComparison.Ordinal)) throw new Exception("Disabling pure text did not restore the original keystrokes and special-key markers.");
        _search.Text = "[Ctrl+V]";
        if (_text.SelectedText != "[Ctrl+V]") throw new Exception("Special-key markers could not be searched with pure text disabled.");
        await LoadDay();
        if (_pureText.Checked || _text.SelectedText != "[Ctrl+V]") throw new Exception("Reloading history lost the pure text toggle state or search.");
        _pureText.Checked = true;
        if (_text.SelectedText.Length != 0 || !_count.Text.StartsWith("No text matches", StringComparison.Ordinal)) throw new Exception("Enabling pure text did not clear a hidden marker search result.");
        _pureText.Checked = false;
        _views.SelectedIndex = 1;
        _search.Clear();
        if (_pureText.Visible) throw new Exception("The pure text toggle is visible outside the text tab.");
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
        _views.SelectedIndex = 0;
        if (_pureText.Checked || !_text.Text.Contains("[Ctrl+V]", StringComparison.Ordinal)) throw new Exception("Switching tabs lost the pure text toggle state.");
        _pureText.Checked = true;
        if (!_count.Text.Contains("best-effort edits", StringComparison.Ordinal)) throw new Exception("The text view status does not describe pure text mode.");
    }

    internal void SelectTableForPreview() => _views.SelectedIndex = 1;

    internal void CheckUnlockForTest() => UpdateUnlockStatus();
    internal bool IsHistoryClearedForTest => _entries.Count == 0 && _text.Text.Length == 0 && _grid.DataSource is null;

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _unlockSession.Locked -= CloseForLock; _lockTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
