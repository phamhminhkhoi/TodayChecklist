using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TodayChecklist;

/// <summary>
/// Bảng màu sáng / tối theo tông nền + màu nhấn, kiểu nút, font, hệ số kích thước và các hàm vẽ dùng chung (xem DESIGN.md).
/// Control đọc màu / font lúc được tạo, nên đổi cài đặt = Apply rồi dựng lại giao diện.
/// </summary>
static class Theme
{
    public static bool Dark { get; private set; }
    public static float Scale { get; private set; } = 1f;
    public static ButtonShape Shape { get; private set; } = ButtonShape.Rounded;
    public static PrimaryStyle Primary { get; private set; } = PrimaryStyle.Ink;

    public static Color Bg { get; private set; }
    public static Color Raised { get; private set; }
    public static Color Field { get; private set; }
    public static Color Hover { get; private set; }
    public static Color Selected { get; private set; }
    public static Color Press { get; private set; }
    public static Color Line { get; private set; }
    public static Color Border { get; private set; }
    public static Color Muted { get; private set; }
    public static Color Text { get; private set; }
    public static Color PrimaryHover { get; private set; }
    public static Color Accent { get; private set; }
    public static Color AccentHover { get; private set; }
    public static Color OnAccent { get; private set; } // chữ trên nền màu nhấn: trắng hoặc mực, tùy màu nào dễ đọc hơn
    public static Color Success { get; private set; }
    public static Color Warning { get; private set; }
    public static Color Danger { get; private set; }
    public static Color ScrollHot { get; private set; }

    // Font: Segoe UI Variable / Fluent Icons trên Windows 11, Sitka cho tiêu đề; máy cũ thì lùi về font tương đương
    public static readonly string Icons = Pick("Segoe Fluent Icons", "Segoe MDL2 Assets");
    public static readonly string Sans = Pick("Segoe UI Variable Text", "Segoe UI");
    public static readonly string SansStrong = Pick("Segoe UI Variable Text Semibold", "Segoe UI Semibold");
    public static readonly string SansSmall = Pick("Segoe UI Variable Small", "Segoe UI");

    /// <summary>Màu nền của 1 tông (giấy, mặt nổi, rãnh, hover, chọn, bấm, kẻ, viền, chữ phụ, chữ, nút chính hover, thanh cuộn).</summary>
    readonly record struct Paper(int Bg, int Raised, int Field, int Hover, int Selected, int Press, int Line, int Border,
        int Muted, int Text, int PrimaryHover, int ScrollHot);

    static Paper PaperOf(BaseTone tone, bool dark) => (tone, dark) switch
    {
        (BaseTone.Cool, false) => new(0xF7F9FB, 0xFFFFFF, 0xEDF1F5, 0xF1F4F8, 0xE4EAF1, 0xDEE5ED, 0xE1E6ED, 0xBAC4D0, 0x7B8695, 0x1B212A, 0x343E4D, 0x9EA9B7),
        (BaseTone.Cool, true) => new(0x171A1F, 0x2E333B, 0x21252B, 0x1E2228, 0x293038, 0x333A43, 0x2A3038, 0x525B67, 0x8E98A6, 0xE2E7EE, 0xC4CCD7, 0x6F7886),
        (BaseTone.Warm, false) => new(0xFAF8F4, 0xFFFFFF, 0xF2EFE9, 0xF5F2ED, 0xEDE8DF, 0xE8E3DA, 0xE7E2D9, 0xC8C2B7, 0x8A847A, 0x23211D, 0x3E3B36, 0xAAA499),
        (BaseTone.Warm, true) => new(0x1C1B19, 0x34322E, 0x252421, 0x22211F, 0x2D2B28, 0x373531, 0x2F2D2A, 0x5A5650, 0x979187, 0xE8E4DC, 0xCEC9C0, 0x7A756D),
        (_, false) => new(0xFAFAFA, 0xFFFFFF, 0xF0F0F1, 0xF4F4F5, 0xE9E9EC, 0xE3E3E6, 0xE5E5E8, 0xC4C5CA, 0x84868D, 0x1E1F23, 0x3A3B41, 0xA6A8AD),
        (_, true) => new(0x1B1B1D, 0x333336, 0x252527, 0x222224, 0x2D2D31, 0x37373B, 0x2E2E32, 0x57585D, 0x94959B, 0xE6E6E9, 0xCBCBD0, 0x78797E),
    };

    static Theme() => Apply(dark: false, scale: 1f, BaseTone.Neutral, Rgb(0x2F62C8), ButtonShape.Rounded, PrimaryStyle.Ink);

    public static void Apply(bool dark, float scale, BaseTone tone, Color accent, ButtonShape shape, PrimaryStyle primary)
    {
        (Dark, Scale, Shape, Primary) = (dark, scale, shape, primary);
        var p = PaperOf(tone, dark);
        Bg = Rgb(p.Bg);
        Raised = Rgb(p.Raised);
        Field = Rgb(p.Field);
        Hover = Rgb(p.Hover);
        Selected = Rgb(p.Selected);
        Press = Rgb(p.Press);
        Line = Rgb(p.Line);
        Border = Rgb(p.Border);
        Muted = Rgb(p.Muted);
        Text = Rgb(p.Text);
        PrimaryHover = Rgb(p.PrimaryHover);
        ScrollHot = Rgb(p.ScrollHot);

        Accent = accent;
        AccentHover = Mix(accent, dark ? Color.White : Color.Black, 0.14f);
        OnAccent = Contrast(accent, Color.White) >= Contrast(accent, Rgb(0x1E1F23)) ? Color.White : Rgb(0x1E1F23);
        Success = Rgb(dark ? 0x7DC08A : 0x2F7D4A);
        Warning = Rgb(dark ? 0xD6AA54 : 0xB07A1E);
        Danger = Rgb(dark ? 0xE8705F : 0xC0392B);
    }

    public static Color Rgb(int hex) => Color.FromArgb((hex >> 16) & 0xFF, (hex >> 8) & 0xFF, hex & 0xFF);

    public static Color Mix(Color a, Color b, float t) =>
        Color.FromArgb((int)Math.Round(a.R + (b.R - a.R) * t), (int)Math.Round(a.G + (b.G - a.G) * t), (int)Math.Round(a.B + (b.B - a.B) * t));

    // Độ tương phản WCAG giữa 2 màu (1–21)
    static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);

        static double Luminance(Color c) => 0.2126 * Lin(c.R) + 0.7152 * Lin(c.G) + 0.0722 * Lin(c.B);
        static double Lin(int v) => v / 255.0 is var s && s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    // Màu nhóm task liên kết (bản cho nền sáng / nền tối): vàng, xanh ngọc, tím, hồng, xanh trời, cam, xanh lá, xám xanh
    static readonly (int Light, int Dark)[] GroupHues =
    [
        (0xD49A00, 0xE8B53A), (0x129C92, 0x3CC3B7), (0x7D5CE6, 0xA08BF5), (0xD9467F, 0xEE79A5),
        (0x2F8FDB, 0x67B2EE), (0xE06A1F, 0xF09455), (0x4E9F35, 0x82C46A), (0x6B7A90, 0xA3B0C2),
    ];

    public static int GroupColorCount => GroupHues.Length;

    /// <summary>Màu đậm của nhóm: viền, chấm màu.</summary>
    public static Color GroupHue(int index) => Rgb(Dark ? GroupHues[index % GroupHues.Length].Dark : GroupHues[index % GroupHues.Length].Light);

    /// <summary>Màu nền nhạt của nhóm, tô sau chữ như bút highlight.</summary>
    public static Color GroupTint(int index) => Mix(GroupHue(index), Bg, Dark ? 0.62f : 0.74f);

    /// <summary>Bán kính bo góc (px) cho khối cao <paramref name="height"/> px: Vuông = 0, Bo góc = <paramref name="rounded"/>, Tròn = nửa chiều cao.</summary>
    public static float Corner(Control control, float height, float rounded) => Shape switch
    {
        ButtonShape.Square => 0,
        ButtonShape.Pill => height / 2,
        _ => Math.Min(height / 2, rounded * Scale * control.DeviceDpi / 96f),
    };

    /// <summary>Windows đang để app ở chế độ tối (Settings › Personalization › Colors).</summary>
    public static bool WindowsDark()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) is 0;
        }
        catch
        {
            return false;
        }
    }

    // --- Kích thước: số đo thiết kế ở 96 DPI, nhân hệ số kích thước rồi đổi theo DPI màn hình ---

    public static int Px(Control control, int value) => control.LogicalToDeviceUnits((int)Math.Round(value * Scale));

    public static Font Ui(float size, FontStyle style = FontStyle.Regular) => new(Sans, size * Scale, style);
    public static Font Semibold(float size) => new(SansStrong, size * Scale);
    public static Font Small(float size) => new(SansSmall, size * Scale);
    public static Font IconFont(float size) => new(Icons, size * Scale);

    /// <summary>Serif cho tiêu đề; Sitka có bản riêng theo cỡ chữ (cỡ quang học) nên chọn bản hợp cỡ.</summary>
    public static Font Heading(float size)
    {
        float pt = size * Scale;
        string sitka = pt >= 18 ? "Sitka Heading Semibold" : pt >= 12 ? "Sitka Subheading Semibold" : "Sitka Text Semibold";
        return HasFont(sitka) ? new Font(sitka, pt) : new Font("Georgia", pt, FontStyle.Bold);
    }

    /// <summary>Ký tự icon theo mã trong font Segoe Fluent Icons / MDL2.</summary>
    public static string Glyph(int code) => ((char)code).ToString();

    /// <summary>Icon Fluent vẽ sẵn thành ảnh nền trong suốt, để đặt cạnh chữ trên nút; gap = khoảng trống bên phải ảnh.</summary>
    public static Bitmap GlyphImage(Control control, int code, Color color, float size, int gap = 0)
    {
        float px = size * Scale * control.DeviceDpi / 72f;
        int side = (int)Math.Ceiling(px) + 2;
        var bmp = new Bitmap(side + gap, side);
        using var g = Graphics.FromImage(bmp);
        using var font = new Font(Icons, px, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.DrawString(Glyph(code), font, brush, new RectangleF(0, 0, side, side), format);
        return bmp;
    }

    static string Pick(string preferred, string fallback) => HasFont(preferred) ? preferred : fallback;

    static bool HasFont(string name)
    {
        using var f = new Font(name, 10f);
        return f.Name == name;
    }

    // --- Thanh tiêu đề: tối theo theme, cùng màu nền app (màu nền chỉ có tác dụng trên Windows 11) ---

    const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void StyleTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return; // chưa có handle → ThemedForm gọi lại lúc tạo handle
        int dark = Dark ? 1 : 0, caption = ColorTranslator.ToWin32(Bg), text = ColorTranslator.ToWin32(Text);
        DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        DwmSetWindowAttribute(form.Handle, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
        DwmSetWindowAttribute(form.Handle, DWMWA_TEXT_COLOR, ref text, sizeof(int));
    }

    // --- Vẽ ---

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius < 0.5f)
        {
            p.AddRectangle(r); // kiểu nút Vuông
            return p;
        }
        float d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color color, RectangleF r, float radius)
    {
        using var path = Round(r, radius);
        using var brush = new SolidBrush(color);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.FillPath(brush, path);
        g.SmoothingMode = old;
    }

    public static void DrawRound(Graphics g, Color color, float width, RectangleF r, float radius)
    {
        using var path = Round(r, radius);
        using var pen = new Pen(color, width);
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.DrawPath(pen, path);
        g.SmoothingMode = old;
    }
}
