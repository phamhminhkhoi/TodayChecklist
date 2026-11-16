using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace TodayChecklist;

record Item(long Id, long? ParentId, string Text, bool Done, bool Expanded, string? Url, string? Status, string? StatusUrl, string? Title,
    long? GroupId, string? Note, int Files, string? Checklist);

/// <summary>Một việc đã xong, cho danh sách "Việc đã xong". DoneAt null = xong từ trước khi app ghi giờ.</summary>
record DoneItem(long Id, DateTime Day, DateTime? DoneAt, string Text, string? Title, string? Parent);

/// <summary>Kết quả tìm kiếm. Carried = bản ở ngày cũ đã được chép sang ngày sau.</summary>
record SearchHit(long Id, DateTime Day, string Text, string? Title, string? Note, string? Parent, bool Done, bool Carried);

/// <summary>Lưu checklist vào 1 file SQLite. Mỗi dòng là 1 việc, parent_id tạo thành cây.</summary>
sealed class Store : IDisposable
{
    const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    readonly SqliteConnection _cn;
    SqliteTransaction? _tx;

    public string FilePath { get; }

    public Store(string filePath)
    {
        FilePath = filePath;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _cn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = filePath, ForeignKeys = true }.ToString());
        _cn.Open();
        Exec("""
            CREATE TABLE IF NOT EXISTS items (
                id        INTEGER PRIMARY KEY AUTOINCREMENT,
                day       TEXT    NOT NULL,
                parent_id INTEGER REFERENCES items(id) ON DELETE CASCADE,
                text      TEXT    NOT NULL,
                done      INTEGER NOT NULL DEFAULT 0,
                sort      INTEGER NOT NULL DEFAULT 0,
                expanded  INTEGER NOT NULL DEFAULT 1,
                url       TEXT,
                gh_status     TEXT,   -- project:{màu}:{tên} (cột Status của GitHub Project), hoặc open / draft / merged / pr_closed / closed / not_planned
                gh_status_url TEXT,   -- link lúc lấy status; khác link hiện tại thì status coi như hết hạn
                gh_title      TEXT,   -- tên issue / PR lấy cùng lúc với status
                gh_checklist  TEXT,   -- link comment có coding checklist, lấy cùng lúc với status; NULL = chưa có
                group_id      INTEGER, -- nhóm task liên kết (task_groups.id), cùng nhóm thì tô cùng màu
                done_at       TEXT,   -- lúc đánh dấu xong (giờ máy, yyyy-MM-dd HH:mm:ss); bỏ tick thì xóa
                note          TEXT,   -- ghi chú của việc
                carried       INTEGER NOT NULL DEFAULT 0 -- đã chép sang ngày sau: bản này không còn tính là việc sót
            );
            CREATE INDEX IF NOT EXISTS ix_items_day    ON items(day);
            CREATE INDEX IF NOT EXISTS ix_items_parent ON items(parent_id);
            CREATE TABLE IF NOT EXISTS settings (key TEXT PRIMARY KEY, value TEXT NOT NULL);
            -- Nhóm task liên kết (vd 2 issue ở 2 repo cùng thuộc 1 việc lớn). Chỉ lưu ở máy này, không đụng GitHub.
            CREATE TABLE IF NOT EXISTS task_groups (id INTEGER PRIMARY KEY AUTOINCREMENT, color INTEGER NOT NULL);
            -- Tệp / thư mục đính kèm: chỉ lưu đường dẫn tới chỗ có sẵn trên máy, không chép file
            CREATE TABLE IF NOT EXISTS attachments (
                id      INTEGER PRIMARY KEY AUTOINCREMENT,
                item_id INTEGER NOT NULL REFERENCES items(id) ON DELETE CASCADE,
                path    TEXT    NOT NULL,
                sort    INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS ix_attachments_item ON attachments(item_id);
            """);

        // DB tạo từ bản trước chưa có các cột mới
        foreach (var (column, type) in new[]
                 {
                     ("url", "TEXT"), ("gh_status", "TEXT"), ("gh_status_url", "TEXT"), ("gh_title", "TEXT"), ("group_id", "INTEGER"),
                     ("done_at", "TEXT"), ("note", "TEXT"), ("carried", "INTEGER NOT NULL DEFAULT 0"), ("gh_checklist", "TEXT"),
                 })
        {
            using var has = Cmd("SELECT COUNT(*) FROM pragma_table_info('items') WHERE name = $c", ("$c", column));
            if ((long)has.ExecuteScalar()! != 0) continue;
            Exec($"ALTER TABLE items ADD COLUMN {column} {type}");
            // Dữ liệu cũ chưa ghi việc nào đã chép đi: việc chưa xong mà ngày sau có việc cùng tên, cùng note cha thì coi là đã chép
            if (column == "carried")
                Exec("""
                    UPDATE items SET carried = 1
                    WHERE done = 0 AND EXISTS (
                        SELECT 1 FROM items later
                        LEFT JOIN items lp ON lp.id = later.parent_id
                        LEFT JOIN items ip ON ip.id = items.parent_id
                        WHERE later.day > items.day AND later.text = items.text AND COALESCE(lp.text, '') = COALESCE(ip.text, ''))
                    """);
        }
    }

    public List<Item> Load(DateTime day)
    {
        using var cmd = Cmd("""
            SELECT id, parent_id, text, done, expanded, url, gh_status, gh_status_url, gh_title, group_id, note,
                   (SELECT COUNT(*) FROM attachments a WHERE a.item_id = items.id), gh_checklist
            FROM items WHERE day = $day ORDER BY sort, id
            """, ("$day", Key(day)));
        using var r = cmd.ExecuteReader();
        var list = new List<Item>();
        while (r.Read())
            list.Add(new Item(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2), r.GetBoolean(3), r.GetBoolean(4),
                Text(5), Text(6), Text(7), Text(8), r.IsDBNull(9) ? null : r.GetInt64(9), Text(10), r.GetInt32(11), Text(12)));
        return list;

        string? Text(int i) => r.IsDBNull(i) ? null : r.GetString(i);
    }

    public long Add(DateTime day, long? parentId, string text, bool expanded = true, string? url = null, long? groupId = null)
    {
        using var cmd = Cmd("""
            INSERT INTO items (day, parent_id, text, expanded, url, group_id, sort)
            VALUES ($day, $p, $text, $exp, $url, $g,
                    (SELECT COALESCE(MAX(sort), -1) + 1 FROM items WHERE day = $day AND parent_id IS $p))
            RETURNING id
            """, ("$day", Key(day)), ("$p", parentId), ("$text", text), ("$exp", expanded), ("$url", url), ("$g", groupId));
        return (long)cmd.ExecuteScalar()!;
    }

    public void Rename(long id, string text) =>
        Exec("UPDATE items SET text = $t WHERE id = $id", ("$t", text), ("$id", id));

    public void SetUrl(long id, string? url) =>
        Exec("UPDATE items SET url = $u WHERE id = $id", ("$u", url), ("$id", id));

    public void SetStatus(IEnumerable<(long Id, string Status, string Url, string? Title, string? Checklist)> items) => InTx(() =>
    {
        foreach (var (id, status, url, title, checklist) in items)
            Exec("UPDATE items SET gh_status = $s, gh_status_url = $u, gh_title = $t, gh_checklist = $c WHERE id = $id",
                ("$s", status), ("$u", url), ("$t", title), ("$c", checklist), ("$id", id));
    });

    public void SetExpanded(long id, bool expanded) =>
        Exec("UPDATE items SET expanded = $v WHERE id = $id", ("$v", expanded), ("$id", id));

    /// <summary>Tick xong thì ghi giờ (giữ giờ cũ nếu đã có), bỏ tick thì xóa giờ.</summary>
    public void SetDone(IEnumerable<(long Id, bool Done)> items) => InTx(() =>
    {
        string now = DateTime.Now.ToString(TimeFormat, CultureInfo.InvariantCulture);
        foreach (var (id, done) in items)
            Exec("UPDATE items SET done = $v, done_at = CASE WHEN $v THEN COALESCE(done_at, $now) END WHERE id = $id",
                ("$v", done), ("$now", now), ("$id", id));
    });

    /// <summary>Ghi lại cha + thứ tự cho cả danh sách anh em (dùng sau khi thêm/di chuyển).</summary>
    public void SetOrder(long? parentId, IEnumerable<long> ids) => InTx(() =>
    {
        int i = 0;
        foreach (var id in ids)
            Exec("UPDATE items SET parent_id = $p, sort = $s WHERE id = $id", ("$p", parentId), ("$s", i++), ("$id", id));
    });

    // Việc con, tệp đính kèm tự xóa theo nhờ ON DELETE CASCADE
    public void Delete(long id) => Exec("DELETE FROM items WHERE id = $id", ("$id", id));

    public DateTime? LastDayWithOpenItems(DateTime before) =>
        Day(Cmd("SELECT MAX(day) FROM items WHERE day < $d AND done = 0", ("$d", Key(before))));

    /// <summary>Ngày gần nhất trước <paramref name="before"/> có ghi việc, dù đã xong hết hay chưa.</summary>
    public DateTime? LastDayWithItems(DateTime before) =>
        Day(Cmd("SELECT MAX(day) FROM items WHERE day < $d", ("$d", Key(before))));

    static DateTime? Day(SqliteCommand cmd)
    {
        using (cmd)
            return cmd.ExecuteScalar() is string s ? DateTime.ParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    /// <summary>
    /// Chép các việc chưa xong (giữ nguyên cây, ghi chú, tệp đính kèm) từ ngày <paramref name="from"/> sang <paramref name="to"/>.
    /// Việc trùng tên cùng chỗ thì gộp vào, nên bấm nhiều lần không bị nhân đôi.
    /// </summary>
    public int CarryOver(DateTime from, DateTime to)
    {
        var src = Load(from).ToLookup(i => i.ParentId);
        var have = new Dictionary<(long?, string), long>();
        foreach (var it in Load(to))
            have.TryAdd((it.ParentId, it.Text), it.Id);

        int added = 0;
        InTx(() => Copy(null, null));
        return added;

        void Copy(long? srcParent, long? dstParent)
        {
            foreach (var it in src[srcParent].Where(i => !i.Done))
            {
                if (!have.TryGetValue((dstParent, it.Text), out var id))
                {
                    id = Add(to, dstParent, it.Text, it.Expanded, it.Url, it.GroupId);
                    if (it.Status != null && it.StatusUrl != null) SetStatus([(id, it.Status, it.StatusUrl, it.Title, it.Checklist)]);
                    if (it.Note != null) SetNote(id, it.Note);
                    if (it.Files > 0) SetAttachments(id, Attachments(it.Id));
                    added++;
                }
                Exec("UPDATE items SET carried = 1 WHERE id = $id", ("$id", it.Id));
                Copy(it.Id, id);
            }
        }
    }

    /// <summary>Các ngày trong khoảng còn việc chưa xong và chưa chép sang ngày sau (chấm trên lịch).</summary>
    public HashSet<DateTime> OpenDays(DateTime from, DateTime to)
    {
        using var cmd = Cmd("SELECT DISTINCT day FROM items WHERE day BETWEEN $a AND $b AND done = 0 AND carried = 0", ("$a", Key(from)), ("$b", Key(to)));
        using var r = cmd.ExecuteReader();
        var days = new HashSet<DateTime>();
        while (r.Read()) days.Add(DateTime.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        return days;
    }

    /// <summary>Số việc con, cháu… trong DB (kể cả việc đang bị ẩn khỏi list).</summary>
    public int CountDescendants(long id)
    {
        using var cmd = Cmd("""
            WITH RECURSIVE sub(id) AS (SELECT id FROM items WHERE parent_id = $id UNION ALL SELECT i.id FROM items i JOIN sub ON i.parent_id = sub.id)
            SELECT COUNT(*) FROM sub
            """, ("$id", id));
        return (int)(long)cmd.ExecuteScalar()!;
    }

    // --- Tìm kiếm ---

    /// <summary>
    /// Tìm ở mọi ngày trong tên việc, tên issue, ghi chú; mọi từ gõ vào đều phải có mặt.
    /// Không phân biệt hoa thường, không cần gõ dấu ("cap nhat" ra "cập nhật"). Mới nhất trước.
    /// </summary>
    public List<SearchHit> Search(string query, int limit = 300)
    {
        var words = Fold(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var hits = new List<SearchHit>();
        if (words.Length == 0) return hits;
        using var cmd = Cmd("""
            SELECT i.id, i.day, i.text, i.gh_title, i.note, p.text, i.done, i.carried
            FROM items i LEFT JOIN items p ON p.id = i.parent_id
            ORDER BY i.day DESC, i.sort, i.id
            """);
        using var r = cmd.ExecuteReader();
        while (r.Read() && hits.Count < limit)
        {
            string text = r.GetString(2);
            string? title = r.IsDBNull(3) ? null : r.GetString(3), note = r.IsDBNull(4) ? null : r.GetString(4);
            string hay = Fold($"{text}\n{title}\n{note}");
            if (words.All(hay.Contains))
                hits.Add(new SearchHit(r.GetInt64(0), DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), text, title, note,
                    r.IsDBNull(5) ? null : r.GetString(5), r.GetBoolean(6), r.GetBoolean(7)));
        }
        return hits;
    }

    /// <summary>Chữ thường, bỏ dấu tiếng Việt (đ → d) để so khớp khi tìm.</summary>
    public static string Fold(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c == 'đ' ? 'd' : c);
        return sb.ToString();
    }

    // --- Ghi chú, tệp đính kèm ---

    public string? Note(long id)
    {
        using var cmd = Cmd("SELECT note FROM items WHERE id = $id", ("$id", id));
        return cmd.ExecuteScalar() as string;
    }

    public void SetNote(long id, string? note) =>
        Exec("UPDATE items SET note = $n WHERE id = $id", ("$n", string.IsNullOrWhiteSpace(note) ? null : note), ("$id", id));

    public List<string> Attachments(long itemId)
    {
        using var cmd = Cmd("SELECT path FROM attachments WHERE item_id = $id ORDER BY sort, id", ("$id", itemId));
        using var r = cmd.ExecuteReader();
        var paths = new List<string>();
        while (r.Read()) paths.Add(r.GetString(0));
        return paths;
    }

    /// <summary>Thay toàn bộ danh sách đính kèm của 1 việc (giữ đúng thứ tự truyền vào).</summary>
    public void SetAttachments(long itemId, IEnumerable<string> paths) => InTx(() =>
    {
        Exec("DELETE FROM attachments WHERE item_id = $id", ("$id", itemId));
        int i = 0;
        foreach (var path in paths)
            Exec("INSERT INTO attachments (item_id, path, sort) VALUES ($id, $p, $s)", ("$id", itemId), ("$p", path), ("$s", i++));
    });

    // --- Việc đã xong ---

    /// <summary>Các việc đã xong, mới nhất trước (việc xong từ trước khi app ghi giờ thì xếp theo ngày của nó).</summary>
    public List<DoneItem> Completed(int limit = 500)
    {
        using var cmd = Cmd("""
            SELECT i.id, i.day, i.done_at, i.text, i.gh_title, p.text
            FROM items i LEFT JOIN items p ON p.id = i.parent_id
            WHERE i.done = 1
            ORDER BY COALESCE(i.done_at, i.day || ' 23:59:59') DESC, i.id DESC
            LIMIT $n
            """, ("$n", limit));
        using var r = cmd.ExecuteReader();
        var list = new List<DoneItem>();
        while (r.Read())
            list.Add(new DoneItem(r.GetInt64(0), DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                r.IsDBNull(2) ? null : DateTime.ParseExact(r.GetString(2), TimeFormat, CultureInfo.InvariantCulture),
                r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5)));
        return list;
    }

    // --- Nhóm task liên kết ---

    /// <summary>Màu (chỉ số trong bảng màu nhóm) của mọi nhóm.</summary>
    public Dictionary<long, int> GroupColors()
    {
        using var cmd = Cmd("SELECT id, color FROM task_groups");
        using var r = cmd.ExecuteReader();
        var colors = new Dictionary<long, int>();
        while (r.Read()) colors[r.GetInt64(0)] = r.GetInt32(1);
        return colors;
    }

    public long NewGroup(int color)
    {
        using var cmd = Cmd("INSERT INTO task_groups (color) VALUES ($c) RETURNING id", ("$c", color));
        return (long)cmd.ExecuteScalar()!;
    }

    public void SetGroupColor(long groupId, int color) =>
        Exec("UPDATE task_groups SET color = $c WHERE id = $g", ("$c", color), ("$g", groupId));

    public void SetGroups(IEnumerable<(long Id, long? GroupId)> items) => InTx(() =>
    {
        foreach (var (id, group) in items)
            Exec("UPDATE items SET group_id = $g WHERE id = $id", ("$g", group), ("$id", id));
    });

    public string? Setting(string key)
    {
        using var cmd = Cmd("SELECT value FROM settings WHERE key = $k", ("$k", key));
        return cmd.ExecuteScalar() as string;
    }

    public void SetSettings(params (string Key, string Value)[] items) => InTx(() =>
    {
        foreach (var (key, value) in items)
            Exec("INSERT INTO settings (key, value) VALUES ($k, $v) ON CONFLICT(key) DO UPDATE SET value = excluded.value", ("$k", key), ("$v", value));
    });

    public void Dispose() => _cn.Dispose();

    static string Key(DateTime day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    void InTx(Action body)
    {
        if (_tx != null) { body(); return; }
        using var tx = _cn.BeginTransaction();
        _tx = tx;
        try { body(); tx.Commit(); }
        finally { _tx = null; }
    }

    void Exec(string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(sql, args);
        cmd.ExecuteNonQuery();
    }

    SqliteCommand Cmd(string sql, params (string Name, object? Value)[] args)
    {
        var cmd = _cn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = _tx;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }
}
