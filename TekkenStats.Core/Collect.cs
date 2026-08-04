namespace TekkenStats.Core;

/// <summary>수집 데이터 소스.</summary>
public enum DataSource
{
    /// <summary>ewgf.gg — 퀵·랭크·방·그룹 전부. 브라우저(Cloudflare 통과) 필요.</summary>
    Ewgf,

    /// <summary>wank.wavu.wiki 공식 JSON API — 랭크전만. 브라우저 불필요.</summary>
    Wavu,
}

/// <summary>수집 결과 — ewgf/wavu 공통.</summary>
public sealed record CollectResult(
    string PlayerId, int Count, string? OutPath, string Error,
    string PlayerName = "", IReadOnlyList<MatchRecord>? Records = null);

/// <summary>수집기 공통 헬퍼.</summary>
public static class Collect
{
    public const string CanceledError = "취소됨";

    /// <summary>파일명에 못 쓰는 문자를 걷어낸다. 한글은 건드리지 않는다.</summary>
    public static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Trim();
    }

    /// <summary>식별코드에서 영숫자만 남긴다 (`3Jae-qRbd-E8aa` 처럼 붙여넣는 사람이 많다).</summary>
    public static string NormalizeId(string raw) =>
        new(( raw ?? "").Where(char.IsLetterOrDigit).ToArray());

    /// <summary>기간 필터 (KST 날짜 기준, 양끝 포함). 지정이 없으면 원본 그대로.</summary>
    public static List<MatchRecord> FilterByDate(
        IEnumerable<MatchRecord> recs, DateTime? start, DateTime? end) =>
        recs.Where(r =>
            (!start.HasValue || r.Dt.Date >= start.Value.Date) &&
            (!end.HasValue || r.Dt.Date <= end.Value.Date)).ToList();

    /// <summary>파일명에 붙일 기간 태그. 기간 지정이 없으면 빈 문자열.</summary>
    public static string DateTag(DateTime? start, DateTime? end) =>
        start.HasValue || end.HasValue
            ? $"_{start?.ToString("yyyyMMdd") ?? ""}-{end?.ToString("yyyyMMdd") ?? ""}"
            : "";
}
