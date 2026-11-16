using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TodayChecklist;

/// <summary>Danh sách việc đã xong, nhóm theo ngày xong, kèm giờ. Bấm 1 việc → về ngày của việc đó.</summary>
sealed class DoneDialog : DialogForm
{
    public DayRow? Picked { get; private set; }

    public DoneDialog(List<DoneItem> items)
    {
        Text = T("Việc đã xong", "Completed tasks");
        int listTop = 78, listHeight = 440;

        var title = new Label
        {
            Text = T("Việc đã xong", "Completed tasks"), ForeColor = Theme.Text, Font = Theme.Heading(12.5f), Bounds = S(new Rectangle(20, 14, 480, 30)),
        };
        var hint = new Label
        {
            Text = T($"{items.Count} việc gần nhất, mới nhất ở trên. Bấm vào 1 việc để mở ngày đó.",
                     $"{items.Count} most recent, newest first. Click a task to open its day."),
            AutoEllipsis = true, ForeColor = Theme.Muted, Font = Theme.Ui(9f), Bounds = S(new Rectangle(20, 46, 480, 20)),
        };
        var rows = items.Select(i => new DayRow(i.Id, i.Day, (i.DoneAt ?? i.Day).Date, i.DoneAt?.ToString("HH:mm") ?? "—", i.Text,
            i.Title != null && Links.IsBareRef(i.Text) ? i.Title : null, i.Parent, Dim: false)).ToList();
        var list = new DayList(rows, T("Chưa có việc nào xong.", "No completed tasks yet.")) { Bounds = S(new Rectangle(16, listTop, 478, listHeight)) };
        var scroll = new ScrollStrip { Bounds = S(new Rectangle(496, listTop, 8, listHeight)) };
        list.ScrollChanged += () => scroll.SetRange(list.FirstRow, list.Page, list.Count);
        scroll.Scrolled += list.ScrollTo;
        list.Picked += row =>
        {
            Picked = row;
            DialogResult = DialogResult.OK;
        };

        var close = DialogButton(T("Đóng", "Close"), primary: true);
        close.DialogResult = DialogResult.Cancel;
        close.Bounds = S(new Rectangle(416, listTop + listHeight + 14, 84, 34));
        AcceptButton = CancelButton = close;
        Controls.AddRange(new Control[] { title, hint, list, scroll, close });
        ClientSize = S(new Size(520, listTop + listHeight + 66));
        Shown += (_, _) => scroll.SetRange(list.FirstRow, list.Page, list.Count);
    }
}

/// <summary>Tìm việc ở mọi ngày theo tên việc, số / tên issue, ghi chú. Gõ tới đâu ra tới đó; Enter mở kết quả đầu tiên.</summary>
sealed class SearchDialog : DialogForm
{
    readonly Func<string, List<DayRow>> _search;
    readonly TextBox _box;
    readonly Label _hint;
    readonly DayList _list;
    readonly System.Windows.Forms.Timer _wait = new() { Interval = 180 };

    public DayRow? Picked { get; private set; }

    public SearchDialog(Func<string, List<DayRow>> search)
    {
        _search = search;
        Text = T("Tìm kiếm", "Search");
        int listTop = 92, listHeight = 440;

        _box = new TextBox
        {
            BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.Ui(11f),
            PlaceholderText = T("Số issue, tên việc, tên issue, ghi chú…", "Issue number, task, issue title, note…"),
        };
        var field = new InputField(_box, glyph: 0xE721) { Dock = DockStyle.None, Bounds = S(new Rectangle(20, 14, 480, 40)) };
        _hint = new Label { AutoEllipsis = true, ForeColor = Theme.Muted, Font = Theme.Ui(9f), Bounds = S(new Rectangle(20, 62, 480, 20)) };
        _list = new DayList([], T("Không thấy việc nào khớp.", "No matching task.")) { Bounds = S(new Rectangle(16, listTop, 478, listHeight)) };
        var scroll = new ScrollStrip { Bounds = S(new Rectangle(496, listTop, 8, listHeight)) };
        _list.ScrollChanged += () => scroll.SetRange(_list.FirstRow, _list.Page, _list.Count);
        scroll.Scrolled += _list.ScrollTo;
        _list.Picked += Pick;

        _wait.Tick += (_, _) => Run();
        _box.TextChanged += (_, _) =>
        {
            _wait.Stop();
            _wait.Start();
        };
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.Handled = e.SuppressKeyPress = true;
            Run();
            if (_list.First is { } first) Pick(first);
        };

        var close = DialogButton(T("Đóng", "Close"), primary: false);
        close.DialogResult = DialogResult.Cancel;
        close.Bounds = S(new Rectangle(416, listTop + listHeight + 14, 84, 34));
        CancelButton = close;
        Controls.AddRange(new Control[] { field, _hint, _list, scroll, close });
        ClientSize = S(new Size(520, listTop + listHeight + 66));
        Run();
        Shown += (_, _) => _box.Focus();
    }

    void Run()
    {
        _wait.Stop();
        string query = _box.Text.Trim();
        List<DayRow> rows = query.Length == 0 ? [] : _search(query);
        _list.SetRows(rows);
        _hint.Text = query.Length == 0
            ? T("Tìm ở mọi ngày, không cần gõ dấu. Bấm vào 1 kết quả để mở ngày đó.", "Searches every day; accents optional. Click a result to open its day.")
            : T($"{rows.Count} kết quả. Chữ xám = đã xong hoặc đã chép sang ngày sau.", $"{rows.Count} results. Grey = done or carried to a later day.");
    }

    void Pick(DayRow row)
    {
        Picked = row;
        DialogResult = DialogResult.OK;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _wait.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>1 dòng trong danh sách theo ngày. Lead: cột trái (giờ xong); Detail: chữ xám sau tên (tên issue, ghi chú); Dim: chữ xám cả dòng.</summary>
record DayRow(long Id, DateTime Day, DateTime Group, string? Lead, string Text, string? Detail, string? Parent, bool Dim);

/// <summary>Danh sách cuộn được, chia nhóm theo ngày: dòng tiêu đề ngày + các dòng việc (cột trái, tên việc, chữ xám, note cha).</summary>
sealed class DayList : Control
{
    // Dòng tiêu đề ngày có Row = null
    sealed record Line(DateTime Day, DayRow? Row, int Count);

    const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    readonly List<Line> _lines = new();
    readonly string _empty;
    readonly Font _strong, _small;
    bool _lead;
    int _top, _hover = -1;

    public event Action? ScrollChanged;
    public event Action<DayRow>? Picked;

    public DayList(List<DayRow> rows, string empty)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _empty = empty;
        Font = Theme.Ui(10f);
        _strong = Theme.Semibold(9.5f);
        _small = Theme.Ui(9f);
        SetRows(rows);
    }

    public int FirstRow => _top;
    public int Count => _lines.Count;
    public int Page => Math.Max(1, Height / RowHeight);
    public DayRow? First => _lines.Select(l => l.Row).FirstOrDefault(r => r != null);
    int RowHeight => Theme.Px(this, 32);

    public void SetRows(List<DayRow> rows)
    {
        _lines.Clear();
        foreach (var day in rows.GroupBy(r => r.Group))
        {
            _lines.Add(new Line(day.Key, null, day.Count()));
            _lines.AddRange(day.Select(r => new Line(day.Key, r, 0)));
        }
        _lead = rows.Any(r => r.Lead != null);
        _hover = -1;
        ScrollTo(0);
    }

    public void ScrollTo(int top)
    {
        _top = Math.Clamp(top, 0, Math.Max(0, _lines.Count - Page));
        Invalidate();
        ScrollChanged?.Invoke();
    }

    int IndexAt(int y) => y / RowHeight + _top is var i && i < _lines.Count ? i : -1;

    static string DayLabel(DateTime day)
    {
        int offset = (day - DateTime.Today).Days;
        string? relative = offset switch { 0 => T("Hôm nay", "Today"), -1 => T("Hôm qua", "Yesterday"), 1 => T("Ngày mai", "Tomorrow"), _ => null };
        string date = day.ToString(T("dddd, dd/MM/yyyy", "dddd, d MMMM yyyy"), Lang.Culture);
        return relative != null ? $"{relative} · {date}" : Lang.Culture.TextInfo.ToTitleCase(date);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        if (_lines.Count == 0)
        {
            TextRenderer.DrawText(g, _empty, Font, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        int h = RowHeight, pad = Theme.Px(this, 8), leadW = _lead ? Theme.Px(this, 52) : 0, parentW = Theme.Px(this, 120);
        for (int i = _top; i < _lines.Count && (i - _top) * h < Height; i++)
        {
            var line = _lines[i];
            var row = new Rectangle(0, (i - _top) * h, Width, h);
            if (line.Row is not { } item)
            {
                // tiêu đề ngày: chữ đậm xám + số việc, kẻ mảnh bên dưới
                var text = row with { X = pad, Width = Width - pad * 2 };
                TextRenderer.DrawText(g, DayLabel(line.Day), _strong, text, Theme.Muted, Flags);
                TextRenderer.DrawText(g, T($"{line.Count} việc", $"{line.Count} tasks"), _small, text, Theme.Muted, Flags | TextFormatFlags.Right);
                using var pen = new Pen(Theme.Line);
                g.DrawLine(pen, pad, row.Bottom - 2, Width - pad, row.Bottom - 2);
                continue;
            }
            if (i == _hover) Theme.FillRound(g, Theme.Hover, new RectangleF(0, row.Y + 1, Width, h - 2), Theme.Corner(this, h, 5));
            int x = pad;
            if (item.Lead != null) TextRenderer.DrawText(g, item.Lead, _small, new Rectangle(x, row.Y, leadW, h), Theme.Muted, Flags);
            x += leadW;
            int right = Width - pad;
            if (item.Parent != null)
            {
                TextRenderer.DrawText(g, item.Parent, _small, new Rectangle(right - parentW, row.Y, parentW, h), Theme.Muted, Flags | TextFormatFlags.Right);
                right -= parentW + pad;
            }
            int tw = Math.Min(TextRenderer.MeasureText(item.Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width, right - x);
            TextRenderer.DrawText(g, item.Text, Font, new Rectangle(x, row.Y, right - x, h), item.Dim ? Theme.Muted : Theme.Text, Flags);
            int dx = x + tw + Theme.Px(this, 8);
            if (item.Detail != null && right - dx > Theme.Px(this, 24))
                TextRenderer.DrawText(g, item.Detail, Font, new Rectangle(dx, row.Y, right - dx, h), Theme.Muted, Flags);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Y) is var j && j >= 0 && _lines[j].Row != null ? j : -1;
        Cursor = i >= 0 ? Cursors.Hand : Cursors.Default;
        if (i == _hover) return;
        _hover = i;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && IndexAt(e.Y) is >= 0 and var i && _lines[i].Row is { } row) Picked?.Invoke(row);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(_top - Math.Sign(e.Delta) * 3);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _strong.Dispose();
            _small.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Chi tiết 1 việc: ghi chú và tệp / thư mục đính kèm. Đính kèm chỉ trỏ tới đường dẫn có sẵn trên máy,
/// bấm Mở là mở bằng ứng dụng mặc định (thư mục thì mở Explorer). Đóng hộp là lưu.
/// </summary>
sealed class DetailsDialog : DialogForm
{
    readonly TextBox _note;
    readonly AttachmentList _files;

    public string Note => _note.Text.Trim();
    public IReadOnlyList<string> Paths => _files.Paths;

    public DetailsDialog(string taskText, string? taskTitle, string? note, List<string> paths)
    {
        Text = T("Ghi chú & tệp đính kèm", "Notes & attachments");
        var title = new Label
        {
            Text = taskText, AutoEllipsis = true, UseMnemonic = false, ForeColor = Theme.Text,
            Font = Theme.Heading(12.5f), Bounds = S(new Rectangle(20, 14, 480, 30)),
        };
        var subtitle = new Label
        {
            Text = taskTitle ?? "", AutoEllipsis = true, UseMnemonic = false, ForeColor = Theme.Muted,
            Font = Theme.Ui(9f), Bounds = S(new Rectangle(20, 44, 480, 20)),
        };

        int y = taskTitle != null ? 76 : 56;
        var noteLabel = Section(T("Ghi chú", "Notes"), y);
        var noteBox = new NoteBox(note, T("Ghi lại gì đó cho việc này…", "Write something about this task…"))
        {
            Bounds = S(new Rectangle(20, y + 26, 480, 140)),
        };
        _note = noteBox.Box;

        y += 26 + 140 + 18;
        var filesLabel = Section(T("Tệp & thư mục đính kèm", "Attached files & folders"), y);
        _files = new AttachmentList(paths) { Bounds = S(new Rectangle(16, y + 24, 488, 5 * 38)) };

        y += 24 + 5 * 38 + 12;
        var addFile = DialogButton(T("Thêm tệp…", "Add files…"), primary: false);
        var addFolder = DialogButton(T("Thêm thư mục…", "Add folder…"), primary: false);
        var done = DialogButton(T("Xong", "Done"), primary: true);
        addFile.Bounds = S(new Rectangle(20, y, 110, 34));
        addFolder.Bounds = S(new Rectangle(138, y, 126, 34));
        done.Bounds = S(new Rectangle(416, y, 84, 34));
        done.DialogResult = DialogResult.OK;
        CancelButton = done; // Enter trong ô ghi chú là xuống dòng; Esc / Xong / nút X đều đóng và lưu
        addFile.Click += (_, _) => AddFiles();
        addFolder.Click += (_, _) => AddFolder();

        // kéo thả tệp / thư mục từ Explorer vào bất kỳ chỗ nào trong hộp
        AllowDrop = true;
        DragEnter += (_, e) => e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Link : DragDropEffects.None;
        DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] dropped) _files.Add(dropped);
        };

        Controls.AddRange(new Control[] { title, subtitle, noteLabel, noteBox, filesLabel, _files, addFile, addFolder, done });
        ClientSize = S(new Size(520, y + 34 + 18));
        Shown += (_, _) =>
        {
            _note.Focus();
            _note.SelectionStart = _note.TextLength;
        };
    }

    Label Section(string text, int y) =>
        new PlainLabel { Text = text, ForeColor = Theme.Muted, Font = Theme.Semibold(9f), Bounds = S(new Rectangle(20, y, 480, 22)) };

    void AddFiles()
    {
        using var dialog = new OpenFileDialog { Multiselect = true, Title = T("Chọn tệp đính kèm", "Choose files to attach") };
        if (dialog.ShowDialog(this) == DialogResult.OK) _files.Add(dialog.FileNames);
    }

    void AddFolder()
    {
        using var dialog = new FolderBrowserDialog { Description = T("Chọn thư mục đính kèm", "Choose a folder to attach"), UseDescriptionForTitle = true };
        if (dialog.ShowDialog(this) == DialogResult.OK) _files.Add([dialog.SelectedPath]);
    }
}

/// <summary>Danh sách tệp / thư mục đính kèm: icon, tên, thư mục chứa; mỗi dòng có nút Mở và nút bỏ đính kèm.</summary>
sealed class AttachmentList : Control
{
    // Kiểm tra tồn tại 1 lần lúc thêm / mở hộp, không phải mỗi lần vẽ (đường dẫn mạng có thể chậm)
    sealed record Entry(string Path, bool IsFolder, bool Exists);

    enum Part { None, Row, Open, Remove }

    const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    readonly List<Entry> _entries;
    readonly Font _small, _icons, _smallIcons;
    int _top, _hover = -1;
    Part _hoverPart;

    public AttachmentList(IEnumerable<string> paths)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Ui(10f);
        _small = Theme.Ui(9f);
        _icons = Theme.IconFont(11f);
        _smallIcons = Theme.IconFont(8.5f);
        _entries = paths.Select(Check).ToList();
    }

    public IReadOnlyList<string> Paths => _entries.Select(e => e.Path).ToList();

    public void Add(IEnumerable<string> paths)
    {
        foreach (var path in paths)
            if (!_entries.Any(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)))
                _entries.Add(Check(path));
        _top = Math.Max(0, _entries.Count - Page); // cuộn tới chỗ vừa thêm
        Invalidate();
    }

    static Entry Check(string path) =>
        Directory.Exists(path) ? new Entry(path, true, true) : new Entry(path, false, File.Exists(path));

    int RowHeight => Theme.Px(this, 38);
    int Page => Math.Max(1, Height / RowHeight);
    Rectangle RowRect(int i) => new(0, (i - _top) * RowHeight, Width, RowHeight);
    Rectangle RemoveRect(Rectangle row) => new(row.Right - Theme.Px(this, 34), row.Y + (row.Height - Theme.Px(this, 28)) / 2, Theme.Px(this, 28), Theme.Px(this, 28));
    Rectangle OpenRect(Rectangle row) => new(RemoveRect(row).X - Theme.Px(this, 60), row.Y + (row.Height - Theme.Px(this, 26)) / 2, Theme.Px(this, 54), Theme.Px(this, 26));

    (int Index, Part Part) HitTest(Point pt)
    {
        int i = pt.Y / RowHeight + _top;
        if (i < 0 || i >= _entries.Count) return (-1, Part.None);
        var row = RowRect(i);
        return (i, OpenRect(row).Contains(pt) ? Part.Open : RemoveRect(row).Contains(pt) ? Part.Remove : Part.Row);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        if (_entries.Count == 0)
        {
            TextRenderer.DrawText(g, T("Chưa có gì. Bấm Thêm tệp / Thêm thư mục, hoặc kéo thả vào đây.", "Nothing yet. Use Add files / Add folder, or drop them here."),
                _small, ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        int pad = Theme.Px(this, 8);
        float px = DeviceDpi / 96f;
        for (int i = _top; i < _entries.Count && (i - _top) * RowHeight < Height; i++)
        {
            var entry = _entries[i];
            var row = RowRect(i);
            bool hot = i == _hover;
            if (hot) Theme.FillRound(g, Theme.Hover, new RectangleF(0, row.Y + 1, Width, row.Height - 2), Theme.Corner(this, row.Height, 5));

            // icon: thư mục / tệp; không còn trên máy thì icon cảnh báo
            var icon = new Rectangle(pad, row.Y, Theme.Px(this, 26), row.Height);
            int glyph = !entry.Exists ? 0xE7BA : entry.IsFolder ? 0xE8B7 : 0xE8A5;
            TextRenderer.DrawText(g, Theme.Glyph(glyph), _icons, icon, entry.Exists ? Theme.Muted : Theme.Warning,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var open = OpenRect(row);
            int x = icon.Right + pad, right = open.X - pad;
            string name = Path.GetFileName(entry.Path.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : entry.Path;
            int nameW = Math.Min(TextRenderer.MeasureText(name, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width, right - x);
            TextRenderer.DrawText(g, name, Font, new Rectangle(x, row.Y, right - x, row.Height), entry.Exists ? Theme.Text : Theme.Muted, Flags);
            string where = entry.Exists ? Path.GetDirectoryName(entry.Path) ?? "" : T("không còn trên máy", "no longer on this computer");
            int dx = x + nameW + pad;
            if (right - dx > Theme.Px(this, 24))
                TextRenderer.DrawText(g, where, _small, new Rectangle(dx, row.Y, right - dx, row.Height), entry.Exists ? Theme.Muted : Theme.Warning,
                    Flags | TextFormatFlags.PathEllipsis);

            // nút Mở (viền mảnh) và nút bỏ đính kèm
            float radius = Theme.Corner(this, open.Height, 5);
            if (hot && _hoverPart == Part.Open) Theme.FillRound(g, Theme.Field, open, radius);
            Theme.DrawRound(g, Theme.Border, px, RectangleF.Inflate(open, -px / 2, -px / 2), radius);
            TextRenderer.DrawText(g, T("Mở", "Open"), _small, open, entry.Exists ? Theme.Text : Theme.Border,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Theme.Glyph(0xE711), _smallIcons, RemoveRect(row),
                hot && _hoverPart == Part.Remove ? Theme.Danger : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    void Open(Entry entry)
    {
        try
        {
            if (!Directory.Exists(entry.Path) && !File.Exists(entry.Path)) throw new FileNotFoundException(T("Không tìm thấy trên máy.", "Not found on this computer."));
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageDialog.Show(FindForm()!, T($"Không mở được:\n{entry.Path}\n\n{ex.Message}", $"Couldn't open:\n{entry.Path}\n\n{ex.Message}"),
                T("Tệp đính kèm", "Attachment"), warning: true);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (i, part) = HitTest(e.Location);
        Cursor = part is Part.Open or Part.Remove ? Cursors.Hand : Cursors.Default;
        if (i == _hover && part == _hoverPart) return;
        (_hover, _hoverPart) = (i, part);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        var (i, part) = HitTest(e.Location);
        if (part == Part.Open) Open(_entries[i]);
        else if (part == Part.Remove)
        {
            _entries.RemoveAt(i);
            _top = Math.Clamp(_top, 0, Math.Max(0, _entries.Count - Page));
            _hover = -1;
            Invalidate();
        }
    }

    // double-click vào dòng cũng mở
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (HitTest(e.Location) is (>= 0 and var i, Part.Row)) Open(_entries[i]);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _top = Math.Clamp(_top - Math.Sign(e.Delta), 0, Math.Max(0, _entries.Count - Page));
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _small.Dispose();
            _icons.Dispose();
            _smallIcons.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Ô ghi chú nhiều dòng: nền bo góc, viền màu nhấn khi đang gõ; Enter là xuống dòng. Dài thì lăn chuột / kéo thanh cuộn mảnh.</summary>
sealed class NoteBox : Panel
{
    readonly ScrollStrip _scroll;

    public NoteTextBox Box { get; }

    public NoteBox(string? text, string placeholder)
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Padding = new Padding(Theme.Px(this, 12), Theme.Px(this, 10), Theme.Px(this, 4), Theme.Px(this, 10));
        Box = new NoteTextBox
        {
            Multiline = true, AcceptsReturn = true, AcceptsTab = false, WordWrap = true, ScrollBars = ScrollBars.None,
            BorderStyle = BorderStyle.None, BackColor = Theme.Field, ForeColor = Theme.Text, Font = Theme.Ui(10f), Text = text ?? "",
            PlaceholderText = placeholder, Dock = DockStyle.Fill,
        };
        // thanh cuộn gốc (nền xám) tắt đi; thanh mảnh tự vẽ thay thế, chỉ hiện khi chữ dài hơn ô
        _scroll = new ScrollStrip { Dock = DockStyle.Right, Width = Theme.Px(this, 10), BackColor = Theme.Field };
        Box.ScrollChanged += () => _scroll.SetRange(Box.FirstLine, Box.Page, Box.LineCount);
        _scroll.Scrolled += Box.ScrollToLine;
        _scroll.MouseWheel += (_, e) => Box.ScrollBy(-Math.Sign(e.Delta) * 3);
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
        Controls.Add(Box);
        Controls.Add(_scroll); // thêm sau cùng → dock trước, ô chữ lấp phần còn lại
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        float radius = Math.Min(Theme.Corner(this, Height, 8), Theme.Px(this, 8));
        Theme.FillRound(e.Graphics, Theme.Field, new RectangleF(0, 0, Width, Height), radius);
        if (Box.Focused)
            Theme.DrawRound(e.Graphics, Theme.Accent, 1.5f, new RectangleF(0.75f, 0.75f, Width - 1.5f, Height - 1.5f), radius);
    }
}

/// <summary>
/// TextBox nhiều dòng không có thanh cuộn gốc: tự xử lý lăn chuột (ô gốc không cuộn khi tắt thanh cuộn)
/// và báo dòng đầu / số dòng cho thanh cuộn tự vẽ.
/// </summary>
sealed class NoteTextBox : TextBox
{
    const int WM_MOUSEWHEEL = 0x020A, WM_VSCROLL = 0x0115, EM_GETFIRSTVISIBLELINE = 0x00CE, EM_GETLINECOUNT = 0x00BA, EM_LINESCROLL = 0x00B6;
    int _wheel; // phần lăn lẻ (touchpad) chưa đủ 1 dòng

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Dòng đầu đang thấy / số dòng (tính cả dòng tự xuống) có thể đã đổi.</summary>
    public event Action? ScrollChanged;

    public int FirstLine => IsHandleCreated ? (int)SendMessage(Handle, EM_GETFIRSTVISIBLELINE, 0, 0) : 0;
    public int LineCount => IsHandleCreated ? (int)SendMessage(Handle, EM_GETLINECOUNT, 0, 0) : 0;
    public int Page => Math.Max(1, ClientSize.Height / Math.Max(1, Font.Height));

    public void ScrollBy(int lines)
    {
        if (!IsHandleCreated || lines == 0) return;
        SendMessage(Handle, EM_LINESCROLL, 0, lines);
        ScrollChanged?.Invoke();
    }

    public void ScrollToLine(int line) => ScrollBy(line - FirstLine);

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_MOUSEWHEEL)
        {
            // 1 nấc lăn (120) = 3 dòng; cộng dồn phần lẻ của touchpad
            _wheel += (short)((long)m.WParam >> 16);
            int lines = _wheel / 40;
            _wheel -= lines * 40;
            ScrollBy(-lines);
            return;
        }
        base.WndProc(ref m);
        if (m.Msg == WM_VSCROLL) ScrollChanged?.Invoke(); // ô tự cuộn theo con trỏ khi gõ / đi lên xuống
    }

    // gõ, xuống dòng, di chuyển con trỏ, chọn chữ bằng chuột, đổi cỡ: dòng đầu / số dòng có thể đổi
    protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); ScrollChanged?.Invoke(); }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); ScrollChanged?.Invoke(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); ScrollChanged?.Invoke(); }
    protected override void OnResize(EventArgs e) { base.OnResize(e); ScrollChanged?.Invoke(); }
    protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ScrollChanged?.Invoke(); }
}

/// <summary>
/// Ghi chú chung: thông tin cần giữ lại (link, tài khoản test, lệnh hay dùng…), không theo ngày.
/// App không bao giờ tự đổi, chỉ người dùng thêm / sửa / xóa. Mở thành cửa sổ riêng (không chặn cửa sổ việc), đóng là lưu.
/// </summary>
sealed class NotepadDialog : DialogForm
{
    readonly NoteBox _note;

    public string Note => _note.Box.Text.TrimEnd();

    public NotepadDialog(string? note)
    {
        Text = T("Ghi chú chung", "Notes");
        ShowInTaskbar = true; // mở bằng Ctrl+Win+Alt khi cửa sổ việc đang ẩn → vẫn Alt+Tab tới được
        var title = new Label
        {
            Text = T("Ghi chú chung", "Notes"), ForeColor = Theme.Text, Font = Theme.Heading(12.5f), Bounds = S(new Rectangle(20, 14, 560, 30)),
        };
        var hint = new Label
        {
            Text = T("Thông tin cần giữ lại: link, tài khoản test, lệnh hay dùng… Không đổi theo ngày, chỉ bạn sửa. Đóng là tự lưu.",
                     "Things worth keeping: links, test accounts, handy commands… Not tied to a day, only you change it. Saved when closed."),
            ForeColor = Theme.Muted, Font = Theme.Ui(9f), Bounds = S(new Rectangle(20, 46, 560, 36)), // dài thì xuống dòng
        };
        _note = new NoteBox(note, T("Gõ những gì cần nhớ…", "Write what you need to remember…")) { Bounds = S(new Rectangle(20, 88, 560, 368)) };

        var done = DialogButton(T("Xong", "Done"), primary: true);
        done.Bounds = S(new Rectangle(496, 472, 84, 34));
        done.Click += (_, _) => Close(); // cửa sổ không modal: DialogResult không tự đóng
        CancelButton = done; // Enter trong ô ghi chú là xuống dòng; Esc / Xong / nút X đều đóng và lưu
        Controls.AddRange(new Control[] { title, hint, _note, done });
        ClientSize = S(new Size(600, 524));
        Shown += (_, _) =>
        {
            _note.Box.Focus();
            _note.Box.SelectionStart = _note.Box.TextLength;
        };
    }
}
