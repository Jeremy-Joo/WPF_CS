using System.Diagnostics;
using System.IO;
using System.Windows;
using TekkenStats.Core;

namespace TekkenStats.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        UpdateEnabled();
        LoadSettings();                       // 지난번 입력값 복원
        Closing += (_, _) => SaveSettings();  // 종료 시 저장
    }

    private void OnAllChanged(object sender, RoutedEventArgs e) => UpdateEnabled();

    private void UpdateEnabled()
    {
        if (dpStart == null || dpEnd == null || chkAll == null) return;
        bool all = chkAll.IsChecked == true;
        dpStart.IsEnabled = !all;
        dpEnd.IsEnabled = !all;
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        var ids = ParseIds(txtIds.Text);
        if (ids.Count == 0) { SetStatus("식별코드를 입력하세요"); return; }

        string outRoot = txtOutDir.Text.Trim();
        string profile = txtProfile.Text.Trim();

        DateTime? start = null, end = null;
        if (chkAll.IsChecked != true)
        {
            start = dpStart.SelectedDate ?? new DateTime(2024, 4, 1);
            end = dpEnd.SelectedDate ?? DateTime.Today;
        }

        void Log(string s) => Dispatcher.Invoke(() => AppendLog(s));

        SaveSettings();   // 실행 시점의 입력값 저장
        SetRunning(true);
        SetStatus("EWGF 수집 중…");
        AppendLog($"=== EWGF 수집 시작: {ids.Count}명 ===");
        AppendLog("(Cloudflare 창이 뜨면 체크박스를 직접 통과해 주세요)");

        try
        {
            foreach (var id in ids)
            {
                AppendLog($"\n--- {id} ---");
                var r = await Task.Run(() => EwgfCollector.CollectAsync(id, start, end, outRoot, profile, Log));
                AppendLog(string.IsNullOrEmpty(r.Error)
                    ? $"[OK] {r.Count}경기 → {Path.GetFileName(r.OutPath)}"
                    : $"[실패] {r.Error}");
            }
            SetStatus("완료");
            AppendLog("\n=== 전체 완료 ===");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            SetStatus("오류");
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => SetStatus("중지는 크롬 창을 닫아주세요");

    private void OnOpenFolder(object sender, RoutedEventArgs e)
    {
        string dir = txtOutDir.Text.Trim();
        if (Directory.Exists(dir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
        else
            SetStatus("폴더가 없습니다");
    }

    private static List<string> ParseIds(string text)
    {
        var result = new List<string>();
        var seen = new HashSet<string>();
        foreach (var tok in (text ?? "").Split(new[] { '\n', '\r', ',', ' ', '\t' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var t = tok.Trim();
            if (t.Length > 0 && seen.Add(t)) result.Add(t);
        }
        return result;
    }

    private void SetRunning(bool running)
    {
        btnRun.IsEnabled = !running;
        btnCancel.IsEnabled = running;
        prog.IsIndeterminate = running;
        txtIds.IsEnabled = !running;
        grpDate.IsEnabled = !running;
    }

    private void SetStatus(string s) => txtStatus.Text = s;

    private void AppendLog(string line)
    {
        txtLog.AppendText(line + "\n");
        txtLog.ScrollToEnd();
    }

    // ── 입력값 저장/복원 (마지막 값 기억) ──────────────────
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TekkenRecordMaker", "settings.json");

    private sealed class UiSettings
    {
        public string? Ids { get; set; }
        public string? OutDir { get; set; }
        public string? Profile { get; set; }
    }

    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var s = System.Text.Json.JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(SettingsPath));
            if (s == null) return;
            if (!string.IsNullOrWhiteSpace(s.Ids)) txtIds.Text = s.Ids;
            if (!string.IsNullOrWhiteSpace(s.OutDir)) txtOutDir.Text = s.OutDir;
            if (!string.IsNullOrWhiteSpace(s.Profile)) txtProfile.Text = s.Profile;
        }
        catch { /* 손상 시 기본값 사용 */ }
    }

    private void SaveSettings()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var s = new UiSettings { Ids = txtIds.Text, OutDir = txtOutDir.Text, Profile = txtProfile.Text };
            File.WriteAllText(SettingsPath,
                System.Text.Json.JsonSerializer.Serialize(s, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* 저장 실패 무시 */ }
    }
}
