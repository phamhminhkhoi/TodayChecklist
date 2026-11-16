using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TodayChecklist;

static class Program
{
    [DllImport("user32.dll")]
    static extern bool AllowSetForegroundWindow(int processId);

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Có thể truyền đường dẫn file DB khác qua tham số đầu tiên
        var db = args.Length > 0 ? Path.GetFullPath(args[0])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TodayChecklist", "checklist.db");

        // Mỗi file DB chỉ 1 cửa sổ: mở thêm lần nữa (shortcut, Startup…) thì gọi cửa sổ đang chạy lên rồi thoát,
        // tránh 2 bản cùng ghi 1 DB. Tên theo đường dẫn DB nên bản mở DB khác (DB thử) vẫn chạy song song được.
        var name = "TodayChecklist-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(db.ToLowerInvariant())))[..16];
        using var wake = new EventWaitHandle(false, EventResetMode.AutoReset, name, out bool first);
        if (!first)
        {
            AllowSetForegroundWindow(-1); // cho bản đang chạy được tự đưa mình lên trước
            wake.Set();
            return;
        }

        // Tín hiệu "thoát" cho lệnh deploy: app có thể đang ở khay hệ thống, không có cửa sổ để gửi lệnh đóng
        using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-quit");

        using var store = new Store(db);
        using var form = new MainForm(store);
        var wait = ThreadPool.RegisterWaitForSingleObject(wake, (_, _) => form.BringUp(), null, Timeout.Infinite, executeOnlyOnce: false);
        var waitQuit = ThreadPool.RegisterWaitForSingleObject(quit, (_, _) => form.CloseFromOutside(), null, Timeout.Infinite, executeOnlyOnce: true);
        Application.Run(form);
        wait.Unregister(null);
        waitQuit.Unregister(null);
    }
}
