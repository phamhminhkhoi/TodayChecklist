using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace TodayChecklist;

enum ButtonKind { Ghost, Primary, Secondary }

/// <summary>
/// Nút tự vẽ theo theme: hình dạng (Vuông / Bo góc / Tròn) và kiểu nút chính (Mực / Màu nhấn / Viền) lấy từ cài đặt.
/// Ghost = nút phẳng trên thanh công cụ, chỉ hiện nền khi rê chuột; Secondary = viền mảnh.
/// </summary>
class ThemedButton : Button
{
    readonly ButtonKind _kind;
    bool _hot, _down;

    public ThemedButton(ButtonKind kind)
    {
        _kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Theme.Bg;
        ForeColor = kind == ButtonKind.Ghost ? Theme.Muted : Theme.Text;
        Cursor = Cursors.Hand;
        TextImageRelation = TextImageRelation.ImageBeforeText;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = _down = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _down = e.Button == MouseButtons.Left;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _down = false;
        Invalidate();
    }

    (Color? Fill, Color? Border, Color Text) Look()
    {
        bool hot = _hot && Enabled;
        Color? wash = _down ? Theme.Press : hot ? Theme.Field : null;
        return _kind switch
        {
            ButtonKind.Primary => Theme.Primary switch
            {
                PrimaryStyle.Accent => (hot ? Theme.AccentHover : Theme.Accent, null, Theme.OnAccent),
                PrimaryStyle.Outline => (wash, Theme.Text, Theme.Text),
                _ => (hot ? Theme.PrimaryHover : Theme.Text, null, Theme.Bg),
            },
            ButtonKind.Secondary => (wash, Theme.Border, Theme.Text),
            _ => (wash, null, ForeColor),
        };
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        var (fill, border, text) = Look();
        if (!Enabled) text = Theme.Border;
        var r = new RectangleF(0, 0, Width, Height);
        float radius = Theme.Corner(this, Height, 6), px = DeviceDpi / 96f;
        if (fill is { } f) Theme.FillRound(g, f, r, radius);
        if (border is { } b)
        {
            float w = (_kind == ButtonKind.Primary ? 1.5f : 1f) * px;
            Theme.DrawRound(g, b, w, RectangleF.Inflate(r, -w / 2, -w / 2), radius);
        }
        if (Focused && ShowFocusCues)
            Theme.DrawRound(g, Theme.Accent, 1.5f * px, RectangleF.Inflate(r, -3 * px, -3 * px), Math.Max(0, radius - 3 * px));

        // icon (nếu có) + chữ, căn giữa
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter;
        int textW = TextRenderer.MeasureText(g, Text, Font, Size.Empty, flags).Width, imageW = Image?.Width ?? 0;
        int x = (Width - imageW - textW) / 2;
        if (Image != null) g.DrawImage(Image, x, (Height - Image.Height) / 2, Image.Width, Image.Height);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(x + imageW, 0, textW + 1, Height), text, flags);
    }
}

/// <summary>Nút phẳng trên thanh công cụ, không nhận focus (bấm xong focus vẫn ở list / ô gõ); có thể kèm icon Fluent trước chữ.</summary>
sealed class FlatButton : ThemedButton
{
    int _glyph;

    public FlatButton() : base(ButtonKind.Ghost)
    {
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
    }

    /// <summary>Mã icon Fluent vẽ trước chữ, cùng màu chữ; 0 = không có icon.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Glyph
    {
        get => _glyph;
        set
        {
            _glyph = value;
            RenderGlyph();
        }
    }

    protected override bool ShowFocusCues => false;

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        RenderGlyph();
    }

    void RenderGlyph()
    {
        var old = Image;
        Image = _glyph == 0 ? null : Theme.GlyphImage(this, _glyph, ForeColor, 9f, gap: Theme.Px(this, 5));
        old?.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) Image?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Label vẽ chữ sát mép trái (bỏ lề trong của TextRenderer) để thẳng hàng với thanh tiến độ, ô nhập.</summary>
sealed class PlainLabel : Label
{
    protected override void OnPaint(PaintEventArgs e) =>
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
}

/// <summary>Ô nhập kiểu dòng kẻ trên giấy: icon bên trái, gạch chân mảnh; đang gõ thì gạch chân dày lên, màu đỏ son.</summary>
sealed class InputField : Panel
{
    readonly TextBox _box;
    readonly string _glyph;

    public InputField(TextBox box, int glyph = 0xE710)
    {
        _box = box;
        _glyph = Theme.Glyph(glyph);
        Dock = DockStyle.Fill;
        Cursor = Cursors.IBeam;
        DoubleBuffered = true;
        ResizeRedraw = true;
        Controls.Add(box);
        box.GotFocus += (_, _) => Invalidate();
        box.LostFocus += (_, _) => Invalidate();
    }

    readonly List<Control> _buttons = new();

    int IconWidth => Theme.Px(this, 28);

    /// <summary>Thêm nút ở cuối dòng (nút thêm trước nằm ngoài cùng bên phải).</summary>
    public void AddButton(Control button)
    {
        _buttons.Add(button);
        Controls.Add(button);
        PerformLayout();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Width;
        foreach (var b in _buttons)
        {
            x -= b.Width;
            b.Location = new Point(x, (Height - b.Height) / 2 - 1);
            x -= Theme.Px(this, 2);
        }
        _box.SetBounds(IconWidth, (Height - _box.Height) / 2, x - IconWidth - Theme.Px(this, 4), _box.Height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        bool focused = _box.Focused;
        int thick = focused ? Theme.Px(this, 2) : Math.Max(1, Theme.Px(this, 1));
        using (var line = new SolidBrush(focused ? Theme.Accent : Theme.Border))
            g.FillRectangle(line, 0, Height - thick, Width, thick);
        using var icon = Theme.IconFont(10f);
        TextRenderer.DrawText(g, _glyph, icon, new Rectangle(0, 0, IconWidth, Height), focused ? Theme.Accent : Theme.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

/// <summary>Thanh tiến độ: một đường kẻ đầu vuông, Text hiện ở cuối thanh; xong hết thì chuyển màu Success.</summary>
sealed class ProgressLine : Control
{
    float _value;

    public ProgressLine()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Font = Theme.Ui(9f);
        ForeColor = Theme.Muted;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Invalidate();
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public float Value
    {
        get => _value;
        set { _value = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        bool all = _value >= 1;
        int textW = Text.Length == 0 ? 0 : TextRenderer.MeasureText(g, Text, Font).Width + Theme.Px(this, 10);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Width - textW, 0, textW, Height), all ? Theme.Success : ForeColor,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        int h = Theme.Px(this, 3);
        var track = new Rectangle(0, (Height - h) / 2, Width - textW, h);
        using (var brush = new SolidBrush(Theme.Line)) g.FillRectangle(brush, track);
        if (_value > 0)
        {
            using var brush = new SolidBrush(all ? Theme.Success : Theme.Accent);
            g.FillRectangle(brush, track with { Width = (int)Math.Round(track.Width * _value) });
        }
    }
}

/// <summary>Lịch tháng gọn theo theme (tuần bắt đầu thứ Hai), dùng trong popup chọn ngày.</summary>
sealed class CalendarView : Control
{
    static string[] WeekDays => English ? ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"] : ["T2", "T3", "T4", "T5", "T6", "T7", "CN"];
    DateTime _month = DateTime.Today, _selected = DateTime.Today;
    Point _mouse = new(-1, -1);

    public event Action<DateTime>? DatePicked;

    /// <summary>Các ngày còn việc chưa xong trong khoảng (từ, đến) → có chấm nhỏ dưới số ngày.</summary>
    public Func<DateTime, DateTime, HashSet<DateTime>>? MarkedDays;

    HashSet<DateTime> _marked = new();

    public CalendarView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
        Font = Theme.Ui(9.5f);
        Size = new Size(S(276), S(274));
    }

    public void Open(DateTime selected)
    {
        _selected = selected.Date;
        _month = new DateTime(selected.Year, selected.Month, 1);
        LoadMarks();
        Invalidate();
    }

    void LoadMarks() => _marked = MarkedDays?.Invoke(GridStart, GridStart.AddDays(41)) ?? new();

    int Pad => S(12);
    int HeaderH => S(36);
    int WeekH => S(24);
    int CellW => S(36);
    int CellH => S(32);
    Rectangle PrevRect => new(Width - Pad - S(64), Pad + S(4), S(28), S(28));
    Rectangle NextRect => new(Width - Pad - S(32), Pad + S(4), S(28), S(28));
    DateTime GridStart => _month.AddDays(-(((int)_month.DayOfWeek + 6) % 7));
    Rectangle CellRect(int i) => new(Pad + i % 7 * CellW, Pad + HeaderH + WeekH + i / 7 * CellH, CellW, CellH);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var bold = Theme.Semibold(9.5f);
        using var titleFont = Theme.Heading(11.5f);
        using var small = Theme.Small(8.5f);
        using var icons = Theme.IconFont(9f);
        const TextFormatFlags center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;

        TextRenderer.DrawText(g, English ? _month.ToString("MMMM yyyy", Lang.Culture) : $"Tháng {_month.Month}, {_month.Year}", titleFont, new Rectangle(Pad + S(6), Pad, Width, HeaderH),
            Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        foreach (var (glyph, r) in new[] { (Theme.Glyph(0xE76B), PrevRect), (Theme.Glyph(0xE76C), NextRect) })
        {
            if (r.Contains(_mouse)) Theme.FillRound(g, Theme.Field, r, Theme.Corner(this, r.Height, 6));
            TextRenderer.DrawText(g, glyph, icons, r, Theme.Muted, center);
        }

        for (int c = 0; c < 7; c++)
            TextRenderer.DrawText(g, WeekDays[c], small, new Rectangle(Pad + c * CellW, Pad + HeaderH, CellW, WeekH), Theme.Muted, center);

        var start = GridStart;
        for (int i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var r = CellRect(i);
            float d = r.Height - S(4);
            var cell = new RectangleF(r.X + (r.Width - d) / 2, r.Y + (r.Height - d) / 2, d, d);
            float radius = Theme.Corner(this, d, 6);
            bool selected = day == _selected, today = day == DateTime.Today;

            // ngày đang chọn: giống nút chính (nền mực / màu nhấn / viền); hôm nay: chữ màu nhấn + chấm nhỏ bên dưới
            Color? fill = selected ? Theme.Primary switch { PrimaryStyle.Accent => Theme.Accent, PrimaryStyle.Outline => null, _ => Theme.Text }
                : r.Contains(_mouse) ? Theme.Field : null;
            if (fill is { } f) Theme.FillRound(g, f, cell, radius);
            if (selected && Theme.Primary == PrimaryStyle.Outline)
                Theme.DrawRound(g, Theme.Text, 1.5f * DeviceDpi / 96f, RectangleF.Inflate(cell, -1, -1), radius);

            var color = !selected ? today ? Theme.Accent : day.Month != _month.Month ? Theme.Border : Theme.Text
                : Theme.Primary switch { PrimaryStyle.Accent => Theme.OnAccent, PrimaryStyle.Outline => Theme.Text, _ => Theme.Bg };
            // chấm dưới số: hôm nay (màu nhấn) hoặc ngày còn việc chưa xong (xám)
            if (today || _marked.Contains(day))
            {
                float dot = S(4);
                using var brush = new SolidBrush(today || selected ? color : Theme.Muted);
                g.FillEllipse(brush, r.X + (r.Width - dot) / 2, cell.Bottom - dot - S(3), dot, dot);
            }
            TextRenderer.DrawText(g, day.Day.ToString(), selected || today ? bold : Font, r with { Height = r.Height - S(2) }, color, center);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.Location;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _mouse = new Point(-1, -1);
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (PrevRect.Contains(e.Location)) ShiftMonth(-1);
        else if (NextRect.Contains(e.Location)) ShiftMonth(1);
        else if (Enumerable.Range(0, 42).FirstOrDefault(i => CellRect(i).Contains(e.Location), -1) is var i and >= 0)
            DatePicked?.Invoke(GridStart.AddDays(i));
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        ShiftMonth(e.Delta > 0 ? -1 : 1);
    }

    void ShiftMonth(int months)
    {
        _month = _month.AddMonths(months);
        LoadMarks();
        Invalidate();
    }

    int S(int value) => Theme.Px(this, value);
}

/// <summary>Form theo theme: font, nền, chữ theo bảng màu; thanh tiêu đề sáng / tối khớp nền; đơn vị đo theo DPI + kích thước.</summary>
class ThemedForm : Form
{
    protected ThemedForm() => ApplyTheme();

    /// <summary>Đặt lại font, màu và thanh tiêu đề theo theme hiện tại (gọi lại khi dựng lại giao diện).</summary>
    protected void ApplyTheme()
    {
        Font = Theme.Ui(10f);
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Theme.StyleTitleBar(this);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.StyleTitleBar(this);
    }

    protected int S(int value) => Theme.Px(this, value);
    protected Size S(Size size) => new(S(size.Width), S(size.Height));
    protected Rectangle S(Rectangle r) => new(S(r.X), S(r.Y), S(r.Width), S(r.Height));
}

/// <summary>Hộp thoại cỡ cố định, mở giữa cửa sổ chính, không hiện trên taskbar.</summary>
class DialogForm : ThemedForm
{
    protected DialogForm()
    {
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
    }

    // Nút chính theo kiểu trong cài đặt (mực / màu nhấn / viền); nút phụ viền mảnh
    protected static Button DialogButton(string text, bool primary) =>
        new ThemedButton(primary ? ButtonKind.Primary : ButtonKind.Secondary) { Text = text };
}

/// <summary>Hộp thông báo / hỏi Có–Không theo theme (MessageBox của Windows luôn nền sáng).</summary>
sealed class MessageDialog : DialogForm
{
    // TextBoxControl: chuỗi dài không có khoảng trắng (đường dẫn file) cũng được ngắt dòng
    const TextFormatFlags TextFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
    readonly string _text;
    readonly Rectangle _textRect;

    MessageDialog(string text, string caption, int glyph, Color glyphColor, bool question)
    {
        Text = caption;
        _text = text;
        int pad = S(20), iconW = S(36), bw = S(88), bh = S(34);
        var size = TextRenderer.MeasureText(text, Font, new Size(S(540), 0), TextFlags);
        _textRect = new Rectangle(pad + iconW, pad + S(3), size.Width + 1, size.Height);
        var icon = new Label { Text = Theme.Glyph(glyph), Font = Theme.IconFont(16f), ForeColor = glyphColor, Bounds = new Rectangle(pad, pad, iconW, S(30)) };

        var buttons = question
            ? new[] { DialogButton(T("Có", "Yes"), primary: true), DialogButton(T("Không", "No"), primary: false) }
            : new[] { DialogButton("OK", primary: true) };
        buttons[0].DialogResult = question ? DialogResult.Yes : DialogResult.OK;
        buttons[^1].DialogResult = question ? DialogResult.No : DialogResult.OK;
        AcceptButton = buttons[0];
        CancelButton = buttons[^1];

        int width = Math.Max(_textRect.Right + pad, S(320)), y = Math.Max(_textRect.Bottom, icon.Bottom) + S(22), x = width - pad;
        for (int i = buttons.Length - 1; i >= 0; i--)
        {
            x -= bw;
            buttons[i].Bounds = new Rectangle(x, y, bw, bh);
            x -= S(8);
        }
        ClientSize = new Size(width, y + bh + pad);
        Controls.AddRange([icon, .. buttons]);
        ActiveControl = buttons[0];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(e.Graphics, _text, Font, _textRect, Theme.Text, TextFlags);
    }

    public static void Show(IWin32Window owner, string text, string caption, bool warning = false)
    {
        using var dialog = new MessageDialog(text, caption, warning ? 0xE7BA : 0xE946, warning ? Theme.Warning : Theme.Muted, question: false);
        dialog.ShowDialog(owner);
    }

    public static bool Confirm(IWin32Window owner, string text, string caption)
    {
        using var dialog = new MessageDialog(text, caption, 0xE9CE, Theme.Accent, question: true);
        return dialog.ShowDialog(owner) == DialogResult.Yes;
    }
}

/// <summary>Tooltip tự vẽ, đảo màu: nền mực, chữ giấy (tooltip gốc của Windows luôn nền sáng).</summary>
sealed class ThemeTip : ToolTip
{
    // Chữ dài (vd tên issue đầy đủ) quá bề ngang tối đa thì xuống dòng
    const TextFormatFlags Flags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;
    readonly Font _font = Theme.Ui(8.5f);

    /// <summary>Bề ngang tối đa (px, gồm lề); 0 = mặc định 460 px theo thiết kế.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int MaxWidth { get; set; }

    public ThemeTip()
    {
        OwnerDraw = true;
        Popup += (_, e) =>
        {
            if (e.AssociatedControl is { } c) e.ToolTipSize = SizeFor(c, GetToolTip(c) ?? "");
        };
        Draw += (_, e) =>
        {
            int pad = e.AssociatedControl is { } c ? Theme.Px(c, 8) : 8;
            e.Graphics.Clear(Theme.Text);
            TextRenderer.DrawText(e.Graphics, e.ToolTipText, _font, Rectangle.Inflate(e.Bounds, -pad, -pad / 2), Theme.Bg, Flags);
        };
    }

    /// <summary>Cỡ tooltip của chữ này (gồm lề), để canh chỗ hiện trước khi Show.</summary>
    public Size SizeFor(Control c, string text)
    {
        int pad = Theme.Px(c, 8), max = MaxWidth > 0 ? Math.Max(Theme.Px(c, 120), MaxWidth - pad * 2) : Theme.Px(c, 460);
        var size = TextRenderer.MeasureText(text, _font, new Size(max, 0), Flags);
        return new Size(size.Width + pad * 2, size.Height + pad);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _font.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Hộp nhập link cho 1 việc. Enter = lưu, Esc = hủy, để trống = bỏ link.</summary>
sealed class LinkDialog : DialogForm
{
    readonly TextBox _box = new() { BorderStyle = BorderStyle.None, BackColor = Theme.Bg, ForeColor = Theme.Text, PlaceholderText = "https://github.com/…" };

    public string? Url => string.IsNullOrWhiteSpace(_box.Text) ? null : _box.Text.Trim();

    public LinkDialog(string itemText, string url, string? inheritedUrl)
    {
        Text = T("Gắn link", "Link");
        ClientSize = S(new Size(440, 180));

        var title = new Label
        {
            Text = T($"Link cho “{itemText}”", $"Link for “{itemText}”"), AutoEllipsis = true, UseMnemonic = false, ForeColor = Theme.Text,
            Font = Theme.Heading(12.5f), Bounds = S(new Rectangle(20, 14, 400, 30)),
        };
        var hint = new Label
        {
            Text = inheritedUrl != null
                ? T($"Đang tự lấy theo note cha: {inheritedUrl}", $"Inherited from the parent note: {inheritedUrl}")
                : T("GitHub issue / PR / repo hoặc link bất kỳ. Để trống = bỏ link.", "GitHub issue / PR / repo, or any link. Leave empty to remove."),
            AutoEllipsis = true, ForeColor = Theme.Muted, Font = Theme.Ui(9f), Bounds = S(new Rectangle(20, 44, 400, 20)),
        };
        _box.Text = url;
        var field = new InputField(_box, glyph: 0xE71B) { Dock = DockStyle.None, Bounds = S(new Rectangle(20, 74, 400, 36)) };

        var save = DialogButton(T("Lưu", "Save"), primary: true);
        var cancel = DialogButton(T("Hủy", "Cancel"), primary: false);
        save.DialogResult = DialogResult.OK;
        cancel.DialogResult = DialogResult.Cancel;
        save.Bounds = S(new Rectangle(336, 130, 84, 34));
        cancel.Bounds = S(new Rectangle(244, 130, 84, 34));
        AcceptButton = save;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { title, hint, field, cancel, save });
        Shown += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }
}

/// <summary>Màu cho menu chuột phải / popup theo theme: nền giấy, viền mảnh, hover nhẹ.</summary>
sealed class MenuColors : ProfessionalColorTable
{
    public override Color MenuItemSelected => Theme.Field;
    public override Color MenuItemBorder => Theme.Field;
    public override Color MenuBorder => Theme.Border;
    public override Color ToolStripDropDownBackground => Theme.Bg;
    public override Color ImageMarginGradientBegin => Theme.Bg;
    public override Color ImageMarginGradientMiddle => Theme.Bg;
    public override Color ImageMarginGradientEnd => Theme.Bg;
    public override Color SeparatorDark => Theme.Line;
    public override Color SeparatorLight => Theme.Bg;
}

/// <summary>Thanh cuộn dọc mảnh tự vẽ (thay thanh gốc nền xám): chỉ có thumb bo tròn, rê chuột vào thì dày lên.</summary>
sealed class ScrollStrip : Control
{
    int _pos, _page = 1, _total, _dragY, _dragPos;
    bool _hot, _dragging;

    /// <summary>Người dùng kéo / bấm thanh cuộn → muốn dòng này nằm trên cùng.</summary>
    public event Action<int>? Scrolled;

    public ScrollStrip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.Bg;
    }

    public int Position => _pos;

    public void SetRange(int pos, int page, int total)
    {
        page = Math.Max(1, page);
        if (pos == _pos && page == _page && total == _total) return;
        (_pos, _page, _total) = (pos, page, total);
        Invalidate();
    }

    bool Scrollable => _total > _page;
    int Range => Math.Max(1, _total - _page);
    Rectangle Track => new(0, S(4), Width, Math.Max(1, Height - S(8)));

    Rectangle Thumb
    {
        get
        {
            var track = Track;
            int h = Math.Min(track.Height, Math.Max(S(28), track.Height * _page / Math.Max(1, _total)));
            int y = track.Y + (track.Height - h) * Math.Clamp(_pos, 0, Range) / Range;
            return new Rectangle(0, y, Width, h);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        if (!Scrollable) return;
        var thumb = Thumb;
        float w = _hot || _dragging ? S(6) : S(4);
        var color = _dragging ? Theme.Muted : _hot ? Theme.ScrollHot : Theme.Border;
        Theme.FillRound(e.Graphics, color, new RectangleF(Width - w - S(2), thumb.Y, w, thumb.Height), w / 2);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Scrollable || e.Button != MouseButtons.Left) return;
        var thumb = Thumb;
        if (thumb.Contains(e.Location))
        {
            (_dragging, _dragY, _dragPos) = (true, e.Y, _pos);
            Capture = true;
            Invalidate();
        }
        else Scrolled?.Invoke(Math.Clamp(e.Y < thumb.Y ? _pos - _page : _pos + _page, 0, Range)); // bấm vào rãnh → lật 1 trang
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        int pixels = Math.Max(1, Track.Height - Thumb.Height);
        Scrolled?.Invoke(Math.Clamp(_dragPos + (int)Math.Round((e.Y - _dragY) * (double)Range / pixels), 0, Range));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
        Invalidate();
    }

    int S(int value) => Theme.Px(this, value);
}
