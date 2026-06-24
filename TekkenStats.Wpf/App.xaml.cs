using System.Diagnostics;
using System.IO;
using System.Windows;

namespace TekkenStats.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (RelaunchFromAsciiPathIfNeeded())
        {
            Shutdown();   // 부모 종료(자식이 영문 추출 경로로 다시 실행됨)
            return;
        }
        base.OnStartup(e);
    }

    /// <summary>
    /// 단일 exe(self-extract)는 .playwright 드라이버를 사용자 임시폴더(%TEMP%)로 푼다. Windows 사용자
    /// 이름에 공백/한글이 있으면 그 경로가 비-ASCII 가 되어 node 드라이버가 스크립트를 못 읽고 즉시
    /// 종료한다(Process exited). 추출 경로가 비-ASCII 면, 항상 영문이고 쓰기 가능한 C:\Users\Public
    /// 아래로 다시 풀도록 환경변수(DOTNET_BUNDLE_EXTRACT_BASE_DIR)를 설정해 자기 자신을 재실행한다.
    /// → 사용자 PC 무수정으로 해결(run.bat 같은 별도 런처 불필요).
    /// </summary>
    private static bool RelaunchFromAsciiPathIfNeeded()
    {
        const string marker = "TEKKEN_ASCII_RELAUNCH";
        if (Environment.GetEnvironmentVariable(marker) == "1") return false;  // 이미 재실행된 자식

        string baseDir = AppContext.BaseDirectory;                          // self-extract 면 추출(임시) 경로
        string exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? "";

        // 단일 exe(self-extract): 실행 파일 경로와 추출 경로가 다르다. 일반 빌드/dotnet run 은 같다.
        bool selfExtracted = !string.Equals(
            baseDir.TrimEnd('\\'), exeDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        bool nonAscii = baseDir.Any(c => c > 127);
        if (!selfExtracted || !nonAscii) return false;   // 영문 경로면 그대로 진행

        try
        {
            string pub = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";
            string asciiBase = Path.Combine(pub, "TekkenRecordMaker", "bundle");
            Directory.CreateDirectory(asciiBase);

            string exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(exe)) return false;

            var psi = new ProcessStartInfo { FileName = exe, UseShellExecute = false };
            psi.Environment["DOTNET_BUNDLE_EXTRACT_BASE_DIR"] = asciiBase;
            psi.Environment[marker] = "1";
            foreach (var a in Environment.GetCommandLineArgs().Skip(1))
                psi.ArgumentList.Add(a);

            Process.Start(psi);
            return true;   // 부모 종료
        }
        catch
        {
            return false;  // 재실행 실패 시 현재 프로세스로 그대로 진행
        }
    }
}
