using Microsoft.Playwright;

namespace TekkenStats.Core;

/// <summary>wavu 전체 수집: 페이지 로드 → Next 클릭 누적 → 날짜필터 → 엑셀. (파이썬 main.py run 포팅)</summary>
public static class WavuCollector
{
    public const string Base = "https://wank.wavu.wiki";
    public const int MaxPages = 5000;
    public const int SleepMs = 800;

    public sealed record Result(string PlayerId, int Count, string? OutPath, string Error);

    public static async Task<Result> CollectAsync(
        string playerId, DateTime? start, DateTime? end,
        string outRoot, string profileDir, Action<string> log)
    {
        string urlId = new string(playerId.Where(char.IsLetterOrDigit).ToArray());
        string baseUrl = $"{Base}/player/{urlId}";

        await using var session = new BrowserSession(profileDir, log);
        try
        {
            await session.StartAsync(baseUrl);
            // Cloudflare 통과(순수 크롬 상태에서 CDP HTTP title 로 감지) → 통과 후에만 Playwright 연결.
            if (!await session.WaitPastCloudflareAsync())
                return new Result(playerId, 0, null,
                    "Cloudflare verification did not complete. Open wank.wavu.wiki once in normal Chrome on this PC, then run again.");
            await session.ConnectAsync();
            await session.SettleRowsAsync();

            var all = new List<MatchRecord>();
            var seen = new HashSet<string>();
            string? playerName = null;

            string html = await session.GetStableContentAsync();
            playerName ??= WavuParser.ExtractPlayerName(html);
            int kept = Accumulate(html, start, end, all, seen, out DateTime? oldest);
            log($"[OK] page=1  kept={kept}  total={all.Count}");

            bool stop = start.HasValue && oldest.HasValue && oldest.Value.Date < start.Value.Date;
            for (int i = 2; i <= MaxPages && !stop; i++)
            {
                var loc = session.Page.Locator("a:has-text('Next page')").First;
                if (await loc.CountAsync() == 0) { log($"[끝] Next page 없음 (page={i - 1})"); break; }
                string prev = session.Page.Url;
                await loc.ClickAsync();
                try { await session.Page.WaitForURLAsync(u => u != prev, new PageWaitForURLOptions { Timeout = 60_000 }); }
                catch (Exception) { }
                await session.SettleRowsAsync();
                if (session.Page.Url == prev) { log("[끝] 페이지 이동 없음 → 중단"); break; }

                html = await session.GetStableContentAsync();
                int before = all.Count;
                kept = Accumulate(html, start, end, all, seen, out oldest);
                int added = all.Count - before;
                log($"[OK] page={i}  kept={kept}  new={added}  total={all.Count}");
                if (added == 0) { log("[끝] 새 경기 없음 → 중단"); break; }
                if (start.HasValue && oldest.HasValue && oldest.Value.Date < start.Value.Date)
                {
                    log($"[중단] start({start:yyyy-MM-dd}) 이전 도달");
                    break;
                }
                await session.Page.WaitForTimeoutAsync(SleepMs);
            }

            if (all.Count == 0) return new Result(playerId, 0, null, "경기 없음");

            string name = Sanitize(string.IsNullOrEmpty(playerName) ? playerId : playerName!);
            string outPath = Path.Combine(outRoot, name, $"{name}_{urlId}_wavu.xlsx");
            string saved = WavuReport.WriteWorkbook(all, outPath);
            log($"[완료] {saved}");
            return new Result(playerId, all.Count, saved, "");
        }
        catch (Exception ex)
        {
            return new Result(playerId, 0, null, BrowserSession.Friendly(ex));
        }
    }

    private static int Accumulate(string html, DateTime? start, DateTime? end,
        List<MatchRecord> all, HashSet<string> seen, out DateTime? oldest)
    {
        oldest = null;
        int kept = 0;
        foreach (var r in WavuParser.ParseGames(html))
        {
            if (oldest == null || r.Dt < oldest) oldest = r.Dt;
            if (start.HasValue && r.Dt.Date < start.Value.Date) continue;
            if (end.HasValue && r.Dt.Date > end.Value.Date) continue;
            string key = $"{r.Dt:yyyy-MM-dd HH:mm:ss}|{r.Player}|{r.Score}|{r.OppName}";
            if (!seen.Add(key)) continue;
            all.Add(r);
            kept++;
        }
        return kept;
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }
}
