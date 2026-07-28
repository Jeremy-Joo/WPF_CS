# TekkenStats — 철권8 전적 수집/분석 (WPF)

철권8 전적을 수집하고 여러 명을 비교 리포트로 뽑는 도구.

## 구조 — 3개 프로젝트

| 프로젝트 | 타깃 | 역할 |
|---|---|---|
| `TekkenStats.Core` | net8.0 | 수집·집계 로직 |
| `TekkenStats.Cli` | net8.0 | CLI |
| `TekkenStats.Wpf` | net8.0-windows | GUI |

로직은 **`Core`에 넣는다.** Wpf/Cli 양쪽에서 쓰므로 UI 프로젝트에 로직을 직접 넣지 말 것.

## 빌드 / 배포

```
dotnet build TekkenStats.slnx
publish.ps1                        # 배포
```

`BUILD.md`에 빌드 절차가 따로 있다. 배포 전에 확인할 것.
`TekkenStats.Confuser.crproj` — ConfuserEx 난독화 설정.

## ⚠️ 과거에 실제로 터진 문제들

커밋 이력에 남은 것들이라 회귀시키지 말 것.

- **한글 디렉터리에서 충돌** — 경로 처리에 인코딩 가정을 넣지 말 것
- **한글 관련 충돌 오류** 별도 수정 이력 있음
- **저사양 PC에서 수집이 안 되던 문제** — 타임아웃/동시성 값을 조일 때 주의
- 식별코드 입력 시 `-`는 자동 삭제된다
- 탭 강제종료 시 자동복구 로직이 있다

## 주의

- 수집은 외부 사이트에 의존한다. 사이트 개편 시 조용히 깨진다.
