# Tekken 전적 수집기 (C# .NET 8 WPF) 배포 스크립트
# 사용: PowerShell 에서  .\publish.ps1
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# ── 0) 이전 산출물 정리 (옛 exe 가 남아 헷갈리는 것 방지) ──────────
if (Test-Path "$root\dist") {
    Remove-Item "$root\dist" -Recurse -Force
    Write-Host "[정리] 기존 dist 폴더 삭제"
}

# ── 1) 독립 실행본: 단일 exe 하나 (파이썬·.NET 런타임 설치 불필요) ──
# .playwright 드라이버까지 exe 안에 내장(IncludeAllContentForSelfExtract) → 실행 시 임시폴더로 자동 추출.
# 결과: dist\standalone\TekkenStats.Wpf.exe  (이 파일 하나만 배포)
dotnet publish "$root\TekkenStats.Wpf" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -p:DebugType=none `
    -o "$root\dist\standalone"
Write-Host "[완료] 단일 exe: dist\standalone\TekkenStats.Wpf.exe (이 파일 하나만 배포)"

# ── 2) (선택) 난독화용 프레임워크 종속 퍼블리시 ──────────────────
# 이후 ConfuserEx 로 dist\fd 의 DLL 을 난독화 → dist\fd\obfuscated
#   ConfuserEx.CLI.exe "$root\TekkenStats.Confuser.crproj"
# (이 빌드 실행에는 대상 PC 에 .NET 8 Desktop Runtime 필요)
dotnet publish "$root\TekkenStats.Wpf" -c Release -o "$root\dist\fd"
Write-Host "[완료] 난독화용(프레임워크 종속): dist\fd  →  ConfuserEx.CLI TekkenStats.Confuser.crproj"
