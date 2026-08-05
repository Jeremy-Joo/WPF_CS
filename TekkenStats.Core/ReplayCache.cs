using System.Text.Json;

namespace TekkenStats.Core;

/// <summary>
/// wavu replay 로컬 캐시. 같은 식별코드를 유효기간 안에 다시 조회하면 wavu 를 안 때리고
/// 저장본을 쓴다. 인구 리포트에서 147명을 반복 수집하는 비용을 줄이려는 것이다.
///
/// <b>원본 Replay[] 를 저장한다</b> — 정규화 결과(MatchRecord)가 아니라. 정규화는 값싸고,
/// 원본을 들고 있어야 나중에 집계 규칙이 바뀌어도 재수집 없이 다시 뽑을 수 있다.
/// (웹의 Blob 캐시가 같은 방침 — 원본을 캐시하고 매번 정규화한다.)
///
/// 실패는 캐시하지 않는다. 없는 식별코드/차단을 저장하면 유효기간 동안 계속 틀린 답을 준다.
/// </summary>
public static class ReplayCache
{
    /// <summary>기본 캐시 위치 — 결과 폴더 아래 <c>.cache</c>. 사람이 지워도 되는 곳.</summary>
    public static string DefaultDir(string outRoot) => Path.Combine(outRoot, ".cache");

    private sealed record Entry(string PolarisId, DateTime FetchedAt, List<Replay> Replays);

    /// <summary>
    /// 유효기간 안의 저장본이 있으면 (replays, 저장시각) 반환. 없거나 만료면 null.
    /// <paramref name="ttl"/> 이 null 이면 <b>영구</b> — 저장된 시각과 무관하게 항상 유효하다
    /// (인구 리포트처럼 "한 번 받으면 다시 안 받는다"가 정책인 경로가 쓴다).
    /// </summary>
    public static (List<Replay> Replays, DateTime FetchedAt)? TryLoad(
        string cacheDir, string polarisId, TimeSpan? ttl)
    {
        try
        {
            string path = PathFor(cacheDir, polarisId);
            if (!File.Exists(path)) return null;

            var entry = JsonSerializer.Deserialize<Entry>(File.ReadAllText(path));
            if (entry?.Replays == null) return null;
            // 만료 판정은 '지금'을 쓴다 — 저장 시각 + ttl 이 지났으면 없는 것으로 친다.
            if (ttl != null && DateTime.Now - entry.FetchedAt > ttl) return null;
            return (entry.Replays, entry.FetchedAt);
        }
        catch { return null; }   // 손상된 캐시는 없는 것으로 — 조용히 재수집한다
    }

    /// <summary>저장. 실패해도 던지지 않는다(캐시는 성능일 뿐, 수집 자체를 막으면 안 된다).</summary>
    public static void Save(string cacheDir, string polarisId, List<Replay> replays)
    {
        try
        {
            Directory.CreateDirectory(cacheDir);
            var entry = new Entry(polarisId, DateTime.Now, replays);
            File.WriteAllText(PathFor(cacheDir, polarisId), JsonSerializer.Serialize(entry));
        }
        catch { /* 저장 실패는 무시 — 다음 조회 때 다시 받으면 된다 */ }
    }

    /// <summary>캐시 JSON 문자열에서 '받은 시각'만 뽑는다. 병합 시 어느 쪽이 최신인지 판단용.</summary>
    public static DateTime? FetchedAtOf(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("FetchedAt", out var v) && v.TryGetDateTime(out var dt))
                return dt;
        }
        catch { /* 손상/형식 불일치는 '모름' */ }
        return null;
    }

    /// <summary>현재 캐시 상태 (파일 수, 총 바이트). 폴더가 없으면 (0,0).</summary>
    public static (int Files, long Bytes) Stats(string cacheDir)
    {
        if (!Directory.Exists(cacheDir)) return (0, 0);
        long bytes = 0;
        var files = Directory.GetFiles(cacheDir, "*.json");
        foreach (var f in files)
            try { bytes += new FileInfo(f).Length; } catch { /* 접근 실패 파일은 건너뜀 */ }
        return (files.Length, bytes);
    }

    /// <summary>캐시 파일을 전부 지운다. 지운 (파일 수, 바이트) 반환. 폴더 자체는 남긴다.</summary>
    public static (int Files, long Bytes) Clear(string cacheDir)
    {
        if (!Directory.Exists(cacheDir)) return (0, 0);
        int n = 0; long bytes = 0;
        foreach (var f in Directory.GetFiles(cacheDir, "*.json"))
        {
            try
            {
                bytes += new FileInfo(f).Length;
                File.Delete(f);
                n++;
            }
            catch { /* 잠김/삭제 실패는 건너뛴다 — 부분 삭제라도 진행 */ }
        }
        return (n, bytes);
    }

    private static string PathFor(string cacheDir, string polarisId) =>
        Path.Combine(cacheDir, $"{Collect.NormalizeId(polarisId)}.json");
}
