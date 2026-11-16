using System.Diagnostics;
using System.Text.RegularExpressions;

namespace TodayChecklist;

/// <summary>Xử lý link gắn vào việc, có ưu tiên cho GitHub issue / PR.</summary>
static partial class Links
{
    // https://github.com/{owner}/{repo}[/issues|pull/{số}]
    [GeneratedRegex(@"^https?://(?:www\.)?github\.com/([\w.-]+)/([\w.-]+?)(?:\.git)?(?:/(?:issues|pull)/(\d+))?(?:[/?#].*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubUrl();

    // Tên việc bắt đầu bằng "#58" → số issue
    [GeneratedRegex(@"^#(\d+)\b")]
    private static partial Regex IssueRef();

    // Tên việc chỉ là số issue: "#58" hoặc "repo#58"
    [GeneratedRegex(@"^[\w.-]*#\d+$")]
    private static partial Regex BareRef();

    /// <summary>Tên việc chỉ có số issue (chưa có mô tả) → nên hiện kèm tên issue lấy từ GitHub.</summary>
    public static bool IsBareRef(string text) => BareRef().IsMatch(text.Trim());

    public static bool IsWebUrl(string text) =>
        Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>"https://github.com/owner/repo" từ mọi link GitHub (repo, issue, PR…), không phải GitHub thì null.</summary>
    public static string? RepoBase(string url) =>
        GitHubUrl().Match(url.Trim()) is { Success: true } m ? $"https://github.com/{m.Groups[1].Value}/{m.Groups[2].Value}" : null;

    /// <summary>(owner, repo, số) nếu là link GitHub issue / PR, không thì null.</summary>
    public static (string Owner, string Repo, string Number)? IssueOf(string? url) =>
        url != null && GitHubUrl().Match(url.Trim()) is { Success: true } m && m.Groups[3].Success
            ? (m.Groups[1].Value, m.Groups[2].Value, m.Groups[3].Value)
            : null;

    /// <summary>Việc tên "#18 …" nằm dưới note có link repo → link tới issue 18 của repo đó.</summary>
    public static string? IssueLink(string text, string? repoBase) =>
        repoBase != null && IssueRef().Match(text) is { Success: true } m ? $"{repoBase}/issues/{m.Groups[1].Value}" : null;

    // Link GitHub issue / PR nằm ở đâu đó trong chữ gõ vào (tới khoảng trắng kế tiếp)
    [GeneratedRegex(@"https?://(?:www\.)?github\.com/[\w.-]+/[\w.-]+/(?:issues|pull)/\d+\S*", RegexOptions.IgnoreCase)]
    private static partial Regex IssueUrlInText();

    /// <summary>
    /// Gõ/dán link làm tên việc → tách thành (tên gọn, link gắn vào việc).
    /// Có link issue / PR GitHub (dán riêng hoặc kèm chữ) → "#39", phần chữ còn lại giữ phía sau ("#39 sửa grid");
    /// nguyên 1 link repo GitHub → tên repo; link khác giữ nguyên làm tên.
    /// </summary>
    public static (string Text, string? Url) FromInput(string input)
    {
        var text = input.Trim();
        if (IssueUrlInText().Match(text) is { Success: true } link && IssueOf(link.Value) is { } issue)
        {
            string rest = Regex.Replace($"{text[..link.Index]} {text[(link.Index + link.Length)..]}", @"\s+", " ").Trim();
            return (rest.Length == 0 ? $"#{issue.Number}" : $"#{issue.Number} {rest}", link.Value);
        }
        if (text.Contains(' ') || !IsWebUrl(text)) return (text, null);
        return GitHubUrl().Match(text) is { Success: true } repo ? (repo.Groups[2].Value, text) : (text, text);
    }

    public static void Open(string url)
    {
        if (IsWebUrl(url)) Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
