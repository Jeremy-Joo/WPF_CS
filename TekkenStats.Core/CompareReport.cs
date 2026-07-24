using NPOI.SS.UserModel;
using static TekkenStats.Core.Headers;

namespace TekkenStats.Core;

/// <summary>2명 이상의 플레이어 결과를 나란히 비교하는 엑셀 리포트.</summary>
public static class CompareReport
{
    public sealed record Player(string PlayerId, string Name, IReadOnlyList<MatchRecord> Records);

    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;
    private static readonly string[] TypeOrder = { "Ranked", "Quick", "Player", "Group" };
    private static int SeasonOrder(string s) => s switch { "S3" => 0, "S2" => 1, "S1" => 2, _ => 99 };
    private static double Wr(int w, int total) => total > 0 ? Math.Round(w * 100.0 / total, 2) : 0.0;

    /// <summary>표시용 라벨: 이름이 있으면 "이름", 없으면 식별코드. 이름이 같은 사람이 둘 이상이면 뒤에 식별코드 일부를 덧붙여 구분.</summary>
    private static Dictionary<Player, string> BuildLabels(IReadOnlyList<Player> players)
    {
        var byName = players.GroupBy(p => string.IsNullOrEmpty(p.Name) ? p.PlayerId : p.Name, OIC)
            .ToDictionary(g => g.Key, g => g.Count(), OIC);
        var labels = new Dictionary<Player, string>();
        foreach (var p in players)
        {
            string baseName = string.IsNullOrEmpty(p.Name) ? p.PlayerId : p.Name;
            labels[p] = byName[baseName] > 1 ? $"{baseName} ({p.PlayerId[..Math.Min(6, p.PlayerId.Length)]})" : baseName;
        }
        return labels;
    }

    public static string WriteWorkbook(IReadOnlyList<Player> players, string outRoot)
    {
        if (players.Count < 2) throw new ArgumentException("비교하려면 2명 이상 필요합니다.");
        var labels = BuildLabels(players);
        string stamp = DateTime.Now.ToString("yyyy_MMdd_HHmmss");
        string joined = string.Join("_vs_", players.Select(p => Sanitize(labels[p])));
        string fileTag = joined[..Math.Min(80, joined.Length)];
        string outPath = Path.Combine(outRoot, "_compare", $"compare_{fileTag}_{stamp}.xlsx");

        return ExcelWriter.SaveWithFallback(outPath, wb =>
        {
            WriteOverview(wb, players, labels);
            WriteByType(wb, players, labels);
            WriteBySeason(wb, players, labels);
            WriteCharacters(wb, players, labels);
            WriteHeadToHead(wb, players, labels);
            WriteVsCommon(wb, players, labels);

            for (int i = 0; i < wb.NumberOfSheets; i++)
                ExcelWriter.AutoFit(wb, wb.GetSheetAt(i));
        });
    }

    // ── overview: 지표 × 플레이어 (전치 표 — 한 화면에 나란히) ──
    private static void WriteOverview(IWorkbook wb, IReadOnlyList<Player> players, Dictionary<Player, string> labels)
    {
        var sheet = wb.CreateSheet("overview");
        var cols = new List<string> { "지표" };
        cols.AddRange(players.Select(p => labels[p]));
        var t = new Table(cols.ToArray());

        object?[] Row(string metric, Func<Player, object?> val) =>
            new object?[] { metric }.Concat(players.Select(val)).ToArray();

        string MainChar(Player p) => p.Records.GroupBy(r => r.MyChar)
            .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "-";
        int Games(Player p) => p.Records.Count;
        int Wins(Player p) => p.Records.Count(r => r.Result == "W");
        int Losses(Player p) => p.Records.Count(r => r.Result == "L");
        double WinRate(Player p) => Wr(Wins(p), Games(p));
        double RoundWr(Player p)
        {
            int rw = p.Records.Sum(r => r.MyRounds), rl = p.Records.Sum(r => r.OppRounds);
            return Wr(rw, rw + rl);
        }
        double CloseWr(Player p)
        {
            var close = p.Records.Where(r => Math.Abs(r.MyRounds - r.OppRounds) == 1).ToList();
            return Wr(close.Count(r => r.Result == "W"), close.Count);
        }
        double ShutoutDealtPct(Player p) => Games(p) > 0
            ? Math.Round(p.Records.Count(r => r.OppRounds == 0) * 100.0 / Games(p), 2) : 0.0;
        double ShutoutRecvPct(Player p) => Games(p) > 0
            ? Math.Round(p.Records.Count(r => r.MyRounds == 0) * 100.0 / Games(p), 2) : 0.0;
        int MaxPower(Player p) => p.Records.Count > 0 ? p.Records.Max(r => r.MyRating) : 0;
        string TopDan(Player p) => p.Records
            .OrderByDescending(r => r.MyRating).Select(r => r.MyDan).FirstOrDefault(d => !string.IsNullOrEmpty(d)) ?? "-";
        string Period(Player p) => p.Records.Count > 0
            ? $"{p.Records.Min(r => r.Dt):yyyy-MM-dd} ~ {p.Records.Max(r => r.Dt):yyyy-MM-dd}" : "-";
        int CharCount(Player p) => p.Records.Select(r => r.MyChar).Distinct().Count();

        t.Rows.Add(Row("경기 수", p => Games(p)));
        t.Rows.Add(Row("승", p => Wins(p)));
        t.Rows.Add(Row("패", p => Losses(p)));
        t.Rows.Add(Row("경기 승률(%)", p => WinRate(p)));
        t.Rows.Add(Row("라운드 승률(%)", p => RoundWr(p)));
        t.Rows.Add(Row("접전 승률(%)", p => CloseWr(p)));
        t.Rows.Add(Row("완승 비율(%)", p => ShutoutDealtPct(p)));
        t.Rows.Add(Row("완패 비율(%)", p => ShutoutRecvPct(p)));
        t.Rows.Add(Row("주 캐릭터", p => MainChar(p)));
        t.Rows.Add(Row("사용 캐릭터 수", p => CharCount(p)));
        t.Rows.Add(Row("최고 단", p => TopDan(p)));
        t.Rows.Add(Row("최고 텍켄파워", p => MaxPower(p)));
        t.Rows.Add(Row("데이터 기간", p => Period(p)));

        ExcelWriter.WriteTable(wb, sheet, Annotate(t, Style.Plain));
    }

    // ── by_type: 매치 종류 × 플레이어 ──
    private static void WriteByType(IWorkbook wb, IReadOnlyList<Player> players, Dictionary<Player, string> labels)
    {
        var cols = new List<string> { "battleType", "지표" };
        cols.AddRange(players.Select(p => labels[p]));
        var t = new Table(cols.ToArray());

        var types = players.SelectMany(p => p.Records.Select(r => r.BattleType)).Distinct()
            .OrderBy(x => Array.IndexOf(TypeOrder, x) is var i && i >= 0 ? i : 99).ToList();
        foreach (var type in types)
        {
            var games = players.Select(p => p.Records.Count(r => r.BattleType == type)).ToList();
            var wins = players.Select(p => p.Records.Count(r => r.BattleType == type && r.Result == "W")).ToList();
            t.Rows.Add(new object?[] { type, "경기수" }.Concat(games.Cast<object?>()).ToArray());
            t.Rows.Add(new object?[] { type, "승률(%)" }
                .Concat(games.Zip(wins, (g, w) => (object?)Wr(w, g))).ToArray());
        }
        ExcelWriter.WriteTable(wb, wb.CreateSheet("by_type"), t);
    }

    // ── by_season: 시즌 × 플레이어 ──
    private static void WriteBySeason(IWorkbook wb, IReadOnlyList<Player> players, Dictionary<Player, string> labels)
    {
        var cols = new List<string> { "season", "지표" };
        cols.AddRange(players.Select(p => labels[p]));
        var t = new Table(cols.ToArray());

        var seasons = players.SelectMany(p => p.Records.Select(r => r.Season)).Distinct()
            .OrderBy(SeasonOrder).ToList();
        foreach (var season in seasons)
        {
            var games = players.Select(p => p.Records.Count(r => r.Season == season)).ToList();
            var wins = players.Select(p => p.Records.Count(r => r.Season == season && r.Result == "W")).ToList();
            t.Rows.Add(new object?[] { season, "경기수" }.Concat(games.Cast<object?>()).ToArray());
            t.Rows.Add(new object?[] { season, "승률(%)" }
                .Concat(games.Zip(wins, (g, w) => (object?)Wr(w, g))).ToArray());
        }
        ExcelWriter.WriteTable(wb, wb.CreateSheet("by_season"), t);
    }

    // ── characters: 캐릭터 × 플레이어(경기수/승률) ──
    private static void WriteCharacters(IWorkbook wb, IReadOnlyList<Player> players, Dictionary<Player, string> labels)
    {
        var cols = new List<string> { "character", "지표" };
        cols.AddRange(players.Select(p => labels[p]));
        var t = new Table(cols.ToArray());

        var chars = players.SelectMany(p => p.Records.Select(r => r.MyChar)).Distinct()
            .OrderBy(c => c, OIC).ToList();
        foreach (var ch in chars)
        {
            var games = players.Select(p => p.Records.Count(r => r.MyChar == ch)).ToList();
            if (games.All(g => g == 0)) continue;
            var wins = players.Select(p => p.Records.Count(r => r.MyChar == ch && r.Result == "W")).ToList();
            t.Rows.Add(new object?[] { ch, "경기수" }.Concat(games.Cast<object?>()).ToArray());
            t.Rows.Add(new object?[] { ch, "승률(%)" }
                .Concat(games.Zip(wins, (g, w) => (object?)Wr(w, g))).ToArray());
        }
        ExcelWriter.WriteTable(wb, wb.CreateSheet("characters"), t);
    }

    // ── head2head: 서로 직접 붙은 경기 (opp_polaris 로 매칭) ──
    private static void WriteHeadToHead(IWorkbook wb, IReadOnlyList<Player> players, Dictionary<Player, string> labels)
    {
        var summary = new Table("player_a", "player_b", "games", "a_wins", "b_wins", "a_winrate(%)", "last_played");
        var detail = new Table("dt", "player_a", "a_char", "score", "result_for_a", "player_b", "b_char");

        for (int i = 0; i < players.Count; i++)
        for (int j = i + 1; j < players.Count; j++)
        {
            var a = players[i]; var b = players[j];
            var matches = a.Records.Where(r => r.OppPolaris == b.PlayerId).ToList();
            if (matches.Count == 0) continue;

            int aWins = matches.Count(r => r.Result == "W");
            int bWins = matches.Count - aWins;
            var last = matches.Max(r => r.Dt);
            summary.Add(labels[a], labels[b], matches.Count, aWins, bWins,
                Wr(aWins, matches.Count), last.ToString("yyyy-MM-dd HH:mm:ss"));

            foreach (var m in matches.OrderByDescending(r => r.Dt))
                detail.Add(m.Dt.ToString("yyyy-MM-dd HH:mm:ss"), labels[a], m.MyChar, m.Score,
                    m.Result, labels[b], m.OppChar);
        }

        ExcelWriter.WriteTable(wb, wb.CreateSheet("head2head"), summary);
        ExcelWriter.WriteTable(wb, wb.CreateSheet("head2head_detail"), detail);
    }

    // ── vs_common: 둘 다 만난 공통 상대에 대한 승률 비교 ──
    private static void WriteVsCommon(IWorkbook wb, IReadOnlyList<Player> players, Dictionary<Player, string> labels)
    {
        var t = new Table(new[] { "opp_polaris", "opp_name" }
            .Concat(players.SelectMany(p => new[] { $"{labels[p]}_games", $"{labels[p]}_winrate(%)" }))
            .ToArray());

        var oppSets = players.Select(p => p.Records
            .Where(r => !string.IsNullOrEmpty(r.OppPolaris) && r.OppPolaris != p.PlayerId)
            .Select(r => r.OppPolaris).ToHashSet()).ToList();
        var common = oppSets.Aggregate((acc, s) => acc.Intersect(s).ToHashSet());
        if (common.Count == 0) { ExcelWriter.WriteTable(wb, wb.CreateSheet("vs_common"), t); return; }

        var rows = new List<(string pol, string name, object?[] stats, int totalGames)>();
        foreach (var pol in common)
        {
            string name = players.SelectMany(p => p.Records)
                .Where(r => r.OppPolaris == pol && !string.IsNullOrEmpty(r.OppName))
                .GroupBy(r => r.OppName).OrderByDescending(g => g.Count())
                .Select(g => g.Key).FirstOrDefault() ?? pol;

            var stats = new List<object?>();
            int totalGames = 0;
            foreach (var p in players)
            {
                var vs = p.Records.Where(r => r.OppPolaris == pol).ToList();
                int w = vs.Count(r => r.Result == "W");
                stats.Add(vs.Count);
                stats.Add(Wr(w, vs.Count));
                totalGames += vs.Count;
            }
            rows.Add((pol, name, stats.ToArray(), totalGames));
        }
        foreach (var r in rows.OrderByDescending(r => r.totalGames))
            t.Add(new object?[] { r.pol, r.name }.Concat(r.stats).ToArray());

        ExcelWriter.WriteTable(wb, wb.CreateSheet("vs_common"), t);
    }

    private static string Sanitize(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Trim();
    }
}
