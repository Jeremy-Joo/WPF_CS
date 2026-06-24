using Microsoft.Playwright;

namespace TekkenStats.Core;

/// <summary>ewgf 전체 수집 흐름: 페이지 로드(CDP/Cloudflare) → 추출 → 정규화 → 엑셀.</summary>
public static class EwgfCollector
{
    public const string Ewgf = "https://ewgf.gg";

    public sealed record Result(string PlayerId, int Count, string? OutPath, string Error);

    public static async Task<Result> CollectAsync(
        string playerId, DateTime? start, DateTime? end, string outRoot, string profileDir, Action<string> log)
    {
        string url = $"{Ewgf}/player/{playerId}";
        await using var session = new BrowserSession(profileDir, log);
        try
        {
            await session.StartAsync(url);
            // Cloudflare 통과(순수 크롬 상태에서 CDP HTTP title 로 감지) → 통과 후에만 Playwright 연결.
            if (!await session.WaitPastCloudflareAsync())
                return new Result(playerId, 0, null,
                    "Cloudflare verification did not complete. Open ewgf.gg once in normal Chrome on this PC, then run again.");
            await session.ConnectAsync();
            try
            {
                await session.Page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 20_000 });
            }
            catch (Exception) { }  // NetworkIdle 미도달은 무시하고 진행
            await session.Page.WaitForTimeoutAsync(1500);

            string html = await session.GetStableContentAsync();
            var battles = EwgfExtractor.ExtractBattles(html);
            log($"[추출] battle {battles.Count}건");

            var (recs, name) = EwgfExtractor.Normalize(battles, playerId);
            log($"[정규화] 내 경기 {recs.Count}건  이름='{name}'");

            // 날짜 필터 (지정 시)
            string tag = "";
            if (start.HasValue || end.HasValue)
            {
                int before = recs.Count;
                recs = recs.Where(r =>
                    (!start.HasValue || r.Dt.Date >= start.Value.Date) &&
                    (!end.HasValue || r.Dt.Date <= end.Value.Date)).ToList();
                log($"[기간필터] {recs.Count}/{before}건");
                tag = $"_{(start?.ToString("yyyyMMdd") ?? "")}-{(end?.ToString("yyyyMMdd") ?? "")}";
            }

            if (recs.Count == 0)
                return new Result(playerId, 0, null, "경기 없음(식별코드/기간 확인)");

            string safe = Sanitize(string.IsNullOrEmpty(name) ? playerId : name);
            string outPath = Path.Combine(outRoot, safe, $"{safe}_{playerId}_ewgf{tag}.xlsx");
            string saved = EwgfReport.WriteWorkbook(recs, outPath);
            log($"[완료] {saved}");
            return new Result(playerId, recs.Count, saved, "");
        }
        catch (Exception ex)
        {
            return new Result(playerId, 0, null, BrowserSession.Friendly(ex));
        }
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s.Trim();
    }
}
