# Combat Preview 0.2.2 — 진단용 개발 빌드

0.2.0 소스를 이어받은 MIT 파생 모드입니다. **현재 hover 미리보기는 차단되어 있습니다.**
기존 DryRun이 실제 게임/모드 핸들러를 실행하므로 필수 조건인 게임 상태·모든 RNG 비간섭을
보장할 수 없었습니다. API 존재 검사나 설정 토글로 이 차단을 해제할 수 없습니다.
게임 API 컴파일 성공과 게임 실행 검증은 별개이며, 실제 게임 실행 검증은 아직 없습니다.

## 0.2.2 진단 보완

사용자 제공 0.2.1 로그에서 공격 굴림 50건·피해 14건이 확인됐고, 무기 공격과 연결된 명중 14건은
모두 피해 이벤트로 연결됐습니다. 이 범위의 로드/관측 성공은 확인했지만 상태/RNG 보존이나 예측 정확도 검증은 아닙니다.

- `RuleAttackRoll.IsFake`인 이벤트와 그 피해는 관측 통계에서 제외합니다.
- 부모 무기 공격이 없는 굴림은 `unassociated-observation`으로 분리하고 예측 비교에 사용하지 않습니다.
  이름이 없는 대상/부모 없는 굴림이 모두 가짜라고 가정하지 않습니다.
- JSONL schema 2는 매 줄에 세션 ID, 레코드 순번, 게임/모드/UMM/Harmony 버전과 MVID를 담습니다.
- 방어 누락 이유와 캐시 상세를 별도 배열로 기록해 앞부분의 긴 안내 때문에 수치가 잘리는 문제를 고쳤습니다.
- 보호막은 양수 풀 캐시가 아니라 실제 `UsePool` 필드로 분류합니다. 고갈된 풀도 풀로 남고, 플래그를
  못 읽으면 불명으로 처리합니다. 캐시 값으로 유효 방어 계산을 완료했다고 표시하지 않습니다.

0.2.2 자체의 게임 재실행 검증은 아직 없습니다. [첫 실전 로그 분석](docs/RUNTIME_LOG_REVIEW.ko.md)을 참고하세요.

## 기존 0.2.1 변경

- 기존 C# 계산 테스트를 실제 실행하고 회귀 검사를 확장했습니다.
- 기본 게임/TTT 방어 투영 계약과 순수 값 스냅샷을 분리했습니다.
- AND/OR 관통, 강화 임계값, 명시적 DR 최대 선택/중첩, 저항/면역을 fixture로 검증합니다.
- 기본 게임의 기존 방어 캐시 필드를 읽는 초안을 추가했습니다. 이 값들은 유효 방어값이 아니며
  자동 판독 완료나 `Complete=true`로 승격하지 않습니다. `AllSources`, `TryInitialize`,
  `ApplyDamageReduction`, `SpendPool`, `Cleanup`을 판독용으로 호출하지 않습니다.
- RAW / 실제 대상 / 수동 시나리오 보고서를 분리하고, 누락 이유와 합계 계산 불가를 전달합니다.
  화면 연결 코드는 컴파일됐지만 hover 자체가 차단되어 게임 화면에서는 아직 사용할 수 없습니다.
- 기본 OFF인 로컬 수동 진단: 실제 명중 굴림·피해 이벤트를 ID로 연결합니다. 추가 이벤트/주사위를
  생성하지 않으며 원격 통신도 하지 않습니다. 아직 실시간 예측과 비교할 완전한 상태 스냅샷은 없습니다.
- d20 수정자 9개 이상은 조용히 8개만 계산하지 않고 계산 불가로 처리합니다.

## 현재 사용 환경과 검증 경계

제공된 UMM DLL 메타데이터는 0.33.3, ModFinder는 1.5.3입니다. 사용자는 TTT 미사용이라고
알려주었습니다. ModFinder는 설치 관리 앱이며 모드의 런타임 의존성은 아닙니다.
제공된 게임 참조에 대한 전체 net472 빌드는 성공했습니다. 게임 DLL은 결과물에 넣지 않습니다.
게임 표시 버전은 Unity `Version` 리소스를 실행 중 읽으므로 Managed DLL만으로 확정하지 않습니다.
버전/어셈블리 식별 및 명령별 결과는 `docs/VALIDATION.md`를 확인하세요.

## 계산 테스트

.NET SDK 8과 Python 3이 필요합니다. 프로젝트 루트 `CombatPreview/`에서:

```bash
dotnet run --project tests/CombatPreview.Tests.csproj -c Release
python3 tests/reference_check.py
python3 -m venv .venv
.venv/bin/python -m pip install tree-sitter==0.25.2 tree-sitter-c-sharp==0.23.1
.venv/bin/python tests/syntax_check.py
```

Windows에서는 `.venv/Scripts/python.exe`를 사용합니다. Python 검사는 C# 실행을 대신하지 않습니다.
예시 진단 보고서 생성 명령은 `docs/DIAGNOSTICS.ko.md`에 있습니다.

## 빌드 및 패키징

본인 게임 설치의 `Wrath_Data/Managed` 전체 DLL과 설치된 UMM DLL을 사용합니다.
게임 DLL은 비공개 위치에 두고 소스 저장소에 추가하지 않습니다.

```bash
dotnet build CombatPreview.csproj -c Release -p:GameDir="게임 설치 경로" -p:UmmDir="UMM DLL 폴더"
python3 tools/package.py --game-dir "게임 설치 경로" --umm-dir "UMM DLL 폴더"
```

Managed 폴더만 제공된 환경에서는 다음을 사용할 수 있습니다.

```bash
python3 tools/package.py --managed-dir "Managed 폴더" --umm-dir "UMM DLL 폴더"
```

Windows PowerShell 대안: `./build.ps1 -GameDir '게임 설치 경로' -UmmDir 'UMM DLL 폴더'`.
패키징은 빌드 성공 뒤 명시한 모드 DLL/설정/라이선스/안내서 6개만 포함합니다.
출력은 `dist/CombatPreview-0.2.2-diagnostic-only-UMM.zip`이며 소스 ZIP과 다릅니다.

## 진단용 설치·설정·제거

1. 게임을 닫고 진단용 UMM ZIP을 ModFinder의 로컬 모드 설치 기능 또는 UMM으로 설치합니다.
   ModFinder의 실제 설치 동작은 이 Linux 환경에서 실행 검증하지 않았습니다.
2. 게임을 시작하고 Ctrl+F10 → `Combat Preview (Diagnostics Only)`를 확인합니다.
3. 로컬 로그가 필요할 때만 `Local passive attack diagnostics`를 켭니다. 기본은 OFF입니다.
4. 자신의 테스트 저장 파일에서 일반 무기 단타/풀어택을 수행합니다.
5. 모드 폴더의 `CombatPreview.diagnostics.jsonl`에서 관측값을 확인합니다.
   기록은 초당 최대 20개, 현재/이전 파일 각각 1 MiB입니다. 자세한 해석은 `DIAGNOSTICS.ko.md` 참조.
6. 제거할 때는 게임을 닫고 ModFinder/UMM에서 제거합니다. 런타임 언로드는 제공하지 않습니다.

현재 진단용 패키지에서는 hover 패널이 나타나지 않는 것이 차단 정책에 맞는 동작입니다.
진단 관측 코드도 게임에서 실행 검증해야 하며, 빌드 성공을 설치/전투 성공으로 간주하지 않습니다.

## 남은 개발 작업

읽기 전용 공격 예측기, 완전한 방어 판독, 상태 변경 검출 및 실제 비교 연결, 게임 상태·모든 RNG
계측이 필요합니다. 암습/조건부 발동 효과, 탈것 합산, 취약성/난이도 배율, 소모형 보호막의 연속
분기 계산은 지원하지 않습니다. 수동 시나리오 수학은 실제 적 피해 자동 계산이 아닙니다.
`docs/SAFETY_AUDIT.md`, `docs/DEFENSE_ADAPTERS.ko.md`, `docs/RUNTIME_TEST_PLAN.ko.md`에 경계와 다음 절차를 기록했습니다.

기존 `LICENSE`와 `THIRD_PARTY.md`의 MIT 저작권/출처 고지를 유지합니다.
