using System.Text;
using TekkenStats.Core;

Console.OutputEncoding = Encoding.UTF8;

// 캐시 조회/비우기: dotnet run -- cache stats|clear <결과폴더>
if (args.Length > 0 && args[0] == "cache")
{
    string sub = args.Length > 1 ? args[1] : "stats";
    string root = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "tekken_population");
    string dir = ReplayCache.DefaultDir(root);

    var (files, bytes) = ReplayCache.Stats(dir);
    Console.WriteLine($"[캐시] {dir}");
    Console.WriteLine($"       {files}개 파일, {bytes / 1024.0 / 1024.0:0.0}MB");
    if (sub == "clear")
    {
        var (cleared, freed) = ReplayCache.Clear(dir);
        Console.WriteLine($"[비움] {cleared}개 삭제, {freed / 1024.0 / 1024.0:0.0}MB 확보");
    }
    else if (sub == "export")
    {
        string zip = args.Length > 3 ? args[3] : Path.Combine(root, "tekken_cache.zip");
        var (n, zbytes) = CacheArchive.Export(dir, zip);
        Console.WriteLine($"[내보냄] {n}개 → {zip} ({zbytes / 1024.0 / 1024.0:0.0}MB)");
    }
    else if (sub == "import")
    {
        if (args.Length < 4) { Console.WriteLine("사용법: cache import <결과폴더> <zip경로>"); return; }
        var (imported, skipped) = CacheArchive.Import(args[3], dir);
        Console.WriteLine($"[불러옴] {imported}개 추가, {skipped}개 건너뜀(이미 최신)");
    }
    return;
}

// 이용 통계 엑셀: dotnet run -- usage <관리자JSON> [출력폴더]   (수집 없음)
if (args.Length > 0 && args[0] == "usage")
{
    if (args.Length < 2) { Console.WriteLine("사용법: usage <관리자JSON> [출력폴더]"); return; }
    var imported = IdImport.FromFile(args[1]);
    if (imported.Log == null) { Console.WriteLine($"[실패] 관리자 JSON 형식이 아닙니다 ({imported.Kind})"); return; }

    var (f, l) = imported.Log.DateRange();
    Console.WriteLine($"요청 {imported.Log.Days}일 / 실제 데이터 {f} ~ {l} ({imported.Log.Daily.Count}일)");
    Console.WriteLine($"총 조회 {imported.Log.TotalViews}, 플레이어 {imported.Log.Players.Count}명");
    string uOut = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "tekken_usage");
    Console.WriteLine($"[엑셀 저장] {UsageReport.WriteWorkbook(imported.Log, uOut)}");
    return;
}

// 인구 리포트: dotnet run -- population <관리자JSON|텍스트> [상위N] [출력폴더]
if (args.Length > 0 && args[0] == "population")
{
    if (args.Length < 2) { Console.WriteLine("사용법: population <파일> [상위N] [출력폴더]"); return; }
    var imported = IdImport.FromFile(args[1]);
    int topN = args.Length > 2 && int.TryParse(args[2], out var n) ? n : 20;
    string pOut = args.Length > 3 ? args[3] : Path.Combine(Path.GetTempPath(), "tekken_population");

    var targets = imported.Items.Take(topN).ToList();
    Console.WriteLine($"[{imported.Kind}] 식별코드 {imported.Items.Count}개 중 상위 {targets.Count}명 수집");

    // 캐시 검증용 — 결과폴더 아래 .cache 에 24시간 유효기간으로 저장/재사용.
    string popCache = ReplayCache.DefaultDir(pOut);

    var entries = new List<PopulationReport.Entry>();
    for (int i = 0; i < targets.Count; i++)
    {
        var it = targets[i];
        // wavu 레이트리밋 회피 — 순차 수집. 병렬로 바꾸지 말 것.
        if (i > 0) await Task.Delay(1000);
        try
        {
            var replays = await WavuApiCollector.FetchWithCacheAsync(
                it.Id, popCache, TimeSpan.FromHours(24), Console.WriteLine);
            var (prs, nm, _) = WavuApiCollector.Normalize(replays, Collect.NormalizeId(it.Id));
            Console.WriteLine($"  [{i + 1}/{targets.Count}] {it.Id} {nm} — {prs.Count}경기");
            if (prs.Count > 0)
                entries.Add(new PopulationReport.Entry(it.Id,
                    string.IsNullOrEmpty(nm) ? it.Name : nm, it.Views, prs));
        }
        catch (WavuApiException ex)
        {
            Console.WriteLine($"  [{i + 1}/{targets.Count}] {it.Id} — 실패: {ex.Message}");
        }
    }

    if (entries.Count == 0) { Console.WriteLine("[실패] 수집된 전적이 없습니다."); return; }
    Console.WriteLine($"[엑셀 저장] {PopulationReport.WriteWorkbook(entries, pOut)}");
    return;
}

// wavu JSON API 실수집: dotnet run -- wavuapi <식별코드> [출력폴더]
// 브라우저가 필요 없으므로 이 경로는 CLI 에서 그대로 검증된다.
if (args.Length > 0 && args[0] == "wavuapi")
{
    string pid = args.Length > 1 ? args[1] : "53deQ2dmLday";
    string outRoot = args.Length > 2 ? args[2] : Path.Combine(Path.GetTempPath(), "wavuapi");

    var res = await WavuApiCollector.CollectAsync(pid, null, null, outRoot, Console.WriteLine);
    if (!string.IsNullOrEmpty(res.Error)) { Console.WriteLine($"[실패] {res.Error}"); return; }

    var wrecs = res.Records!;
    Console.WriteLine($"\n{res.PlayerName} — {res.Count}경기  → {res.OutPath}");
    Console.WriteLine($"기간: {wrecs.Min(r => r.Dt):yyyy-MM-dd} ~ {wrecs.Max(r => r.Dt):yyyy-MM-dd}");
    Console.WriteLine($"캐릭터: {string.Join(", ", wrecs.Select(r => r.MyChar).Distinct().OrderBy(c => c))}");
    Console.WriteLine("시즌별:");
    foreach (var g in wrecs.GroupBy(r => r.Season).OrderByDescending(g => g.Key))
        Console.WriteLine($"  {g.Key}: {g.Count()}");

    // 매핑 안 된 캐릭터가 있으면 조용히 넘어가지 않고 드러낸다(신캐 추가 감지).
    var unknown = wrecs.SelectMany(r => new[] { r.MyChar, r.OppChar })
        .Where(c => c.StartsWith('#')).Distinct().ToList();
    Console.WriteLine(unknown.Count == 0
        ? "[체크] 미매핑 캐릭터 없음"
        : $"[경고] 미매핑 chara_id: {string.Join(", ", unknown)} → WavuChars.cs 에 추가 필요");

    var last = wrecs.OrderByDescending(r => r.Dt).First();
    Console.WriteLine($"최신: {last.Dt:yyyy-MM-dd HH:mm} {last.MyChar} {last.MyRating}({last.MyDelta:+#;-#;0}) " +
                      $"{last.Score} {last.Result} vs {last.OppChar}/{last.OppName}[{last.OppPolaris}]");
    return;
}

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
