using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;

namespace TekkenStats.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (RelaunchWithAsciiExtractDirIfNeeded())
        {
            Shutdown();   // 부모 프로세스는 종료(자식이 영문 추출 경로로 다시 실행됨)
            return;
        }
        base.OnStartup(e);
    }

    /// <summary>
    /// 단일 exe(self-extract)는 .playwright 드라이버를 %TEMP% 로 추출해 실행하는데, Windows 사용자
    /// 이름에 공백/한글이 있으면 그 경로에서 node 드라이버 실행이 실패(Process exited)한다.
    /// 추출 기본 경로가 비-ASCII 면, 추출 위치를 영문 경로로 강제하는 환경변수를 설정해 자기 자신을
    /// 한 번 재실행한다. → run.bat 같은 별도 런처 없이 단일 exe 만으로 해결.
    /// </summary>
    private static bool RelaunchWithAsciiExtractDirIfNeeded()
    {
        const string marker = "TEKKEN_ASCII_RELAUNCH";
        if (Environment.GetEnvironmentVariable(marker) == "1") return false;  // 이미 재실행된 자식

        // 단일 파일(self-extract)일 때만. dotnet run/일반 빌드는 EntryAssembly.Location 이 비어있지 않다.
        bool singleFile = string.IsNullOrEmpty(Assembly.GetEntryAssembly()?.Location);
        if (!singleFile) return false;

        // 드라이버가 추출될 기본 경로(%TEMP%)에 비-ASCII 문자가 있으면 문제가 된다.
        if (!Path.GetTempPath().Any(c => c > 127)) return false;

        try
        {
            string asciiBase = Path.Combine(
                Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData",
                "TekkenRecordMaker", "bundle");
            Directory.CreateDirectory(asciiBase);

            string exe = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? "";
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
