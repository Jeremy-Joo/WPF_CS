namespace TekkenStats.Core;

/// <summary>
/// wavu 공식 JSON API 로 수집 → 정규화 → 엑셀. <b>브라우저가 필요 없다.</b>
///
/// 구형 <see cref="WavuCollector"/>(Playwright + HTML + Next 클릭)의 대체품이다.
/// 요청 한 번이면 전체 이력이 오고, Cloudflare 챌린지도 없다.
/// </summary>
public static class WavuApiCollector
{
    /// <summary>wavu 는 랭크전만 저장한다(battle_type=2). 다른 값이 오면 번호를 그대로 드러낸다.</summary>
    private static string TypeLabel(int? t) => t switch
    {
        2 => "Ranked",
        null => "?",
        _ => $"#{t}",
    };

    /// <summary>
    /// replay 를 받아온다 — 캐시가 켜져 있고 유효한 저장본이 있으면 그걸 쓴다.
    /// <paramref name="cacheDir"/> 가 null 이면 캐시를 아예 안 쓴다(항상 wavu 를 때린다).
    /// <paramref name="ttl"/> 이 null 이면 저장본을 영구로 취급한다(<see cref="ReplayCache.TryLoad"/> 참조).
    /// 인구 리포트처럼 여러 명을 반복 수집하는 경로가 이걸 공유한다.
    /// </summary>
    public static async Task<List<Replay>> FetchWithCacheAsync(
        string polarisId, string? cacheDir, TimeSpan? ttl, Action<string> log,
        CancellationToken ct = default)
    {
        string id = Collect.NormalizeId(polarisId);
        if (cacheDir != null)
        {
            var hit = ReplayCache.TryLoad(cacheDir, id, ttl);
            if (hit != null)
            {
                log($"[캐시] {id} — 저장본 사용 (받은 시각 {hit.Value.FetchedAt:yyyy-MM-dd HH:mm}, {hit.Value.Replays.Count}건)");
                return hit.Value.Replays;
            }
        }

        log($"[요청] {WavuApi.Base}/player/{id}/replays  (Accept: application/json)");
        var replays = await WavuApi.FetchReplaysAsync(id, ct);
        log($"[수신] replay {replays.Count}건");
        // 빈 결과·실패는 캐시하지 않는다(위 FetchReplays 가 실패면 예외로 빠짐).
        if (cacheDir != null && replays.Count > 0)
            ReplayCache.Save(cacheDir, id, replays);
        return replays;
    }

    public static async Task<CollectResult> CollectAsync(
        string playerId, DateTime? start, DateTime? end, string outRoot, Action<string> log,
        CancellationToken ct = default, string? cacheDir = null, TimeSpan? ttl = null)
    {
        string id = Collect.NormalizeId(playerId);
        try
        {
            var replays = await FetchWithCacheAsync(id, cacheDir, ttl, log, ct);
            if (replays.Count == 0)
                return new CollectResult(playerId, 0, null, "경기 없음(식별코드 확인)");

            ct.ThrowIfCancellationRequested();

            var (recs, name, stats) = Normalize(replays, id);
            log($"[정규화] 내 경기 {recs.Count}건  이름='{name}'" +
                (stats.Dropped > 0 || stats.Dupes > 0 ? $"  (제외 {stats.Dropped}, 중복 {stats.Dupes})" : ""));

            if (recs.Count == 0)
                return new CollectResult(playerId, 0, null,
                    $"내 경기가 없습니다 — replay {replays.Count}건 중 식별코드 '{id}' 와 맞는 경기가 0건입니다.");

            string tag = Collect.DateTag(start, end);
            if (tag.Length > 0)
            {
                int before = recs.Count;
                recs = Collect.FilterByDate(recs, start, end);
                log($"[기간필터] {recs.Count}/{before}건");
            }

            if (recs.Count == 0)
                return new CollectResult(playerId, 0, null, "경기 없음(기간 확인)");

            string safe = Collect.Sanitize(string.IsNullOrEmpty(name) ? playerId : name);
            string stamp = DateTime.Now.ToString("yyyy_MMdd_HHmmss");   // 생성 시각: 연_월일_시분초
            string outPath = Path.Combine(outRoot, safe, $"{safe}_{id}_wavu{tag}_{stamp}.xlsx");
            string saved = WavuReport.WriteWorkbook(recs, outPath, hasPolaris: true);
            log($"[완료] {saved}");
            return new CollectResult(playerId, recs.Count, saved, "", name, recs);
        }
        catch (OperationCanceledException)
        {
            log("[중지됨] 사용자 요청으로 중지했습니다.");
            return new CollectResult(playerId, 0, null, Collect.CanceledError);
        }
        catch (WavuApiException ex)
        {
            log($"[오류] {ex.Kind}: {ex.Message}");
            return new CollectResult(playerId, 0, null, ex.Message);
        }
        catch (Exception ex)
        {
            log($"[오류 상세] {ex.GetType().Name}: {ex.Message}");
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
                log($"[내부 오류] {inner.GetType().Name}: {inner.Message}");
            return new CollectResult(playerId, 0, null, ex.Message);
        }
    }

    /// <summary>정규화 통계 — 조용한 누락을 로그에 드러내려고 따로 센다.</summary>
    public sealed record Stats(int Total, int Kept, int Dropped, int Dupes);

    /// <summary>
    /// wavu Replay[] → '나' 관점 <see cref="MatchRecord"/>[].
    ///
    /// - p1/p2 중 누가 '나'인지는 polaris_id 로 판별한다.
    /// - 승패는 <c>winner</c> 필드를 신뢰한다(라운드 비교로 재계산하지 않는다).
    /// - <see cref="MatchRecord.MyRating"/> 은 경기 <b>후</b> 값 = before + change.
    ///   사이트 표시(경기 전 값 + 변동)와 대조해 실측으로 확인된 의미다.
    /// - 시각은 KST 벽시계를 담는다 — ewgf 경로(<see cref="EwgfExtractor"/>)와 같은 규약이라
    ///   두 소스의 엑셀이 같은 날짜 기준으로 나온다.
    /// </summary>
    public static (List<MatchRecord> records, string myName, Stats stats) Normalize(
        IEnumerable<Replay> replays, string polarisId)
    {
        var recs = new List<MatchRecord>();
        var seen = new HashSet<string>();
        var pendingRating = new HashSet<string>();   // rating_before=null(TBD) 인 경기
        int total = 0, dropped = 0, dupes = 0;
        string myName = "";
        long nameAt = -1;

        foreach (var b in replays)
        {
            total++;
            bool meIs1 = b.P1PolarisId == polarisId;
            bool meIs2 = b.P2PolarisId == polarisId;
            if (!meIs1 && !meIs2) { dropped++; continue; }

            if (!string.IsNullOrEmpty(b.BattleId) && !seen.Add(b.BattleId!)) { dupes++; continue; }

            int? myRounds = meIs1 ? b.P1Rounds : b.P2Rounds;
            int? oppRounds = meIs1 ? b.P2Rounds : b.P1Rounds;
            if (b.BattleAt == null || myRounds == null || oppRounds == null || b.Winner == null)
            {
                dropped++;
                continue;
            }

            bool iWon = (b.Winner == 1) == meIs1;

            int? myBeforeRaw = meIs1 ? b.P1RatingBefore : b.P2RatingBefore;
            int myBefore = myBeforeRaw ?? 0;
            int myChange = (meIs1 ? b.P1RatingChange : b.P2RatingChange) ?? 0;
            int opBefore = (meIs1 ? b.P2RatingBefore : b.P1RatingBefore) ?? 0;
            int opChange = (meIs1 ? b.P2RatingChange : b.P1RatingChange) ?? 0;

            // 최신 이름: battle_at 이 가장 큰 경기의 내 이름
            if (b.BattleAt.Value >= nameAt)
            {
                nameAt = b.BattleAt.Value;
                myName = (meIs1 ? b.P1Name : b.P2Name) ?? myName;
            }

            string battleId = !string.IsNullOrEmpty(b.BattleId)
                ? b.BattleId!
                : $"{b.BattleAt}:{(meIs1 ? b.P2PolarisId : b.P1PolarisId) ?? ""}";
            if (myBeforeRaw == null) pendingRating.Add(battleId);

            int gv = b.GameVersion ?? 0;
            int? myRank = meIs1 ? b.P1Rank : b.P2Rank;
            int? oppRank = meIs1 ? b.P2Rank : b.P1Rank;

            recs.Add(new MatchRecord
            {
                Dt = KstFromEpoch(b.BattleAt.Value),
                BattleId = battleId,
                Player = (meIs1 ? b.P1Name : b.P2Name) ?? "",
                MyPolaris = polarisId,
                MyChar = WavuChars.Name(meIs1 ? b.P1CharaId : b.P2CharaId),
                MyRating = myBefore + myChange,
                MyDelta = myChange,
                Score = $"{myRounds}-{oppRounds}",
                MyRounds = myRounds.Value,
                OppRounds = oppRounds.Value,
                Result = iWon ? "W" : "L",
                OppRating = opBefore + opChange,
                OppDelta = opChange,
                OppChar = WavuChars.Name(meIs1 ? b.P2CharaId : b.P1CharaId),
                OppName = (meIs1 ? b.P2Name : b.P1Name) ?? "",
                OppPolaris = (meIs1 ? b.P2PolarisId : b.P1PolarisId) ?? "",
                BattleType = TypeLabel(b.BattleType),
                GameVersion = gv,
                // 자릿수로 계산한다 — S4 가 열려도 저절로 따라간다. 경계 날짜를 적지 말 것.
                Season = gv > 0 ? $"S{gv / 10000}" : "?",
                // 단은 숫자다. wavu 가 이름을 노출하지 않아 매핑이 없으므로 '#숫자'로 정직하게 남긴다.
                MyDan = myRank != null ? $"#{myRank}" : "",
                OppDan = oppRank != null ? $"#{oppRank}" : "",
                MyRank = myRank,
                OppRank = oppRank,
                Region = "",   // /replays JSON 에는 지역이 없다(필드 전수 확인)
            });
        }

        recs.Sort((a, b) => a.Dt.CompareTo(b.Dt));

        // 방금 끝난 경기는 wavu 가 아직 레이팅을 계산 안 해 rating_before 가 null 로 온다
        // (사이트에는 'TBD'). 0 으로 두면 그래프가 0 으로 꺼지고 EndRating 이 0 이 되므로,
        // 같은 캐릭터의 직전 레이팅을 이어받는다(변동은 0 유지).
        if (pendingRating.Count > 0)
        {
            var lastByChar = new Dictionary<string, int>();
            foreach (var r in recs)
            {
                if (pendingRating.Contains(r.BattleId))
                    r.MyRating = lastByChar.TryGetValue(r.MyChar, out var prev) ? prev : r.MyRating;
                else
                    lastByChar[r.MyChar] = r.MyRating;
            }
        }

        return (recs, myName, new Stats(total, recs.Count, dropped, dupes));
    }

    /// <summary>epoch(초) → KST 벽시계를 담은 DateTime (ewgf 경로와 같은 규약).</summary>
    private static DateTime KstFromEpoch(long epochSec) =>
        DateTimeOffset.FromUnixTimeSeconds(epochSec).UtcDateTime.AddHours(9);
}
