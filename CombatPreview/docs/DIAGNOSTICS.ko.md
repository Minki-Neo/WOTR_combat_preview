# 로컬 진단 사용법 — 0.2.2

현재 게임 연결은 **수동 관측 전용**입니다. 라이브 예측이 없으므로 실제 로그에 예측 비교 성공을
기대하면 안 됩니다. 사용자 제공 0.2.1 로그에서는 로드와 이벤트 관측이 확인됐습니다. 0.2.2 재실행 및 구독 해제/실패 복구는 미검증입니다.

진단용 UMM ZIP 설치 후 Ctrl+F10 → Combat Preview → `Local passive attack diagnostics`를 켭니다.
기본 OFF이며 자동 전송은 없습니다. 실제 일반 무기 공격을 수행한 뒤 모드 폴더의
`CombatPreview.diagnostics.jsonl`을 확인합니다. 게임 표시 버전/MVID/UMM/Harmony/패치 소유자 정보는
UMM의 모드 로그에 기록됩니다. 기본 OFF인 진단과 모드의 기본 오류/호환성 로그는 별개입니다.

- `unassociated-observation`: 부모 RuleAttackWithWeapon이 없는 비가짜 굴림. 일반 무기 공격/명중 표본에 섞지 않습니다.
- `observation`: 이미 완료된 공격 굴림의 공격자·대상·무기·턴·타격 번호, AB/AC/페널티, 풀어택/치명타 확인 상태, rolled/hit.
- `damage-observation`: 이미 완료된 피해 이벤트의 `reportedDamage`, `rawDamage`, `beforeDifficulty`.
  `rollEventId`로 원래 공격 굴림의 `eventId`와 연결합니다. 이는 HP 변화량, 별도 공격, 자동 예측값이 아닙니다.
  피해 리다이렉트 시 실제 피해 이벤트의 공격자/대상 ID를 기록합니다.
- schema 2의 `sessionId`와 `recordSequence`를 사용합니다. 객체 ID는 세션 내 식별자이므로 서로 다른 세션의 같은 숫자를 연결하지 않습니다.
- `runtime`은 모든 레코드에 게임/모드/UMM/Harmony 버전, 게임 MVID, TTT 어셈블리 감지 여부를 담습니다.
  파일 회전으로 시작 부분이 사라져도 식별 정보가 남습니다. TTT 감지는 규칙 활성화의 증거가 아닙니다.
- `IsFake`인 굴림/피해는 기록하지 않으며 UMM 설정의 Ignored simulated events 카운터에 반영합니다.
- `defenseMissing`과 `defenseObserved`를 각각 확인합니다. 각 배열은 32개까지 기록하고 넘치면 잘림 표시를 추가합니다.
  개별 문자열은 기존 512자 제한을 유지합니다. `defenseEvidence`는 짧은 상태 안내이며 수치가 아닙니다.
- 진단 경로는 Rulebook.Trigger, 생성한 공격/피해/굴림 실행, RNG, live 감소/소모/정리를 호출하지 않습니다.
  실제 이벤트의 사후 숫자와 이미 존재하는 방어 캐시 필드만 읽습니다. 게임 내 비간섭 증명은 별도입니다.
- 라이브 장면/전체 상태 revision이 없으므로 conditionsComplete=false입니다. 상태가 같다고 추정하지 않습니다.
- 피해 구성/감소 서명은 live에서 미확정입니다. cached defenseEvidence도 계산 가능한 방어값이 아닙니다.
- 진단 파일은 UTF-8 바이트 기준 현재/이전 `.prev` 각각 최대 1 MiB, 초당 최대 20개 레코드입니다.
  최근 256개 이벤트의 중복을 억제합니다. 제한으로 잘린 문자열에는 `[truncated]`가 붙습니다.
  파일 쓰기 실패 시 기록이 중단되며 UMM 설정에서 중단 상태를 표시합니다. 알려진 파일 I/O 실패는
  recorder에서 게임 콜백으로 전파하지 않습니다. 기타 관측 예외도 진단을 끕니다.

## 비교 코어 재현 (게임 없이)

프로젝트 루트에서 아직 없는 파일 경로를 지정합니다. 기존 보고서를 덮어쓰지 않습니다.

```bash
dotnet run --project tests/CombatPreview.Tests.csproj -c Release -- --diagnostic-fixture /tmp/combat-preview-fixture.jsonl
```

이 명령은 **fixture 예시**만 생성합니다. `docs/examples/diagnostic-fixture.jsonl`에도 실행 결과가 있습니다.
Fixture를 실제 전투 결과로 해석하면 안 됩니다. 순수 비교 코어는 예측 당시 공격자/대상/무기/턴/장면/
타격 번호 및 완전한 conditionRevision이 같은 경우에만 AB/AC/피해 구성/감소 서명을 비교합니다.
조건 변경, 1초 초과, 시계 역행, 불완전 revision, 수동 출처, 다른 턴, invalidate 후에는 비교하지 않습니다.
예측은 한 번 소비하며, 로그에 쓰이지 못한 예측으로 후속 관측을 연결하지 않습니다.

`prediction.hit`는 치명타 포함 명중 확률이고 `prediction.crit`는 무조건부 확정 치명타 확률입니다.
`normalHitMin/normalHitMax`는 일반 명중 피해 범위입니다. 실제 무작위 피해를 기대값과 비교하지 않습니다.
확률 검증은 동일 조건의 충분한 표본과 별도의 통계 절차가 필요합니다. 한 번의 주사위 차이는 불일치가 아닙니다.

실제 사용 가능한 예측/실전 비교 모드를 완성하려면 읽기 전용 공격·방어 스냅샷과 전체 conditionRevision,
턴/장면 무효화 연동 및 실제 이벤트 순서 검증이 필요합니다. 현재 이 부분은 fixture로만 검증했습니다.

## 전달받은 로그 분석

프로젝트 루트에서 `python3 tools/analyze_diagnostics.py 받은파일.zip`을 실행하면 이름/원시 행을 출력하지 않는
집계가 나옵니다. JSONL 직접 입력도 지원합니다. schema 1은 세션 정보가 없으므로 재시작 간 동일 ID 판별을
보장하지 못합니다. schema 2는 세션별로 연결합니다. NoPrediction은 현재 예측 차단 정책에서 정상이며 오류가 아닙니다.

파일은 재시작 후에도 이어 쓰므로 전체 행 수와 최신 실행 행 수를 구분하세요. 분석 도구 출력의
`sessions`는 세션별 집계, `latestIdentifiedSession`은 파일에서 마지막으로 관측된 식별 가능 세션입니다.
이전 schema 1의 세션 불명 행은 별도 그룹으로 남기며 최신 버전의 오류로 합산하지 않습니다.
