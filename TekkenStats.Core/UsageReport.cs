using static TekkenStats.Core.Headers;

namespace TekkenStats.Core;

/// <summary>
/// 사이트 이용 통계 엑셀 — 관리자 JSON 하나로 만든다(전적 수집 없음).
///
/// 이 리포트가 답하려는 것: <b>사람들이 이 사이트를 어떻게 쓰는가.</b>
/// 조회가 소수에 몰리는가, 자기 걸 보는가 남을 찾는가, 다시 오는가, 어디서 오는가.
/// </summary>
public static class UsageReport
{
    public static string WriteWorkbook(AdminLog log, string outRoot)
    {
        var (first, last) = log.DateRange();
        string stamp = DateTime.Now.ToString("yyyy_MMdd_HHmmss");
        string outPath = Path.Combine(outRoot, $"이용통계_{first}_{last}_{stamp}.xlsx");

        return ExcelWriter.SaveWithFallback(outPath, wb =>
        {
            WriteSheet(wb, "summary", BuildSummary(log, first, last));
            WriteSheet(wb, "daily", Annotate(BuildDaily(log), Style.Bracket));
            WriteSheet(wb, "players", Annotate(BuildPlayers(log), Style.Bracket));
            WriteSheet(wb, "sources", BuildSources(log));

            for (int i = 0; i < wb.NumberOfSheets; i++)
                ExcelWriter.AutoFit(wb, wb.GetSheetAt(i));
        });
    }

    private static void WriteSheet(NPOI.SS.UserModel.IWorkbook wb, string name, Table t)
    {
        var sheet = wb.CreateSheet(ExcelWriter.SafeSheetName(name));
        ExcelWriter.WriteTable(wb, sheet, t);
    }

    private static double Pct(int n, int total) => total > 0 ? Math.Round(n * 100.0 / total, 2) : 0.0;

    /// <summary>
    /// 한 화면 요약. 요청한 기간(<c>days</c>)과 <b>실제 데이터가 있는 기간</b>을 나란히 둔다 —
    /// 파일명이 '28d' 여도 데이터는 이틀뿐일 수 있고, 그걸 모르면 평균을 28로 나눠 잘못 읽는다.
    /// </summary>
    private static Table BuildSummary(AdminLog log, string first, string last)
    {
        var t = new Table("항목", "값", "설명");
        var players = log.Players;
        int tot = log.TotalViews;

        t.Add("요청 기간", $"{log.Days}일", "관리자 페이지에서 고른 창");
        t.Add("실제 데이터 기간", log.Daily.Count > 0 ? $"{first} ~ {last} ({log.Daily.Count}일)" : "없음",
            "이 값으로 평균을 낼 것 — 요청 기간과 다를 수 있다");
        t.Add("총 조회", tot, "");
        t.Add("조회된 플레이어 수", log.UniquePlayers, "서로 다른 식별코드");
        if (log.Daily.Count > 0)
            t.Add("일평균 조회", Math.Round((double)tot / log.Daily.Count, 1), "실제 데이터 기간 기준");

        var byViews = players.OrderByDescending(p => p.Views).ToList();
        foreach (int n in new[] { 5, 10, 20, 50 })
            if (players.Count > n)
                t.Add($"상위 {n}명 점유율", Pct(byViews.Take(n).Sum(p => p.Views), tot),
                    "%. 조회가 소수에 몰리는 정도");

        int multi = players.Count(p => p.Users >= 2);
        t.Add("2명 이상이 찾아본 플레이어", multi,
            $"나머지 {players.Count - multi}명은 조회자 1명 — 대체로 자기 전적을 본 경우");
        t.Add("재방문(2일 이상 조회)", players.Count(p => p.DaysSeen >= 2), "");
        t.Add("1회 이하 조회", players.Count(p => p.Views <= 1), "롱테일 꼬리 두께");

        int noname = players.Count(p => string.IsNullOrEmpty(p.Name));
        t.Add("이름 미확보", $"{noname} ({Pct(noname, players.Count)}%)",
            "조회는 됐는데 닉네임이 안 남은 건수 — 조회수가 많은데 비어 있으면 기록 누락을 의심할 것");
        return t;
    }

    private static Table BuildDaily(AdminLog log)
    {
        var t = new Table("Date", "Views", "Users", "ViewsPerUser");
        foreach (var d in log.Daily.OrderByDescending(x => x.Date, StringComparer.Ordinal))
            t.Add(d.Date, d.Views, d.Users,
                d.Users > 0 ? Math.Round((double)d.Views / d.Users, 2) : 0.0);
        return t;
    }

    private static Table BuildPlayers(AdminLog log)
    {
        int tot = log.TotalViews;
        var t = new Table("Rank", "opp_name", "opp_polaris", "Views", "Share(%)", "Users",
            "DaysSeen", "FirstDate", "LastDate");
        int i = 0;
        foreach (var p in log.Players.OrderByDescending(x => x.Views).ThenByDescending(x => x.Users))
            t.Add(++i, string.IsNullOrEmpty(p.Name) ? "(이름없음)" : p.Name, p.Id,
                p.Views, Pct(p.Views, tot), p.Users, p.DaysSeen, p.FirstDate, p.LastDate);
        return t;
    }

    private static Table BuildSources(AdminLog log)
    {
        int tot = log.Sources.Sum(s => s.Users);
        var t = new Table("유입 경로", "Users", "Share(%)");
        foreach (var s in log.Sources.OrderByDescending(x => x.Users))
            t.Add(s.Source, s.Users, Pct(s.Users, tot));
        return t;
    }
}
