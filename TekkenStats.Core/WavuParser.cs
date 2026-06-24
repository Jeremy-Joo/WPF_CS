using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace TekkenStats.Core;

/// <summary>wavu wank 플레이어 페이지 HTML 파싱 (파이썬 parse_games_from_html 포팅).</summary>
public static class WavuParser
{
    private static readonly string[] KnownChars =
    {
        "Alisa", "Anna", "Armor King", "Asuka", "Azucena", "Bryan", "Claudio",
        "Clive", "Devil Jin", "Dragunov", "Eddy", "Fahkumram", "Feng", "Heihachi",
        "Hwoarang", "Jack-8", "Jin", "Jun", "Kazuya", "King", "Kuma", "Kunimitsu",
        "Lars", "Law", "Lee", "Leo", "Leroy", "Lidia", "Lili", "Miary Zo", "Nina",
        "Panda", "Paul", "Raven", "Reina", "Shaheen", "Steve", "Victor", "Xiaoyu",
        "Yoshimitsu", "Zafina",
    };
    private static readonly HashSet<string> MultiWord = new() { "Armor King", "Devil Jin", "Miary Zo" };

    private static readonly string CharsAlt =
        string.Join("|", KnownChars.OrderByDescending(c => c.Length).Select(Regex.Escape));

    private static readonly Regex PlayerRe = new(
        @"^(?<nick>.*?)\s*(?<char>" + CharsAlt + @")\s+(?<rating>\d+)\s*(?<delta>[+-]?\d+)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex OppRe = new(
        @"^(?<rating>\d+)\s+(?<delta>[+-]?\d+)\s*(?<char>" + CharsAlt + @")\s*(?<nick>.*?)\s*$",
        RegexOptions.Compiled);
    private static readonly Regex ScoreRe = new(@"^\s*(\d+)\s*-\s*(\d+)\s*$", RegexOptions.Compiled);
    private static readonly Regex KoreanDateRe = new(
        @"(\d{2})\s*년\s*(\d{1,2})\s*월\s*(\d{1,2})\s*일\s*(\d{2}):(\d{2})", RegexOptions.Compiled);
    private static readonly Regex HistEnRe = new(@"\d{1,2}\s+[A-Za-z]{3}\s+\d{2}", RegexOptions.Compiled);
    private static readonly Regex HistKoRe = new(@"\d{1,2}\s*년\s*\d{1,2}\s*월\s*\d{1,2}\s*일", RegexOptions.Compiled);

    private static readonly Dictionary<char, char> Minus = new()
    {
        ['−'] = '-', ['‒'] = '-', ['–'] = '-', ['—'] = '-',
        ['―'] = '-', ['﹣'] = '-', ['－'] = '-', [' '] = ' ',
    };

    private static string NormNum(string s)
    {
        var chars = s.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (Minus.TryGetValue(chars[i], out var v)) chars[i] = v;
        return new string(chars);
    }

    private static int ToIntSigned(string token) =>
        int.Parse(NormNum(token).Trim().Replace("+", ""), CultureInfo.InvariantCulture);

    private static string? ParseDt(string text)
    {
        string s = NormNum(text ?? "").Trim();
        if (s.Length == 0) return null;
        if (DateTime.TryParseExact(s, new[] { "d MMM yy H:mm", "d MMM yy HH:mm" },
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        var m = KoreanDateRe.Match(s);
        if (m.Success)
        {
            try
            {
                var d = new DateTime(2000 + int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value),
                    int.Parse(m.Groups[3].Value), int.Parse(m.Groups[4].Value), int.Parse(m.Groups[5].Value), 0);
                return d.ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { return null; }
        }
        return null;
    }

    private static (string nick, string ch, int rating, int delta) SplitPlayer(string text)
    {
        string s = NormNum(text).Trim();
        var m = PlayerRe.Match(s);
        if (m.Success)
            return (m.Groups["nick"].Value.Trim(), m.Groups["char"].Value,
                    int.Parse(m.Groups["rating"].Value), ToIntSigned(m.Groups["delta"].Value));

        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new FormatException("토큰 수 부족");
        int rating = int.Parse(parts[^2]); int delta = ToIntSigned(parts[^1]);
        string? two = parts.Length >= 4 ? $"{parts[^4]} {parts[^3]}" : null;
        if (two != null && MultiWord.Contains(two))
            return (string.Join(" ", parts[..^4]), two, rating, delta);
        return (string.Join(" ", parts[..^3]), parts[^3], rating, delta);
    }

    private static (string name, string ch, int rating, int delta) SplitOpp(string text)
    {
        string s = NormNum(text).Trim();
        var m = OppRe.Match(s);
        if (m.Success)
            return (m.Groups["nick"].Value.Trim(), m.Groups["char"].Value,
                    int.Parse(m.Groups["rating"].Value), ToIntSigned(m.Groups["delta"].Value));

        var parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) throw new FormatException("토큰 수 부족");
        int rating = int.Parse(parts[0]);
        var md = Regex.Match(parts[1], @"^([+-]?\d+)(.*)$");
        if (!md.Success) throw new FormatException("델타 파싱 실패");
        int delta = ToIntSigned(md.Groups[1].Value);
        string glued = md.Groups[2].Value;
        var rest = (glued.Length > 0 ? new[] { glued } : Array.Empty<string>()).Concat(parts[2..]).ToArray();
        if (rest.Length < 1) throw new FormatException("상대 없음");
        string? two = rest.Length >= 2 ? $"{rest[0]} {rest[1]}" : null;
        if (two != null && MultiWord.Contains(two))
            return (string.Join(" ", rest[2..]), two, rating, delta);
        return (string.Join(" ", rest[1..]), rest[0], rating, delta);
    }

    private static bool LooksLikeNameHistory(IReadOnlyList<string> cells) =>
        cells.Count == 2 && (HistEnRe.IsMatch(cells[1]) || HistKoRe.IsMatch(cells[1]));

    private static MatchRecord? ParseRow(List<string> cells)
    {
        for (int i = 0; i < cells.Count; i++) cells[i] = NormNum(cells[i] ?? "").Trim();
        while (cells.Count > 0 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
        if (cells.Count < 4) return null;  // 이름이력/짧은행 무시

        string? dt = ParseDt(cells[0]);
        if (dt == null) return null;
        var sm = ScoreRe.Match(cells[2]);
        if (!sm.Success) return null;
        int myR = int.Parse(sm.Groups[1].Value), oppR = int.Parse(sm.Groups[2].Value);

        try
        {
            var (player, myChar, myRating, myDelta) = SplitPlayer(cells[1]);
            var (oppName, oppChar, oppRating, _) = SplitOpp(cells[3]);
            return new MatchRecord
            {
                Dt = DateTime.ParseExact(dt, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                Player = player, MyChar = myChar, MyRating = myRating, MyDelta = myDelta,
                Score = $"{myR}-{oppR}", MyRounds = myR, OppRounds = oppR,
                Result = myR > oppR ? "W" : "L",
                OppRating = oppRating, OppChar = oppChar, OppName = oppName,
                OppPolaris = oppName,           // wavu 는 식별코드가 없어 닉네임으로 h2h 그룹화
                BattleType = "Ranked", Season = "?",
            };
        }
        catch { return null; }
    }

    private static string CellText(HtmlNode td)
    {
        var texts = td.DescendantsAndSelf()
            .Where(n => n.NodeType == HtmlNodeType.Text)
            // <script>printDateTime(...)</script> 같은 스크립트/스타일 텍스트는 제외(BS4 동작과 일치)
            .Where(n =>
            {
                var p = n.ParentNode?.Name?.ToLowerInvariant();
                return p != "script" && p != "style";
            })
            .Select(n => HtmlEntity.DeEntitize(n.InnerText).Trim())
            .Where(s => s.Length > 0);
        // 연속 공백을 하나로 (시(時)가 공백 패딩되어 '26  9:59' 처럼 오는 경우 방지)
        return Regex.Replace(string.Join(" ", texts), @"\s+", " ").Trim();
    }

    public static List<MatchRecord> ParseGames(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var rows = new List<MatchRecord>();
        var tables = doc.DocumentNode.SelectNodes("//table");
        if (tables == null) return rows;
        foreach (var table in tables)
        {
            var trs = table.SelectNodes(".//tr");
            if (trs == null) continue;
            foreach (var tr in trs)
            {
                var tds = tr.SelectNodes("./td");
                if (tds == null || tds.Count == 0) continue;
                var cells = tds.Select(CellText).ToList();
                var rec = ParseRow(cells);
                if (rec != null) rows.Add(rec);
            }
        }
        return rows;
    }

    // 진단용
    public static List<string>? DebugFirstCells(string html)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var trs = doc.DocumentNode.SelectNodes("//table//tr");
        if (trs == null) return null;
        foreach (var tr in trs)
        {
            var tds = tr.SelectNodes("./td");
            if (tds != null && tds.Count > 0) return tds.Select(CellText).ToList();
        }
        return null;
    }

    public static List<string> DebugFailures(string html)
    {
        var doc = new HtmlDocument(); doc.LoadHtml(html);
        var res = new List<string>();
        var trs = doc.DocumentNode.SelectNodes("//table//tr");
        if (trs == null) return res;
        foreach (var tr in trs)
        {
            var tds = tr.SelectNodes("./td");
            if (tds == null || tds.Count == 0) continue;
            var cells = tds.Select(CellText).ToList();
            var c = cells.Select(x => NormNum(x).Trim()).ToList();
            while (c.Count > 0 && c[^1].Length == 0) c.RemoveAt(c.Count - 1);
            if (c.Count < 4) continue;
            if (ParseRow(cells.ToList()) == null) res.Add(string.Join(" | ", c));
        }
        return res;
    }

    public static string? DebugDt(string s) => ParseDt(s);
    public static bool DebugPlayer(string s) => PlayerRe.IsMatch(NormNum(s).Trim());
    public static bool DebugOpp(string s) => OppRe.IsMatch(NormNum(s).Trim());

    public static string? ExtractPlayerName(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();
        if (string.IsNullOrEmpty(title)) return null;
        foreach (var sep in new[] { '•', '|' })
        {
            int i = title.IndexOf(sep);
            if (i >= 0) return title[..i].Trim();
        }
        return title.Trim();
    }
}
