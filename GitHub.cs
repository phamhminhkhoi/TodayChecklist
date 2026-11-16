using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TodayChecklist;

/// <summary>Lấy trạng thái issue / PR từ GitHub API. Chỉ đọc, không ghi gì lên GitHub.</summary>
static partial class GitHub
{
    /// <summary>Status lấy từ cột Status của GitHub Project: "project:{màu}:{tên}", vd "project:YELLOW:In progress".</summary>
    public const string ProjectPrefix = "project:";

    static readonly HttpClient Http = CreateClient();

    // Token: biến môi trường GH_TOKEN / GITHUB_TOKEN, không có thì mượn của GitHub CLI (`gh auth token`).
    // Chỉ giữ trong bộ nhớ, không lưu xuống DB. Không có token thì chỉ đọc được repo public.
    static readonly Lazy<Task<string?>> Token = new(LoadTokenAsync);

    public sealed class Error(HttpStatusCode code) : Exception($"GitHub trả về {(int)code}")
    {
        public HttpStatusCode Code { get; } = code;
    }

    /// <summary>
    /// Status: cột Status của GitHub Project nếu issue / PR nằm trong project (xem <see cref="ProjectPrefix"/>),
    /// không thì open / draft / merged / pr_closed (PR đóng không merge) / closed (issue xong) / not_planned.
    /// Checklist: link tới comment (hoặc phần mô tả) có coding checklist; null = chưa có.
    /// </summary>
    public sealed record Issue(string Status, string Title, string? Checklist);

    // Phần chung của issue và PR: project + status trong project, mô tả, 100 comment gần nhất
    const string Common = """
        title url body
        projectItems(first: 10, includeArchived: false) {
          nodes { status: fieldValueByName(name: "Status") { ... on ProjectV2ItemFieldSingleSelectValue { name color } } }
        }
        comments(last: 100) { nodes { body url } }
        """;

    const string Query = $$"""
        query($owner: String!, $repo: String!, $number: Int!) {
          repository(owner: $owner, name: $repo) {
            issueOrPullRequest(number: $number) {
              ... on Issue { state stateReason {{Common}} }
              ... on PullRequest { state isDraft {{Common}} }
            }
          }
        }
        """;

    public static async Task<Issue> FetchAsync(string url)
    {
        var (owner, repo, number) = Links.IssueOf(url) ?? throw new ArgumentException("Không phải link GitHub issue / PR", nameof(url));
        // GraphQL bắt buộc có token; không có thì dùng REST (chỉ đọc được open / closed của repo public)
        if (await Token.Value is not { } token) return await FetchRestAsync(owner, repo, number);

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.github.com/graphql")
        {
            Content = JsonContent.Create(new { query = Query, variables = new { owner, repo, number = int.Parse(number) } }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode) throw new Error(response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        // GraphQL báo lỗi trong "errors" với mã 200. Lỗi riêng 1 phần (vd token thiếu quyền đọc project) thì vẫn dùng phần còn lại
        if (Field(Field(Field(root, "data"), "repository"), "issueOrPullRequest") is not { } item)
            throw new Error(FirstError(root) switch
            {
                "RATE_LIMITED" => HttpStatusCode.TooManyRequests,
                "FORBIDDEN" or "INSUFFICIENT_SCOPES" => HttpStatusCode.Forbidden, // token thiếu quyền read:project → gh auth refresh -s read:project
                _ => HttpStatusCode.NotFound,
            });
        return new Issue(ProjectStatus(item) ?? StatusOf(item), Field(item, "title")?.GetString() ?? "", ChecklistOf(item));
    }

    static async Task<Issue> FetchRestAsync(string owner, string repo, string number)
    {
        using var response = await Http.GetAsync($"https://api.github.com/repos/{owner}/{repo}/issues/{number}");
        if (!response.IsSuccessStatusCode) throw new Error(response.StatusCode);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return new Issue(RestStatusOf(json.RootElement), json.RootElement.GetProperty("title").GetString() ?? "", null);
    }

    static JsonElement? Field(JsonElement? obj, string name) =>
        obj is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v) && v.ValueKind != JsonValueKind.Null ? v : null;

    static string? FirstError(JsonElement root) =>
        Field(root, "errors") is { ValueKind: JsonValueKind.Array } errors && errors.GetArrayLength() > 0 ? Field(errors[0], "type")?.GetString() : null;

    static IEnumerable<JsonElement> Nodes(JsonElement item, string connection) =>
        Field(Field(item, connection), "nodes") is { ValueKind: JsonValueKind.Array } nodes ? nodes.EnumerateArray() : [];

    // Nằm trong nhiều project thì lấy project đầu tiên có cột Status
    static string? ProjectStatus(JsonElement item) =>
        Nodes(item, "projectItems")
            .Select(n => Field(n, "status"))
            .Where(s => Field(s, "name")?.GetString() is { Length: > 0 })
            .Select(s => $"{ProjectPrefix}{Field(s, "color")?.GetString() ?? "GRAY"}:{Field(s, "name")!.Value.GetString()}")
            .FirstOrDefault();

    static string StatusOf(JsonElement item)
    {
        string? state = Field(item, "state")?.GetString();
        if (Field(item, "isDraft") is { } draft) // PR
            return state switch { "MERGED" => "merged", "CLOSED" => "pr_closed", _ => draft.GetBoolean() ? "draft" : "open" };
        if (state == "OPEN") return "open";
        return Field(item, "stateReason")?.GetString() is "NOT_PLANNED" or "DUPLICATE" ? "not_planned" : "closed";
    }

    // Khung của /coding-checklist: tiêu đề "Coding Checklist" (heading hoặc chữ đậm),
    // hoặc checklist không có tiêu đề nhưng đủ 2 mục "Impacted Modules" + "Logic Flow"
    [GeneratedRegex(@"^[ \t]{0,3}(?:#{1,6}[ \t]*|\*\*[ \t]*)coding checklist\b", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex ChecklistHeading();

    public static bool IsChecklist(string? text) =>
        text != null && (ChecklistHeading().IsMatch(text) ||
            text.Contains("Impacted Modules", StringComparison.OrdinalIgnoreCase) && text.Contains("Logic Flow", StringComparison.OrdinalIgnoreCase));

    // Link tới checklist mới nhất: comment sau cùng có checklist, không có thì phần mô tả
    static string? ChecklistOf(JsonElement item) =>
        Nodes(item, "comments").Prepend(item)
            .LastOrDefault(n => IsChecklist(Field(n, "body")?.GetString())) is { ValueKind: JsonValueKind.Object } found
            ? Field(found, "url")?.GetString()
            : null;

    // API issues trả cả PR (có thêm field pull_request), nên 1 request là đủ
    static string RestStatusOf(JsonElement issue)
    {
        bool open = issue.GetProperty("state").GetString() == "open";
        if (issue.TryGetProperty("pull_request", out var pr))
        {
            if (open) return issue.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True ? "draft" : "open";
            return pr.TryGetProperty("merged_at", out var merged) && merged.ValueKind == JsonValueKind.String ? "merged" : "pr_closed";
        }
        if (open) return "open";
        return issue.TryGetProperty("state_reason", out var reason) && reason.GetString() == "not_planned" ? "not_planned" : "closed";
    }

    /// <summary>Lý do lỗi dễ hiểu để báo cho người dùng.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        Error { Code: HttpStatusCode.NotFound } => T("không tìm thấy (repo private chưa có quyền, hoặc sai số)", "not found (no access to a private repo, or wrong number)"),
        Error { Code: HttpStatusCode.Unauthorized } => T("token không hợp lệ / hết hạn", "invalid or expired token"),
        Error { Code: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => T("bị giới hạn số lần gọi hoặc không có quyền", "rate limited or no permission"),
        Error e => T($"GitHub trả về lỗi {(int)e.Code}", $"GitHub returned error {(int)e.Code}"),
        TaskCanceledException => T("quá thời gian chờ", "timed out"),
        HttpRequestException => T("không kết nối được GitHub", "couldn't reach GitHub"),
        _ => ex.Message,
    };

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TodayChecklist", "1.0"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    static async Task<string?> LoadTokenAsync()
    {
        var env = Environment.GetEnvironmentVariable("GH_TOKEN") ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
        try
        {
            using var gh = Process.Start(new ProcessStartInfo("gh", "auth token")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            });
            if (gh == null) return null;
            var output = await gh.StandardOutput.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await gh.WaitForExitAsync(timeout.Token);
            return gh.ExitCode == 0 && output.Trim() is { Length: > 0 } token ? token : null;
        }
        catch
        {
            return null; // chưa cài GitHub CLI hoặc chưa đăng nhập
        }
    }
}
