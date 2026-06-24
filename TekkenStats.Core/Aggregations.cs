namespace TekkenStats.Core;

/// <summary>집계 로직(파이썬 build_* 포팅). 모두 LINQ 기반.</summary>
public static class Aggregations
{
    public const int WeakMinGames = 5;
    public const double WeakMaxWr = 50.0;
    public const int H2hMinGames = 2;

    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;
    private static readonly Dictionary<string, int> TypeOrder = new()
    {
        ["Ranked"] = 0, ["Quick"] = 1, ["Player"] = 2, ["Group"] = 3,
    };

    private static double Wr(int w, int total) => total > 0 ? Math.Round(w * 100.0 / total, 2) : 0.0;
    private static double Pct(int n, int total) => total > 0 ? Math.Round(n * 100.0 / total, 2) : 0.0;
    private static double Avg(int n, int games) => games > 0 ? Math.Round((double)n / games, 2) : 0.0;

    // ── Total: 캐릭터별 + ALL ──
    public static Table BuildTotal(IEnumerable<MatchRecord> df)
    {
        var t = new Table("my_char", "Total", "W", "L", "WinRate(%)");
        var groups = df.GroupBy(r => r.MyChar)
            .Select(g => { int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L"); return new { C = g.Key, W = w, L = l, Total = w + l }; })
            .OrderByDescending(x => x.Total).ThenByDescending(x => Wr(x.W, x.Total)).ThenBy(x => x.C, OIC)
            .ToList();
        foreach (var x in groups) t.Add(x.C, x.Total, x.W, x.L, Wr(x.W, x.Total));
        int aw = groups.Sum(x => x.W), al = groups.Sum(x => x.L), at = aw + al;
        t.Add("ALL", at, aw, al, Wr(aw, at));
        return t;
    }

    // ── 상대 캐릭터 피벗 (games / winrate 정렬) ──
    public static Table BuildPivot(IEnumerable<MatchRecord> df, string sortKey = "games")
    {
        var rows = df.GroupBy(r => r.OppChar)
            .Select(g => { int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L"); return new { Opp = g.Key, W = w, L = l, Games = w + l }; });
        rows = sortKey == "winrate"
            ? rows.OrderByDescending(x => Wr(x.W, x.Games)).ThenByDescending(x => x.Games).ThenBy(x => x.Opp, OIC)
            : rows.OrderByDescending(x => x.Games).ThenByDescending(x => Wr(x.W, x.Games)).ThenBy(x => x.Opp, OIC);
        var t = new Table("opp_char", "Games", "W", "L", "WinRate(%)");
        foreach (var x in rows) t.Add(x.Opp, x.Games, x.W, x.L, Wr(x.W, x.Games));
        return t;
    }

    // ── 약점 매치업 (표본 충분 + 승률 낮은 상대) ──
    public static Table BuildWeak(IEnumerable<MatchRecord> df, int minG = WeakMinGames, double maxWr = WeakMaxWr)
    {
        var rows = df.GroupBy(r => r.OppChar)
            .Select(g => { int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L"); return new { Opp = g.Key, W = w, L = l, Games = w + l }; })
            .Where(x => x.Games >= minG && Wr(x.W, x.Games) < maxWr)
            .OrderBy(x => Wr(x.W, x.Games)).ThenByDescending(x => x.Games);
        var t = new Table("opp_char", "Games", "W", "L", "WinRate(%)");
        foreach (var x in rows) t.Add(x.Opp, x.Games, x.W, x.L, Wr(x.W, x.Games));
        return t;
    }

    // ── 라운드 통계 (캐릭터별 + ALL) ──
    public static Table BuildRound(IEnumerable<MatchRecord> df)
    {
        var list = df as IReadOnlyList<MatchRecord> ?? df.ToList();
        var t = new Table("my_char", "Games", "RoundsWon", "RoundsLost", "RoundWR(%)",
            "AvgRoundsWon", "AvgRoundsLost", "CloseGames", "Close(%)", "CloseWins", "CloseWin(%)",
            "CloseLosses", "CloseLoss(%)", "Shutouts_Dealt", "ShutoutWin(%)", "Shutouts_Received", "ShutoutLoss(%)");

        var perChar = list.GroupBy(r => r.MyChar)
            .Select(g => new { C = g.Key, Row = RoundRow(g, g.Key), Games = g.Count() })
            .OrderByDescending(x => x.Games).ToList();
        foreach (var x in perChar) t.Rows.Add(x.Row);
        t.Rows.Add(RoundRow(list, "ALL"));
        return t;
    }

    private static object?[] RoundRow(IEnumerable<MatchRecord> sub, string label)
    {
        var s = sub as IReadOnlyList<MatchRecord> ?? sub.ToList();
        int games = s.Count;
        int rw = s.Sum(r => r.MyRounds), rl = s.Sum(r => r.OppRounds), rtot = rw + rl;
        int close = s.Count(r => Math.Abs(r.MyRounds - r.OppRounds) == 1);
        int closeW = s.Count(r => Math.Abs(r.MyRounds - r.OppRounds) == 1 && r.Result == "W");
        int closeL = s.Count(r => Math.Abs(r.MyRounds - r.OppRounds) == 1 && r.Result == "L");
        int sd = s.Count(r => r.OppRounds == 0);
        int sr = s.Count(r => r.MyRounds == 0);
        return new object?[]
        {
            label, games, rw, rl, Wr(rw, rtot), Avg(rw, games), Avg(rl, games),
            close, Pct(close, games), closeW, Pct(closeW, games), closeL, Pct(closeL, games),
            sd, Pct(sd, games), sr, Pct(sr, games),
        };
    }

    // ── h2h: 식별코드(opp_polaris)+상대캐릭터 기준 ──
    public static Table BuildH2h(IEnumerable<MatchRecord> df, int minG = H2hMinGames)
    {
        var rows = df.GroupBy(r => (r.OppPolaris, r.OppChar))
            .Select(g =>
            {
                int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L");
                string name = g.Where(r => !string.IsNullOrEmpty(r.OppName))
                    .GroupBy(r => r.OppName).OrderByDescending(x => x.Count()).Select(x => x.Key).FirstOrDefault() ?? "(unknown)";
                return new { Name = name, Pol = g.Key.OppPolaris, Opp = g.Key.OppChar, W = w, L = l, Games = w + l, Last = g.Max(r => r.Dt) };
            })
            .Where(x => x.Games >= minG)
            .OrderByDescending(x => x.Games).ThenByDescending(x => Wr(x.W, x.Games)).ThenBy(x => x.Name, OIC);
        var t = new Table("opp_name", "opp_polaris", "opp_char", "Games", "W", "L", "WinRate(%)", "LastPlayed");
        foreach (var x in rows)
            t.Add(x.Name, x.Pol, x.Opp, x.Games, x.W, x.L, Wr(x.W, x.Games), x.Last.ToString("yyyy-MM-dd HH:mm:ss"));
        return t;
    }

    // ── 일별 집계 (날짜 × 캐릭터) ──
    public static Table BuildDaily(IEnumerable<MatchRecord> df)
    {
        var rows = df.GroupBy(r => (Date: r.Dt.Date, r.MyChar))
            .Select(g =>
            {
                var ord = g.OrderBy(r => r.Dt).ToList();
                int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L");
                return new { g.Key.Date, g.Key.MyChar, Games = w + l, W = w, L = l, Delta = g.Sum(r => r.MyDelta), End = ord[^1].MyRating };
            })
            .OrderByDescending(x => x.Date).ThenByDescending(x => x.Games).ThenBy(x => x.MyChar, OIC);
        var t = new Table("Date", "my_char", "Games", "W", "L", "WinRate(%)", "RatingDelta", "EndRating");
        foreach (var x in rows)
            t.Add(x.Date.ToString("yyyy-MM-dd"), x.MyChar, x.Games, x.W, x.L, Wr(x.W, x.Games), x.Delta, x.End);
        return t;
    }

    // ── 캐릭터 × 종류 (종류 순 정렬) ──
    public static Table BuildByType(IEnumerable<MatchRecord> df)
    {
        var rows = df.GroupBy(r => (r.MyChar, r.BattleType))
            .Select(g => { int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L"); return new { g.Key.MyChar, Type = g.Key.BattleType, W = w, L = l, Games = w + l }; })
            .OrderBy(x => TypeOrder.GetValueOrDefault(x.Type, 99)).ThenByDescending(x => x.Games);
        var t = new Table("my_char", "battleType", "Games", "W", "L", "WinRate(%)");
        foreach (var x in rows) t.Add(x.MyChar, x.Type, x.Games, x.W, x.L, Wr(x.W, x.Games));
        return t;
    }

    // ── power_trend (와이드: 캐릭터별 레이팅 컬럼) ──
    public static (Table table, List<string> chars) BuildPowerTrend(IEnumerable<MatchRecord> df)
    {
        var ordered = df.OrderBy(r => r.Dt).ToList();
        var chars = ordered.Select(r => r.MyChar).Distinct().OrderBy(c => c, OIC).ToList();
        var cols = new List<string> { "dt", "my_rating", "my_char", "result" };
        cols.AddRange(chars);
        var t = new Table(cols.ToArray());
        foreach (var r in ordered)
        {
            var row = new object?[cols.Count];
            row[0] = r.Dt.ToString("yyyy-MM-dd HH:mm:ss");
            row[1] = r.MyRating;
            row[2] = r.MyChar;
            row[3] = r.Result;
            for (int i = 0; i < chars.Count; i++)
                row[4 + i] = chars[i] == r.MyChar ? r.MyRating : (object?)null;
            t.Rows.Add(row);
        }
        return (t, chars);
    }

    // ── 캐릭터별 빌더 결과를 my_char 컬럼 붙여 1개로 합침 ──
    public static Table ConsolidatePerChar(IReadOnlyList<MatchRecord> df, List<string> chars,
        Func<IEnumerable<MatchRecord>, Table> builder)
    {
        Table? outT = null;
        foreach (var ch in chars)
        {
            var sub = df.Where(r => r.MyChar == ch).ToList();
            var d = builder(sub);
            if (d.Count == 0) continue;
            outT ??= new Table(new[] { "my_char" }.Concat(d.Columns).ToArray());
            foreach (var row in d.Rows)
                outT.Add(new object?[] { ch }.Concat(row).ToArray());
        }
        outT ??= new Table("my_char");
        int gi = outT.Columns.IndexOf("Games");
        outT.Rows.Sort((a, b) =>
        {
            int c = OIC.Compare(a[0]?.ToString(), b[0]?.ToString());
            if (c != 0) return c;
            if (gi >= 0) return Convert.ToInt32(b[gi]).CompareTo(Convert.ToInt32(a[gi]));
            return 0;
        });
        return outT;
    }

    // ── 종류별/시즌별 요약 (플레이 횟수 + 승률) ──
    public static Table SummaryBy(IEnumerable<MatchRecord> df, Func<MatchRecord, string> key, string keyName,
        Func<string, int>? order = null)
    {
        var rows = df.GroupBy(key)
            .Select(g => { int w = g.Count(r => r.Result == "W"), l = g.Count(r => r.Result == "L"); return new { Key = g.Key, W = w, L = l, Games = w + l }; });
        rows = order != null ? rows.OrderBy(x => order(x.Key)) : rows.OrderByDescending(x => x.Games);
        var t = new Table(keyName, "Games", "W", "L", "WinRate(%)");
        foreach (var x in rows) t.Add(x.Key, x.Games, x.W, x.L, Wr(x.W, x.Games));
        return t;
    }
}
