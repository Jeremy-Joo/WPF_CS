using System.Text;
using TekkenStats.Core;

Console.OutputEncoding = Encoding.UTF8;

// wavu 파서 검증 모드: dotnet run -- wavu <html>
if (args.Length > 0 && args[0] == "wavu")
{
    string wp = args.Length > 1 ? args[1] : @"D:\Git_jerry\tk8_data_Wavuwank\User\_ewgf_probe\wavu_fixture.html";
    string wh = File.ReadAllText(wp);
    // 진단
    var doc = new HtmlAgilityPack.HtmlDocument(); doc.LoadHtml(wh);
    var tbls = doc.DocumentNode.SelectNodes("//table");
    Console.WriteLine($"[진단] table 수: {tbls?.Count ?? 0}");
    if (tbls != null)
    {
        var trs = tbls[0].SelectNodes(".//tr");
        Console.WriteLine($"[진단] tr 수: {trs?.Count ?? 0}");
        var firstTd = trs?.FirstOrDefault(t => t.SelectNodes("./td") != null);
        if (firstTd != null)
        {
            var tds = firstTd.SelectNodes("./td");
            Console.WriteLine($"[진단] 첫 행 td 수: {tds.Count}");
            Console.WriteLine($"[진단] 첫 행 cells: {string.Join(" || ", tds.Select(td => td.InnerText.Trim()))}");
        }
    }
    var dc = WavuParser.DebugFirstCells(wh);
    if (dc != null)
        for (int i = 0; i < dc.Count; i++)
            Console.WriteLine($"[진단] CellText[{i}] = <{dc[i]}>");
    Console.WriteLine($"[진단] dt parse: {WavuParser.DebugDt("3 May 26 10:39") ?? "FAIL"}");
    Console.WriteLine($"[진단] player match: {WavuParser.DebugPlayer("JackFather Jack-8 1665 +12")}");
    Console.WriteLine($"[진단] opp match: {WavuParser.DebugOpp("1715 -13 Lili IzzNa22")}");
    var fails = WavuParser.DebugFailures(wh);
    Console.WriteLine($"[진단] 실패행 {fails.Count}건:");
    foreach (var fr in fails.Take(10)) Console.WriteLine($"   FAIL: {fr}");
    var recsW = WavuParser.ParseGames(wh);
    Console.WriteLine($"wavu 파싱 레코드: {recsW.Count}  (이름: {WavuParser.ExtractPlayerName(wh)})");
    if (recsW.Count == 0) return;
    var sw = recsW.OrderByDescending(r => r.Dt).First();
    Console.WriteLine($"최신: {sw.Dt:yyyy-MM-dd HH:mm} {sw.Player}/{sw.MyChar} {sw.MyRating}({sw.MyDelta:+#;-#;0}) " +
                      $"{sw.Score} {sw.Result} vs {sw.OppChar}/{sw.OppName} {sw.OppRating}");
    Console.WriteLine($"캐릭터: {string.Join(", ", recsW.Select(r => r.MyChar).Distinct().OrderBy(c => c))}");
    string wout = Path.Combine(Path.GetTempPath(), "wavu_cs_test.xlsx");
    if (File.Exists(wout)) File.Delete(wout);
    string wsaved = WavuReport.WriteWorkbook(recsW, wout);
    Console.WriteLine($"[엑셀 저장] {wsaved}");
    return;
}

// 비교 리포트 검증 모드: dotnet run -- compare
// 같은 기류사단(4JGy2FayQFMT) 덤프에서 JackFather(53deQ2dmLday) 관점도 뽑아
// 실제 head2head 데이터(둘이 직접 붙은 경기)로 CompareReport 를 검증한다.
if (args.Length > 0 && args[0] == "compare")
{
    string cp = @"D:\Git_jerry\tk8_data_Wavuwank\User\_ewgf_probe\ewgf_4JGy2FayQFMT.html";
    string ch = File.ReadAllText(cp);
    var cb = EwgfExtractor.ExtractBattles(ch);

    var (recsA, nameA) = EwgfExtractor.Normalize(cb, "4JGy2FayQFMT");
    var (recsB, nameB) = EwgfExtractor.Normalize(cb, "53deQ2dmLday");
    Console.WriteLine($"A={nameA} ({recsA.Count}건)  B={nameB} ({recsB.Count}건, 이 덤프엔 A와 붙은 경기만 존재)");

    var players = new List<CompareReport.Player>
    {
        new("4JGy2FayQFMT", nameA, recsA),
        new("53deQ2dmLday", nameB, recsB),
    };
    string cmpOutPath = Path.Combine(Path.GetTempPath(), "compare_test");
    string cmpSaved = CompareReport.WriteWorkbook(players, cmpOutPath);
    Console.WriteLine($"[엑셀 저장] {cmpSaved}");

    // head2head 직접 검증 (A 시점에서 B 와 붙은 경기 수)
    int direct = recsA.Count(r => r.OppPolaris == "53deQ2dmLday");
    int aWins = recsA.Count(r => r.OppPolaris == "53deQ2dmLday" && r.Result == "W");
    Console.WriteLine($"[체크] A 시점 A vs B 직접 대결: {direct}건 (A {aWins}승 {direct - aWins}패)");
    Console.WriteLine($"[체크] B 레코드 수(={direct} 이어야 함, B 관점도 같은 경기라서): {recsB.Count}");
    return;
}

// 슬라이스 2 검증: 이미 받아둔 덤프 HTML 로 추출/정규화 (브라우저 불필요)
string htmlPath = args.Length > 0
    ? args[0]
    : @"D:\Git_jerry\tk8_data_Wavuwank\User\_ewgf_probe\ewgf_4JGy2FayQFMT.html";
string mePid = args.Length > 1 ? args[1] : "4JGy2FayQFMT";

if (!File.Exists(htmlPath))
{
    Console.WriteLine($"HTML 없음: {htmlPath}");
    return;
}

string html = File.ReadAllText(htmlPath);
var battles = EwgfExtractor.ExtractBattles(html);
Console.WriteLine($"추출 battle: {battles.Count}");

var (recs, name) = EwgfExtractor.Normalize(battles, mePid);
Console.WriteLine($"정규화 레코드: {recs.Count}  이름: {name}");

Console.WriteLine("종류별:");
foreach (var g in recs.GroupBy(r => r.BattleType).OrderByDescending(g => g.Count()))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

Console.WriteLine("시즌별:");
foreach (var g in recs.GroupBy(r => r.Season).OrderByDescending(g => g.Key))
    Console.WriteLine($"  {g.Key}: {g.Count()}");

if (recs.Count == 0) return;

void Print(string title, Table t, int n = 6)
{
    Console.WriteLine($"\n=== {title} ({t.Count}행) ===");
    Console.WriteLine(string.Join(" | ", t.Columns));
    foreach (var row in t.Rows.Take(n))
        Console.WriteLine(string.Join(" | ", row.Select(v => v?.ToString() ?? "")));
}

Print("Total", Aggregations.BuildTotal(recs), 12);
Print("by_type", Aggregations.BuildByType(recs), 6);
Print("종류 요약", Aggregations.SummaryBy(recs, r => r.BattleType, "battleType"));
Print("시즌 요약", Aggregations.SummaryBy(recs, r => r.Season, "season",
    s => s == "S3" ? 0 : s == "S2" ? 1 : s == "S1" ? 2 : 99));
Print("round_stats", Aggregations.BuildRound(recs), 4);
Print("h2h (전체)", Aggregations.BuildH2h(recs), 4);
Print("weak_TOTAL", Aggregations.BuildWeak(recs), 6);

// 정합성 체크: 합계가 7751 과 일치하는지
var tot = Aggregations.BuildTotal(recs);
var allRow = tot.Rows.Last();
Console.WriteLine($"\n[체크] Total ALL Games = {allRow[1]} (기대 {recs.Count})");
var round = Aggregations.BuildRound(recs);
Console.WriteLine($"[체크] round ALL Games = {round.Rows.Last()[1]} (기대 {recs.Count})");

// 슬라이스 4: 실제 엑셀 생성
string outPath = args.Length > 2
    ? args[2]
    : Path.Combine(Path.GetTempPath(), $"ewgf_cs_{name}.xlsx");
string saved = EwgfReport.WriteWorkbook(recs, outPath);
Console.WriteLine($"\n[엑셀 저장] {saved}");
