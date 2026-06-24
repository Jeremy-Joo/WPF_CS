namespace TekkenStats.Core;

/// <summary>컬럼명 한글 병기 (파이썬 ko()/round rename 포팅).</summary>
public static class Headers
{
    public enum Style { Bracket, Round, Plain }

    private static readonly Dictionary<string, string> Bracket = new()
    {
        ["my_char"] = "my_char[캐릭터]",
        ["opp_char"] = "opp_char[상대캐릭터]",
        ["opp_name"] = "opp_name[상대닉네임]",
        ["opp_polaris"] = "opp_polaris[식별코드]",
        ["Total"] = "Total[총경기수]",
        ["Games"] = "Games[경기수]",
        ["W"] = "W[승]",
        ["L"] = "L[패]",
        ["WinRate(%)"] = "WinRate(%)[승률]",
        ["Date"] = "Date[날짜]",
        ["RatingDelta"] = "RatingDelta[레이팅증감]",
        ["EndRating"] = "EndRating[마감레이팅]",
        ["LastPlayed"] = "LastPlayed[최근대전]",
        ["dt"] = "dt[일시]",
        ["my_rating"] = "my_rating[레이팅]",
        ["result"] = "result[결과]",
    };

    private static readonly Dictionary<string, string> Round = new()
    {
        ["my_char"] = "my_char\n캐릭터",
        ["Games"] = "Games\n경기수",
        ["RoundsWon"] = "RoundsWon\n획득라운드",
        ["RoundsLost"] = "RoundsLost\n실점라운드",
        ["RoundWR(%)"] = "RoundWR(%)\n라운드승률",
        ["AvgRoundsWon"] = "AvgRoundsWon\n평균획득라운드",
        ["AvgRoundsLost"] = "AvgRoundsLost\n평균실점라운드",
        ["CloseGames"] = "CloseGames\n접전경기수",
        ["Close(%)"] = "Close(%)\n접전비율",
        ["CloseWins"] = "CloseWins\n접전승",
        ["CloseWin(%)"] = "CloseWin(%)\n접전승비율",
        ["CloseLosses"] = "CloseLosses\n접전패",
        ["CloseLoss(%)"] = "CloseLoss(%)\n접전패비율",
        ["Shutouts_Dealt"] = "Shutouts_Dealt\n완승",
        ["ShutoutWin(%)"] = "ShutoutWin(%)\n완승비율",
        ["Shutouts_Received"] = "Shutouts_Received\n완패",
        ["ShutoutLoss(%)"] = "ShutoutLoss(%)\n완패비율",
    };

    /// <summary>표의 컬럼명을 스타일에 맞게 한글 병기로 바꾼다(모르는 컬럼은 그대로).</summary>
    public static Table Annotate(Table t, Style style)
    {
        if (style == Style.Plain) return t;
        var map = style == Style.Round ? Round : Bracket;
        for (int i = 0; i < t.Columns.Count; i++)
            if (map.TryGetValue(t.Columns[i], out var v))
                t.Columns[i] = v;
        return t;
    }
}
