using System.Diagnostics;
using Microsoft.Playwright;

namespace TekkenStats.Core;

/// <summary>
/// 실제 크롬을 디버그 모드로 직접 띄우고 CDP 로 연결한다(파이썬 launch_chrome_and_connect 포팅).
/// Playwright 가 띄우는 '자동화 브라우저'가 아니라 정상 크롬이라 Cloudflare 봇 탐지를 크게 회피한다.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    public const int CdpPort = 9222;
    public static string CdpUrl => $"http://127.0.0.1:{CdpPort}";
    public const int ChallengeWaitMs = 180_000;  // Cloudflare 수동 통과 대기
    public const int RowsWaitMs = 15_000;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(1) };

    private readonly string _profileDir;
    private readonly Action<string>? _log;
    private IPlaywright? _pw;
    private IBrowser? _browser;
    private Process? _chrome;

    public IBrowserContext Context { get; private set; } = default!;
    public IPage Page { get; private set; } = default!;

    public BrowserSession(string profileDir, Action<string>? log = null)
    {
        _profileDir = profileDir;
        _log = log;
    }

    /// <summary>설치된 크롬(우선) 또는 엣지 실행 파일 경로.</summary>
    public static string? FindBrowserExe()
    {
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            Path.Combine(pf,    @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf86,  @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(local, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(pf86,  @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(pf,    @"Microsoft\Edge\Application\msedge.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<bool> CdpReadyAsync()
    {
        try
        {
            using var r = await Http.GetAsync(CdpUrl + "/json/version");
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>크롬을 디버그 모드로 띄우고 CDP 로 연결한다.</summary>
    public async Task StartAsync(string startUrl)
    {
        if (!await CdpReadyAsync())
        {
            string exe = FindBrowserExe()
                ?? throw new InvalidOperationException("크롬/엣지 실행 파일을 찾지 못했습니다.");
            _log?.Invoke($"[브라우저] 디버그 모드로 실행: {exe}");
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                UseShellExecute = false,
                ArgumentList =
                {
                    $"--remote-debugging-port={CdpPort}",
                    $"--user-data-dir={_profileDir}",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-blink-features=AutomationControlled",
                    "--hide-crash-restore-bubble",
                    "--disable-session-crashed-bubble",
                    "--restore-last-session=false",
                    startUrl,
                },
            };
            _chrome = Process.Start(psi);
            for (int i = 0; i < 60; i++)
            {
                if (await CdpReadyAsync()) break;
                await Task.Delay(500);
            }
            if (!await CdpReadyAsync())
                throw new InvalidOperationException("크롬 디버그 포트 연결 실패 (CDP 미응답)");
        }
        else
        {
            _log?.Invoke("[브라우저] 이미 실행 중인 디버그 크롬에 연결");
        }

        _pw = await Playwright.CreateAsync();
        _browser = await _pw.Chromium.ConnectOverCDPAsync(CdpUrl);
        Context = _browser.Contexts.Count > 0 ? _browser.Contexts[0] : await _browser.NewContextAsync();
        Page = Context.Pages.Count > 0 ? Context.Pages[0] : await Context.NewPageAsync();
    }

    private static readonly string[] ChallengeMarkers =
    {
        "just a moment", "보안 확인", "사람인지", "attention required",
        "checking your browser", "cloudflare",
    };

    /// <summary>현재 페이지가 Cloudflare 보안 확인 페이지인지 추정.</summary>
    public async Task<bool> LooksLikeChallengeAsync()
    {
        string title;
        try { title = (await Page.TitleAsync() ?? "").ToLowerInvariant(); }
        catch { title = ""; }
        if (ChallengeMarkers.Any(m => title.Contains(m))) return true;

        string body;
        try
        {
            var loc = Page.Locator("body");
            body = ((await loc.InnerTextAsync()) ?? "");
            body = body[..Math.Min(3000, body.Length)].ToLowerInvariant();
        }
        catch { body = ""; }
        return ChallengeMarkers.Any(m => body.Contains(m));
    }

    /// <summary>Cloudflare 가 사라질 때까지(사용자 수동 통과 포함) 대기.</summary>
    public async Task<bool> WaitPastCloudflareAsync(int maxWaitMs = ChallengeWaitMs)
    {
        int waited = 0;
        bool notified = false;
        while (waited < maxWaitMs)
        {
            if (!await LooksLikeChallengeAsync()) return true;
            if (!notified)
            {
                _log?.Invoke("[수동 확인 필요] Cloudflare 보안 확인 — 크롬 창에서 체크박스를 눌러주세요.");
                notified = true;
            }
            await Page.WaitForTimeoutAsync(1000);
            waited += 1000;
        }
        return !await LooksLikeChallengeAsync();
    }

    /// <summary>경기 테이블이 렌더될 때까지 대기. 없으면 Cloudflare 로 보고 통과 대기.</summary>
    public async Task SettleRowsAsync()
    {
        // DOM 에 테이블 셀이 붙을 때까지 대기(SSR 페이지는 즉시). 'visible' 이 아닌 'attached'.
        var opt = new PageWaitForSelectorOptions { Timeout = RowsWaitMs, State = WaitForSelectorState.Attached };
        try
        {
            await Page.WaitForSelectorAsync("table tr td", opt);
        }
        catch (Exception)  // 타임아웃 등은 무시하고 진행(파이썬과 동일). Cloudflare 면 통과 대기.
        {
            if (await LooksLikeChallengeAsync())
            {
                await WaitPastCloudflareAsync();
                try { await Page.WaitForSelectorAsync("table tr td", opt); }
                catch (Exception) { }
            }
        }
        await Page.WaitForTimeoutAsync(300);
    }

    public async ValueTask DisposeAsync()
    {
        // 안전 종료: CDP 로 크롬에 정상 종료 요청 → 비정상 종료/복원 안내 방지
        try
        {
            if (Page != null && Context != null)
            {
                var cdp = await Context.NewCDPSessionAsync(Page);
                await cdp.SendAsync("Browser.close");
            }
        }
        catch { }
        try { if (_browser != null) await _browser.CloseAsync(); } catch { }
        _pw?.Dispose();
        if (_chrome != null)
        {
            for (int i = 0; i < 40; i++)
            {
                if (_chrome.HasExited) break;
                await Task.Delay(250);
            }
            try { if (!_chrome.HasExited) _chrome.Kill(entireProcessTree: true); } catch { }
            _chrome.Dispose();
        }
    }
}
