using static TekkenStats.Core.Headers;

namespace TekkenStats.Core;

/// <summary>
/// 인구 리포트 — 여러 플레이어의 전적을 <b>한 덩어리로</b> 집계한다.
///
/// <see cref="CompareReport"/> 와 목적이 다르다. 저쪽은 2~4명을 나란히 놓고 맞대결까지 보는
/// '누가 더 잘하나'이고, 이쪽은 수십~수백 명을 합쳐 <b>"이 집단은 어떤 사람들인가"</b>를 본다.
/// 관리자 로그의 조회 상위 N명에 물리면 '우리 사이트 이용자층' 리포트가 된다.
///
/// 조회수(<see cref="Entry.Views"/>)는 있으면 같이 싣고 없으면 0 이다 —
/// 관리자 JSON 없이 그냥 식별코드 목록으로 돌려도 나머지는 다 나온다.
/// </summary>
public static class PopulationReport
{
    public sealed record Entry(string PlayerId, string Name, int Views, IReadOnlyList<MatchRecord> Records);

    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;

    private static double Wr(int w, int total) => total > 0 ? Math.Round(w * 100.0 / total, 2) : 0.0;
    private static double Pct(int n, int total) => total > 0 ? Math.Round(n * 100.0 / total, 2) : 0.0;

    public static string WriteWorkbook(IReadOnlyList<Entry> entries, string outRoot)
    {
        var valid = entries.Where(e => e.Records.Count > 0).ToList();
        if (valid.Count == 0) throw new InvalidOperationException("전적이 있는 플레이어가 없습니다.");

        var all = valid.SelectMany(e => e.Records).ToList();
        string stamp = DateTime.Now.ToString("yyyy_MMdd_HHmmss");
        string outPath = Path.Combine(outRoot, $"인구리포트_{valid.Count}명_{stamp}.xlsx");

        return ExcelWriter.SaveWithFallback(outPath, wb =>
        {
            WriteSheet(wb, "summary", BuildSummary(valid, all));
            WriteSheet(wb, "players", Annotate(BuildPlayers(valid), Style.Bracket));
            WriteSheet(wb, "chars", Annotate(BuildChars(valid, all), Style.Bracket));
            WriteSheet(wb, "vs_chars", Annotate(Aggregations.BuildPivot(all), Style.Bracket));
            WriteSheet(wb, "rating_dist", BuildRatingDist(valid));
            WriteSheet(wb, "wr_dist", BuildWrDist(valid));
            WriteSheet(wb, "games_dist", BuildGamesDist(valid));
            var rankDist = BuildRankDist(valid);   // 단(段) 정보는 wavu JSON 만 준다
            if (rankDist.Count > 0) WriteSheet(wb, "rank_dist", rankDist);
            WriteSheet(wb, "time_patterns", Annotate(Aggregations.BuildTimePatterns(all), Style.Bracket));
            WriteSheet(wb, "season", Annotate(
                Aggregations.SummaryBy(all, r => r.Season, "Season",
                    s => s.Length > 1 && int.TryParse(s.AsSpan(1), out int n) ? n : 99),
                Style.Bracket));
            WriteSheet(wb, "round_stats", Annotate(Aggregations.BuildRound(all), Style.Round));

            for (int i = 0; i < wb.NumberOfSheets; i++)
                ExcelWriter.AutoFit(wb, wb.GetSheetAt(i));
        });
    }

    private static void WriteSheet(NPOI.SS.UserModel.IWorkbook wb, string name, Table t)
    {
        var sheet = wb.CreateSheet(ExcelWriter.SafeSheetName(name));
        ExcelWriter.WriteTable(wb, sheet, t);
    }

    private static Table BuildSummary(List<Entry> valid, List<MatchRecord> all)
    {
        var t = new Table("항목", "값", "설명");
        int w = all.Count(r => r.Result == "W");

        t.Add("플레이어 수", valid.Count, "전적이 있는 사람만");
        t.Add("총 경기 수", all.Count, "");
        t.Add("1인당 평균 경기", Math.Round((double)all.Count / valid.Count, 1), "");
        t.Add("1인당 중앙값 경기", Median(valid.Select(e => (double)e.Records.Count).ToList()),
            "평균과 크게 벌어지면 소수 헤비유저가 끌어올린 것");
        t.Add("전체 승률", Wr(w, all.Count),
            "%. 각자 자기 관점이라 50%에 수렴하는 게 정상 — 크게 벗어나면 표본이 한쪽으로 치우친 것");
        t.Add("데이터 범위", $"{all.Min(r => r.Dt):yyyy-MM-dd} ~ {all.Max(r => r.Dt):yyyy-MM-dd}", "");
        t.Add("사용된 캐릭터 종류", all.Select(r => r.MyChar).Distinct().Count(), "");

        var ratings = valid.Select(e => (double)e.Records.OrderBy(r => r.Dt).Last().MyRating)
            .Where(x => x > 0).ToList();
        if (ratings.Count > 0)
        {
            t.Add("최종 레이팅 중앙값", Median(ratings), "");
            t.Add("최종 레이팅 최소~최대", $"{ratings.Min():0} ~ {ratings.Max():0}", "");
        }
        return t;
    }

    private static double Median(List<double> xs)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToList();
        int m = s.Count / 2;
        return Math.Round(s.Count % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2.0, 1);
    }

    /// <summary>플레이어별 한 줄 요약. 조회수 순(관리자 로그가 있을 때) → 경기수 순.</summary>
    private static Table BuildPlayers(List<Entry> valid)
    {
        var t = new Table("Rank", "opp_name", "opp_polaris", "Views", "Games", "W", "L",
            "WinRate(%)", "MainChar", "MainCharGames", "EndRating", "PeakRating",
            "FirstDate", "LastDate", "ActiveDays");
        int i = 0;
        foreach (var e in valid.OrderByDescending(e => e.Views).ThenByDescending(e => e.Records.Count))
        {
            var recs = e.Records.OrderBy(r => r.Dt).ToList();
            int w = recs.Count(r => r.Result == "W");
            var main = recs.GroupBy(r => r.MyChar).OrderByDescending(g => g.Count()).First();
            t.Add(++i, string.IsNullOrEmpty(e.Name) ? "(이름없음)" : e.Name, e.PlayerId, e.Views,
                recs.Count, w, recs.Count - w, Wr(w, recs.Count),
                main.Key, main.Count(),
                recs[^1].MyRating, recs.Max(r => r.MyRating),
                recs[0].Dt.ToString("yyyy-MM-dd"), recs[^1].Dt.ToString("yyyy-MM-dd"),
                recs.Select(r => r.Dt.Date).Distinct().Count());
        }
        return t;
    }

    /// <summary>
    /// 캐릭터 사용 분포. <b>경기 수와 사람 수를 같이 낸다</b> —
    /// 한 명이 3천 경기를 돌리면 경기 기준만으로는 그 사람의 주캐가 '인기 캐릭터'로 보인다.
    /// </summary>
    private static Table BuildChars(List<Entry> valid, List<MatchRecord> all)
    {
        var t = new Table("my_char", "Players", "PlayerShare(%)", "Games", "GameShare(%)",
            "W", "L", "WinRate(%)", "MainOf");

        var playerCount = new Dictionary<string, int>(OIC);
        var mainCount = new Dictionary<string, int>(OIC);
        foreach (var e in valid)
        {
            foreach (var c in e.Records.Select(r => r.MyChar).Distinct())
                playerCount[c] = playerCount.GetValueOrDefault(c) + 1;
            string main = e.Records.GroupBy(r => r.MyChar)
                .OrderByDescending(g => g.Count()).First().Key;
            mainCount[main] = mainCount.GetValueOrDefault(main) + 1;
        }

        var rows = all.GroupBy(r => r.MyChar)
            .Select(g =>
            {
                int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L");
                return new { Char = g.Key, W = w, L = l, Games = w + l };
            })
            .OrderByDescending(x => x.Games).ThenBy(x => x.Char, OIC);

        foreach (var x in rows)
            t.Add(x.Char, playerCount.GetValueOrDefault(x.Char),
                Pct(playerCount.GetValueOrDefault(x.Char), valid.Count),
                x.Games, Pct(x.Games, all.Count), x.W, x.L, Wr(x.W, x.Games),
                mainCount.GetValueOrDefault(x.Char));
        return t;
    }

    private static readonly (string Label, int Lo, int Hi)[] RatingBands =
    {
        ("~1000", int.MinValue, 1000), ("1000~1200", 1000, 1200), ("1200~1400", 1200, 1400),
        ("1400~1600", 1400, 1600), ("1600~1800", 1600, 1800), ("1800~2000", 1800, 2000),
        ("2000~", 2000, int.MaxValue),
    };

    /// <summary>최종 레이팅 분포 — 이 집단이 어느 층인지.</summary>
    private static Table BuildRatingDist(List<Entry> valid)
    {
        var t = new Table("RatingBand", "Players", "Share(%)", "AvgGames");
        var final = valid
            .Select(e => (Rating: e.Records.OrderBy(r => r.Dt).Last().MyRating, Games: e.Records.Count))
            .Where(x => x.Rating > 0).ToList();
        if (final.Count == 0) return t;

        foreach (var b in RatingBands)
        {
            var inBand = final.Where(x => x.Rating >= b.Lo && x.Rating < b.Hi).ToList();
            if (inBand.Count == 0) continue;   // 빈 구간은 행을 만들지 않는다
            t.Add(b.Label, inBand.Count, Pct(inBand.Count, final.Count),
                Math.Round(inBand.Average(x => (double)x.Games), 1));
        }
        return t;
    }

    // ── 사람별 분포 3종: "어느 구간에 몇 명이 몰려 있나" ──
    // rating_dist(실력)·time_patterns(플레이 시간)는 이미 있고, 아래로 나머지를 채운다.

    private static readonly (string Label, double Lo, double Hi)[] WrBands =
    {
        ("~40% (많이 짐)", double.NegativeInfinity, 40), ("40~45%", 40, 45),
        ("45~50%", 45, 50), ("50~55%", 50, 55), ("55~60%", 55, 60),
        ("60%~ (많이 이김)", 60, double.PositiveInfinity),
    };

    /// <summary>승률 분포 — 이 집단의 실력이 50% 근처에 뭉치는지, 양극단이 있는지.</summary>
    private static Table BuildWrDist(List<Entry> valid)
    {
        var t = new Table("WinRateBand", "Players", "Share(%)", "AvgGames");
        var perPlayer = valid.Select(e =>
        {
            int w = e.Records.Count(r => r.Result == "W");
            return (Wr: w * 100.0 / e.Records.Count, Games: e.Records.Count);
        }).ToList();

        foreach (var b in WrBands)
        {
            var inBand = perPlayer.Where(x => x.Wr >= b.Lo && x.Wr < b.Hi).ToList();
            if (inBand.Count == 0) continue;
            t.Add(b.Label, inBand.Count, Pct(inBand.Count, valid.Count),
                Math.Round(inBand.Average(x => (double)x.Games), 1));
        }
        return t;
    }

    private static readonly (string Label, int Lo, int Hi)[] GamesBands =
    {
        ("~100 (라이트)", 0, 100), ("100~500", 100, 500), ("500~1000", 500, 1000),
        ("1000~3000", 1000, 3000), ("3000~10000", 3000, 10000),
        ("10000~ (헤비)", 10000, int.MaxValue),
    };

    /// <summary>플레이 횟수 분포 — 라이트 유저와 헤비 유저의 비율. 평균이 왜 중앙값과 벌어지는지 여기서 보인다.</summary>
    private static Table BuildGamesDist(List<Entry> valid)
    {
        var t = new Table("GamesBand", "Players", "Share(%)", "TotalGames", "GameShare(%)", "AvgWinRate(%)");
        int allGames = valid.Sum(e => e.Records.Count);

        foreach (var b in GamesBands)
        {
            var inBand = valid.Where(e => e.Records.Count >= b.Lo && e.Records.Count < b.Hi).ToList();
            if (inBand.Count == 0) continue;
            int bandGames = inBand.Sum(e => e.Records.Count);
            int bandW = inBand.Sum(e => e.Records.Count(r => r.Result == "W"));
            t.Add(b.Label, inBand.Count, Pct(inBand.Count, valid.Count),
                bandGames, Pct(bandGames, allGames), Wr(bandW, bandGames));
        }
        return t;
    }

    /// <summary>
    /// 단(段) 분포 — 각 플레이어의 <b>최종</b> 단 기준 인원.
    /// wavu 가 단 이름을 노출하지 않아 숫자 그대로(<c>#31</c>)다. 크기순으로 정렬해 방향은 정확하다.
    /// 단 정보(MyRank)가 없는 소스면 빈 표 → 시트를 만들지 않는다.
    /// </summary>
    private static Table BuildRankDist(List<Entry> valid)
    {
        var t = new Table("Rank(단)", "Players", "Share(%)", "AvgEndRating");
        var finals = valid
            .Select(e => e.Records.OrderBy(r => r.Dt).Last())
            .Where(r => r.MyRank != null)
            .ToList();
        if (finals.Count == 0) return t;

        foreach (var g in finals.GroupBy(r => r.MyRank!.Value).OrderByDescending(g => g.Key))
            t.Add($"#{g.Key}", g.Count(), Pct(g.Count(), finals.Count),
                Math.Round(g.Average(r => (double)r.MyRating), 0));
        return t;
    }
}
