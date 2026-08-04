using System.Text.Json;
using System.Text.RegularExpressions;

namespace TekkenStats.Core;

/// <summary>
/// tekken8stats 관리자 페이지가 내보내는 접속 통계 JSON.
/// (예: <c>tekken8stats_admin_28d_20260803_1052.json</c>)
///
/// 전적이 아니라 <b>누가 어떤 식별코드를 조회했는가</b>의 기록이다.
/// 여기서 두 가지가 나온다 — 이용 통계(<see cref="UsageReport"/>)와,
/// 실제 조회된 <b>식별코드 목록</b>(그대로 수집기 입력이 된다).
/// </summary>
public sealed class AdminLog
{
    public sealed record PlayerStat(string Id, string Name, int Views, int Users,
        string FirstDate, string LastDate, int DaysSeen);
    public sealed record DailyStat(string Date, int Views, int Users);
    public sealed record SourceStat(string Source, int Users);

    public int Days { get; set; }
    public int TotalViews { get; set; }
    public int UniquePlayers { get; set; }
    public List<PlayerStat> Players { get; set; } = new();
    public List<DailyStat> Daily { get; set; } = new();
    public List<SourceStat> Sources { get; set; } = new();

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    /// <summary>관리자 JSON 이면 파싱, 아니면 null (다른 형식의 파일일 수 있으므로 예외로 만들지 않는다).</summary>
    public static AdminLog? TryParse(string json)
    {
        try
        {
            var log = JsonSerializer.Deserialize<AdminLog>(json, Opts);
            // players 가 없으면 이 형식이 아니다 — 빈 결과를 성공으로 위장하지 않는다.
            return log is { Players.Count: > 0 } ? log : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>실제 데이터가 있는 날짜 범위. 파일명의 '28d' 와 다를 수 있어 따로 계산한다.</summary>
    public (string First, string Last) DateRange()
    {
        if (Daily.Count == 0) return ("", "");
        var dates = Daily.Select(x => x.Date).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return (dates[0], dates[^1]);
    }
}

/// <summary>식별코드 목록 불러오기 — 관리자 JSON / CSV / 텍스트 공통.</summary>
public static class IdImport
{
    /// <summary>연속 12자 영숫자, 또는 4-4-4 대시 표기. wavu 가 실제 쓰는 두 가지뿐이다.</summary>
    private static readonly Regex IdRe = new(
        @"\b(?:[A-Za-z0-9]{12}|[A-Za-z0-9]{4}-[A-Za-z0-9]{4}-[A-Za-z0-9]{4})\b",
        RegexOptions.Compiled);

    public sealed record Item(string Id, string Name, int Views);

    public sealed record Result(IReadOnlyList<Item> Items, AdminLog? Log, string Kind);

    /// <summary>
    /// 파일에서 식별코드를 뽑는다. 관리자 JSON 이면 조회수 순으로 정렬해 돌려주고,
    /// 그 외(텍스트/CSV)는 나타난 순서대로 중복만 제거한다.
    /// </summary>
    public static Result FromFile(string path)
    {
        string text = File.ReadAllText(path);

        var log = AdminLog.TryParse(text);
        if (log != null)
        {
            var items = log.Players
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .OrderByDescending(p => p.Views).ThenByDescending(p => p.Users)
                .Select(p => new Item(Collect.NormalizeId(p.Id), p.Name ?? "", p.Views))
                .ToList();
            return new Result(Dedupe(items), log, "관리자 JSON");
        }

        // 텍스트/CSV — 식별코드 표기만 골라낸다.
        // 닉네임이 12자 영숫자면 식별코드와 구분이 원리적으로 불가능하다(웹 token.ts 사고 이력).
        // 여기서는 목록 채우기가 목적이라 통과시키고, 틀리면 수집 단계에서 '없는 플레이어'로 드러난다.
        var found = IdRe.Matches(text)
            .Select(m => new Item(Collect.NormalizeId(m.Value), "", 0))
            .ToList();
        return new Result(Dedupe(found), null, Path.GetExtension(path).ToLowerInvariant() == ".csv" ? "CSV" : "텍스트");
    }

    private static List<Item> Dedupe(IEnumerable<Item> items)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var outp = new List<Item>();
        foreach (var it in items)
            if (it.Id.Length > 0 && seen.Add(it.Id)) outp.Add(it);
        return outp;
    }
}
