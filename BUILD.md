# Tekken 전적 수집기 — C# (.NET 8) 네이티브

파이썬(main.py/ewgf.py)을 .NET 8로 포팅한 버전. WPF UI에서 **Wavu/EWGF 선택 + 식별코드·기간**을 입력해 수집한다. 파이썬 불필요.

## 프로젝트 구성
| 프로젝트 | 역할 |
|---|---|
| `TekkenStats.Core` | 수집 엔진 (CDP/Cloudflare, 추출·파싱, 집계, 엑셀/NPOI) |
| `TekkenStats.Wpf` | WPF UI (.NET 8) — Core 직접 호출 |
| `TekkenStats.Cli` | 검증용 콘솔 (덤프 HTML로 오프라인 테스트) |

Core 주요 클래스: `BrowserSession`(CDP), `EwgfExtractor`/`WavuParser`(파싱),
`Aggregations`(LINQ 집계), `ExcelWriter`+`EwgfReport`/`WavuReport`(엑셀),
`EwgfCollector`/`WavuCollector`(전체 흐름).

## 빌드 / 실행
```powershell
cd csharp            # 모든 명령은 csharp 폴더 기준 (또는 경로 앞에 csharp\ 붙이기)
dotnet build TekkenStats.slnx -c Release
dotnet run --project TekkenStats.Wpf      # UI 실행
```
> C# 프로젝트는 모두 저장소 하위 **`csharp\`** 폴더에 있다. 결과 엑셀은 기본적으로 상위
> `..\User\<이름>\` 에 저장된다(UI에서 변경 가능).
- 실행 PC에 **크롬(또는 엣지)** 설치 필요 (Playwright가 실제 크롬에 CDP로 붙음 — 브라우저 다운로드 `playwright install` 불필요).
- 첫 실행 시 Cloudflare가 뜨면 크롬 창에서 체크박스 한 번 통과 → 이후 프로필에 저장돼 자동.

## 배포
```powershell
.\publish.ps1
```
1. **독립 실행본** `dist\standalone\` — `TekkenStats.Wpf.exe`(~250MB, .NET 런타임 내장) **+ `.playwright` 폴더**.
   - 이 폴더 **전체**를 함께 배포해야 한다(드라이버는 단일 exe에 못 들어감).
   - 대상 PC에 .NET 런타임·파이썬 **불필요**, 크롬만 있으면 됨.
2. **난독화용** `dist\fd\` (프레임워크 종속) — ConfuserEx 입력용.

## 난독화 (ConfuserEx)
```
ConfuserEx.CLI.exe TekkenStats.Confuser.crproj   # → dist\fd\obfuscated\
```
- `TekkenStats.Core.dll`: rename+제어흐름+상수+ref proxy+anti-debug (강).
- `TekkenStats.Wpf.dll`: 상수/흐름만 — **rename 제외**(XAML 바인딩 보호).
- Core의 rename은 Wpf의 참조까지 자동 갱신되므로 호출은 정상 동작.
- 주의: `[JsonPropertyName]`으로 JSON 매핑이 고정돼 있어 멤버 rename 안전.
- NativeAOT는 WPF 미지원이므로 난독화로 대체.

## 검증 (오프라인)
```powershell
dotnet run --project TekkenStats.Cli            # ewgf: 덤프 HTML → 22시트 엑셀
dotnet run --project TekkenStats.Cli -- wavu    # wavu: 덤프 HTML → 9시트 엑셀
```
파이썬 결과와 수치 동일함을 확인(7,751경기 ewgf / 500경기 wavu).
