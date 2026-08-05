using Microsoft.Playwright;

namespace TekkenStats.Core;

/// <summary>ewgf 전체 수집 흐름: 페이지 로드(CDP/Cloudflare) → 추출 → 정규화 → 엑셀.</summary>
public static class EwgfCollector
{
    public const string Ewgf = "https://ewgf.gg";
    public const string CanceledError = Collect.CanceledError;

    public static async Task<CollectResult> CollectAsync(
        string playerId, DateTime? start, DateTime? end, string outRoot, string profileDir, Action<string> log,
        CancellationToken ct = default)
    {
        string url = $"{Ewgf}/player/{playerId}";
        await using var session = new BrowserSession(profileDir, log);
        try
        {
            await session.StartAsync(url, ct);
            // Cloudflare 통과(순수 크롬 상태에서 CDP HTTP title 로 감지) → 통과 후에만 Playwright 연결.
            if (!await session.WaitPastCloudflareAsync(ct: ct))
                return new CollectResult(playerId, 0, null,
                    "Cloudflare verification did not complete. Open ewgf.gg once in normal Chrome on this PC, then run again.");
            await session.ConnectAsync(ct);
            try
            {
                await session.Page.WaitForLoadStateAsync(LoadState.NetworkIdle,
                    new PageWaitForLoadStateOptions { Timeout = 20_000 });
            }
            catch (Exception) { }  // NetworkIdle 미도달은 무시하고 진행
            ct.ThrowIfCancellationRequested();
            await Task.Delay(1500, ct);

            string html = await session.GetStableContentAsync(ct: ct);
            var battles = EwgfExtractor.ExtractBattles(html);
            log($"[추출] battle {battles.Count}건");

            var (recs, name) = EwgfExtractor.Normalize(battles, playerId);
            log($"[정규화] 내 경기 {recs.Count}건  이름='{name}'");

            // 날짜 필터 (지정 시)
            string tag = Collect.DateTag(start, end);
            if (tag.Length > 0)
            {
                int before = recs.Count;
                recs = Collect.FilterByDate(recs, start, end);
                log($"[기간필터] {recs.Count}/{before}건");
            }

            if (recs.Count == 0)
                return new CollectResult(playerId, 0, null, "경기 없음(식별코드/기간 확인)");

            string safe = Collect.Sanitize(string.IsNullOrEmpty(name) ? playerId : name);
            string stamp = DateTime.Now.ToString("yyyy_MMdd_HHmmss");   // 생성 시각: 연_월일_시분초
            string outPath = Path.Combine(outRoot, safe, $"{safe}_{playerId}_ewgf{tag}_{stamp}.xlsx");
            string saved = EwgfReport.WriteWorkbook(recs, outPath);
            log($"[완료] {saved}");
            return new CollectResult(playerId, recs.Count, saved, "", name, recs);
        }
        catch (OperationCanceledException)
        {
            log("[중지됨] 사용자 요청으로 중지했습니다.");
            return new CollectResult(playerId, 0, null, CanceledError);
        }
        catch (Exception ex)
        {
            log($"[오류 상세] {ex.GetType().Name}: {ex.Message}");
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                log($"[내부 오류] {inner.GetType().Name}: {inner.Message}");
            return new CollectResult(playerId, 0, null, BrowserSession.Friendly(ex));
        }
    }
}
