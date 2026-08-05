using System.Text.Json.Serialization;

namespace TekkenStats.Core;

/// <summary>ewgf RSC flight 에서 파싱한 원본 경기 객체.</summary>
public sealed class Battle
{
    [JsonPropertyName("battleAt")] public string? BattleAt { get; set; }
    [JsonPropertyName("battleType")] public string? BattleType { get; set; }
    [JsonPropertyName("gameVersion")] public int? GameVersion { get; set; }
    [JsonPropertyName("winner")] public int? Winner { get; set; }
    [JsonPropertyName("stageId")] public int? StageId { get; set; }

    [JsonPropertyName("p1Name")] public string? P1Name { get; set; }
    [JsonPropertyName("p1PolarisId")] public string? P1PolarisId { get; set; }
    [JsonPropertyName("p1Char")] public string? P1Char { get; set; }
    [JsonPropertyName("p1RegionId")] public string? P1RegionId { get; set; }
    [JsonPropertyName("p1TekkenPower")] public int? P1TekkenPower { get; set; }
    [JsonPropertyName("p1DanRank")] public string? P1DanRank { get; set; }
    [JsonPropertyName("p1RoundsWon")] public int? P1RoundsWon { get; set; }

    [JsonPropertyName("p2Name")] public string? P2Name { get; set; }
    [JsonPropertyName("p2PolarisId")] public string? P2PolarisId { get; set; }
    [JsonPropertyName("p2Char")] public string? P2Char { get; set; }
    [JsonPropertyName("p2RegionId")] public string? P2RegionId { get; set; }
    [JsonPropertyName("p2TekkenPower")] public int? P2TekkenPower { get; set; }
    [JsonPropertyName("p2DanRank")] public string? P2DanRank { get; set; }
    [JsonPropertyName("p2RoundsWon")] public int? P2RoundsWon { get; set; }
}

/// <summary>'나' 관점으로 정규화한 경기 레코드(파이썬 normalize 결과와 동일).</summary>
public sealed class MatchRecord
{
    public DateTime Dt { get; set; }            // KST
    public string BattleId { get; set; } = "";  // wavu battle_id (중복 제거 키). ewgf 는 안 준다 → 빈 값
    public string Player { get; set; } = "";
    public string MyPolaris { get; set; } = "";  // 내 식별코드 (비교 리포트의 맞대결 매칭용)
    public string MyChar { get; set; } = "";
    public int MyRating { get; set; }           // 레이팅 자리 = TekkenPower
    public int MyDelta { get; set; }            // 캐릭터별 시간순 파워 변화
    public string Score { get; set; } = "";
    public int MyRounds { get; set; }
    public int OppRounds { get; set; }
    public string Result { get; set; } = "";    // "W" / "L"
    public int OppRating { get; set; }
    public int OppDelta { get; set; }           // 상대 레이팅 변동. wavu JSON 만 준다(ewgf 는 0)
    public string OppChar { get; set; } = "";
    public string OppName { get; set; } = "";
    public string OppPolaris { get; set; } = "";
    public string BattleType { get; set; } = "";  // Quick/Ranked/Player/Group
    public int GameVersion { get; set; }
    public string Season { get; set; } = "";       // S1/S2/S3
    public string MyDan { get; set; } = "";     // ewgf 는 단 이름, wavu 는 "#숫자"
    public string OppDan { get; set; } = "";
    // 단(숫자) 원본. wavu 만 준다 — 승단 이력은 크기 비교가 필요해서 문자열로는 안 된다.
    public int? MyRank { get; set; }
    public int? OppRank { get; set; }
    public string Region { get; set; } = "";
}
