using NPOI.SS.UserModel;
using static TekkenStats.Core.Headers;

namespace TekkenStats.Core;

/// <summary>wavu 레코드 → 엑셀 (랭크 전적; 레이팅=MMR). 파이썬 main.py save_workbook 포팅.</summary>
public static class WavuReport
{
    private static readonly StringComparer OIC = StringComparer.OrdinalIgnoreCase;

    public static string WriteWorkbook(IReadOnlyList<MatchRecord> recs, string outPath)
    {
        var chars = recs.Select(r => r.MyChar).Distinct().OrderBy(c => c, OIC).ToList();
        string oldest = recs.Min(r => r.Dt).ToString("yyyy-MM-dd HH:mm:ss");
        string newest = recs.Max(r => r.Dt).ToString("yyyy-MM-dd HH:mm:ss");

        return ExcelWriter.SaveWithFallback(outPath, wb =>
        {
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
            WriteSheet(wb, "weak_TOTAL", Annotate(Aggregations.BuildWeak(recs), Style.Bracket));

            // 캐릭터별 합친 시트
            WriteSheet(wb, "weak", Annotate(Aggregations.ConsolidatePerChar(recs, chars, s => Aggregations.BuildWeak(s)), Style.Bracket));

            var h2h = Aggregations.ConsolidatePerChar(recs, chars, s => Aggregations.BuildH2h(s));
            h2h.RemoveColumn("opp_polaris");  // wavu 는 식별코드가 없으므로 닉네임만
            WriteSheet(wb, "h2h", Annotate(h2h, Style.Bracket));

            WriteSheet(wb, "daily", Annotate(Aggregations.BuildDaily(recs), Style.Bracket));

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
