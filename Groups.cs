using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace TodayChecklist;

/// <summary>
/// Liên kết task: tick các task cùng thuộc 1 việc lớn với task này (vd 2 issue ở 2 repo) và chọn màu chung.
/// Chỉ lưu trong DB trên máy này, không tạo hay sửa gì trên GitHub.
/// </summary>
sealed class GroupDialog : DialogForm
{
    readonly TaskPicker _picker;
    int _color;

    public IReadOnlyCollection<TreeNode> Linked => _picker.Items.Where(i => i.Checked).Select(i => i.Node).ToList();
    public int ColorIndex => _color;

    public GroupDialog(string taskText, List<TaskPicker.Item> items, int color, bool linked)
    {
        Text = T("Liên kết task", "Link tasks");
        _color = color;
        int rows = Math.Clamp(items.Count, 3, 9), listTop = 78, listHeight = rows * 34;

        var title = new Label
        {
            Text = T($"Liên kết “{taskText}”", $"Link “{taskText}”"), AutoEllipsis = true, UseMnemonic = false, ForeColor = Theme.Text,
            Font = Theme.Heading(12.5f), Bounds = S(new Rectangle(20, 14, 480, 30)),
        };
        var hint = new Label
        {
            Text = T("Tick các task cùng thuộc 1 việc lớn với task này. Task cùng nhóm được tô cùng màu.",
                     "Tick the tasks that belong to the same bigger issue. Linked tasks share a color."),
            AutoEllipsis = true, ForeColor = Theme.Muted, Font = Theme.Ui(9f), Bounds = S(new Rectangle(20, 46, 480, 20)),
        };

        _picker = new TaskPicker(items, color) { Bounds = S(new Rectangle(16, listTop, 478, listHeight)) };
        var scroll = new ScrollStrip { Bounds = S(new Rectangle(496, listTop, 8, listHeight)) };
        _picker.ScrollChanged += () => scroll.SetRange(_picker.FirstRow, _picker.Page, _picker.Count);
        scroll.Scrolled += _picker.ScrollTo;

        int y = listTop + listHeight + 14;
        var colorLabel = new PlainLabel { Text = T("Màu", "Color"), ForeColor = Theme.Text, Font = Theme.Ui(9.5f), Bounds = S(new Rectangle(20, y, 70, 30)) };
        var colors = new GroupColorPicker(color) { Bounds = S(new Rectangle(90, y, 320, 30)) };
        colors.Picked += c =>
        {
            _color = c;
            _picker.PreviewColor = c;
        };

        y += 52;
        var save = DialogButton(T("Lưu", "Save"), primary: true);
        var cancel = DialogButton(T("Hủy", "Cancel"), primary: false);
        save.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        save.Bounds = S(new Rectangle(416, y, 84, 34));
        cancel.Bounds = S(new Rectangle(324, y, 84, 34));
        AcceptButton = save;
        CancelButton = cancel;
        Controls.AddRange(new Control[] { title, hint, _picker, scroll, colorLabel, colors, cancel, save });

        if (linked)
        {
            // bỏ hết tick rồi lưu = đưa task này ra khỏi nhóm (các task còn lại vẫn liên kết với nhau)
            var unlink = DialogButton(T("Bỏ liên kết", "Unlink"), primary: false);
            unlink.Bounds = S(new Rectangle(20, y, 116, 34));
            unlink.DialogResult = DialogResult.OK;
            unlink.Click += (_, _) => _picker.UncheckAll();
            Controls.Add(unlink);
        }

        ClientSize = S(new Size(520, y + 34 + 18));
        Shown += (_, _) => scroll.SetRange(_picker.FirstRow, _picker.Page, _picker.Count);
    }
}

/// <summary>Danh sách task có ô tick để chọn nhiều, theo thứ tự cây (thụt lề theo cấp); cuộn bằng con lăn chuột.</summary>
sealed class TaskPicker : Control
{
    public sealed class Item(TreeNode node, string text, string? detail, int level, int? group)
    {
        public TreeNode Node { get; } = node;
        public string Text { get; } = text;
        public string? Detail { get; } = detail; // tên issue, chữ xám
        public int Level { get; } = level;
        public int? Group { get; } = group;      // màu nhóm task đang thuộc (nếu có)
        public bool Checked { get; set; }
    }

    const TextFormatFlags Flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
        TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    readonly List<Item> _items;
    readonly Font _strong;
    int _top, _hover = -1, _preview;

    public event Action? ScrollChanged;

    public TaskPicker(List<Item> items, int previewColor)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        (_items, _preview) = (items, previewColor);
        Font = Theme.Ui(10f);
        _strong = Theme.Semibold(10f);
        Cursor = Cursors.Hand;
    }

    public IReadOnlyList<Item> Items => _items;
    public int FirstRow => _top;
    public int Count => _items.Count;
    public int Page => Math.Max(1, Height / RowHeight);
    int RowHeight => Theme.Px(this, 34);

    /// <summary>Màu đang chọn trong hộp thoại: task được tick tô thử bằng màu này.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int PreviewColor
    {
        get => _preview;
        set
        {
            _preview = value;
            Invalidate();
        }
    }

    public void ScrollTo(int top)
    {
        _top = Math.Clamp(top, 0, Math.Max(0, _items.Count - Page));
        Invalidate();
        ScrollChanged?.Invoke();
    }

    public void UncheckAll()
    {
        foreach (var item in _items) item.Checked = false;
        Invalidate();
    }

    int IndexAt(int y) => y / RowHeight + _top is var i && i < _items.Count ? i : -1;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, T("Ngày này chưa có task nào khác để liên kết.", "No other task on this day to link."), Font, ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        int h = RowHeight, box = Theme.Px(this, 16), chipH = Theme.Px(this, 22);
        float px = DeviceDpi / 96f;
        for (int i = _top; i < _items.Count && (i - _top) * h < Height; i++)
        {
            var item = _items[i];
            var row = new Rectangle(0, (i - _top) * h, Width, h);
            if (i == _hover) Theme.FillRound(g, Theme.Hover, new RectangleF(row.X, row.Y + 1, row.Width, row.Height - 2), Theme.Corner(this, h, 5));

            // ô tick
            int x = Theme.Px(this, 8) + item.Level * Theme.Px(this, 20);
            var check = new RectangleF(x + 0.75f, row.Y + (h - box) / 2f + 0.75f, box - 1.5f, box - 1.5f);
            float radius = Theme.Corner(this, box, 4);
            if (item.Checked)
            {
                Theme.FillRound(g, Theme.Text, check, radius);
                var old = g.SmoothingMode;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var pen = new Pen(Theme.Bg, 1.6f * px) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
                g.DrawLines(pen, new[] { P(0.26f, 0.53f), P(0.43f, 0.69f), P(0.75f, 0.34f) });
                g.SmoothingMode = old;
            }
            else Theme.DrawRound(g, Theme.Border, 1.5f * px, check, radius);

            // tên task, tô màu nhóm như ngoài list: task được tick tô thử màu đang chọn
            var font = item.Level == 0 ? _strong : Font;
            int tx = x + box + Theme.Px(this, 12), right = Width - Theme.Px(this, 8);
            int tw = Math.Min(TextRenderer.MeasureText(item.Text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width, right - tx);
            if ((item.Checked ? _preview : item.Group) is { } color)
                Theme.FillRound(g, Theme.GroupTint(color), new RectangleF(tx - Theme.Px(this, 6), row.Y + (h - chipH) / 2f, tw + Theme.Px(this, 12), chipH),
                    Theme.Corner(this, chipH, 5));
            TextRenderer.DrawText(g, item.Text, font, new Rectangle(tx, row.Y, right - tx, h), Theme.Text, Flags);
            int dx = tx + tw + Theme.Px(this, 8);
            if (item.Detail != null && right - dx > Theme.Px(this, 24))
                TextRenderer.DrawText(g, item.Detail, Font, new Rectangle(dx, row.Y, right - dx, h), Theme.Muted, Flags);

            PointF P(float fx, float fy) => new(check.X + check.Width * fx, check.Y + check.Height * fy);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Y);
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
        if (e.Button != MouseButtons.Left || IndexAt(e.Y) is not (>= 0 and var i)) return;
        _items[i].Checked = !_items[i].Checked;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ScrollTo(_top - Math.Sign(e.Delta) * 3);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _strong.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Hàng ô màu để chọn màu cho nhóm task liên kết.</summary>
sealed class GroupColorPicker : Control
{
    int _selected, _hover = -1;

    public event Action<int>? Picked;

    public GroupColorPicker(int selected)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _selected = selected;
        Cursor = Cursors.Hand;
    }

    int Pitch => Theme.Px(this, 34);
    int Dot => Theme.Px(this, 20);
    RectangleF Swatch(int i) => new(i * Pitch + Theme.Px(this, 5), (Height - Dot) / 2f, Dot, Dot);
    int IndexAt(int x) => x / Pitch is var i && i < Theme.GroupColorCount ? i : -1;

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        float ring = Theme.Px(this, 3) + 1;
        for (int i = 0; i < Theme.GroupColorCount; i++)
        {
            var r = Swatch(i);
            float radius = Theme.Corner(this, r.Height, 5);
            Theme.FillRound(g, Theme.GroupHue(i), r, radius);
            if (i == _selected || i == _hover)
                Theme.DrawRound(g, i == _selected ? Theme.Text : Theme.Border, i == _selected ? 1.75f : 1f,
                    RectangleF.Inflate(r, ring, ring), radius == 0 ? 0 : radius + ring);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.X);
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
        int i = IndexAt(e.X);
        if (e.Button != MouseButtons.Left || i < 0 || i == _selected) return;
        _selected = i;
        Invalidate();
        Picked?.Invoke(i);
    }
}
