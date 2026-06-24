using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TekkenStats.Core;

/// <summary>ewgf 페이지 HTML(RSC flight)에서 경기 추출 + 정규화 (파이썬 ewgf.py 포팅).</summary>
public static class EwgfExtractor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private static readonly Dictionary<string, string> TypeLabel = new()
    {
        ["QUICK_BATTLE"] = "Quick",
        ["RANKED_BATTLE"] = "Ranked",
        ["PLAYER_BATTLE"] = "Player",
        ["GROUP_BATTLE"] = "Group",
    };

    /// <summary>페이지 HTML 에서 battle 객체들을 추출(중복 제거).</summary>
    public static List<Battle> ExtractBattles(string html)
    {
        // RSC flight 청크를 모은다
        var sb = new StringBuilder(html);
        foreach (Match mm in Regex.Matches(html, @"self\.__next_f\.push\(\[\s*1\s*,\s*(.*?)\]\)",
                     RegexOptions.Singleline))
        {
            string chunk = mm.Groups[1].Value.Trim();
            try { sb.Append('\n').Append(JsonSerializer.Deserialize<string>(chunk)); }
            catch { sb.Append('\n').Append(chunk); }
        }
        string u = sb.ToString().Replace("\\\"", "\"").Replace("\\\\", "\\");

        var battles = new List<Battle>();
        var seen = new HashSet<string>();
        int idx = 0;
        while (true)
        {
            int i = u.IndexOf("\"battleAt\"", idx, StringComparison.Ordinal);
            if (i < 0) break;
            int start = u.LastIndexOf('{', i);
            if (start < 0) { idx = i + 10; continue; }

            int depth = 0, end = -1;
            int limit = Math.Min(u.Length, start + 4000);
            for (int j = start; j < limit; j++)
            {
                char ch = u[j];
                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) { end = j + 1; break; }
                }
            }
            if (end < 0) { idx = i + 10; continue; }

            try
            {
                var b = JsonSerializer.Deserialize<Battle>(u.AsSpan(start, end - start), JsonOpts);
                if (b?.BattleType != null && (b.P1PolarisId != null || b.P2PolarisId != null))
                {
                    string key = $"{b.BattleAt}|{b.P1PolarisId}|{b.P2PolarisId}|{b.P1Char}|{b.P2Char}";
                    if (seen.Add(key)) battles.Add(b);
                }
            }
            catch { /* 깨진 조각 무시 */ }
            idx = end;
        }
        return battles;
    }

    private static DateTime? ToKst(string? battleAt)
    {
        if (string.IsNullOrEmpty(battleAt)) return null;
        if (DateTime.TryParse(battleAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var dt))
            return dt.AddHours(9);  // UTC → KST
        return null;
    }

    /// <summary>battle 들을 '나'(mePid) 관점 레코드로 변환. (records, myName) 반환.</summary>
    public static (List<MatchRecord> records, string myName) Normalize(IEnumerable<Battle> battles, string mePid)
    {
        var recs = new List<MatchRecord>();
        foreach (var b in battles)
        {
            bool meIs1;
            if (b.P1PolarisId == mePid) meIs1 = true;
            else if (b.P2PolarisId == mePid) meIs1 = false;
            else continue;

            var dt = ToKst(b.BattleAt);
            if (dt == null) continue;

            int meNum = meIs1 ? 1 : 2;
            int myRounds = (meIs1 ? b.P1RoundsWon : b.P2RoundsWon) ?? 0;
            int oppRounds = (meIs1 ? b.P2RoundsWon : b.P1RoundsWon) ?? 0;
            int gv = b.GameVersion ?? 0;

            recs.Add(new MatchRecord
            {
                Dt = dt.Value,
                Player = (meIs1 ? b.P1Name : b.P2Name) ?? "",
                MyChar = (meIs1 ? b.P1Char : b.P2Char) ?? "",
                MyRating = (meIs1 ? b.P1TekkenPower : b.P2TekkenPower) ?? 0,
                MyDelta = 0,
                Score = $"{myRounds}-{oppRounds}",
                MyRounds = myRounds,
                OppRounds = oppRounds,
                Result = (b.Winner == meNum) ? "W" : "L",
                OppRating = (meIs1 ? b.P2TekkenPower : b.P1TekkenPower) ?? 0,
                OppChar = (meIs1 ? b.P2Char : b.P1Char) ?? "",
                OppName = (meIs1 ? b.P2Name : b.P1Name) ?? "",
                OppPolaris = (meIs1 ? b.P2PolarisId : b.P1PolarisId) ?? "",
                BattleType = b.BattleType != null && TypeLabel.TryGetValue(b.BattleType, out var lbl)
                    ? lbl : (b.BattleType ?? "?"),
                GameVersion = gv,
                Season = gv > 0 ? $"S{gv / 10000}" : "?",
                MyDan = (meIs1 ? b.P1DanRank : b.P2DanRank) ?? "",
                OppDan = (meIs1 ? b.P2DanRank : b.P1DanRank) ?? "",
                Region = (meIs1 ? b.P1RegionId : b.P2RegionId) ?? "",
            });
        }

        // TekkenPower 변화량: 캐릭터별 시간순 차분
        var last = new Dictionary<string, int>();
        foreach (var r in recs.OrderBy(r => r.Dt))
        {
            if (last.TryGetValue(r.MyChar, out var prev))
                r.MyDelta = r.MyRating - prev;
            last[r.MyChar] = r.MyRating;
        }

        string myName = recs
            .Where(r => !string.IsNullOrEmpty(r.Player))
            .GroupBy(r => r.Player)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "";

        return (recs, myName);
    }
}
