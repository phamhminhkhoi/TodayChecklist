using System.Globalization;

namespace TodayChecklist;

enum ThemeMode { Light, Dark, System }
enum BaseTone { Neutral, Cool, Warm }
enum ButtonShape { Square, Rounded, Pill }
enum PrimaryStyle { Ink, Accent, Outline }

/// <summary>
/// Cài đặt giao diện, lưu trong bảng settings của DB. Size là chỉ số trong <see cref="Scales"/>;
/// Accent là khóa màu có sẵn (<see cref="Accents"/>) hoặc màu tự chọn dạng "#RRGGBB".
/// </summary>
sealed record Settings(ThemeMode Mode, bool English, int Size, BaseTone Tone, string Accent, ButtonShape Shape, PrimaryStyle Primary,
    bool AutoCarry, bool AutoStatus, bool Tray, int Hotkey, bool NotesHotkey)
{
    public static readonly float[] Scales = [0.9f, 1f, 1.15f, 1.3f];

    /// <summary>Phím gọi app từ bất kỳ đâu; Hotkey = chỉ số trong mảng này, bằng độ dài mảng = tắt.</summary>
    public static readonly string[] Hotkeys = ["Ctrl+Alt+T", "Win+Shift+T"];

    public static Settings Load(Store store) => new(
        Parse(store.Setting("theme"), ThemeMode.Light),
        store.Setting("language") == "en",
        int.TryParse(store.Setting("size"), out var size) && size >= 0 && size < Scales.Length ? size : 1,
        Parse(store.Setting("tone"), BaseTone.Neutral),
        store.Setting("accent") is { } accent && Accents.IsValid(accent) ? accent : Accents.Default,
        Parse(store.Setting("shape"), ButtonShape.Rounded),
        Parse(store.Setting("primary"), PrimaryStyle.Ink),
        store.Setting("auto_carry") != "off",
        store.Setting("auto_status") != "off",
        store.Setting("tray") != "off",
        int.TryParse(store.Setting("hotkey"), out var hotkey) && hotkey >= 0 && hotkey <= Hotkeys.Length ? hotkey : 0,
        store.Setting("notes_hotkey") != "off");

    static TEnum Parse<TEnum>(string? value, TEnum fallback) where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, out var v) && Enum.IsDefined(v) ? v : fallback;

    public void Save(Store store) => store.SetSettings(
        ("theme", Mode.ToString()), ("language", English ? "en" : "vi"), ("size", Size.ToString(CultureInfo.InvariantCulture)),
        ("tone", Tone.ToString()), ("accent", Accent), ("shape", Shape.ToString()), ("primary", Primary.ToString()),
        ("auto_carry", AutoCarry ? "on" : "off"), ("auto_status", AutoStatus ? "on" : "off"),
        ("tray", Tray ? "on" : "off"), ("hotkey", Hotkey.ToString(CultureInfo.InvariantCulture)),
        ("notes_hotkey", NotesHotkey ? "on" : "off"));

    /// <summary>Đẩy cài đặt vào Theme / Lang; giao diện tạo sau lời gọi này sẽ theo cài đặt mới.</summary>
    public void Apply()
    {
        bool dark = Mode == ThemeMode.Dark || (Mode == ThemeMode.System && Theme.WindowsDark());
        Theme.Apply(dark, Scales[Size], Tone, Accents.Resolve(Accent, dark), Shape, Primary);
        Lang.English = English;
    }
}

sealed record AccentPreset(string Key, string Vi, string En, int Light, int Dark);

/// <summary>Các màu nhấn có sẵn (mỗi màu 1 bản cho nền sáng, 1 bản sáng hơn cho nền tối) và màu tự chọn.</summary>
static class Accents
{
    public const string Default = "blue";

    public static readonly AccentPreset[] Presets =
    [
        new("blue", "Xanh dương", "Blue", 0x2F62C8, 0x7AA2F0),
        new("teal", "Xanh ngọc", "Teal", 0x0E7C72, 0x4FC1B1),
        new("green", "Xanh lá", "Green", 0x2F7D3A, 0x7CC488),
        new("violet", "Tím", "Violet", 0x6B4FC8, 0xA994F2),
        new("rose", "Hồng", "Rose", 0xC1356B, 0xF07FA6),
        new("vermilion", "Đỏ son", "Vermilion", 0xB5492B, 0xE07A58),
        new("amber", "Cam", "Amber", 0xB8680C, 0xE8A55A),
        new("graphite", "Mực", "Graphite", 0x3C4048, 0xCDD0D6),
    ];

    public static bool IsCustom(string key) =>
        key.Length == 7 && key[0] == '#' && int.TryParse(key.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);

    public static bool IsValid(string key) => IsCustom(key) || Presets.Any(p => p.Key == key);

    /// <summary>Màu nhấn cho nền sáng / tối; màu tự chọn thì bản tối được pha sáng lên cho dễ đọc trên nền tối.</summary>
    public static Color Resolve(string key, bool dark)
    {
        if (IsCustom(key))
        {
            var color = Theme.Rgb(int.Parse(key.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return dark ? Theme.Mix(color, Color.White, 0.3f) : color;
        }
        var preset = Presets.FirstOrDefault(p => p.Key == key) ?? Presets[0];
        return Theme.Rgb(dark ? preset.Dark : preset.Light);
    }

    public static string KeyOf(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

/// <summary>Chữ hiển thị 2 thứ tiếng: viết thẳng T("tiếng Việt", "English") tại chỗ dùng.</summary>
static class Lang
{
    static readonly CultureInfo Vi = new("vi-VN"), En = new("en-US");

    public static bool English { get; set; }

    public static CultureInfo Culture => English ? En : Vi;

    public static string T(string vi, string en) => English ? en : vi;

    /// <summary>Ngày ngắn gọn trong câu thông báo: 26/10/2026 hoặc 26 Oct 2026.</summary>
    public static string Date(DateTime day) => day.ToString(English ? "d MMM yyyy" : "dd/MM/yyyy", Culture);
}

/// <summary>Cài đặt màu sắc / nút / ngôn ngữ / kích thước. Bấm tới đâu áp dụng ngay tới đó, cả cửa sổ chính phía sau.</summary>
sealed class SettingsDialog : DialogForm
{
    const int DialogWidth = 540, LabelX = 24, ControlX = 164, ControlWidth = 352, RowHeight = 34;

    readonly Action<Settings> _apply;
    Settings _settings;
    ThemeTip? _tip;

    public SettingsDialog(Settings settings, Action<Settings> apply)
    {
        _settings = settings;
        _apply = apply;
        Build();
    }

    // Dựng lại toàn bộ mỗi lần đổi, để chính hộp này cũng đổi màu / chữ / cỡ / kiểu nút theo
    void Build()
    {
        SuspendLayout();
        var old = Controls.Cast<Control>().ToArray();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        _tip?.Dispose();
        _tip = new ThemeTip();
        ApplyTheme();
        Text = T("Cài đặt", "Settings");

        int y = 4;
        Section(T("Màu sắc", "Colors"));
        Row(T("Giao diện", "Theme"), Choice([T("Sáng", "Light"), T("Tối", "Dark"), T("Theo Windows", "System")],
            (int)_settings.Mode, i => _settings with { Mode = (ThemeMode)i }));
        Row(T("Tông nền", "Base tone"), Choice([T("Trung tính", "Neutral"), T("Lạnh", "Cool"), T("Ấm", "Warm")],
            (int)_settings.Tone, i => _settings with { Tone = (BaseTone)i }));
        var swatches = new SwatchPicker(_settings.Accent, _tip);
        swatches.Picked += key => Change(_settings with { Accent = key });
        Row(T("Màu nhấn", "Accent"), swatches);

        Section(T("Nút", "Buttons"));
        Row(T("Hình dạng", "Shape"), Choice([T("Vuông", "Square"), T("Bo góc", "Rounded"), T("Tròn", "Round")],
            (int)_settings.Shape, i => _settings with { Shape = (ButtonShape)i }));
        Row(T("Nút chính", "Main button"), Choice([T("Màu mực", "Ink"), T("Màu nhấn", "Accent"), T("Viền", "Outline")],
            (int)_settings.Primary, i => _settings with { Primary = (PrimaryStyle)i }));

        Section(T("Tự động", "Automatic"));
        Row(T("Việc chưa xong", "Unfinished tasks"), Choice([T("Tự chép sang hôm nay", "Carry to today"), T("Bấm tay", "Manual")],
            _settings.AutoCarry ? 0 : 1, i => _settings with { AutoCarry = i == 0 }));
        Row(T("Status GitHub", "GitHub status"), Choice([T("Tự cập nhật", "Auto refresh"), T("Bấm tay (F5)", "Manual (F5)")],
            _settings.AutoStatus ? 0 : 1, i => _settings with { AutoStatus = i == 0 }));

        Section(T("Chung", "General"));
        Row(T("Ngôn ngữ", "Language"), Choice(["Tiếng Việt", "English"],
            _settings.English ? 1 : 0, i => _settings with { English = i == 1 }));
        Row(T("Kích thước", "Size"), Choice([T("Nhỏ", "Small"), T("Vừa", "Medium"), T("Lớn", "Large"), T("Rất lớn", "X-Large")],
            _settings.Size, i => _settings with { Size = i }));
        Row(T("Thu nhỏ", "Minimize"), Choice([T("Xuống khay hệ thống", "To the tray"), T("Như thường", "Normally")],
            _settings.Tray ? 0 : 1, i => _settings with { Tray = i == 0 }));
        Row(T("Phím gọi app", "Global hotkey"), Choice([.. Settings.Hotkeys, T("Tắt", "Off")],
            _settings.Hotkey, i => _settings with { Hotkey = i }));
        Row(T("Phím mở ghi chú", "Notes hotkey"), Choice(["Ctrl+Win+Alt", T("Tắt", "Off")],
            _settings.NotesHotkey ? 0 : 1, i => _settings with { NotesHotkey = i == 0 }));

        var done = DialogButton(T("Xong", "Done"), primary: true);
        done.DialogResult = DialogResult.OK;
        done.Bounds = S(new Rectangle(DialogWidth - LabelX - 88, y + 14, 88, 34));
        AcceptButton = CancelButton = done;
        Controls.Add(done);
        ClientSize = S(new Size(DialogWidth, y + 66));
        ResumeLayout();

        void Section(string title)
        {
            y += 14;
            Controls.Add(new PlainLabel { Text = title, ForeColor = Theme.Muted, Font = Theme.Semibold(9f), Bounds = S(new Rectangle(LabelX, y, 300, 22)) });
            y += 26;
        }

        void Row(string label, Control control)
        {
            Controls.Add(new PlainLabel { Text = label, ForeColor = Theme.Text, Font = Theme.Ui(9.5f), Bounds = S(new Rectangle(LabelX, y, ControlX - LabelX, RowHeight)) });
            control.Bounds = S(new Rectangle(ControlX, y, ControlWidth, RowHeight));
            Controls.Add(control);
            y += RowHeight + 8;
        }

        Segmented Choice(string[] items, int selected, Func<int, Settings> pick)
        {
            var choice = new Segmented(items, selected);
            choice.Picked += i => Change(pick(i));
            return choice;
        }
    }

    void Change(Settings settings)
    {
        _settings = settings;
        _apply(settings);
        BeginInvoke(Build); // đang ở trong sự kiện chuột của chính control sắp bị thay → dựng lại sau
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip?.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Nhóm nút chọn 1 trong n: mục đang bật là một mặt nổi có viền mảnh, chữ đậm; mục khác chữ xám.</summary>
sealed class Segmented : Control
{
    readonly string[] _items;
    int _selected, _hover = -1;

    public event Action<int>? Picked;

    public Segmented(string[] items, int selected)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        (_items, _selected) = (items, selected);
        Font = Theme.Ui(9.5f);
        Cursor = Cursors.Hand;
    }

    Rectangle ItemRect(int i)
    {
        int w = Width / _items.Length;
        return new Rectangle(i * w, 0, i == _items.Length - 1 ? Width - i * w : w, Height);
    }

    int IndexAt(int x) => Math.Clamp(x * _items.Length / Math.Max(1, Width), 0, _items.Length - 1);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        int inset = Theme.Px(this, 3);
        Theme.FillRound(g, Theme.Field, new RectangleF(0, 0, Width, Height), Theme.Corner(this, Height, 7));
        using var bold = new Font(Theme.SansStrong, Font.Size);
        for (int i = 0; i < _items.Length; i++)
        {
            var r = ItemRect(i);
            var face = RectangleF.Inflate(r, -inset, -inset);
            if (i == _selected)
            {
                float radius = Theme.Corner(this, face.Height, 5);
                Theme.FillRound(g, Theme.Raised, face, radius);
                Theme.DrawRound(g, Theme.Dark ? Theme.Raised : Theme.Line, 1f, face, radius);
            }
            var color = i == _selected || i == _hover ? Theme.Text : Theme.Muted;
            TextRenderer.DrawText(g, _items[i], i == _selected ? bold : Font, r, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
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
        if (e.Button != MouseButtons.Left || i == _selected) return;
        _selected = i;
        Invalidate();
        Picked?.Invoke(i);
    }
}

/// <summary>Dãy ô màu nhấn có sẵn, ô cuối để tự chọn màu bất kỳ (hộp chọn màu của Windows). Ô đang chọn có vòng viền.</summary>
sealed class SwatchPicker : Control
{
    readonly ToolTip _tip;
    string _selected;
    int _hover = -1;

    public event Action<string>? Picked;

    public SwatchPicker(string selected, ToolTip tip)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        (_selected, _tip) = (selected, tip);
        Cursor = Cursors.Hand;
    }

    int Count => Accents.Presets.Length + 1; // ô cuối = tự chọn
    int Pitch => Theme.Px(this, 36);
    int Dot => Theme.Px(this, 22);

    RectangleF Swatch(int i) => new(i * Pitch + Theme.Px(this, 5), (Height - Dot) / 2f, Dot, Dot);

    int IndexAt(Point pt) => pt.X / Pitch is var i && i < Count && RectangleF.Inflate(Swatch(i), Theme.Px(this, 5), Theme.Px(this, 5)).Contains(pt) ? i : -1;

    string NameOf(int i) => i < Accents.Presets.Length
        ? T(Accents.Presets[i].Vi, Accents.Presets[i].En)
        : Accents.IsCustom(_selected) ? T($"Màu tự chọn {_selected} (bấm để đổi)", $"Custom {_selected} (click to change)") : T("Tự chọn màu…", "Custom color…");

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Theme.Bg);
        bool customSelected = Accents.IsCustom(_selected);
        float ring = Theme.Px(this, 3) + 1;
        for (int i = 0; i < Count; i++)
        {
            var r = Swatch(i);
            float radius = Theme.Corner(this, r.Height, 6);
            bool custom = i == Count - 1;
            bool selected = custom ? customSelected : Accents.Presets[i].Key == _selected;
            if (!custom) Theme.FillRound(g, Accents.Resolve(Accents.Presets[i].Key, Theme.Dark), r, radius);
            else if (customSelected) Theme.FillRound(g, Accents.Resolve(_selected, Theme.Dark), r, radius);
            else
            {
                // ô tự chọn còn trống: viền + dấu cộng
                Theme.DrawRound(g, Theme.Border, 1.5f, RectangleF.Inflate(r, -0.75f, -0.75f), radius);
                using var icon = Theme.IconFont(8f);
                TextRenderer.DrawText(g, Theme.Glyph(0xE710), icon, Rectangle.Round(r), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            if (selected || i == _hover)
            {
                var outer = RectangleF.Inflate(r, ring, ring);
                Theme.DrawRound(g, selected ? Theme.Text : Theme.Border, selected ? 1.75f : 1f, outer, radius == 0 ? 0 : radius + ring);
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int i = IndexAt(e.Location);
        if (i == _hover) return;
        _hover = i;
        _tip.SetToolTip(this, i < 0 ? "" : NameOf(i));
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
        int i = IndexAt(e.Location);
        if (e.Button != MouseButtons.Left || i < 0) return;
        if (i < Accents.Presets.Length)
        {
            Pick(Accents.Presets[i].Key);
            return;
        }
        using var dialog = new ColorDialog { FullOpen = true, AnyColor = true, Color = Accents.Resolve(_selected, dark: false) };
        if (dialog.ShowDialog(FindForm()) == DialogResult.OK) Pick(Accents.KeyOf(dialog.Color));
    }

    void Pick(string key)
    {
        if (key == _selected) return;
        _selected = key;
        Invalidate();
        Picked?.Invoke(key);
    }
}
