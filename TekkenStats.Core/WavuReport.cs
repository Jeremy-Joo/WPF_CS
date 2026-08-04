using NPOI.SS.UserModel;
using static TekkenStats.Core.Headers;

namespace TekkenStats.Core;

/// <summary>wavu 레코드 → 엑셀 (랭크 전적; 레이팅=MMR). 파이썬 main.py save_workbook 포팅.</summary>
public static class WavuReport
{
    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;

    /// <param name="hasPolaris">
    /// 상대 식별코드를 아는 소스인가. JSON API 경로(<see cref="WavuApiCollector"/>)는 준다.
    /// HTML 파싱 경로(<see cref="WavuParser"/>)는 못 주므로 h2h 에서 빈 열을 빼야 한다.
    /// </param>
    public static string WriteWorkbook(IReadOnlyList<MatchRecord> recs, string outPath,
        bool hasPolaris = false)
    {
        var chars = recs.Select(r => r.MyChar).Distinct().OrderBy(c => c, OIC).ToList();
        string oldest = recs.Min(r => r.Dt).ToString("yyyy-MM-dd HH:mm:ss");
        string newest = recs.Max(r => r.Dt).ToString("yyyy-MM-dd HH:mm:ss");

        return ExcelWriter.SaveWithFallback(outPath, wb =>
        {
            // flow 를 맨 앞에 둔다 = 파일을 열면 이 시트가 먼저 보인다.
            // 조회 직후 궁금한 건 캐릭터별 누적이 아니라 "지금 상태가 어떤가 / 더 해도 되나"다.
            WriteSheet(wb, "flow", Annotate(Aggregations.BuildFlow(recs), Style.Bracket));

            // Total + 제목행
            var total = Annotate(Aggregations.BuildTotal(recs), Style.Bracket);
            var sTotal = wb.CreateSheet("Total");
            ExcelWriter.WriteTable(wb, sTotal, total, startRow: 2);
            sTotal.CreateRow(0).CreateCell(0).SetCellValue("집계 범위[데이터 실제 범위]");
            sTotal.GetRow(0).CreateCell(1).SetCellValue($"{oldest} ~ {newest}");

            // rating_trend (레이팅=MMR) + 캐릭터별 차트
            var (trend, trendChars) = Aggregations.BuildPowerTrend(recs);
            var sTrend = wb.CreateSheet("rating_trend");
            ExcelWriter.WriteTable(wb, sTrend, Annotate(trend, Style.Bracket));
            ExcelWriter.AddLineChart(sTrend, trend.Count, firstCharCol: 4, charCount: trendChars.Count);

            WriteSheet(wb, "round_stats", Annotate(Aggregations.BuildRound(recs), Style.Round));

            // 시즌별 요약 — S1/S2/S3 오름차순. 경계 날짜가 아니라 game_version 에서 파생된 값이다.
            WriteSheet(wb, "season", Annotate(
                Aggregations.SummaryBy(recs, r => r.Season, "Season",
                    s => s.Length > 1 && int.TryParse(s.AsSpan(1), out int n) ? n : 99),
                Style.Bracket));

            WriteSheet(wb, "strong", Annotate(Aggregations.BuildStrong(recs), Style.Bracket));
            WriteSheet(wb, "weak_TOTAL", Annotate(Aggregations.BuildWeak(recs), Style.Bracket));

            // 캐릭터별 합친 시트
            WriteSheet(wb, "weak", Annotate(Aggregations.ConsolidatePerChar(recs, chars, s => Aggregations.BuildWeak(s)), Style.Bracket));

            var h2h = Aggregations.ConsolidatePerChar(recs, chars, s => Aggregations.BuildH2h(s));
            if (!hasPolaris) h2h.RemoveColumn("opp_polaris");  // HTML 경로는 식별코드가 없어 닉네임만
            WriteSheet(wb, "h2h", Annotate(h2h, Style.Bracket));

            WriteSheet(wb, "daily", Annotate(Aggregations.BuildDaily(recs), Style.Bracket));
            WriteSheet(wb, "sessions", Annotate(Aggregations.BuildSessions(recs), Style.Bracket));
            WriteSheet(wb, "time_patterns", Annotate(Aggregations.BuildTimePatterns(recs), Style.Bracket));

            // 아래 둘은 wavu JSON 만 채워주는 필드에 기댄다. 값이 없는 소스(HTML 파싱)에서는
            // 표를 만들어봐야 전부 0/빈칸이라, 조용히 틀린 표를 내느니 시트를 만들지 않는다.
            if (recs.Any(r => r.OppDelta != 0))
                WriteSheet(wb, "vs_rating", Annotate(Aggregations.BuildVsRating(recs), Style.Bracket));
            if (recs.Any(r => r.MyRank != null))
                WriteSheet(wb, "rank_history", Annotate(Aggregations.BuildRankHistory(recs), Style.Bracket));

            var sumwr = Aggregations.ConsolidatePerChar(recs, chars, s => Aggregations.BuildPivot(s, "winrate"));
            if (sumwr.Columns.Count > 0) sumwr.Columns[0] = "my_char[캐릭터]";
            WriteSheet(wb, "summary_wr", sumwr);

            WriteSheet(wb, "games_all", BuildGamesAll(recs));

            for (int i = 0; i < wb.NumberOfSheets; i++)
                ExcelWriter.AutoFit(wb, wb.GetSheetAt(i));
        });
    }

    private static void WriteSheet(IWorkbook wb, string name, Table t)
    {
        var sheet = wb.CreateSheet(ExcelWriter.SafeSheetName(name));
        ExcelWriter.WriteTable(wb, sheet, t);
    }

    private static Table BuildGamesAll(IReadOnlyList<MatchRecord> recs)
    {
        var t = new Table("dt", "player", "my_char", "my_rating", "my_delta", "score",
            "my_rounds", "opp_rounds", "result", "opp_rating", "opp_char", "opp_name");
        foreach (var r in recs.OrderByDescending(r => r.Dt))
            t.Add(r.Dt.ToString("yyyy-MM-dd HH:mm:ss"), r.Player, r.MyChar, r.MyRating, r.MyDelta, r.Score,
                r.MyRounds, r.OppRounds, r.Result, r.OppRating, r.OppChar, r.OppName);
        return t;
    }
}
