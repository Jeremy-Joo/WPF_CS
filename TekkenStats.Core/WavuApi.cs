using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TekkenStats.Core;

/// <summary>
/// wank.wavu.wiki replay 원본 스키마.
///
/// <code>
/// GET https://wank.wavu.wiki/player/&lt;식별코드&gt;/replays
/// Accept: application/json
/// </code>
///
/// 실측: <b>한 번의 요청으로 해당 플레이어의 전체 이력</b>이 온다(7,808건 → gzip 489KB).
/// 페이지네이션 없음. 필드는 스네이크케이스이고 캐릭터·단은 숫자 ID 다.
///
/// 컬럼명을 <see cref="JsonPropertyName"/> 으로 하나씩 적은 것은 의도다 —
/// 네이밍 정책에 맡기면 <c>p1_name</c> 같은 숫자 낀 이름에서 조용히 어긋나고,
/// 그 결과가 '전부 null = 0경기'라 원인을 찾기 어렵다.
/// </summary>
public sealed class Replay
{
    [JsonPropertyName("battle_at")] public long? BattleAt { get; set; }        // epoch 초 (UTC)
    [JsonPropertyName("battle_id")] public string? BattleId { get; set; }      // 중복 제거 키
    [JsonPropertyName("battle_type")] public int? BattleType { get; set; }     // 실측상 2(랭크전)만 존재
    [JsonPropertyName("game_version")] public int? GameVersion { get; set; }   // 1xxxx=S1, 2xxxx=S2, 3xxxx=S3
    [JsonPropertyName("stage_id")] public int? StageId { get; set; }
    [JsonPropertyName("winner")] public int? Winner { get; set; }              // 1 = p1 승, 2 = p2 승

    [JsonPropertyName("p1_name")] public string? P1Name { get; set; }
    [JsonPropertyName("p1_polaris_id")] public string? P1PolarisId { get; set; }
    [JsonPropertyName("p1_chara_id")] public int? P1CharaId { get; set; }
    [JsonPropertyName("p1_power")] public int? P1Power { get; set; }           // 테켄 파워
    [JsonPropertyName("p1_rank")] public int? P1Rank { get; set; }             // 단(숫자) — 사이트가 이름을 노출 안 함
    [JsonPropertyName("p1_rating_before")] public int? P1RatingBefore { get; set; }  // glicko2 (경기 전)
    [JsonPropertyName("p1_rating_change")] public int? P1RatingChange { get; set; }
    [JsonPropertyName("p1_rounds")] public int? P1Rounds { get; set; }

    [JsonPropertyName("p2_name")] public string? P2Name { get; set; }
    [JsonPropertyName("p2_polaris_id")] public string? P2PolarisId { get; set; }
    [JsonPropertyName("p2_chara_id")] public int? P2CharaId { get; set; }
    [JsonPropertyName("p2_power")] public int? P2Power { get; set; }
    [JsonPropertyName("p2_rank")] public int? P2Rank { get; set; }
    [JsonPropertyName("p2_rating_before")] public int? P2RatingBefore { get; set; }
    [JsonPropertyName("p2_rating_change")] public int? P2RatingChange { get; set; }
    [JsonPropertyName("p2_rounds")] public int? P2Rounds { get; set; }
}

/// <summary>wavu 호출 실패. <see cref="Kind"/> 로 원인을 구분한다.</summary>
public sealed class WavuApiException : Exception
{
    public enum Cause { NotFound, Blocked, BadResponse, Network }

    public WavuApiException(string message, Cause kind, int status = 0) : base(message)
    {
        Kind = kind;
        Status = status;
    }

    public Cause Kind { get; }
    public int Status { get; }
}

/// <summary>
/// wavu 공식 JSON API 클라이언트. <b>브라우저·Playwright·Cloudflare 우회가 전부 불필요하다.</b>
///
/// HTML 페이지 경로(<c>?before=</c> 페이지네이션, <c>/opps</c>)는 Cloudflare 403 에 막힌다.
/// <b>HTML 스크레이핑으로 되돌리려는 시도는 하지 말 것</b> — 그게 구형 <see cref="WavuCollector"/> 다.
/// </summary>
public static class WavuApi
{
    public const string Base = "https://wank.wavu.wiki";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            // wavu 문서가 /api 계열은 압축 수락을 요구한다고 명시한다.
            // 15MB 짜리 응답(3만 경기)이 실제로 있어서 압축 없이는 느리다.
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                                     | DecompressionMethods.Brotli,
        };
        var c = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
        // 누가 보내는 요청인지 알 수 있게 UA 를 남긴다.
        c.DefaultRequestHeaders.Add("User-Agent", "TekkenStats-WPF (personal stats collector)");
        return c;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// 해당 플레이어의 <b>전체</b> 랭크전 이력을 한 번에 받아온다.
    ///
    /// <c>Accept: application/json</c> 이 핵심이다. 이 헤더가 없으면 같은 URL 이 HTML 을 돌려준다.
    /// </summary>
    public static async Task<List<Replay>> FetchReplaysAsync(string polarisId, CancellationToken ct = default)
    {
        string id = Collect.NormalizeId(polarisId);
        if (id.Length == 0)
            throw new WavuApiException("식별코드가 비었습니다.", WavuApiException.Cause.NotFound);

        var req = new HttpRequestMessage(HttpMethod.Get, $"{Base}/player/{Uri.EscapeDataString(id)}/replays");
        req.Headers.Add("Accept", "application/json");

        HttpResponseMessage res;
        try
        {
            res = await Http.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new WavuApiException($"wavu 에 연결하지 못했습니다: {ex.Message}",
                WavuApiException.Cause.Network);
        }

        using (res)
        {
            int status = (int)res.StatusCode;
            if (status is 403 or 429)
                throw new WavuApiException(
                    status == 429
                        ? "wavu 레이트리밋에 걸렸습니다. 잠시 후 다시 시도하세요."
                        : "wavu 가 요청을 차단했습니다(Cloudflare). 잠시 후 다시 시도하세요.",
                    WavuApiException.Cause.Blocked, status);
            if (status == 404)
                throw new WavuApiException("그런 식별코드의 플레이어가 없습니다.",
                    WavuApiException.Cause.NotFound, 404);
            if (!res.IsSuccessStatusCode)
                throw new WavuApiException($"wavu 응답 오류 (HTTP {status})",
                    WavuApiException.Cause.BadResponse, status);

            // 없는 식별코드면 wavu 는 404 가 아니라 **200 + HTML 에러 페이지**를 돌려준다(실측).
            // 그래서 content-type 이 JSON 이 아니면 '없는 플레이어'를 먼저 안내하고,
            // 진짜 구조 변화는 그다음 의심한다.
            string ctype = res.Content.Headers.ContentType?.MediaType ?? "";
            if (!ctype.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                string body = await res.Content.ReadAsStringAsync(ct);
                if (body.Contains("Error • Wavu Wank", StringComparison.Ordinal))
                    throw new WavuApiException(
                        $"'{id}' 플레이어를 찾을 수 없습니다. 식별코드를 확인하세요.",
                        WavuApiException.Cause.NotFound, 404);
                throw new WavuApiException(
                    $"wavu 가 JSON 대신 {(ctype.Length > 0 ? ctype : "알 수 없는 형식")} 을 돌려줬습니다. " +
                    "사이트 구조가 바뀌었을 수 있습니다.",
                    WavuApiException.Cause.BadResponse, status);
            }

            List<Replay>? data;
            try
            {
                await using var stream = await res.Content.ReadAsStreamAsync(ct);
                data = await JsonSerializer.DeserializeAsync<List<Replay>>(stream, JsonOpts, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (JsonException ex)
            {
                throw new WavuApiException($"wavu 응답을 해석하지 못했습니다(배열이 아님?): {ex.Message}",
                    WavuApiException.Cause.BadResponse, status);
            }

            if (data == null)
                throw new WavuApiException("wavu 응답이 비었습니다(null).",
                    WavuApiException.Cause.BadResponse, status);

            // 스키마가 바뀌면 역직렬화는 성공하되 필드가 전부 null 이 된다 —
            // 그러면 '0 경기'로 조용히 위장된다. 여기서 먼저 터뜨린다.
            if (data.Count > 0 && data.All(r => r.BattleAt == null))
                throw new WavuApiException(
                    $"wavu 가 {data.Count}건을 줬지만 battle_at 필드가 하나도 없습니다. " +
                    "JSON 스키마가 바뀐 것으로 보입니다(WavuApi.Replay 확인).",
                    WavuApiException.Cause.BadResponse, status);

            return data;
        }
    }
}
