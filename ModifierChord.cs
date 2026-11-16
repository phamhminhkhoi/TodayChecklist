using System.Runtime.InteropServices;

namespace TodayChecklist;

/// <summary>
/// Bắt tổ hợp chỉ gồm phím bổ trợ Ctrl + Win + Alt từ bất kỳ đâu (RegisterHotKey không nhận tổ hợp thiếu phím chữ).
/// Dùng hook bàn phím mức thấp nhưng chỉ xem 3 phím đó đang giữ hay không, không ghi lại phím nào.
/// Kích hoạt ngay lúc cả 3 cùng được giữ (1 lần cho mỗi lần bấm; nhả 1 phím rồi bấm lại thì kích hoạt tiếp).
/// </summary>
sealed class ModifierChord : IDisposable
{
    const int WH_KEYBOARD_LL = 13, WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, KEYEVENTF_KEYUP = 0x2;
    const byte VK_DUMMY = 0xE8; // mã phím chưa dùng: bấm giả khi đang giữ Win để lúc nhả Win, Windows không bật Start menu

    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    readonly HookProc _proc; // giữ delegate sống suốt đời hook, kẻo bị GC thu mất
    readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    IntPtr _hook;
    bool _fired; // đã kích hoạt cho lần giữ tổ hợp hiện tại

    /// <summary>
    /// Phím đã đăng ký RegisterHotKey cùng Ctrl+Win+Alt (vd F23). Khi có: bấm giả phím này để Windows gửi WM_HOTKEY
    /// cho app, nhờ đó app được Windows cho đưa cửa sổ lên trước. 0 = không có, gọi <see cref="Pressed"/>.
    /// </summary>
    public byte ForwardKey { get; set; }

    /// <summary>Đã bấm đủ Ctrl + Win + Alt (chỉ khi không có <see cref="ForwardKey"/>). Gọi trên luồng giao diện, sau khi hook đã trả về.</summary>
    public event Action? Pressed;

    public ModifierChord()
    {
        _proc = OnKey;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(null), 0);
    }

    public bool Active => _hook != IntPtr.Zero;

    IntPtr OnKey(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
            if (vk != VK_DUMMY && vk != ForwardKey) Track(vk, (int)wParam is WM_KEYDOWN or WM_SYSKEYDOWN);
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    static bool Held(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    void Track(int vk, bool down)
    {
        // Phím của sự kiện này lấy theo sự kiện; phím khác hỏi thẳng Windows, nên lỡ mất 1 sự kiện nhả cũng không bị kẹt
        bool ctrl = vk is 0x11 or 0xA2 or 0xA3 ? down : Held(0x11);
        bool alt = vk is 0x12 or 0xA4 or 0xA5 ? down : Held(0x12);
        bool win = vk is 0x5B or 0x5C ? down : Held(0x5B) || Held(0x5C);
        bool shift = vk is 0x10 or 0xA0 or 0xA1 ? down : Held(0x10); // Ctrl+Shift+Alt+Win là phím Office → không đụng tới
        bool chord = ctrl && alt && win && !shift;
        if (!chord)
        {
            _fired = false;
            return;
        }
        if (_fired) return;
        _fired = true;

        if (ForwardKey != 0)
        {
            // Windows thấy Ctrl+Win+Alt+phím này (đã đăng ký) → gửi WM_HOTKEY; đồng thời là "phím khác" nên nhả Win không bật Start.
            // Bấm sau khi hook trả về: bấm ngay trong hook thì phím giả tới trước phím vừa bấm, Windows chưa thấy đủ 3 phím.
            byte key = ForwardKey;
            void Forward()
            {
                keybd_event(key, 0, 0, UIntPtr.Zero);
                keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            if (_ui != null) _ui.Post(_ => Forward(), null);
            else Forward();
            return;
        }
        keybd_event(VK_DUMMY, 0, 0, UIntPtr.Zero);
        keybd_event(VK_DUMMY, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        if (_ui != null) _ui.Post(_ => Pressed?.Invoke(), null); // không làm gì nặng trong hook, kẻo Windows gỡ hook
        else Pressed?.Invoke();
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }
}
