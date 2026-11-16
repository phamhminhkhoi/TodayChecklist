using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace TodayChecklist;

/// <summary>Dòng đã lưu DB. Node chưa lưu (đang gõ tên) thì Tag = null.</summary>
sealed class Row(long id, bool done, string? url)
{
    public long Id { get; } = id;
    public bool Done { get; set; } = done;
    public string? Url { get; set; } = url; // link gắn trực tiếp; link suy ra từ note cha không lưu ở đây
    public string? Status { get; set; }     // trạng thái GitHub lấy về lần gần nhất
    public string? StatusUrl { get; set; }  // link lúc lấy status đó
    public string? Title { get; set; }      // tên issue / PR lấy cùng lúc với status
    public string? Checklist { get; set; }  // link comment có coding checklist, lấy cùng lúc với status
    public bool HasNote { get; set; }       // có ghi chú
    public int Files { get; set; }          // số tệp / thư mục đính kèm
    public long? GroupId { get; set; }      // nhóm task liên kết: cùng nhóm thì tô cùng màu
    public int HiddenDone { get; set; }     // số việc con đã xong đang bị ẩn khỏi list (vẫn tính vào bộ đếm)
}

/// <summary>
/// TreeView tự vẽ toàn bộ: chevron, checkbox tròn, bộ đếm việc con, nút link / + / xóa khi rê chuột.
/// Chuột trái do control tự xử lý (không qua TreeView gốc) để làm kéo thả và bấm vào chữ link.
/// </summary>
sealed class TodoTree : TreeView
{
    const int MK_SHIFT = 0x0004, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONDOWN = 0x0204;
    const int WM_SIZE = 0x0005, WM_PAINT = 0x000F, WM_STYLECHANGED = 0x007D, WM_VSCROLL = 0x0115, WM_MOUSEWHEEL = 0x020A;
    const int GWL_STYLE = -16, WS_VSCROLL = 0x00200000, SB_VERT = 1, SIF_ALL = 0x17;
    const int TVS_NOTOOLTIPS = 0x80, TVS_NOHSCROLL = 0x8000, TVM_SETEXTENDEDSTYLE = 0x112C, TVS_EX_DOUBLEBUFFER = 0x4;
    static readonly string ChevronRight = Theme.Glyph(0xE970), ChevronDown = Theme.Glyph(0xE96E),
        AddIcon = Theme.Glyph(0xE710), DeleteIcon = Theme.Glyph(0xE74D), LinkIcon = Theme.Glyph(0xE71B),
        NoteIcon = Theme.Glyph(0xE70B), AttachIcon = Theme.Glyph(0xE723), ChecklistIcon = Theme.Glyph(0xF0E3);

    enum Part { None, Chevron, Check, Text, LinkText, LinkButton, Add, Delete, Details, Checklist }
    enum DropMode { Top, Bottom, Box }

    readonly record struct Parts(Rectangle Row, Rectangle Chevron, Rectangle Check, Rectangle Text, Rectangle Counter, Rectangle Badge,
        Rectangle Checklist, Rectangle Link, Rectangle Add, Rectangle Delete, Rectangle Details, Rectangle Info);

    /// <summary>Chỗ thả khi kéo: chèn vào Parent tại Index (tính trước khi gỡ node ra); vẽ gợi ý trên dòng Row.</summary>
    readonly record struct Drop(TreeNode? Parent, int Index, TreeNode Row, DropMode Mode, int Level);

    readonly System.Windows.Forms.Timer _scrollTimer = new() { Interval = 60 };
    readonly ThemeTip _tip = new();
    readonly System.Windows.Forms.Timer _tipTimer = new() { Interval = 500 };
    TreeNode? _tipNode; // dòng đang chờ / đang hiện tooltip chữ đầy đủ
    bool _tipChecklist; // tooltip đó là của icon coding checklist, không phải chữ đầy đủ
    TreeNode? _hover, _editing, _press, _drag;
    Part _hoverPart, _pressPart;
    Point _pressAt, _dragAt;
    Drop? _drop;
    Font _rootFont = null!, _doneFont = null!, _rootDoneFont = null!, _smallFont = null!, _iconFont = null!, _badgeFont = null!;

    /// <summary>Link của 1 node (gắn trực tiếp hoặc suy ra từ note cha); null = không có link.</summary>
    public Func<TreeNode, string?> LinkOf = _ => null;

    /// <summary>Trạng thái GitHub còn hợp lệ của 1 node (open / merged…); null = không hiện badge.</summary>
    public Func<TreeNode, string?> StatusOf = _ => null;

    /// <summary>Tên issue hiện bằng chữ xám ngay sau tên việc (việc chỉ ghi "#18"); null = không hiện.</summary>
    public Func<TreeNode, string?> TitleOf = _ => null;

    /// <summary>Link coding checklist của issue; có thì hiện icon trước status, bấm để mở. null = chưa có checklist.</summary>
    public Func<TreeNode, string?> ChecklistOf = _ => null;

    /// <summary>Nhóm liên kết của 1 node (id + chỉ số màu); null = không thuộc nhóm nào.</summary>
    public Func<TreeNode, (long Id, int Color)?> GroupOf = _ => null;

    public event Action<TreeNode>? CheckClicked, AddChildClicked, DeleteClicked, EditRequested, LinkClicked, LinkEditClicked, DetailsClicked, ChecklistClicked;

    /// <summary>Shift + bấm tên việc có link: mở link của cả nhóm liên kết.</summary>
    public event Action<TreeNode>? GroupLinkClicked;
    /// <summary>Kéo thả xong: (node, cha mới, vị trí sau khi đã gỡ node khỏi chỗ cũ).</summary>
    public event Action<TreeNode, TreeNode?, int>? NodeDropped;

    /// <summary>Thanh cuộn có thể đã đổi (cuộn, thêm/bớt dòng, đổi cỡ) → cập nhật thanh cuộn tự vẽ.</summary>
    public event Action? ScrollChanged;

    [StructLayout(LayoutKind.Sequential)]
    struct SCROLLINFO { public int cbSize, fMask, nMin, nMax, nPage, nPos, nTrackPos; }

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool GetScrollInfo(IntPtr hWnd, int bar, ref SCROLLINFO info);

    [DllImport("user32.dll")]
    static extern int GetWindowLong(IntPtr hWnd, int index);

    public TodoTree()
    {
        DrawMode = TreeViewDrawMode.OwnerDrawAll;
        ShowLines = ShowPlusMinus = ShowRootLines = false;
        FullRowSelect = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        MakeFonts();
        _scrollTimer.Tick += (_, _) => AutoScrollWhileDragging();
        _tipTimer.Tick += (_, _) => ShowFullText();
    }

    /// <summary>Node đang sửa bằng ô TextBox phủ lên: chỉ vẽ nền + gạch chân, không vẽ chữ.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public TreeNode? EditingNode
    {
        get => _editing;
        set
        {
            _editing = value;
            HideTip();
            Invalidate();
        }
    }

    public Font FontFor(TreeNode node) => node.Level == 0 ? _rootFont : Font;

    public Rectangle TextRect(TreeNode node) => PartsOf(node, node.Bounds.Y).Text;

    /// <summary>Thanh cuộn gốc: có hiện không, dòng đầu đang thấy, số dòng vừa khung, tổng số dòng đang mở.</summary>
    public (bool Native, int Pos, int Page, int Total) ScrollState()
    {
        if (!IsHandleCreated || (GetWindowLong(Handle, GWL_STYLE) & WS_VSCROLL) == 0) return (false, 0, 1, 0);
        var si = new SCROLLINFO { cbSize = Marshal.SizeOf<SCROLLINFO>(), fMask = SIF_ALL };
        return GetScrollInfo(Handle, SB_VERT, ref si) ? (true, si.nPos, Math.Max(1, si.nPage), si.nMax - si.nMin + 1) : (true, 0, 1, 0);
    }

    public void ScrollToRow(int row)
    {
        var node = Nodes.Count > 0 ? Nodes[0] : null;
        for (int i = 0; i < row && node?.NextVisibleNode is { } next; i++) node = next;
        if (node != null) TopNode = node;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= TVS_NOTOOLTIPS | TVS_NOHSCROLL;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SendMessage(Handle, TVM_SETEXTENDEDSTYLE, TVS_EX_DOUBLEBUFFER, TVS_EX_DOUBLEBUFFER);
    }

    // Đổi dòng chọn → vẽ lại để viền các task cùng nhóm với dòng mới chọn
    protected override void OnAfterSelect(TreeViewEventArgs e)
    {
        base.OnAfterSelect(e);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        MakeFonts();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scrollTimer.Dispose();
            _tipTimer.Dispose();
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }

    void MakeFonts()
    {
        _rootFont = new Font(Theme.SansStrong, Font.Size);
        _doneFont = new Font(Font, FontStyle.Strikeout);
        _rootDoneFont = new Font(_rootFont, FontStyle.Strikeout);
        _smallFont = new Font(Theme.Sans, Font.Size - 1.5f * Theme.Scale);
        _iconFont = new Font(Theme.Icons, Font.Size - 2f * Theme.Scale);
        _badgeFont = new Font(Theme.SansSmall, Font.Size - 2f * Theme.Scale);
    }

    // Chấm màu theo nghĩa trạng thái của GitHub, dịu lại cho hợp nền giấy (bản sáng / bản tối).
    // Status trong GitHub Project dùng màu của option đó (Backlog xám, Todo xanh lá, In progress vàng…)
    static (string Label, Color Color) Badge(string status)
    {
        Color green = Theme.Dark ? Color.FromArgb(120, 184, 128) : Color.FromArgb(52, 128, 76),
            gray = Theme.Dark ? Color.FromArgb(128, 122, 113) : Color.FromArgb(170, 164, 154),
            purple = Theme.Dark ? Color.FromArgb(166, 140, 222) : Color.FromArgb(124, 92, 184),
            red = Theme.Dark ? Color.FromArgb(226, 116, 100) : Color.FromArgb(186, 66, 50);
        if (status.StartsWith(GitHub.ProjectPrefix) && status.Split(':', 3) is [_, var hue, var name])
            return (name, hue switch
            {
                "GREEN" => green,
                "PURPLE" => purple,
                "RED" => red,
                "YELLOW" => Theme.Dark ? Color.FromArgb(222, 184, 92) : Color.FromArgb(178, 128, 16),
                "ORANGE" => Theme.Dark ? Color.FromArgb(232, 150, 92) : Color.FromArgb(196, 102, 30),
                "BLUE" => Theme.Dark ? Color.FromArgb(112, 160, 232) : Color.FromArgb(48, 108, 196),
                "PINK" => Theme.Dark ? Color.FromArgb(230, 128, 180) : Color.FromArgb(188, 64, 128),
                _ => gray,
            });
        return status switch
        {
            "open" => ("Open", green),
            "draft" => ("Draft", gray),
            "merged" => ("Merged", purple),
            "pr_closed" => ("Closed", red),
            "closed" => ("Closed", purple),
            "not_planned" => ("Not planned", gray),
            _ => (status, Theme.Muted),
        };
    }

    // --- Bố cục 1 dòng ---

    Parts PartsOf(TreeNode node, int y)
    {
        int h = ItemHeight, w = ClientSize.Width, box = S(17), btn = S(30);
        int x = S(6) + node.Level * S(24);
        var chevron = new Rectangle(x, y, S(22), h);
        var check = new Rectangle(chevron.Right, y + (h - box) / 2, box, box);
        var delete = new Rectangle(w - S(6) - btn, y, btn, h);
        var add = new Rectangle(delete.X - btn, y, btn, h);
        var link = new Rectangle(add.X - btn, y, btn, h);
        var details = new Rectangle(link.X - btn, y, btn, h);
        bool actions = ActionsVisible(node);
        // việc có ghi chú / tệp đính kèm: icon nhỏ ở mép phải lúc không rê chuột (rê chuột thì thành nút trong hàng nút)
        var info = !actions && HasDetails(node) ? new Rectangle(w - S(6) - S(26), y, S(26), h) : new Rectangle(w, y, 0, h);
        // chỉ chừa chỗ cho các nút khi chúng đang hiện, để chữ dài có thêm chỗ
        int right = actions ? details.X : info.Width > 0 ? info.X - S(2) : w - S(10);
        var counter = Kids(node).Total > 0 ? new Rectangle(right - S(40), y, S(40), h) : new Rectangle(right, y, 0, h);
        var badge = new Rectangle(counter.X, y, 0, h);
        if (StatusOf(node) is { } status)
        {
            // chấm + chữ, không có nền
            int bw = DotSize + DotGap + TextRenderer.MeasureText(Badge(status).Label, _badgeFont, Size.Empty, TextFormatFlags.NoPadding).Width;
            badge = new Rectangle(counter.X - bw - S(12), y, bw, h);
        }
        // issue đã có coding checklist: icon ngay trước status
        var checklist = ChecklistOf(node) != null ? new Rectangle(badge.X - S(26), y, S(20), h) : new Rectangle(badge.X, y, 0, h);
        int tx = check.Right + S(10);
        return new(new Rectangle(0, y, w, h), chevron, check, new Rectangle(tx, y, Math.Max(0, checklist.X - tx - S(4)), h), counter, badge,
            checklist, link, add, delete, details, info);
    }

    int DotSize => S(7);
    int DotGap => S(6);

    // Việc con xong / tổng, tính cả việc con đã xong đang bị ẩn
    static (int Done, int Total) Kids(TreeNode node)
    {
        int hidden = (node.Tag as Row)?.HiddenDone ?? 0;
        return (node.Nodes.Cast<TreeNode>().Count(n => n.Tag is Row { Done: true }) + hidden, node.Nodes.Count + hidden);
    }

    static bool HasDetails(TreeNode node) => node.Tag is Row { HasNote: true } or Row { Files: > 0 };

    bool ActionsVisible(TreeNode node) =>
        _drag == null && node.Tag != null && node != _editing && (node == _hover || node == SelectedNode);

    Rectangle TextBounds(TreeNode node, Parts p)
    {
        int w = TextRenderer.MeasureText(node.Text, FontFor(node), new Size(int.MaxValue, p.Text.Height),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        return new Rectangle(p.Text.X, p.Row.Y + S(4), Math.Min(w, p.Text.Width), p.Row.Height - S(8));
    }

    // Vùng bấm mở link: tên việc, cộng tên issue nếu đang hiện
    Rectangle LinkArea(TreeNode node, Parts p)
    {
        var t = TextBounds(node, p);
        if (TitleOf(node) is not { } title) return t;
        int right = t.Right + S(8) + TextRenderer.MeasureText(title, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        return t with { Width = Math.Min(p.Text.Right, right) - t.X };
    }

    Part HitPart(TreeNode node, Point pt)
    {
        var p = PartsOf(node, node.Bounds.Y);
        if (node.Nodes.Count > 0 && p.Chevron.Contains(pt)) return Part.Chevron;
        if (node.Tag == null) return Part.None;
        if (Rectangle.Inflate(p.Check, S(5), S(5)).Contains(pt)) return Part.Check;
        if (ActionsVisible(node))
        {
            if (p.Details.Contains(pt)) return Part.Details;
            if (p.Link.Contains(pt)) return Part.LinkButton;
            if (p.Add.Contains(pt)) return Part.Add;
            if (p.Delete.Contains(pt)) return Part.Delete;
        }
        else if (p.Info.Contains(pt)) return Part.Details;
        if (p.Checklist.Contains(pt)) return Part.Checklist;
        if (LinkOf(node) != null && LinkArea(node, p).Contains(pt)) return Part.LinkText;
        return p.Text.Contains(pt) ? Part.Text : Part.None;
    }

    // --- Chuột ---

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_LBUTTONDOWN or WM_LBUTTONDBLCLK when GetNodeAt(PointOf(m)) != null:
                OnLeftDown(PointOf(m), m.Msg == WM_LBUTTONDBLCLK);
                return;
            case WM_LBUTTONUP when _press != null:
                OnLeftUp(PointOf(m), shift: ((long)m.WParam & MK_SHIFT) != 0);
                return;
            case WM_RBUTTONDOWN when GetNodeAt(PointOf(m)) is { Tag: not null } node:
                Focus();
                if (node.TreeView == this) SelectedNode = node; // chuột phải cũng chọn dòng để menu áp đúng dòng
                break;
        }
        base.WndProc(ref m);
        if (m.Msg is WM_PAINT or WM_SIZE or WM_VSCROLL or WM_MOUSEWHEEL or WM_STYLECHANGED) ScrollChanged?.Invoke();
        if (m.Msg is WM_VSCROLL or WM_MOUSEWHEEL)
        {
            HideTip(); // các dòng đã trượt đi, tooltip không còn đúng chỗ
            _tipNode = null;
        }
    }

    static Point PointOf(Message m)
    {
        long lp = m.LParam.ToInt64();
        return new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
    }

    void OnLeftDown(Point pt, bool doubleClick)
    {
        HideTip();
        Focus(); // ô sửa tên (nếu đang mở) mất focus → lưu lại; node nháp rỗng có thể bị gỡ nên tìm lại node
        if (GetNodeAt(pt) is not { } node) return;
        var part = HitPart(node, pt);
        SelectedNode = node;

        if (doubleClick && part == Part.Text)
        {
            EditRequested?.Invoke(node);
            return;
        }
        Action<TreeNode>? handler = part switch
        {
            Part.Chevron => n => n.Toggle(),
            Part.Check => CheckClicked,
            Part.LinkButton => LinkEditClicked,
            Part.Details => DetailsClicked,
            Part.Checklist => ChecklistClicked,
            Part.Add => AddChildClicked,
            Part.Delete => DeleteClicked,
            _ => null,
        };
        if (handler != null)
        {
            handler(node);
            return;
        }
        if (doubleClick) return;

        // Chữ hoặc chỗ trống: chờ xem là click (mở link) hay kéo
        _press = node;
        _pressAt = pt;
        _pressPart = part;
        Capture = true;
    }

    void OnLeftUp(Point pt, bool shift)
    {
        var (press, part, drag, drop) = (_press, _pressPart, _drag, _drop);
        EndPress();
        if (drag != null)
        {
            if (drop is { } d)
                NodeDropped?.Invoke(drag, d.Parent, d.Parent == drag.Parent && d.Index > drag.Index ? d.Index - 1 : d.Index);
        }
        else if (press != null && part == Part.LinkText && GetNodeAt(pt) == press && HitPart(press, pt) == Part.LinkText)
            (shift ? GroupLinkClicked : LinkClicked)?.Invoke(press);
    }

    void EndPress()
    {
        var (drag, drop) = (_drag, _drop);
        _press = _drag = null;
        _drop = null;
        _scrollTimer.Stop();
        Capture = false;
        Cursor = Cursors.Default;
        InvalidateRow(drag);
        InvalidateRow(drop?.Row);
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture && _press != null) EndPress(); // mất capture giữa chừng (alt-tab…) → hủy kéo
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_drag != null && e.KeyCode == Keys.Escape)
        {
            EndPress();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_press != null && _drag == null && _editing == null &&
            (Math.Abs(e.X - _pressAt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - _pressAt.Y) > SystemInformation.DragSize.Height))
        {
            _drag = _press;
            HideTip();
            InvalidateRow(_hover);
            _hover = null;
            _scrollTimer.Start();
        }
        if (_drag != null)
        {
            _dragAt = e.Location;
            UpdateDrop(e.Location);
            return;
        }

        var old = _hover;
        _hover = GetNodeAt(e.Location);
        var part = _hover == null ? Part.None : HitPart(_hover, e.Location);
        if (_hover != _tipNode || (part == Part.Checklist) != _tipChecklist) RestartTip(part == Part.Checklist);
        if (_hover == old && part == _hoverPart) return;
        _hoverPart = part;
        InvalidateRow(old);
        InvalidateRow(_hover);
        Cursor = part is Part.Chevron or Part.Check or Part.LinkText or Part.LinkButton or Part.Add or Part.Delete or Part.Details or Part.Checklist
            ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_drag != null) return;
        HideTip();
        _tipNode = null;
        InvalidateRow(_hover);
        _hover = null;
        _hoverPart = Part.None;
    }

    // --- Tooltip chữ đầy đủ: rê chuột lên dòng có tên việc / tên issue bị cắt "…"; rê lên icon checklist thì giải thích icon ---

    void RestartTip(bool checklist = false)
    {
        HideTip();
        _tipNode = _hover;
        _tipChecklist = checklist;
        if (_tipNode != null) _tipTimer.Start();
    }

    void HideTip()
    {
        _tipTimer.Stop();
        _tip.Hide(this);
    }

    void ShowFullText()
    {
        _tipTimer.Stop();
        if (_tipNode is not { } node || node != _hover || node.TreeView != this || node == _editing || _drag != null) return;
        if (_tipChecklist)
        {
            // canh mép phải theo icon (icon nằm sát bên phải list) để tooltip không lòi ra ngoài cửa sổ
            var icon = PartsOf(node, node.Bounds.Y).Checklist;
            string hint = T("Đã có coding checklist · bấm để mở", "Has a coding checklist · click to open");
            _tip.MaxWidth = 0;
            _tip.Show(hint, this, Math.Max(S(4), icon.Right - _tip.SizeFor(this, hint).Width), node.Bounds.Bottom + S(2));
            return;
        }
        if (FullText(node) is not { } text) return;
        int x = PartsOf(node, node.Bounds.Y).Text.X - S(8);
        _tip.MaxWidth = ClientSize.Width - x - S(6); // gọn trong khung list, không lòi ra ngoài cửa sổ
        _tip.Show(text, this, x, node.Bounds.Bottom + S(2));
    }

    /// <summary>Tên việc (kèm tên issue) đầy đủ nếu đang bị cắt "…"; null = đang hiện đủ, không cần tooltip.</summary>
    string? FullText(TreeNode node)
    {
        const TextFormatFlags measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var p = PartsOf(node, node.Bounds.Y);
        bool cut = TextRenderer.MeasureText(node.Text, FontFor(node), Size.Empty, measure).Width > p.Text.Width;
        var title = TitleOf(node);
        if (title != null)
            cut |= TextRenderer.MeasureText(title, Font, Size.Empty, measure).Width > p.Text.Right - (TextBounds(node, p).Right + S(8));
        return !cut ? null : title != null ? $"{node.Text}  {title}" : node.Text;
    }

    void InvalidateRow(TreeNode? node)
    {
        if (node?.TreeView == this) Invalidate(new Rectangle(0, node.Bounds.Y, ClientSize.Width, ItemHeight));
    }

    // --- Kéo thả ---

    void UpdateDrop(Point pt)
    {
        var drop = DropAt(pt);
        if (drop == _drop) return;
        InvalidateRow(_drop?.Row);
        _drop = drop;
        InvalidateRow(drop?.Row);
        Cursor = drop == null ? Cursors.No : Cursors.Default;
    }

    /// <summary>Mép trên dòng → chèn trước; mép dưới → chèn sau; giữa dòng → thành việc con; dưới cùng → cuối danh sách.</summary>
    Drop? DropAt(Point pt)
    {
        if (_drag == null || Nodes.Count == 0) return null;
        pt.X = Math.Clamp(pt.X, 1, Math.Max(1, ClientSize.Width - 2));
        var target = GetNodeAt(pt);
        if (target == null)
        {
            if (pt.Y < 0) return null;
            var last = Nodes[^1];
            while (last.IsExpanded && last.Nodes.Count > 0) last = last.Nodes[^1];
            return new Drop(null, Nodes.Count, last, DropMode.Bottom, 0);
        }
        if (target.Tag == null || IsWithin(target, _drag)) return null;

        int y = pt.Y - target.Bounds.Y, h = ItemHeight;
        if (y < h * 3 / 10) return new Drop(target.Parent, target.Index, target, DropMode.Top, target.Level);
        if (y > h * 7 / 10)
            return target.IsExpanded && target.Nodes.Count > 0
                ? new Drop(target, 0, target, DropMode.Bottom, target.Level + 1) // ngay dưới note đang mở = việc con đầu tiên
                : new Drop(target.Parent, target.Index + 1, target, DropMode.Bottom, target.Level);
        return new Drop(target, target.Nodes.Count, target, DropMode.Box, target.Level + 1);
    }

    static bool IsWithin(TreeNode node, TreeNode ancestor)
    {
        for (var n = node; n != null; n = n.Parent)
            if (n == ancestor) return true;
        return false;
    }

    // Giữ chuột sát mép trên/dưới (hoặc ra ngoài) khi kéo → cuộn dần; dùng vị trí chuột cuối cùng control nhận được
    void AutoScrollWhileDragging()
    {
        var pt = _dragAt;
        int edge = ItemHeight / 2;
        if (TopNode is not { } top) return;
        if (pt.Y < edge && top.PrevVisibleNode is { } prev) TopNode = prev;
        else if (pt.Y > ClientSize.Height - edge && top.NextVisibleNode is { } next) TopNode = next;
        else return;
        Invalidate();
        UpdateDrop(pt);
    }

    // --- Vẽ ---

    const TextFormatFlags TextFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    protected override void OnDrawNode(DrawTreeNodeEventArgs e)
    {
        var node = e.Node;
        if (node == null || e.Bounds.Height == 0) return;

        var g = e.Graphics;
        var p = PartsOf(node, e.Bounds.Y);
        bool editing = node == _editing;
        bool selected = editing || (e.State & TreeNodeStates.Selected) != 0;
        bool hover = node == _hover;
        bool done = node.Tag is Row { Done: true };
        bool link = LinkOf(node) != null;

        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, p.Row);
        var rowRect = new RectangleF(S(2), p.Row.Y + S(1), p.Row.Width - S(4), p.Row.Height - S(2));
        if (selected || hover) Theme.FillRound(g, selected ? Theme.Selected : Theme.Hover, rowRect, Theme.Corner(this, rowRect.Height, 5));

        // Dòng đang chọn: vạch màu nhấn bên trái, kiểu danh sách Windows 11
        if (selected)
        {
            // hàng bo tròn: vạch ngắn hơn, lùi vào trong để nằm gọn trong phần nền cong
            bool pill = Theme.Shape == ButtonShape.Pill;
            float bar = S(pill ? 12 : 16), inset = S(pill ? 3 : 1);
            Theme.FillRound(g, Theme.Accent, new RectangleF(rowRect.X + inset, p.Row.Y + (p.Row.Height - bar) / 2, S(3), bar), Theme.Corner(this, S(3), 1.5f));
        }

        if (node.Nodes.Count > 0)
            DrawIcon(g, node.IsExpanded ? ChevronDown : ChevronRight, p.Chevron, hover && _hoverPart == Part.Chevron ? Theme.Text : Theme.Muted);

        DrawCheck(g, p.Check, done, hover && _hoverPart == Part.Check);

        if (editing)
        {
            using var pen = new Pen(Theme.Accent, S(2));
            int y = p.Row.Bottom - S(7);
            g.DrawLine(pen, p.Text.X, y, p.Text.Right, y);
        }
        else
        {
            var font = done ? (node.Level == 0 ? _rootDoneFont : _doneFont) : FontFor(node);
            if (GroupOf(node) is { } group) DrawGroup(g, node, p, group);
            bool hotLink = link && hover && _hoverPart == Part.LinkText;
            var color = done || node == _drag ? Theme.Muted : hotLink ? Theme.Accent : Theme.Text;
            TextRenderer.DrawText(g, node.Text, font, p.Text, color, TextFlags);
            // Link: bình thường trông như chữ thường; rê chuột thì chữ chuyển màu nhấn và có gạch chân
            if (hotLink && !done)
            {
                var t = TextBounds(node, p);
                int y = Baseline(font, p.Text) + S(3);
                using var pen = new Pen(Theme.Accent);
                g.DrawLine(pen, t.X, y, t.Right - 1, y);
            }
            // Việc chỉ ghi "#18": hiện thêm tên issue bằng chữ xám ngay sau
            if (TitleOf(node) is { } title)
            {
                int x = TextBounds(node, p).Right + S(8);
                if (p.Text.Right - x > S(24))
                    TextRenderer.DrawText(g, title, Font, new Rectangle(x, p.Row.Y, p.Text.Right - x, p.Row.Height), Theme.Muted, TextFlags);
            }
        }

        if (Kids(node) is { Total: > 0 } kids)
        {
            TextRenderer.DrawText(g, $"{kids.Done}/{kids.Total}", _smallFont, p.Counter, Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }

        // Trạng thái GitHub: chấm màu + chữ xám, không tô nền
        if (StatusOf(node) is { } status && p.Badge.Width > 0)
        {
            var (label, color) = Badge(status);
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var dot = new SolidBrush(color))
                g.FillEllipse(dot, p.Badge.X, p.Badge.Y + (p.Badge.Height - DotSize) / 2f, DotSize, DotSize);
            g.SmoothingMode = old;
            var text = p.Badge with { X = p.Badge.X + DotSize + DotGap, Width = p.Badge.Width - DotSize - DotGap };
            TextRenderer.DrawText(g, label, _badgeFont, text, Theme.Muted, TextFlags);
        }
        if (p.Checklist.Width > 0)
            DrawIcon(g, ChecklistIcon, p.Checklist, hover && _hoverPart == Part.Checklist ? Theme.AccentHover : Theme.Accent);

        if (ActionsVisible(node))
        {
            bool hasUrl = node.Tag is Row { Url: not null };
            DrawIcon(g, NoteIcon, p.Details, HasDetails(node) || (hover && _hoverPart == Part.Details) ? Theme.Text : Theme.Muted);
            DrawIcon(g, LinkIcon, p.Link, hasUrl || (hover && _hoverPart == Part.LinkButton) ? Theme.Text : Theme.Muted);
            DrawIcon(g, AddIcon, p.Add, hover && _hoverPart == Part.Add ? Theme.Text : Theme.Muted);
            DrawIcon(g, DeleteIcon, p.Delete, hover && _hoverPart == Part.Delete ? Theme.Danger : Theme.Muted);
        }
        else if (p.Info.Width > 0)
            DrawIcon(g, node.Tag is Row { Files: > 0 } ? AttachIcon : NoteIcon, p.Info, hover && _hoverPart == Part.Details ? Theme.Text : Theme.Muted);

        if (_drop is { } drop && drop.Row == node) DrawDropHint(g, drop, p, rowRect);
    }

    // Task liên kết: tô nền sau chữ bằng màu nhóm như bút highlight;
    // task cùng nhóm với dòng đang chọn có thêm viền đậm để dễ thấy nhau trong list dài
    void DrawGroup(Graphics g, TreeNode node, Parts p, (long Id, int Color) group)
    {
        var t = TextBounds(node, p);
        float h = Math.Min(p.Row.Height - S(8), S(24));
        var chip = new RectangleF(t.X - S(6), p.Row.Y + (p.Row.Height - h) / 2, t.Width + S(12), h);
        float radius = Theme.Corner(this, h, 5);
        Theme.FillRound(g, Theme.GroupTint(group.Color), chip, radius);
        if (node != SelectedNode && SelectedNode is { } selected && GroupOf(selected)?.Id == group.Id)
            Theme.DrawRound(g, Theme.GroupHue(group.Color), Sf(1.5f), RectangleF.Inflate(chip, -Sf(0.75f), -Sf(0.75f)), radius);
    }

    // Chân chữ khi vẽ căn giữa dọc (TextRenderer căn theo ascent + descent của font)
    int Baseline(Font font, Rectangle box)
    {
        var family = font.FontFamily;
        float em = font.SizeInPoints * DeviceDpi / 72f, units = family.GetEmHeight(FontStyle.Regular);
        float ascent = em * family.GetCellAscent(FontStyle.Regular) / units, descent = em * family.GetCellDescent(FontStyle.Regular) / units;
        return (int)Math.Round(box.Y + (box.Height - ascent - descent) / 2 + ascent);
    }

    void DrawDropHint(Graphics g, Drop drop, Parts p, RectangleF rowRect)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(Theme.Accent, Sf(2));
        if (drop.Mode == DropMode.Box)
        {
            using var path = Theme.Round(RectangleF.Inflate(rowRect, -1, -1), Theme.Corner(this, rowRect.Height, 5));
            g.DrawPath(pen, path);
        }
        else
        {
            int y = drop.Mode == DropMode.Top ? p.Row.Y + S(1) : p.Row.Bottom - S(2);
            int x = S(6) + drop.Level * S(24) + S(22);
            int r = S(3);
            g.DrawLine(pen, x + r, y, p.Row.Right - S(8), y);
            g.DrawEllipse(pen, x - r, y - r, r * 2, r * 2);
        }
        g.SmoothingMode = old;
    }

    void DrawIcon(Graphics g, string glyph, Rectangle r, Color color) =>
        TextRenderer.DrawText(g, glyph, _iconFont, r, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

    // Ô tick theo kiểu nút (vuông / bo góc / tròn); việc xong lùi xuống màu xám như gạch trên giấy
    void DrawCheck(Graphics g, Rectangle r, bool done, bool hot)
    {
        var rf = new RectangleF(r.X + 0.75f, r.Y + 0.75f, r.Width - 1.5f, r.Height - 1.5f);
        float radius = Theme.Corner(this, rf.Height, 4.5f); // Tròn = ô tick hình tròn
        PointF[] tick = [P(0.26f, 0.53f), P(0.43f, 0.69f), P(0.75f, 0.34f)];

        if (done) Theme.FillRound(g, hot ? Theme.Text : Theme.Muted, rf, radius);
        else Theme.DrawRound(g, hot ? Theme.Text : Theme.Border, Sf(1.5f), rf, radius);
        if (done || hot)
        {
            // chưa xong mà rê chuột vào thì hiện mờ dấu tick để biết bấm vào là xong
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(done ? Theme.Bg : Theme.Muted, Sf(1.7f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, tick);
            g.SmoothingMode = old;
        }

        PointF P(float fx, float fy) => new(rf.X + rf.Width * fx, rf.Y + rf.Height * fy);
    }

    int S(int value) => Theme.Px(this, value);
    float Sf(float value) => value * Theme.Scale * DeviceDpi / 96f;
}
