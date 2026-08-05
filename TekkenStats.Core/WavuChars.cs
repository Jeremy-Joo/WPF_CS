namespace TekkenStats.Core;

/// <summary>
/// wavu JSON 의 chara_id → 캐릭터명.
///
/// 도출 방법(추측 아님): 플레이어 4명의 HTML 페이지(캐릭터명이 텍스트로 있음)와
/// 같은 플레이어의 /replays JSON(chara_id 가 숫자)을 battle_at 으로 조인해 2,000경기에서 뽑았다.
/// 41종 전부 나왔고 id↔이름 충돌은 0건. wavu 플레이어 페이지의 'All characters' 드롭다운과 일치한다.
///
/// 빠진 id(25,26,27,30,31,37)는 2,000경기에서 한 번도 안 나왔다. 철권8 로스터에 없는 번호로
/// 보이나 단정하지 않는다 — 신캐가 추가되면 여기 없는 id 가 오고, 그때는 <c>#숫자</c> 로 떨어진다.
/// </summary>
public static class WavuChars
{
    private static readonly Dictionary<int, string> Names = new()
    {
        [0] = "Paul", [1] = "Law", [2] = "King", [3] = "Yoshimitsu", [4] = "Hwoarang",
        [5] = "Xiaoyu", [6] = "Jin", [7] = "Bryan", [8] = "Kazuya", [9] = "Steve",
        [10] = "Jack-8", [11] = "Asuka", [12] = "Devil Jin", [13] = "Feng", [14] = "Lili",
        [15] = "Dragunov", [16] = "Leo", [17] = "Lars", [18] = "Alisa", [19] = "Claudio",
        [20] = "Shaheen", [21] = "Nina", [22] = "Lee", [23] = "Kuma", [24] = "Panda",
        [28] = "Zafina", [29] = "Leroy", [32] = "Jun", [33] = "Reina", [34] = "Azucena",
        [35] = "Victor", [36] = "Raven", [38] = "Eddy", [39] = "Lidia", [40] = "Heihachi",
        [41] = "Clive", [42] = "Anna", [43] = "Fahkumram", [44] = "Armor King",
        [45] = "Miary Zo", [46] = "Kunimitsu",
    };

    /// <summary>
    /// 모르는 id 는 <c>#숫자</c> 로 남긴다.
    /// 조용히 'Unknown' 으로 뭉뚱그리면 신캐 추가를 눈치채지 못한 채 집계가 섞인다.
    /// 번호를 그대로 보이게 해서 표에서 바로 드러나게 한다.
    /// </summary>
    public static string Name(int? id) =>
        id == null ? "#?" : Names.TryGetValue(id.Value, out var n) ? n : $"#{id.Value}";
}
