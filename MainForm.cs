using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TodayChecklist;

sealed class MainForm : ThemedForm
{
    readonly Store _store;
    Settings _settings;

    // Control tạo trong BuildUi, tạo lại mỗi lần đổi cài đặt
    TodoTree _tree = null!;
    TextBox _editor = null!, _add = null!;
    Label _title = null!, _subtitle = null!, _empty = null!;
    ProgressLine _progress = null!;
    ScrollStrip _scroll = null!;
    CalendarView _calendar = null!;
    ToolStripDropDown _calendarDrop = null!;
    ThemeTip _tip = null!;
    FlatButton _todayBtn = null!, _pinBtn = null!, _calendarBtn = null!, _syncBtn = null!, _carryBtn = null!, _hideBtn = null!;

    static string SyncText => T("Status GitHub", "GitHub status");
    static string SyncingText => T("Đang cập nhật…", "Updating…");
    static string CarryText => T("Lấy việc chưa xong", "Bring over unfinished");
    const int SyncGlyph = 0xE72C, DoneGlyph = 0xE73E, WarnGlyph = 0xE7BA, CarryGlyph = 0xE81C;
    readonly System.Windows.Forms.Timer _clock = new() { Interval = 60_000 };
    DateTime _today = DateTime.Today; // "hôm nay" ở lần kiểm tra trước, để biết lúc qua ngày mới
    DateTime _lastFetch;              // lần lấy status gần nhất (tự động hoặc bấm tay)
    DateTime _day = DateTime.Today;
    string _dateText = "";
    TreeNode? _editing; // node đang sửa tên bằng _editor
    bool _busy;         // code đang tự đóng/mở node → không ghi trạng thái expand vào DB
    bool _fetching;     // đang lấy status GitHub
    Dictionary<long, int> _groupColors = new(); // nhóm task liên kết → chỉ số màu
    bool _hideDone;     // đang ẩn việc đã xong khỏi list
    int _hiddenCount;   // số việc đã xong đang bị ẩn trong ngày
    readonly System.Windows.Forms.Timer _hideTimer = new() { Interval = 1200 };
    readonly NotifyIcon _trayIcon = new();
    bool _hotkeyOn;     // phím gọi app đã đăng ký được với Windows
    ModifierChord? _notesChord;   // Ctrl+Win+Alt → ghi chú chung
    bool _notesKeyOn;             // đã đăng ký Ctrl+Win+Alt+F23 (đường đi của Ctrl+Win+Alt) với Windows
    NotepadDialog? _notes;        // ghi chú chung đang mở (cửa sổ riêng)

    public MainForm(Store store)
    {
        _store = store;
        _settings = Settings.Load(store);
        _settings.Apply();
        _hideDone = store.Setting("hide_done") == "on";
        Icon = LoadIcon();
        _trayIcon.Icon = Icon;
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowFromTray();
        };
        _hideTimer.Tick += (_, _) => HideDoneNow();
        StartPosition = FormStartPosition.CenterScreen;
        BuildUi();
        ClientSize = S(new Size(420, 640));
        ActiveControl = _add;
        LoadDay();
        SystemEvents.UserPreferenceChanged += SystemThemeChanged;
        _clock.Tick += (_, _) => OnClock();
        _clock.Start();
        ApplyNotesChord();
        Shown += (_, _) => StartOfDay();
    }

    /// <summary>Lần mở app sau (cùng DB) gọi tới: hiện lại cửa sổ, kể cả đang thu nhỏ, và đưa lên trước. Gọi từ luồng khác.</summary>
    public void BringUp()
    {
        try
        {
            BeginInvoke(ShowFromTray);
        }
        catch (InvalidOperationException)
        {
            // cửa sổ đang đóng / chưa có handle → bỏ qua
        }
    }

    /// <summary>Lệnh deploy bản mới báo đóng app (kể cả khi đang ở khay, không có cửa sổ để gửi lệnh đóng). Gọi từ luồng khác.</summary>
    public void CloseFromOutside()
    {
        try
        {
            BeginInvoke(Close);
        }
        catch (InvalidOperationException)
        {
            // đang đóng rồi
        }
    }

    // Đóng app (kể cả khi deploy bản mới tự đóng) giữa lúc đang sửa tên → vẫn lưu
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        EndEdit(true);
        _notes?.Close(); // cửa sổ ghi chú chung không thuộc cửa sổ này nên phải tự đóng (để lưu)
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= SystemThemeChanged;
            _clock.Dispose();
            _hideTimer.Dispose();
            _notesChord?.Dispose();
            _trayIcon.Visible = false;
            _trayIcon.ContextMenuStrip?.Dispose();
            _trayIcon.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>Dựng (lại) toàn bộ control theo theme / ngôn ngữ / kích thước hiện tại.</summary>
    void BuildUi()
    {
        SuspendLayout();
        _tip?.Dispose();
        _calendarDrop?.Dispose();
        _tree?.ContextMenuStrip?.Dispose();
        var old = Controls.Cast<Control>().ToArray();
        Controls.Clear();
        foreach (var c in old) c.Dispose();

        ApplyTheme();
        MinimumSize = S(new Size(360, 380));
        _tip = new ThemeTip();

        // --- Header: tiêu đề ngày, nút điều hướng, thanh tiến độ ---
        _title = new PlainLabel { Dock = DockStyle.Top, AutoSize = false, ForeColor = Theme.Text, Font = Theme.Heading(21f), Height = S(42) };
        _subtitle = new PlainLabel { AutoSize = true, ForeColor = Theme.Muted, Font = Theme.Ui(9.5f), Margin = new Padding(0, S(4), S(6), 0) };
        // ngay sau dòng ngày: nút mở ghi chú chung (không theo ngày, chỉ người dùng sửa)
        var notes = TextButton(T("Ghi chú", "Notes"),
            T("Ghi chú chung: thông tin cần giữ lại, không đổi theo ngày (Ctrl+J, hoặc Ctrl+Win+Alt từ bất kỳ đâu)",
              "Notes: things worth keeping, not tied to a day (Ctrl+J, or Ctrl+Win+Alt from anywhere)"), ShowNotepad);
        notes.Glyph = 0xE70B;
        notes.Font = Theme.Ui(9f);
        notes.MinimumSize = new Size(0, S(26));
        notes.Padding = new Padding(S(4), 0, S(6), 0);
        notes.Margin = Padding.Empty;
        var dateRow = new FlowLayoutPanel { Dock = DockStyle.Top, Height = S(26), WrapContents = false };
        dateRow.Controls.AddRange(new Control[] { _subtitle, notes });
        _progress = new ProgressLine { Dock = DockStyle.Bottom, Height = S(18) };

        _todayBtn = TextButton(T("Hôm nay", "Today"), T("Về hôm nay (Ctrl+Home)", "Back to today (Ctrl+Home)"), () => GoTo(DateTime.Today));
        _todayBtn.ForeColor = Theme.Accent;
        _pinBtn = IconButton(Theme.Glyph(0xE718), T("Ghim: luôn nằm trên các cửa sổ khác", "Pin: keep on top of other windows"), TogglePin);
        UpdatePin();
        _calendarBtn = IconButton(Theme.Glyph(0xE787), T("Chọn ngày", "Pick a date"), ShowCalendar);
        var nav = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false, Padding = new Padding(0, S(4), 0, 0) };
        nav.Controls.AddRange(new Control[]
        {
            _todayBtn,
            IconButton(Theme.Glyph(0xE76B), T("Ngày trước (Ctrl+PgUp)", "Previous day (Ctrl+PgUp)"), () => GoTo(_day.AddDays(-1))),
            _calendarBtn,
            IconButton(Theme.Glyph(0xE76C), T("Ngày sau (Ctrl+PgDn)", "Next day (Ctrl+PgDn)"), () => GoTo(_day.AddDays(1))),
            _pinBtn,
        });
        var header = new Panel { Dock = DockStyle.Top, Height = S(106), Padding = new Padding(S(20), S(14), S(16), S(4)) };
        // Dock xếp từ control thêm sau cùng: thanh tiến độ dưới đáy, nav bên phải, rồi tiêu đề
        header.Controls.AddRange(new Control[] { dateRow, _title, nav, _progress });

        _calendar = new CalendarView();
        _calendarDrop = new ToolStripDropDown { Padding = Padding.Empty, Renderer = new ToolStripProfessionalRenderer(new MenuColors()) };
        _calendarDrop.Items.Add(new ToolStripControlHost(_calendar) { Margin = Padding.Empty, Padding = Padding.Empty });
        _calendar.DatePicked += d => { _calendarDrop.Close(); GoTo(d); };
        _calendar.MarkedDays = _store.OpenDays;

        // --- Ô thêm note nhanh ---
        _add = new TextBox { BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Text, PlaceholderText = T("Thêm note mới…", "Add a new note…") };
        var addField = new InputField(_add);
        addField.Click += (_, _) => _add.Focus();
        // cuối dòng thêm việc: tìm kiếm (ngoài cùng) và ẩn / hiện việc đã xong
        var search = IconButton(Theme.Glyph(0xE721), T("Tìm ở mọi ngày (Ctrl+F)", "Search every day (Ctrl+F)"), ShowSearch);
        _hideBtn = IconButton(Theme.Glyph(0xE71C), "", ToggleHideDone);
        search.Size = _hideBtn.Size = S(new Size(30, 30));
        search.Margin = _hideBtn.Margin = Padding.Empty;
        addField.AddButton(search);
        addField.AddButton(_hideBtn);
        _add.KeyDown += Add_KeyDown;
        var addBar = new Panel { Dock = DockStyle.Top, Height = S(54), Padding = new Padding(S(20), S(6), S(20), S(12)) };
        addBar.Controls.Add(addField);

        // --- Danh sách ---
        _tree = new TodoTree { Font = Theme.Ui(10.5f), ItemHeight = S(36) };
        _editor = new TextBox { BorderStyle = BorderStyle.None, Visible = false };
        _empty = new Label
        {
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.Muted, BackColor = Theme.Bg, Visible = false,
            Text = T("Chưa có việc nào.\nGõ vào ô phía trên rồi Enter để thêm.", "Nothing here yet.\nType in the box above and press Enter."),
        };
        _tree.Controls.Add(_editor);
        _tree.Controls.Add(_empty);
        _tree.CheckClicked += ToggleDone;
        _tree.AddChildClicked += n => AddAt(n, n.Nodes.Count);
        _tree.DeleteClicked += Delete;
        _tree.EditRequested += BeginEdit;
        _tree.LinkOf = ResolveLink;
        _tree.StatusOf = StatusOf;
        _tree.TitleOf = TitleOf;
        _tree.ChecklistOf = ChecklistOf;
        _tree.GroupOf = GroupOf;
        _tree.LinkClicked += OpenLink;
        _tree.ChecklistClicked += OpenChecklist;
        _tree.GroupLinkClicked += OpenGroupLinks;
        _tree.DetailsClicked += ShowDetails;
        _tree.LinkEditClicked += EditLink;
        _tree.NodeDropped += Relocate;
        _tree.ContextMenuStrip = BuildMenu();
        _tree.AfterExpand += (_, e) => SaveExpanded(e.Node, true);
        _tree.AfterCollapse += (_, e) => SaveExpanded(e.Node, false);
        _tree.KeyDown += Tree_KeyDown;
        _tree.MouseWheel += (_, _) => EndEdit(true);
        _editor.KeyDown += Editor_KeyDown;
        _editor.LostFocus += (_, _) => EndEdit(true);
        _empty.Click += (_, _) => _add.Focus();
        // Thanh cuộn gốc (nền xám) bị đẩy ra ngoài vùng clip; ScrollStrip mảnh tự vẽ thay thế
        var clip = new Panel { Dock = DockStyle.Fill };
        clip.Controls.Add(_tree);
        _scroll = new ScrollStrip { Dock = DockStyle.Right, Width = S(10) };
        var list = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(10), 0, S(2), 0) };
        list.Controls.AddRange(new Control[] { clip, _scroll });
        _tree.ScrollChanged += () => SyncScroll(clip);
        clip.Resize += (_, _) => SyncScroll(clip);
        _scroll.Scrolled += _tree.ScrollToRow;
        _scroll.MouseWheel += (_, e) => _tree.ScrollToRow(Math.Max(0, _scroll.Position - Math.Sign(e.Delta) * 3));

        // --- Footer ---
        var footer = new Panel { Dock = DockStyle.Bottom, Height = S(42), Padding = new Padding(S(10), S(5), S(10), S(5)) };
        footer.Paint += (_, e) => { using var pen = new Pen(Theme.Line); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        _carryBtn = TextButton(CarryText,
            T("Chép các việc chưa xong của ngày gần nhất sang ngày này", "Copy unfinished tasks from the most recent day to this day"), CarryOver);
        _carryBtn.Glyph = CarryGlyph;
        _carryBtn.Dock = DockStyle.Left;
        _syncBtn = TextButton(_fetching ? SyncingText : SyncText,
            T("Lấy Status mới nhất trong GitHub Project (Todo / In progress / Done…) và coding checklist của các task GitHub trong ngày (F5)",
              "Fetch the latest GitHub Project status (Todo / In progress / Done…) and coding checklist of this day's GitHub tasks (F5)"), () => FetchStatuses());
        _syncBtn.Glyph = SyncGlyph;
        _syncBtn.Enabled = !_fetching;
        var completed = IconButton(Theme.Glyph(0xE9D5), T("Việc đã xong: ngày nào, giờ nào (Ctrl+H)", "Completed tasks: which day, what time (Ctrl+H)"), ShowCompleted);
        var help = IconButton(Theme.Glyph(0xE897), T("Phím tắt (F1)", "Keyboard shortcuts (F1)"), ShowHelp);
        var settings = IconButton(Theme.Glyph(0xE713), T("Cài đặt: màu sắc, nút, tự động, ngôn ngữ (Ctrl+,)", "Settings: colors, buttons, automation, language (Ctrl+,)"), ShowSettings);
        completed.Size = help.Size = settings.Size = S(new Size(32, 32));
        _syncBtn.Margin = completed.Margin = help.Margin = settings.Margin = Padding.Empty;
        var right = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, WrapContents = false };
        right.Controls.AddRange(new Control[] { _syncBtn, completed, help, settings });
        footer.Controls.AddRange(new Control[] { _carryBtn, right });

        // icon khay hệ thống: menu theo theme / ngôn ngữ hiện tại
        _trayIcon.ContextMenuStrip?.Dispose();
        _trayIcon.ContextMenuStrip = BuildTrayMenu();
        _trayIcon.Text = _settings.Hotkey < Settings.Hotkeys.Length
            ? $"{T("Hôm nay làm gì?", "What's on today?")} ({Settings.Hotkeys[_settings.Hotkey]})"
            : T("Hôm nay làm gì?", "What's on today?");
        _trayIcon.Visible = _settings.Tray;

        Controls.AddRange(new Control[] { list, footer, addBar, header });
        ResumeLayout();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool typing = _editor.Focused || _add.Focused;
        switch (keyData)
        {
            case Keys.Control | Keys.N: _add.Focus(); return true;
            case Keys.Control | Keys.Oemcomma: ShowSettings(); return true;
            case Keys.F1: ShowHelp(); return true;
            case Keys.Control | Keys.H: ShowCompleted(); return true;
            case Keys.Control | Keys.Shift | Keys.H: ToggleHideDone(); return true;
            case Keys.Control | Keys.F: ShowSearch(); return true;
            case Keys.Control | Keys.J: ShowNotepad(); return true;
            case Keys.F5: FetchStatuses(); return true;
            case Keys.Control | Keys.PageUp: GoTo(_day.AddDays(-1)); return true;
            case Keys.Control | Keys.PageDown: GoTo(_day.AddDays(1)); return true;
            case Keys.Control | Keys.Home when !typing: GoTo(DateTime.Today); return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void Tree_KeyDown(object? sender, KeyEventArgs e)
    {
        var sel = _tree.SelectedNode;
        switch (e.KeyData)
        {
            case Keys.Enter when sel == null: _add.Focus(); break;
            case Keys.Enter: AddAt(sel!.Parent, sel.Index + 1); break;
            case Keys.Control | Keys.Enter:
            case Keys.Insert: if (sel != null) AddAt(sel, sel.Nodes.Count); break;
            case Keys.F2: if (sel != null) BeginEdit(sel); break;
            case Keys.Control | Keys.L: if (sel != null) EditLink(sel); break;
            case Keys.Control | Keys.G: if (sel != null) LinkTasks(sel); break;
            case Keys.Control | Keys.E: if (sel != null) ShowDetails(sel); break;
            case Keys.Control | Keys.O: if (sel != null) OpenLink(sel); break;
            case Keys.Control | Keys.Shift | Keys.O: if (sel != null) OpenGroupLinks(sel); break;
            case Keys.Space: if (sel != null) ToggleDone(sel); break;
            case Keys.Delete: if (sel != null) Delete(sel); break;
            case Keys.Alt | Keys.Up: MoveBy(-1); break;
            case Keys.Alt | Keys.Down: MoveBy(1); break;
            case Keys.Alt | Keys.Right: Indent(); break;
            case Keys.Alt | Keys.Left: Outdent(); break;
            default: return;
        }
        e.Handled = e.SuppressKeyPress = true;
    }

    void GoTo(DateTime day)
    {
        EndEdit(true);
        _day = day.Date;
        LoadDay();
    }

    void LoadDay()
    {
        var kids = _store.Load(_day).ToLookup(i => i.ParentId);
        _groupColors = _store.GroupColors();
        _hiddenCount = 0;
        Quiet(() =>
        {
            _tree.BeginUpdate();
            _tree.Nodes.Clear();
            Build(_tree.Nodes, null, null);
            _tree.EndUpdate();
        });
        UpdateHideButton();

        int offset = (_day - DateTime.Today).Days;
        string? relative = offset switch { 0 => T("Hôm nay", "Today"), -1 => T("Hôm qua", "Yesterday"), 1 => T("Ngày mai", "Tomorrow"), _ => null };
        var culture = Lang.Culture;
        _title.Text = relative ?? culture.TextInfo.ToTitleCase(_day.ToString("dddd", culture));
        _dateText = _day.ToString(relative != null ? T("dddd, dd/MM/yyyy", "dddd, d MMMM yyyy") : T("dd/MM/yyyy", "d MMMM yyyy"), culture);
        Text = offset == 0 ? T("Hôm nay làm gì?", "What's on today?") : $"{_title.Text} · {_day.ToString(T("dd/MM", "d MMM"), culture)}";
        _todayBtn.Visible = offset != 0;
        UpdateProgress();

        void Build(TreeNodeCollection into, long? parentId, Row? parent)
        {
            foreach (var it in kids[parentId])
            {
                // đang ẩn việc đã xong: bỏ cả nhánh (cha xong thì con cũng xong hết), nhưng vẫn đếm cho bộ đếm của cha
                if (_hideDone && it.Done)
                {
                    _hiddenCount += Size(it.Id);
                    if (parent != null) parent.HiddenDone++;
                    continue;
                }
                var node = new TreeNode(it.Text) { Tag = new Row(it.Id, it.Done, it.Url) { Status = it.Status, StatusUrl = it.StatusUrl, Title = it.Title, Checklist = it.Checklist, GroupId = it.GroupId, HasNote = it.Note != null, Files = it.Files } };
                into.Add(node);
                Build(node.Nodes, it.Id, (Row)node.Tag);
                if (it.Expanded) node.Expand();
            }
        }

        int Size(long id) => 1 + kids[id].Sum(k => Size(k.Id));
    }

    // --- Cài đặt ---

    void ShowSettings()
    {
        EndEdit(true);
        bool inTree = _tree.Focused;
        using (var dialog = new SettingsDialog(_settings, ApplySettings))
            dialog.ShowDialog(this);
        (inTree ? (Control)_tree : _add).Focus();
    }

    void ApplySettings(Settings settings)
    {
        EndEdit(true);
        float oldScale = Theme.Scale;
        var oldSize = Size;
        long? selected = (_tree.SelectedNode?.Tag as Row)?.Id;
        string draft = _add.Text;

        bool hotkeyChanged = settings.Hotkey != _settings.Hotkey;
        _settings = settings;
        settings.Save(_store);
        settings.Apply();
        ApplyNotesChord();
        if (!RegisterHotkey() && hotkeyChanged)
            MessageDialog.Show(this, T($"Phím {Settings.Hotkeys[settings.Hotkey]} đang bị Windows hoặc app khác dùng. Chọn phím khác nhé.",
                $"{Settings.Hotkeys[settings.Hotkey]} is already taken by Windows or another app. Pick another one."), Text, warning: true);
        BuildUi();
        _add.Text = draft;
        if (Theme.Scale != oldScale) Rescale(oldSize, Theme.Scale / oldScale);
        LoadDay();
        _tree.SelectedNode = Flatten(_tree.Nodes).FirstOrDefault(n => n.Tag is Row row && row.Id == selected);
    }

    // Đổi kích thước → cửa sổ to / nhỏ theo cùng tỉ lệ, vẫn nằm gọn trong màn hình
    void Rescale(Size oldSize, float factor)
    {
        if (WindowState != FormWindowState.Normal) return;
        var area = Screen.FromControl(this).WorkingArea;
        int w = Math.Min(area.Width, (int)(oldSize.Width * factor)), h = Math.Min(area.Height, (int)(oldSize.Height * factor));
        Bounds = new Rectangle(Math.Clamp(Left, area.Left, area.Right - w), Math.Clamp(Top, area.Top, area.Bottom - h), w, h);
    }

    // Đang để "Theo Windows" mà người dùng đổi sáng / tối trong Windows → đổi theo luôn
    void SystemThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _settings.Mode == ThemeMode.System && IsHandleCreated)
            BeginInvoke(() =>
            {
                if (Theme.WindowsDark() != Theme.Dark) ApplySettings(_settings);
            });
    }

    // --- Thêm / sửa ---

    void Add_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter: AddQuick(); break;
            case Keys.Escape: _add.Clear(); _tree.Focus(); break;
            case Keys.Down: _tree.Focus(); break;
            default: return;
        }
        e.Handled = e.SuppressKeyPress = true;
    }

    void AddQuick()
    {
        if (_add.Text.Trim().Length == 0) return;
        var (text, url) = Links.FromInput(_add.Text);
        var node = new TreeNode(text) { Tag = new Row(_store.Add(_day, null, text, url: url), false, url) };
        _tree.Nodes.Add(node);
        _tree.SelectedNode = node;
        _add.Clear();
        UpdateProgress();
        FetchNow(node);
    }

    // Node mới chưa có Tag (chưa vào DB); gõ tên xong mới lưu, bỏ trống thì hủy
    void AddAt(TreeNode? parent, int index)
    {
        EndEdit(true);
        var list = parent?.Nodes ?? _tree.Nodes;
        var node = new TreeNode();
        list.Insert(Math.Min(index, list.Count), node);
        parent?.Expand();
        UpdateProgress();
        BeginEdit(node);
    }

    void BeginEdit(TreeNode node)
    {
        EndEdit(true);
        if (node.TreeView == null) return;
        _editing = node;
        _tree.SelectedNode = node;
        _tree.EditingNode = node;

        var r = _tree.TextRect(node);
        _editor.Font = _tree.FontFor(node);
        _editor.BackColor = Theme.Selected;
        _editor.ForeColor = Theme.Text;
        _editor.PlaceholderText = node.Level == 0 ? T("Tên note…", "Note name…") : T("Tên việc…", "Task name…");
        _editor.Text = node.Text;
        _editor.SetBounds(r.X - S(1), r.Y + (r.Height - _editor.Height) / 2 - S(1), r.Width, _editor.Height);
        _editor.Visible = true;
        _editor.Focus();
        _editor.SelectAll();
    }

    void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Enter: EndEdit(true, addNext: true); break; // việc mới: Enter → mở luôn dòng kế để gõ tiếp
            case Keys.Escape: EndEdit(false); break;
            default: return;
        }
        e.Handled = e.SuppressKeyPress = true;
    }

    void EndEdit(bool commit, bool addNext = false)
    {
        if (_editing is not { } node) return;
        _editing = null;
        _tree.EditingNode = null;
        var text = _editor.Text.Trim();
        if (_editor.Focused) _tree.Focus(); // trả focus về list trước khi ẩn ô sửa
        _editor.Visible = false;

        if (!commit || text.Length == 0)
        {
            if (node.Tag == null)
            {
                var back = node.PrevNode ?? node.Parent;
                node.Remove();
                if (back != null) _tree.SelectedNode = back;
                UpdateProgress();
            }
            return;
        }

        // Dán link issue / PR làm tên → tự tách thành "#39" + link
        var (label, url) = Links.FromInput(text);
        if (node.Tag is not Row row)
        {
            node.Tag = new Row(_store.Add(_day, IdOf(node.Parent), label, url: url), false, url);
            node.Text = label;
            SaveOrder(node.Parent);
            Commit(SyncUp(node.Parent)); // cha đang xong mà thêm con mới → cha thành chưa xong
            FetchNow(node);
            if (addNext) AddAt(node.Parent, node.Index + 1);
            return;
        }
        if (url != null)
        {
            row.Url = url;
            _store.SetUrl(row.Id, url);
        }
        if (label != node.Text)
        {
            _store.Rename(row.Id, label);
            node.Text = label;
        }
        _tree.Invalidate();
        if (url != null) FetchNow(node);
    }

    /// <summary>
    /// Việc vừa tạo / vừa gắn link issue / PR GitHub: lấy luôn status + tên issue (khi bật tự cập nhật),
    /// không đợi lần lấy định kỳ. Lỗi thì thôi, lần lấy sau hoặc F5 sẽ thử lại.
    /// </summary>
    async void FetchNow(TreeNode node)
    {
        if (!_settings.AutoStatus || node.Tag is not Row row || ResolveLink(node) is not { } url || Links.IssueOf(url) == null) return;
        try
        {
            var issue = await GitHub.FetchAsync(url);
            (row.Status, row.StatusUrl, row.Title, row.Checklist) = (issue.Status, url, issue.Title, issue.Checklist);
            _store.SetStatus([(row.Id, issue.Status, url, issue.Title, issue.Checklist)]);
            _tree.Invalidate();
        }
        catch (Exception)
        {
            // mất mạng, repo private chưa có quyền… → để lần lấy sau
        }
    }

    // --- Link ---

    /// <summary>Link gắn trực tiếp, hoặc "#18" dưới note có link GitHub → issue 18 của repo đó.</summary>
    string? ResolveLink(TreeNode node) =>
        node.Tag is Row { Url: { } url } ? url : Links.IssueLink(node.Text, ParentRepo(node));

    string? ParentRepo(TreeNode node)
    {
        for (var p = node.Parent; p != null; p = p.Parent)
            if (p.Tag is Row { Url: { } url } && Links.RepoBase(url) is { } repo) return repo;
        return null;
    }

    /// <summary>Status chỉ còn đúng nếu link hiện tại vẫn là link lúc lấy status.</summary>
    string? StatusOf(TreeNode node) =>
        node.Tag is Row { Status: { } status, StatusUrl: { } url } && url == ResolveLink(node) ? status : null;

    /// <summary>Link coding checklist của issue, cùng điều kiện với status (lấy cùng lúc).</summary>
    string? ChecklistOf(TreeNode node) =>
        StatusOf(node) != null && node.Tag is Row { Checklist: { } checklist } ? checklist : null;

    void OpenChecklist(TreeNode node)
    {
        if (ChecklistOf(node) is not { } url) return;
        try { Links.Open(url); }
        catch (Exception ex) { MessageDialog.Show(this, T($"Không mở được link:\n{url}\n\n{ex.Message}", $"Couldn't open the link:\n{url}\n\n{ex.Message}"), Text, warning: true); }
    }

    /// <summary>Tên issue chỉ hiện cho việc ghi mỗi số ("#18"), và chỉ khi status lấy cùng lúc còn đúng.</summary>
    string? TitleOf(TreeNode node) =>
        node.Tag is Row { Title: { Length: > 0 } title } && StatusOf(node) != null && Links.IsBareRef(node.Text) ? title : null;

    sealed record Fetched(TreeNode Node, string Url, GitHub.Issue? Issue, string? Error);

    /// <summary>
    /// Lấy status + tên issue của các task GitHub đang hiện.
    /// quiet: app tự chạy (lúc mở, mỗi 30 phút) → không hiện hộp thoại, có lỗi thì giữ cảnh báo trên nút tới khi bấm lại.
    /// </summary>
    async void FetchStatuses(bool quiet = false)
    {
        if (_fetching) return;
        _lastFetch = DateTime.Now;
        if (!quiet) EndEdit(true);
        var targets = Flatten(_tree.Nodes)
            .Where(n => n.Tag is Row)
            .Select(n => (Node: n, Url: ResolveLink(n)))
            .Where(t => Links.IssueOf(t.Url) != null)
            .ToList();
        if (targets.Count == 0)
        {
            if (!quiet) MessageDialog.Show(this, T("Ngày này chưa có task nào gắn link GitHub issue / PR.", "No task on this day is linked to a GitHub issue / PR."), Text);
            return;
        }

        _fetching = true;
        _syncBtn.Enabled = false;
        _syncBtn.Text = SyncingText;
        bool keepResult = false;
        try
        {
            using var gate = new SemaphoreSlim(6);
            var results = await Task.WhenAll(targets.Select(async t =>
            {
                await gate.WaitAsync();
                try { return new Fetched(t.Node, t.Url!, await GitHub.FetchAsync(t.Url!), null); }
                catch (Exception ex) { return new Fetched(t.Node, t.Url!, null, GitHub.Describe(ex)); }
                finally { gate.Release(); }
            }));

            var ok = results.Where(r => r.Issue != null).ToList();
            foreach (var r in ok)
            {
                var row = (Row)r.Node.Tag!;
                (row.Status, row.StatusUrl, row.Title, row.Checklist) = (r.Issue!.Status, r.Url, r.Issue.Title, r.Issue.Checklist);
            }
            _store.SetStatus(ok.Select(r => (Id(r.Node), r.Issue!.Status, r.Url, (string?)r.Issue.Title, r.Issue.Checklist)));
            // Trong lúc chờ mà list đã dựng lại (đổi cài đặt / đổi ngày) thì các node trên không còn hiện → nạp lại từ DB
            if (results.Any(r => r.Node.TreeView != _tree)) { if (_editing == null) LoadDay(); }
            else _tree.Invalidate();

            var failed = results.Where(r => r.Error != null).ToList();
            _syncBtn.Glyph = failed.Count == 0 ? DoneGlyph : WarnGlyph;
            _syncBtn.Text = failed.Count == 0
                ? T($"{ok.Count} task", $"{ok.Count} updated")
                : T($"{ok.Count}/{results.Length} task", $"{ok.Count}/{results.Length} updated");
            if (failed.Count > 0 && quiet) keepResult = true;
            else if (failed.Count > 0)
                MessageDialog.Show(this,
                    T("Không lấy được status:\n", "Couldn't get the status of:\n") +
                    string.Join("\n", failed.Take(8).Select(f => $"•  {f.Node.Text}: {f.Error}")) +
                    (failed.Count > 8 ? T($"\n…và {failed.Count - 8} task khác", $"\n…and {failed.Count - 8} more") : "") +
                    T("\n\nRepo private cần đăng nhập GitHub CLI (gh auth login) hoặc đặt biến môi trường GITHUB_TOKEN.",
                      "\n\nPrivate repos need a GitHub CLI sign-in (gh auth login) or a GITHUB_TOKEN environment variable."),
                    Text, warning: true);
        }
        catch (Exception ex)
        {
            _syncBtn.Text = SyncText;
            _syncBtn.Glyph = SyncGlyph;
            if (!quiet) MessageDialog.Show(this, T($"Cập nhật status bị lỗi: {ex.Message}", $"Updating status failed: {ex.Message}"), Text, warning: true);
        }
        finally
        {
            _fetching = false;
            _syncBtn.Enabled = true;
            // giữ kết quả trên nút vài giây rồi trả về chữ cũ
            if (!keepResult)
                RestoreLater(3000, () =>
                {
                    if (_fetching) return;
                    _syncBtn.Text = SyncText;
                    _syncBtn.Glyph = SyncGlyph;
                });
        }
    }

    static void RestoreLater(int ms, Action restore)
    {
        var timer = new System.Windows.Forms.Timer { Interval = ms };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            restore();
        };
        timer.Start();
    }

    // --- Tự động: đầu ngày, qua ngày mới, cập nhật status định kỳ ---

    /// <summary>Việc đầu ngày (tùy cài đặt): chép việc chưa xong của ngày làm việc trước sang hôm nay, rồi lấy status GitHub.</summary>
    void StartOfDay()
    {
        var today = DateTime.Today;
        string key = today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        // Mỗi ngày chỉ tự chép 1 lần: xóa bớt việc vừa chép rồi mở lại app thì không bị chép lại
        string? last = _store.Setting("auto_carry_day");
        if (_settings.AutoCarry && last != key)
        {
            _store.SetSettings(("auto_carry_day", key));
            // Lần đầu có tính năng này mà hôm nay đã có việc (đã tự chép tay) → không chép thêm, kẻo việc đã xóa hiện lại
            bool firstRunWithTasks = last == null && _store.Load(today).Count > 0;
            // chỉ lấy từ ngày gần nhất có việc: ngày đó xong hết thì thôi, không lục lại các ngày cũ hơn
            if (!firstRunWithTasks && _store.LastDayWithItems(today) is { } from && _store.CarryOver(from, today) is var added and > 0)
            {
                if (_day == today && _editing == null) LoadDay();
                _carryBtn.Glyph = DoneGlyph;
                _carryBtn.Text = T($"Đã chép {added} việc từ {from:dd/MM}", $"Copied {added} from {from.ToString("MMM d", Lang.Culture)}");
                RestoreLater(8000, () =>
                {
                    _carryBtn.Text = CarryText;
                    _carryBtn.Glyph = CarryGlyph;
                });
            }
        }
        if (_settings.AutoStatus) FetchStatuses(quiet: true);
    }

    // Mỗi phút: qua ngày mới thì chuyển sang hôm nay và làm việc đầu ngày; đủ 30 phút thì lấy lại status
    void OnClock()
    {
        if (DateTime.Today != _today)
        {
            if (_editing != null) return; // đang gõ dở → đợi lần sau
            bool onToday = _day == _today;
            _today = DateTime.Today;
            if (onToday) GoTo(_today);
            else LoadDay(); // nhãn "Hôm nay / Hôm qua" đổi theo ngày mới
            StartOfDay();
            return;
        }
        if (_settings.AutoStatus && DateTime.Now - _lastFetch >= TimeSpan.FromMinutes(30)) FetchStatuses(quiet: true);
    }

    void OpenLink(TreeNode node)
    {
        if (ResolveLink(node) is not { } url) return;
        try { Links.Open(url); }
        catch (Exception ex) { MessageDialog.Show(this, T($"Không mở được link:\n{url}\n\n{ex.Message}", $"Couldn't open the link:\n{url}\n\n{ex.Message}"), Text, warning: true); }
    }

    void EditLink(TreeNode node)
    {
        EndEdit(true);
        if (node.TreeView == null || node.Tag is not Row row) return;
        var suggested = row.Url ?? ClipboardUrl() ?? "";
        using var dialog = new LinkDialog(node.Text, suggested, ResolveLink(node) is { } inherited && row.Url == null ? inherited : null);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.Url != null && !Links.IsWebUrl(dialog.Url))
        {
            MessageDialog.Show(this, T("Link phải bắt đầu bằng http:// hoặc https://", "The link must start with http:// or https://"), Text, warning: true);
            return;
        }
        row.Url = dialog.Url;
        _store.SetUrl(row.Id, row.Url);
        _tree.Invalidate();
    }

    static string? ClipboardUrl()
    {
        try { return Clipboard.ContainsText() && Clipboard.GetText().Trim() is var t && Links.IsWebUrl(t) ? t : null; }
        catch { return null; } // clipboard đang bị app khác giữ
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false, Font = Theme.Ui(9.5f), ForeColor = Theme.Text, Renderer = new ToolStripProfessionalRenderer(new MenuColors()),
        };
        ToolStripMenuItem Item(string text, string keys, Action<TreeNode> action)
        {
            var item = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys, Padding = new Padding(0, S(3), 0, S(3)) };
            item.Click += (_, _) => { if (_tree.SelectedNode is { } n) action(n); };
            menu.Items.Add(item);
            return item;
        }

        var open = Item(T("Mở link", "Open link"), "Ctrl+O", OpenLink);
        var openGroup = Item(T("Mở link cả nhóm", "Open the group's links"), "Shift+Click", OpenGroupLinks);
        var link = Item(T("Gắn link…", "Add link…"), "Ctrl+L", EditLink);
        var unlink = Item(T("Bỏ link", "Remove link"), "", n =>
        {
            ((Row)n.Tag!).Url = null;
            _store.SetUrl(Id(n), null);
            _tree.Invalidate();
        });
        menu.Items.Add(new ToolStripSeparator());
        Item(T("Ghi chú & tệp đính kèm…", "Notes & attachments…"), "Ctrl+E", ShowDetails);
        Item(T("Liên kết với task khác…", "Link with other tasks…"), "Ctrl+G", LinkTasks);
        var ungroup = Item(T("Bỏ liên kết task", "Unlink task"), "", n => ApplyGroup(n, [], 0));
        menu.Items.Add(new ToolStripSeparator());
        Item(T("Sửa tên", "Rename"), "F2", BeginEdit);
        Item(T("Thêm việc con", "Add subtask"), "Ctrl+Enter", n => AddAt(n, n.Nodes.Count));
        Item(T("Xóa", "Delete"), "Delete", Delete);

        menu.Opening += (_, e) =>
        {
            if (_tree.SelectedNode is not { Tag: Row row } node)
            {
                e.Cancel = true;
                return;
            }
            open.Visible = ResolveLink(node) != null;
            link.Text = row.Url == null ? T("Gắn link…", "Add link…") : T("Sửa link…", "Edit link…");
            unlink.Visible = row.Url != null;
            ungroup.Visible = row.GroupId != null;
            openGroup.Visible = GroupOf(node) != null && GroupLinks(node).Count > 1;
        };
        return menu;
    }

    // Link của mọi task cùng nhóm liên kết với node trong ngày (theo thứ tự trong list), bỏ trùng
    List<string> GroupLinks(TreeNode node)
    {
        if (GroupOf(node) is not { } group) return ResolveLink(node) is { } url ? [url] : [];
        return Flatten(_tree.Nodes).Where(n => GroupOf(n)?.Id == group.Id).Select(ResolveLink).OfType<string>().Distinct().ToList();
    }

    /// <summary>Shift + bấm tên việc / Ctrl+Shift+O: mở link của task này và mọi task cùng nhóm liên kết.</summary>
    void OpenGroupLinks(TreeNode node)
    {
        var urls = GroupLinks(node);
        if (urls.Count > 10 && !MessageDialog.Confirm(this, T($"Mở {urls.Count} link cùng lúc?", $"Open {urls.Count} links at once?"), Text)) return;
        var failed = new List<string>();
        foreach (var url in urls)
        {
            try { Links.Open(url); }
            catch (Exception ex) { failed.Add($"{url}\n   {ex.Message}"); }
        }
        if (failed.Count > 0)
            MessageDialog.Show(this, T("Không mở được link:\n", "Couldn't open:\n") + string.Join("\n", failed), Text, warning: true);
    }

    // --- Ghi chú, tệp đính kèm, việc đã xong ---

    /// <summary>Ghi chú + tệp / thư mục đính kèm của 1 việc. Đóng hộp (Xong / Esc / X) là lưu.</summary>
    void ShowDetails(TreeNode node)
    {
        EndEdit(true);
        if (node.TreeView == null || node.Tag is not Row row) return;
        using var dialog = new DetailsDialog(node.Text, TitleOf(node), _store.Note(row.Id), _store.Attachments(row.Id));
        dialog.ShowDialog(this);
        _store.SetNote(row.Id, dialog.Note);
        _store.SetAttachments(row.Id, dialog.Paths);
        row.HasNote = dialog.Note.Length > 0;
        row.Files = dialog.Paths.Count;
        _tree.Invalidate();
    }

    /// <summary>
    /// Ghi chú chung (không theo ngày, chỉ người dùng sửa) mở thành cửa sổ riêng, không chặn cửa sổ việc;
    /// đang mở thì đưa lên trước. Đóng cửa sổ là lưu.
    /// </summary>
    void ShowNotepad()
    {
        if (_notes != null)
        {
            BringToFront(_notes);
            return;
        }
        EndEdit(true);
        var notes = new NotepadDialog(_store.Setting("notepad")) { Icon = Icon, TopMost = TopMost };
        if (Visible && WindowState == FormWindowState.Normal)
        {
            // cửa sổ việc đang mở: đặt giữa cửa sổ đó
            notes.StartPosition = FormStartPosition.Manual;
            notes.Location = new Point(Left + (Width - notes.Width) / 2, Math.Max(Screen.FromControl(this).WorkingArea.Top, Top + (Height - notes.Height) / 2));
        }
        else notes.StartPosition = FormStartPosition.CenterScreen;
        notes.FormClosed += (_, _) =>
        {
            _store.SetSettings(("notepad", notes.Note));
            _notes = null;
            notes.Dispose();
        };
        _notes = notes;
        notes.Show();
        BringToFront(notes);
    }

    // Ctrl+Win+Alt từ bất kỳ đâu: mở ghi chú chung mà không cần mở cửa sổ việc; đang mở trước mặt thì đóng (lưu)
    void ToggleNotes()
    {
        if (_notes is { } open && ActiveForm == open) open.Close();
        else ShowNotepad();
    }

    // Ctrl+Win+Alt: hook bắt tổ hợp rồi bấm giả F23. Ctrl+Win+Alt+F23 được đăng ký với Windows nên Windows gửi WM_HOTKEY,
    // và chỉ app nhận phím tắt mới được Windows cho đưa cửa sổ lên trước → ghi chú lần nào cũng hiện lên trên cùng.
    void ApplyNotesChord()
    {
        if (IsHandleCreated)
        {
            if (_notesKeyOn) UnregisterHotKey(Handle, NotesHotkeyId);
            _notesKeyOn = _settings.NotesHotkey && RegisterHotKey(Handle, NotesHotkeyId, MOD_CONTROL | MOD_ALT | MOD_WIN | MOD_NOREPEAT, VK_F23);
        }
        if (_settings.NotesHotkey && _notesChord == null)
        {
            _notesChord = new ModifierChord();
            _notesChord.Pressed += ToggleNotes; // khi không đăng ký được F23
        }
        else if (!_settings.NotesHotkey && _notesChord != null)
        {
            _notesChord.Dispose();
            _notesChord = null;
        }
        if (_notesChord != null) _notesChord.ForwardKey = _notesKeyOn ? VK_F23 : (byte)0;
    }

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr processId);

    [DllImport("kernel32.dll")]
    static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);

    [DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    // Đưa cửa sổ lên trước kể cả khi app khác đang giữ foreground (mở bằng Ctrl+Win+Alt lúc đang làm việc ở app khác)
    static void BringToFront(Form form)
    {
        uint fg = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero), me = GetCurrentThreadId();
        bool attach = fg != 0 && fg != me && AttachThreadInput(fg, me, true);
        SetForegroundWindow(form.Handle);
        form.Activate();
        if (attach) AttachThreadInput(fg, me, false);
    }

    /// <summary>Danh sách việc đã xong kèm ngày giờ; chọn 1 việc thì mở ngày đó và chọn sẵn việc.</summary>
    void ShowCompleted()
    {
        EndEdit(true);
        using var dialog = new DoneDialog(_store.Completed());
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Picked is { } row) Reveal(row.Day, row.Id);
    }

    /// <summary>Tìm việc ở mọi ngày (tên việc, số / tên issue, ghi chú); chọn 1 kết quả thì mở ngày đó.</summary>
    void ShowSearch()
    {
        EndEdit(true);
        using var dialog = new SearchDialog(query => _store.Search(query)
            .Select(h => new DayRow(h.Id, h.Day, h.Day, null, h.Text, SearchDetail(h), h.Parent, Dim: h.Done || h.Carried))
            .ToList());
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Picked is { } row) Reveal(row.Day, row.Id);
    }

    // Chữ xám sau tên kết quả: tên issue (việc chỉ ghi số), không thì dòng đầu của ghi chú
    static string? SearchDetail(SearchHit hit) =>
        hit.Title != null && Links.IsBareRef(hit.Text) ? hit.Title
        : hit.Note?.Split('\n')[0].Trim() is { Length: > 0 } line ? line : null;

    /// <summary>Mở ngày của 1 việc và chọn sẵn việc đó; việc đã xong mà đang bị ẩn thì bỏ ẩn để thấy được.</summary>
    void Reveal(DateTime day, long id)
    {
        GoTo(day);
        if (Find() is null && _hideDone) ToggleHideDone();
        if (Find() is { } node)
        {
            node.EnsureVisible();
            _tree.SelectedNode = node;
        }
        _tree.Focus();

        TreeNode? Find() => Flatten(_tree.Nodes).FirstOrDefault(n => n.Tag is Row r && r.Id == id);
    }

    // --- Ẩn việc đã xong ---

    void ToggleHideDone()
    {
        EndEdit(true);
        _hideTimer.Stop();
        _hideDone = !_hideDone;
        _store.SetSettings(("hide_done", _hideDone ? "on" : "off"));
        long? selected = (_tree.SelectedNode?.Tag as Row)?.Id;
        LoadDay();
        _tree.SelectedNode = Flatten(_tree.Nodes).FirstOrDefault(n => n.Tag is Row r && r.Id == selected);
    }

    void UpdateHideButton()
    {
        _hideBtn.ForeColor = _hideDone ? Theme.Accent : Theme.Muted;
        _tip.SetToolTip(_hideBtn, _hideDone
            ? T($"Đang ẩn {_hiddenCount} việc đã xong, bấm để hiện lại (Ctrl+Shift+H)", $"{_hiddenCount} completed tasks hidden, click to show (Ctrl+Shift+H)")
            : T("Ẩn việc đã xong (Ctrl+Shift+H)", "Hide completed tasks (Ctrl+Shift+H)"));
    }

    // Đang ẩn việc đã xong: việc vừa tick để lại một lúc (lỡ tay thì kịp bỏ tick) rồi mới ẩn
    void HideDoneNow()
    {
        _hideTimer.Stop();
        if (!_hideDone) return;
        if (_editing != null)
        {
            _hideTimer.Start(); // đang gõ dở → đợi thêm
            return;
        }
        var done = Flatten(_tree.Nodes).Where(n => IsDone(n) && (n.Parent is not { } p || !IsDone(p))).ToList();
        if (done.Count == 0) return;
        Quiet(() =>
        {
            foreach (var n in done)
            {
                if (n.Parent?.Tag is Row parent) parent.HiddenDone++;
                _hiddenCount += Flatten(n).Sum(x => 1 + ((x.Tag as Row)?.HiddenDone ?? 0));
                n.Remove();
            }
        });
        _tree.Invalidate();
        UpdateProgress();
        UpdateHideButton();
    }

    // --- Khay hệ thống, phím gọi app ---

    const int WM_HOTKEY = 0x0312, HotkeyId = 1, NotesHotkeyId = 2, MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
    const byte VK_F23 = 0x86; // phím không có trên bàn phím thường, chỉ dùng làm đường đi cho Ctrl+Win+Alt

    [DllImport("user32.dll")]
    static extern bool RegisterHotKey(IntPtr hWnd, int id, int modifiers, int key);

    [DllImport("user32.dll")]
    static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Đăng ký lại phím gọi app theo cài đặt; false nếu phím đang bị Windows / app khác giữ.</summary>
    bool RegisterHotkey()
    {
        if (!IsHandleCreated) return true;
        if (_hotkeyOn) UnregisterHotKey(Handle, HotkeyId);
        _hotkeyOn = false;
        int mods = _settings.Hotkey switch { 0 => MOD_CONTROL | MOD_ALT, 1 => MOD_WIN | MOD_SHIFT, _ => 0 };
        if (mods == 0) return true;
        _hotkeyOn = RegisterHotKey(Handle, HotkeyId, mods | MOD_NOREPEAT, (int)Keys.T);
        return _hotkeyOn;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RegisterHotkey();
        if (_settings != null) ApplyNotesChord(); // handle có thể được tạo ngay trong constructor của Form, trước khi đọc cài đặt
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hotkeyOn) UnregisterHotKey(Handle, HotkeyId);
        if (_notesKeyOn) UnregisterHotKey(Handle, NotesHotkeyId);
        _hotkeyOn = _notesKeyOn = false;
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY && (int)m.WParam is HotkeyId or NotesHotkeyId)
        {
            if ((int)m.WParam == HotkeyId) ToggleFromHotkey();
            else ToggleNotes();
            return;
        }
        base.WndProc(ref m);
    }

    // Phím gọi app: đang mở trước mặt thì cất đi; không thì hiện lên ở hôm nay, con trỏ đặt sẵn ở ô thêm việc
    void ToggleFromHotkey()
    {
        if (Visible && WindowState != FormWindowState.Minimized && ActiveForm == this)
        {
            if (_settings.Tray) Hide();
            else WindowState = FormWindowState.Minimized;
            return;
        }
        QuickAdd();
    }

    void QuickAdd()
    {
        ShowFromTray();
        if (_day != DateTime.Today) GoTo(DateTime.Today);
        _add.Focus();
    }

    /// <summary>Hiện lại cửa sổ (đang ở khay hoặc thu nhỏ) và đưa lên trước.</summary>
    void ShowFromTray()
    {
        if (!Visible) Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }

    // Bật "thu nhỏ xuống khay": thu nhỏ là ẩn hẳn cửa sổ, chỉ còn icon ở khay hệ thống
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized && _settings is { Tray: true } && Visible) Hide();
    }

    ContextMenuStrip BuildTrayMenu()
    {
        var menu = new ContextMenuStrip
        {
            ShowImageMargin = false, Font = Theme.Ui(9.5f), ForeColor = Theme.Text, Renderer = new ToolStripProfessionalRenderer(new MenuColors()),
        };
        void Item(string text, Action action, bool strong = false)
        {
            var item = new ToolStripMenuItem(text) { Padding = new Padding(0, S(3), 0, S(3)) };
            if (strong) item.Font = Theme.Semibold(9.5f);
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }
        Item(T("Mở", "Open"), ShowFromTray, strong: true);
        Item(T("Thêm việc mới", "Add a task"), QuickAdd);
        menu.Items.Add(new ToolStripSeparator());
        Item(T("Thoát", "Exit"), Close);
        return menu;
    }

    // --- Liên kết task (nhóm cùng màu, chỉ lưu ở DB máy này) ---

    (long Id, int Color)? GroupOf(TreeNode node) =>
        node.Tag is Row { GroupId: { } id } && _groupColors.TryGetValue(id, out var color) ? (id, color) : null;

    /// <summary>Tick các task cùng thuộc 1 việc lớn với task này (vd 2 issue ở 2 repo) → tô cùng màu.</summary>
    void LinkTasks(TreeNode node)
    {
        EndEdit(true);
        if (node.TreeView == null || node.Tag is not Row) return;
        var current = GroupOf(node);
        var items = Flatten(_tree.Nodes)
            .Where(n => n != node && n.Tag is Row)
            .Select(n => new TaskPicker.Item(n, n.Text, TitleOf(n), n.Level, GroupOf(n)?.Color)
            {
                Checked = current != null && GroupOf(n)?.Id == current.Value.Id,
            })
            .ToList();
        using var dialog = new GroupDialog(node.Text, items, current?.Color ?? NextGroupColor(), linked: current != null);
        if (dialog.ShowDialog(this) == DialogResult.OK) ApplyGroup(node, dialog.Linked, dialog.ColorIndex);
    }

    /// <summary>
    /// Đưa node + các task <paramref name="linked"/> vào chung 1 nhóm màu <paramref name="color"/>;
    /// linked rỗng = chỉ đưa node ra khỏi nhóm của nó (các task còn lại vẫn liên kết với nhau).
    /// </summary>
    void ApplyGroup(TreeNode node, IReadOnlyCollection<TreeNode> linked, int color)
    {
        var all = Flatten(_tree.Nodes).Where(n => n.Tag is Row).ToList();
        long? old = ((Row)node.Tag!).GroupId;
        var touched = linked.Append(node).Select(n => ((Row)n.Tag!).GroupId).OfType<long>().ToHashSet();
        var changes = new Dictionary<TreeNode, long?>();

        long? group = null;
        if (linked.Count > 0)
        {
            group = old ?? _store.NewGroup(color);
            _store.SetGroupColor(group.Value, color);
            _groupColors[group.Value] = color;
        }
        Set(node, group);
        foreach (var n in linked) Set(n, group);
        // đang sửa thành viên nhóm: task trước đó cùng nhóm mà giờ bỏ tick thì ra khỏi nhóm
        if (group != null && old != null)
            foreach (var n in all.Where(n => n != node && !linked.Contains(n) && ((Row)n.Tag!).GroupId == old)) Set(n, null);
        // nhóm cũ nào trong ngày chỉ còn 1 task thì bỏ luôn: 1 mình 1 màu chẳng liên kết với ai
        foreach (var g in touched)
            if (all.Where(n => ((Row)n.Tag!).GroupId == g).ToList() is [var alone]) Set(alone, null);

        _store.SetGroups(changes.Select(c => (Id(c.Key), c.Value)));
        _tree.Invalidate();

        void Set(TreeNode n, long? g)
        {
            var row = (Row)n.Tag!;
            if (row.GroupId == g) return;
            row.GroupId = g;
            changes[n] = g;
        }
    }

    // Màu cho nhóm mới: màu đang ít nhóm dùng nhất trong ngày
    int NextGroupColor()
    {
        var used = Flatten(_tree.Nodes).Select(GroupOf).OfType<(long Id, int Color)>().DistinctBy(g => g.Id).Select(g => g.Color).ToList();
        return Enumerable.Range(0, Theme.GroupColorCount).MinBy(c => used.Count(u => u == c));
    }

    void Delete(TreeNode node)
    {
        if (node.Tag == null) return;
        int kids = _store.CountDescendants(Id(node)); // đếm trong DB: tính cả việc con đã xong đang bị ẩn
        if (kids > 0 && !MessageDialog.Confirm(this,
                T($"Xóa \"{node.Text}\" cùng {kids} việc con?", $"Delete \"{node.Text}\" and its {kids} subtasks?"), Text))
            return;

        var parent = node.Parent;
        _store.Delete(Id(node));
        node.Remove();
        Commit(SyncUp(parent));
    }

    // --- Tick xong: tick cha → tick hết con; con xong hết → cha tự xong ---

    void ToggleDone(TreeNode node)
    {
        if (node.Tag is not Row row) return;
        bool done = !row.Done;
        var changed = Flatten(node).Where(n => n.Tag is Row).ToList();
        foreach (var n in changed) ((Row)n.Tag!).Done = done;
        changed.AddRange(SyncUp(node.Parent));
        Commit(changed);
        if (_hideDone && done)
        {
            _hideTimer.Stop();
            _hideTimer.Start();
        }
    }

    static List<TreeNode> SyncUp(TreeNode? parent)
    {
        var changed = new List<TreeNode>();
        for (var p = parent; p?.Tag is Row row && p.Nodes.Count > 0; p = p.Parent)
        {
            bool all = p.Nodes.Cast<TreeNode>().All(IsDone);
            if (row.Done == all) break;
            row.Done = all;
            changed.Add(p);
        }
        return changed;
    }

    void Commit(List<TreeNode> changed)
    {
        if (changed.Count > 0) _store.SetDone(changed.Select(n => (Id(n), IsDone(n))));
        _tree.Invalidate();
        UpdateProgress();
    }

    // --- Di chuyển (Alt + mũi tên) ---

    void MoveBy(int delta)
    {
        var node = _tree.SelectedNode;
        if (node?.Tag == null) return;
        int index = node.Index + delta;
        int count = (node.Parent?.Nodes ?? _tree.Nodes).Count;
        if (index >= 0 && index < count) Relocate(node, node.Parent, index);
    }

    void Indent()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag != null && node.PrevNode is { } prev) Relocate(node, prev, prev.Nodes.Count);
    }

    void Outdent()
    {
        var node = _tree.SelectedNode;
        if (node?.Tag != null && node.Parent is { } parent) Relocate(node, parent.Parent, parent.Index + 1);
    }

    void Relocate(TreeNode node, TreeNode? newParent, int index)
    {
        var oldParent = node.Parent;
        var expanded = Flatten(node).Where(n => n.IsExpanded).ToList();
        Quiet(() =>
        {
            node.Remove();
            (newParent?.Nodes ?? _tree.Nodes).Insert(index, node);
            foreach (var n in expanded) n.Expand();
        });
        newParent?.Expand();
        _tree.SelectedNode = node;

        SaveOrder(oldParent);
        if (newParent != oldParent) SaveOrder(newParent);
        var changed = SyncUp(oldParent);
        changed.AddRange(SyncUp(newParent));
        Commit(changed);
    }

    // TreeView rộng hơn khung đúng bằng thanh cuộn gốc để thanh đó bị che, rồi đồng bộ vị trí sang thanh tự vẽ
    void SyncScroll(Control clip)
    {
        var (native, pos, page, total) = _tree.ScrollState();
        int extra = native ? _tree.Width - _tree.ClientSize.Width : 0;
        if (native && extra <= 0) extra = SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
        var size = new Size(clip.Width + extra, clip.Height);
        if (_tree.Size != size) _tree.Size = size;
        _scroll.SetRange(pos, page, total);
    }

    void SaveOrder(TreeNode? parent)
    {
        long? parentId = IdOf(parent);
        var ids = (parent?.Nodes ?? _tree.Nodes).Cast<TreeNode>().Where(n => n.Tag is Row).Select(Id).ToList();
        // việc đã xong đang bị ẩn vẫn là con của cha này: xếp sau các việc đang hiện để không mất chỗ
        if (_hideDone)
            ids.AddRange(_store.Load(_day).Where(i => i.ParentId == parentId && i.Done && !ids.Contains(i.Id)).Select(i => i.Id));
        _store.SetOrder(parentId, ids);
    }

    void SaveExpanded(TreeNode? node, bool expanded)
    {
        if (!_busy && node?.Tag is Row row) _store.SetExpanded(row.Id, expanded);
    }

    // --- Khác ---

    void CarryOver()
    {
        EndEdit(true);
        var from = _store.LastDayWithOpenItems(_day);
        if (from == null)
        {
            MessageDialog.Show(this, T("Các ngày trước không còn việc nào chưa xong.", "No unfinished tasks on earlier days."), Text);
            return;
        }
        if (!MessageDialog.Confirm(this, T($"Chép các việc chưa xong của {Lang.Date(from.Value)} sang {Lang.Date(_day)}?",
                $"Copy unfinished tasks from {Lang.Date(from.Value)} to {Lang.Date(_day)}?"), Text))
            return;

        if (_store.CarryOver(from.Value, _day) == 0)
            MessageDialog.Show(this, T("Các việc đó đã có sẵn ở ngày này rồi.", "Those tasks are already on this day."), Text);
        LoadDay();
    }

    void ShowCalendar()
    {
        EndEdit(true);
        _calendar.Open(_day);
        _calendarDrop.Show(_calendarBtn, new Point(_calendarBtn.Width, _calendarBtn.Height), ToolStripDropDownDirection.BelowLeft);
    }

    void TogglePin()
    {
        TopMost = !TopMost;
        UpdatePin();
    }

    void UpdatePin()
    {
        _pinBtn.Text = Theme.Glyph(TopMost ? 0xE840 : 0xE718);
        _pinBtn.ForeColor = TopMost ? Theme.Accent : Theme.Muted;
    }

    void UpdateProgress()
    {
        // tính từ DB để việc đã xong đang bị ẩn vẫn được đếm
        var items = _store.Load(_day);
        var parents = items.Select(i => i.ParentId).OfType<long>().ToHashSet();
        var leaves = items.Where(i => !parents.Contains(i.Id)).ToList();
        int done = leaves.Count(i => i.Done);
        _progress.Value = leaves.Count == 0 ? 0 : (float)done / leaves.Count;
        _subtitle.Text = _dateText;
        _progress.Text = leaves.Count == 0 ? "" : done == leaves.Count ? T("Xong hết", "All done") : T($"{done}/{leaves.Count} xong", $"{done}/{leaves.Count} done");
        _empty.Visible = _tree.Nodes.Count == 0;
        _empty.Text = _hiddenCount > 0
            ? T($"Xong hết rồi.\nĐang ẩn {_hiddenCount} việc đã xong.", $"All done.\n{_hiddenCount} completed tasks are hidden.")
            : T("Chưa có việc nào.\nGõ vào ô phía trên rồi Enter để thêm.", "Nothing here yet.\nType in the box above and press Enter.");
    }

    void ShowHelp() => MessageDialog.Show(this, T(
            "Ctrl+N  —  tới ô thêm note mới\n" +
            "Enter  —  thêm việc cùng cấp (gõ xong Enter để thêm tiếp, Esc để dừng)\n" +
            "Ctrl+Enter  —  thêm việc con\n" +
            "F2 / double-click  —  sửa tên\n" +
            "Space / bấm ô vuông  —  tick xong / bỏ tick\n" +
            "Delete  —  xóa\n" +
            "Kéo thả  —  đổi thứ tự; thả vào giữa 1 dòng để thành việc con\n" +
            "Alt + ↑ ↓  —  đổi thứ tự\n" +
            "Alt + ← →  —  lùi / thụt cấp\n" +
            "Ctrl+L  —  gắn link · Ctrl+O / bấm vào tên việc có link  —  mở link\n" +
            "Ctrl+G  —  liên kết các task cùng 1 việc lớn (vd issue ở 2 repo) → tô cùng màu\n" +
            "Shift + bấm tên việc / Ctrl+Shift+O  —  mở link của mọi task cùng nhóm liên kết\n" +
            "Ctrl+E  —  ghi chú & tệp / thư mục đính kèm của việc\n" +
            "Ctrl+H  —  danh sách việc đã xong, kèm ngày giờ xong\n" +
            "Ctrl+F  —  tìm việc ở mọi ngày (tên việc, số / tên issue, ghi chú; không cần gõ dấu)\n" +
            "Ctrl+Shift+H  —  ẩn / hiện việc đã xong\n" +
            "Ctrl+J  —  ghi chú chung (thông tin cần giữ lại, không đổi theo ngày)\n" +
            "Ctrl+Win+Alt (từ bất kỳ đâu)  —  mở ghi chú chung mà không cần mở cửa sổ việc, bấm lần nữa để đóng\n" +
            "Ctrl+Alt+T (từ bất kỳ đâu)  —  gọi app lên để thêm việc, bấm lần nữa để cất đi (đổi trong Cài đặt)\n" +
            "F5  —  cập nhật Status trong GitHub Project + tên issue GitHub (app cũng tự làm lúc mở và mỗi 30 phút)\n" +
            "   (task có icon bảng kẹp giấy = issue đã có coding checklist trong mô tả / comment, bấm icon để mở;\n" +
            "    dán link GitHub issue làm tên việc → tự rút gọn thành #số;\n" +
            "    gắn link repo cho note cha → các dòng \"#số\" bên dưới tự thành link)\n" +
            "Ctrl+PgUp / PgDn  —  ngày trước / sau\n" +
            "Ctrl+Home  —  về hôm nay\n" +
            "Ctrl+,  —  cài đặt (giao diện sáng / tối, ngôn ngữ, kích thước)\n\n" +
            "Dữ liệu: ",
            "Ctrl+N  —  go to the new note box\n" +
            "Enter  —  add a task at the same level (Enter again to keep adding, Esc to stop)\n" +
            "Ctrl+Enter  —  add a subtask\n" +
            "F2 / double-click  —  rename\n" +
            "Space / click the box  —  check / uncheck\n" +
            "Delete  —  delete\n" +
            "Drag & drop  —  reorder; drop on the middle of a row to make it a subtask\n" +
            "Alt + ↑ ↓  —  reorder\n" +
            "Alt + ← →  —  outdent / indent\n" +
            "Ctrl+L  —  add a link · Ctrl+O / click a linked task's name  —  open the link\n" +
            "Ctrl+G  —  link tasks of one bigger issue (e.g. issues in 2 repos) so they share a color\n" +
            "Shift + click a task name / Ctrl+Shift+O  —  open the links of every task in its group\n" +
            "Ctrl+E  —  notes & attached files / folders of a task\n" +
            "Ctrl+H  —  completed tasks with the day and time they were done\n" +
            "Ctrl+F  —  search every day (task, issue number / title, notes; accents optional)\n" +
            "Ctrl+Shift+H  —  hide / show completed tasks\n" +
            "Ctrl+J  —  notes (things worth keeping, not tied to a day)\n" +
            "Ctrl+Win+Alt (from anywhere)  —  open the notes without the task window, press again to close\n" +
            "Ctrl+Alt+T (from anywhere)  —  bring the app up to add a task, press again to tuck it away (change in Settings)\n" +
            "F5  —  refresh the GitHub Project status + issue names (the app also does it on start and every 30 min)\n" +
            "   (a clipboard icon = the issue has a coding checklist in its description / comments, click it to open;\n" +
            "    paste a GitHub issue link as the task name → shortened to #number;\n" +
            "    give the parent note a repo link → \"#number\" rows under it become links)\n" +
            "Ctrl+PgUp / PgDn  —  previous / next day\n" +
            "Ctrl+Home  —  back to today\n" +
            "Ctrl+,  —  settings (light / dark theme, language, size)\n\n" +
            "Data: ") + _store.FilePath,
        T("Phím tắt", "Keyboard shortcuts"));

    void Quiet(Action action)
    {
        var old = _busy;
        _busy = true;
        try { action(); }
        finally { _busy = old; }
    }

    FlatButton IconButton(string glyph, string tip, Action click)
    {
        var b = new FlatButton { Text = glyph, Font = Theme.IconFont(10f), Size = S(new Size(34, 34)), Margin = new Padding(S(2), 0, 0, 0) };
        _tip.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    FlatButton TextButton(string text, string tip, Action click)
    {
        var b = new FlatButton
        {
            Text = text, Font = Theme.Ui(9.5f), AutoSize = true,
            MinimumSize = new Size(0, S(32)), Padding = new Padding(S(6), 0, S(6), 0), Margin = new Padding(S(2), S(1), 0, 0),
        };
        _tip.SetToolTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    // app.ico (bìa kẹp hồ sơ, lấy từ shell32.dll #260) nhúng vào exe, đủ cỡ cho thanh tiêu đề lẫn taskbar
    static Icon LoadIcon()
    {
        using var stream = typeof(MainForm).Assembly.GetManifestResourceStream("app.ico")!;
        return new Icon(stream);
    }

    static bool IsDone(TreeNode node) => node.Tag is Row { Done: true };
    static long Id(TreeNode node) => ((Row)node.Tag!).Id;
    static long? IdOf(TreeNode? node) => (node?.Tag as Row)?.Id;

    static IEnumerable<TreeNode> Flatten(TreeNode node) => Flatten(node.Nodes).Prepend(node);
    static IEnumerable<TreeNode> Flatten(TreeNodeCollection nodes) => nodes.Cast<TreeNode>().SelectMany(Flatten);
}
