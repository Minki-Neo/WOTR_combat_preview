# 로컬 진단 사용법 — 0.4.0 베타

최신 자동 방어/공통 계산 결과 필드는 문서 끝의0.4.0 절을 참조하세요. 이전 절의 지원 제한은 당시 버전의 기록입니다.

Ctrl+F10 → Combat Preview (Read-only Beta)에서 **베타 무기 미리보기 켜기**와
**표시 모델과 실제 AB/AC/무기 구성의 차이를 로컬 로그에 기록**을 켭니다.
미리보기는 기본 OFF, 비교 기록은 기본 ON, 화면 알림은 기본 OFF입니다.
비교를 켜면 별도의 Local passive attack diagnostics 체크 없이도 로컬 로그를 기록합니다.
전체 수동 관측만 필요하면 기존 Local passive attack diagnostics를 사용합니다. 자동 전송은 없습니다.

일반 제조 무기를 주무기로 든 캐릭터로 턴제 전투에서 적을 1초 가리킨 뒤 실제 공격하세요.
모드 폴더의 `CombatPreview.diagnostics.jsonl`과 패널 캡처를 함께 확인합니다.
패널 상단의 체력은 현재/최대 HP와 별도의 임시 HP이며 최대 0.2초 간격으로 갱신합니다.
체력 수치 자체는 모델 차이 경고의 비교 항목에 포함하지 않습니다.

## 베타 비교 레코드

- `beta-preview`: 실제로 그려진 기본 주무기 모델의 행별 AB/AC, 확률, 방어 전 피해 분포와 기대값.
  `quality=PartialEstimate`, `fullConditionsVerified=false`, `afterDefenseAvailable=false`입니다.
- `observation.betaPreviewId`: 실제 공격 굴림과 표시 모델의 연결. 이때 status는 `BetaPreviewAssociated`입니다.
  연결만 의미하며 계산의 정확성 확인은 아닙니다.
- `beta-comparison`: `previewId`와 `rollEventId`로 모델과 실제 굴림을 연결합니다.
  `comparedFields`는 실제로 대조한 항목, `differentFields`는 차이 항목입니다.
  비교 대상은 AB, 일반 AC, 첫 무기 피해의 주사위/고정 보너스와 피해 구성 수입니다.
  `ComparedPartialModel`에서 차이가 있어도 미반영 특기·강화·조건부 효과 등이 원인일 수 있습니다.
  확정 버그나 전체 조건 동일의 증거가 아닙니다. 일치 역시 전체 예측 정확도의 증거가 아닙니다.
- 다른 상태 입력은 `InputsChanged`, 2초 지난 표시/10초 지난 공격 완료는 `PreviewExpired`,
  다른 무기/턴/대상은 `PairChanged`, 공격 모드 변경은 `AttackModeChanged`, 모델 밖 타격은
  `UnmodeledAttack`, 비교 항목이 전혀 없으면 `NoComparableFields`로 기록하며 차이 경고를 내지 않습니다.
- 화면 알림은 최대 3초에 한 번, 7초 동안 표시합니다. 전투를 멈추지 않습니다.
- DR/저항/보호막은 실제 적용 후 피해를 예측할 수 없어 자동 일치/불일치 판정하지 않습니다.
  방어 파트 존재를 미리 알리고 아래 실제 피해와 방어 증거를 수집합니다.
- 실제 무작위 피해 한 번을 기대값과 비교하지 않습니다. 치명타/명중 확률 역시 한 번으로 판정하지 않습니다.

`rateLimitedRecords`와 `ignoredSimulatedEvents`는 기록 제한/가짜 이벤트 제외 카운터입니다.
회전이나 기록 제한으로 연결 레코드가 빠질 수 있으며 분석 도구가 누락을 보고합니다.
게임 버전/MVID/UMM/Harmony 정보는 각 레코드와 UMM 로그에서 확인합니다.
**0.3.0의 패널/체력 표시와 비교 연결은 사용자 실행에서 관측했습니다. 0.3.1 새 UI와 게임 추정 사본 경로는 아직 게임에서 검증하지 않았습니다.**

## 실제 이벤트 관측

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

## 기존 완전 조건 비교 코어 재현 (게임 없이)

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

완전한 예측/실전 비교 모드를 완성하려면 읽기 전용 공격·방어 스냅샷과 전체 conditionRevision,
턴/장면 무효화 연동 및 실제 이벤트 순서 검증이 필요합니다. 현재 이 부분은 fixture로만 검증했습니다.

## 전달받은 로그 분석

프로젝트 루트에서 `python3 tools/analyze_diagnostics.py 받은파일.zip`을 실행하면 이름/원시 행을 출력하지 않는
집계가 나옵니다. JSONL 직접 입력도 지원합니다. schema 1은 세션 정보가 없으므로 재시작 간 동일 ID 판별을
보장하지 못합니다. schema 2는 세션별로 연결합니다. NoPrediction은 표시 모델 없이 관측한 공격에서 정상이며 오류가 아닙니다.

파일은 재시작 후에도 이어 쓰므로 전체 행 수와 최신 실행 행 수를 구분하세요. 분석 도구 출력의
`sessions`는 세션별 집계, `latestIdentifiedSession`은 파일에서 마지막으로 관측된 식별 가능 세션입니다.
이전 schema 1의 세션 불명 행은 별도 그룹으로 남기며 최신 버전의 오류로 합산하지 않습니다.

## 베타 fixture 재현

```bash
dotnet run --project tests/CombatPreview.Tests.csproj -c Release -- --beta-fixture /tmp/new-beta-example.jsonl
python3 tools/analyze_diagnostics.py /tmp/new-beta-example.jsonl
```

`docs/examples/beta-fixture.jsonl`은 AB 차이 1건과 기대값 비교를 하지 않는 실제 피해 예시입니다.
게임 실행 결과가 아닌 `FIXTURE`입니다. 실제 세션과 합쳐 정확도 통계를 내지 않습니다.

## 0.3.1 추가 필드

`beta-preview.basis`는 기본 능력치 모델 또는 `observed-existing-estimate/basic-primary-iteratives`입니다.
후자는 게임이 이미 만든 완료된 가짜 추정의 스칼라 사본이며 이 모드가 생성한 이벤트가 아닙니다.
가짜 이벤트는 계속 실제 공격/피해 통계에서 제외하며 제외 카운터를 유지합니다.

`damageComponents`는 화면의 모든 피해 구성(kind/rolls/sides/bonus/multiplies), 행별 `meanOnHitRaw`는
치명타 포함·빗나감 제외의 조건부 방어 전 평균입니다. `actualDamageComponents`와 `Damage[n]`
차이 필드로 추가 구성의 주사위/보너스/종류도 비교합니다. 적용 후 방어가 확인됐다는 뜻은 아닙니다.

수동 시나리오를 선택하면 `manualScenario=true`이고 `expectedManualScenario` 및
`meanOnHitManualScenario`를 별도로 기록합니다. 실제 대상의 `afterDefenseAvailable`은 계속 false입니다.
기본 방어 전 수치는 덮어쓰지 않습니다. 화면의 `—`는 미확인이며 0을 의미하지 않습니다.

## 0.3.2 기승/상세/완료 규칙 관측

F6 상세/F7 대상 고정. 패널로 이동하거나 고정 뒤 대상을 벗어나면 표시 시점 정보를 보관하며
보관 화면을 새 beta-preview로 기록하거나 tracker의 표시 시간을 갱신하지 않습니다.

기수/탈것/각 자연 무기마다 별도의 beta-preview를 기록합니다. actor/target/weapon/turn ID가 각각
실제 공격과 연결되며 자연 무기는 전역 attackIndex가 1,2여도 해당 무기 행에 연결합니다.
같은 프레임의 여러 모델을 반복 기록하지 않도록 표시 ID와 수동 시나리오 키를 중복 억제합니다.

새 basis `observed-completed-rules`는 이미 실행된 AB/AC 계산의 Result를 복사한 것입니다.
가짜 굴림 생성 없이 직접 OnTrigger를 호출하는 외부 계산도 postfix가 관측할 수 있습니다.
`completedRuleObserver`는 Installed/Unavailable, `observedABResults`와 `observedACResults`는 관측 횟수입니다.
횟수가 0이면 해당 완료 경로가 아직 관측되지 않은 것입니다. 횟수가 있어도 현재 대상/무기/턴/제한 키 및
1.5초 수명을 만족하는 한 쌍이 없으면 기본 모델로 돌아갑니다. 현재 모든 조건의 일치를 보장하지 않습니다.

`totalsObserved`는 관측 출처 여부이며 정확도 통과가 아닙니다. `naturalWeapon`은 자연 무기 모델 여부,
행의 `requiredD20`은 자연1 실패/자연20 명중을 반영한 최소 눈금(2–20)입니다.
AC17/AB9라면 8입니다. 전체 상태/확률 효과는 미확정이므로 fullConditionsVerified는 계속 false입니다.
새 postfix의 실제 설치/콜백/상호 운용은 사용자 게임에서 확인해야 합니다.

## 0.3.3 잡기/공격 수/방어 증거

`grip`은 실제 판독한 잡기 해석, `fullAttack`/`bab`는 기본 공격 모드/공격 보너스,
`attackCountSource`는 basic-bab / active-extra-attack-components / observed-attack-count-rule입니다.
`attackCountReasons`에 판독 가능한 활성 추가 공격 효과명과 수를 기록합니다. `observedAttackCounts`는
완료된 공격 수 관측 횟수입니다. 행별 penalty=0인 두 행은 같은 AB의 추가 공격이며 -5 반복이 아닙니다.
관측 패턴이 있으면 활성 효과 수를 다시 더하지 않아 중복 계산하지 않습니다.

방어의 `defenseObserved`에는 energy resistance type 및 physical bypass: OR/material/form/magicMinimum/
alignment/reality/weaponType/melee/epic/validWeapons/attackTypes 같은 캐시/설정 증거를 추가합니다.
값이 있다는 이유로 관통 판정이나 후처리 피해 계산이 지원되는 것은 아닙니다. 미확인 상태를 유지합니다.

현재 파일이 가득 차면 `.prev`로 회전합니다. 이번 사용자.prev의 최신0.3.2 세션은 순번1–97의
미리보기만 담았고 이후 실제 공격은 현재 파일에 있을 수 있습니다. 두 파일을 함께 전달하세요.

## 0.4.0 공통 규칙 관측과 자동 방어

이 절이 최신 동작입니다. 이전 절의 “자동 방어 항상 불가”는0.3.x 이전 기록입니다.

`beta-preview`의 각 행에 `expectedAfterDefense`, `meanOnHitAfterDefense`, `afterDefenseReason`을 기록합니다.
숫자는 해당 패널 행에서 사용하는 동일한 `AfterDefense` 계산 결과이며, 수동 시나리오는 기존 별도 필드를 유지합니다.
`afterDefenseAvailable`은 모든 표시 행의 지원된 정적 방어 계산 가능 여부입니다. `fullConditionsVerified=false`는 그대로입니다.
`defenseNotes`에는 DR 후보/관통/최대 적용값, 저항/면역/억제 및 계산 불가 이유를 기록합니다.

`attackObserved` / `acObserved` / `damageObserved`를 구분합니다. 대상 없는 무기 규칙을 읽어도 `acObserved=false`입니다.
예약 효과를 추정으로 더한 명중/피해는 해당 observed 플래그가 false입니다. `pendingAttackModifier` / `pendingDamageModifier`가 추정 보정입니다.
`modifierNotes`에는 게임 규칙의 명중 합계/보정 내역 또는 제한된 보조 판독 내역을 남깁니다.
`basis`의 `observed-weapon-rules/estimated-target-ac`는 공통 무기 AB/피해를 읽고 대상 AC는 추정한 경로,
`observed-weapon-damage/estimated-attack-ac`는 피해만 읽은 경로입니다. `observed-target-attack/estimated-ac`는 대상 AB를 읽고 AC는 추정한 경로입니다.
`observed-completed-rules`는 대상 규칙 경로이며 예약 추정이 더해졌는지는 별도 pending/observed 필드로 확인합니다.

`observedBaseABResults`, `observedWeaponStats`가 새 공통 규칙 관측 횟수입니다. 기존 `observedABResults`, `observedACResults`,
`observedAttackCounts`도 유지합니다. 설치 상태가 ON이어도 특정 항목이 아직 관측되지 않으면 추정일 수 있습니다.

권장 시험은 같은 공격자/무기/적/모드에서 강력한 공격 OFF/ON을 번갈아 F6 상세로 비교하는 것입니다.
BAB4 일반 강력한 공격은 명중−2, 한손 피해+4/양손+6입니다. 게임이 실제로 효과 버프를 붙인 뒤에는 예약 델타가0이 되어 중복 가산하지 않아야 합니다.
`미로드`/`동적 값`/`특수 액션`은 실행 없이 확인할 수 없는 예약 효과이며 상세와 로그에 남깁니다.
DR 적에 대해서는 `defenseNotes`·각 행의 방어 전/후 수치·실제 damage 기록을 함께 확인합니다.
한 번의 무작위 실제 피해를 기댓값과 비교해 불일치 알림을 띄우지는 않습니다.
현재 JSONL과 `.prev`를 함께 전달하면 회전 전후 자료를 이어 볼 수 있습니다.

## 로컬 캐시 수정 빌드의 추가 진단 필드

`beta-preview.nativeCacheStatus`: `Hit`는 동일 상태의1500ms 이내 무기 계산 결과,
`Expired`는 만료, `ContextMiss`는 저장된 결과는 있지만 현재 상태 키에 결과가 없음,
`NotObserved`는 캐시가 비어 있음, `ClockRegression`은 시계 역행이다.
`ContextMiss`만으로 대상/무기/버프 중 어느 필드가 원인인지 단정하지 않는다.
`TargetRulesSelected`, `ExistingEstimateSelected`, `Disabled`는 각각 대상 규칙 경로,
기존 추정 경로 선택, 관측 사용 비활성 상태를 뜻한다.

`beta-comparison.inputChanges`는 표시 당시와 공격 시작 시의 입력 차이를 필드 범주로 기록한다.
`Source`, `AB`, `AC`, `BAB`, `WeaponDamage`, `Critical`, `PendingModifiers`, `Grip`,
`AttackPattern`, `ModifierEvidence`, `DefenseEvidence`, `ContextSignature`가 사용된다.
`ContextSignature`는 세부 원인이 분해되지 않은 상태/출처 서명 변경이며 숫자 오류 판정이 아니다.
`displayAgeAtBeginMs`는 공격 시작 때 표시 결과의 나이이고, `attackDurationMs`는 시작부터
공격 결과 수신까지의 시간이다. 로그 완료 시각과 미리보기 기록 시각의 차이만으로 만료를 판정하지 않는다.

새 무기 통계가 기존 결과와 피해/치명/관통 특성에서 다르면 이전 AB 결합 결과로 덮지 않는다.
새 피해 결과를 사용하고 이전 AB는 버린다. 동일 결과의 콜백은 이전 AB의 유효기간을 연장하지 않는다.
이 수정은 게임 규칙 재실행이나 캐시 유효기간 확대를 도입하지 않는다.

## 물리/전체 피해 면역 수정 테스트 빌드

자동 방어는 활성 `AddPhysicalImmunity`의 피해 형태 마스크를 합산한다.
무기 피해의 모든 형태가 면역 마스크에 포함돼야 물리 피해 면역이다.
예를 들어 베기 면역만 있으면 베기+찌르기 피해 전체를 면역으로 판정하지 않는다.
`CompleteDamageImmunity`는 지원하는 모든 피해 유형에 면역을 적용한다.
에너지 면역 억제 조건을 물리/전체 피해 면역에 잘못 적용하지 않는다.
면역 파생 효과나 무기 피해 형태를 확인하지 못하면 계산 불가 사유를 기록한다.
`defenseNotes`에는 물리 면역 형태, 현재 무기 적용 여부와 최종 적용 면역이 기록된다.
물리 면역은 추가 화염 등 비물리 구성까지 없애지 않는다.
실제 난이도/다른 모드/면역 무시 공격까지 검증한 의미는 아니며 부분 모델 품질을 유지한다.

## 0.5.0 주문·범위 판정 미리보기 (로컬 테스트)

주문을 선택하고 대상을 가리키면 무기 표 대신 주문 판정 표가 표시된다.
일반 근접/원거리 접촉은 저장된 BAB·힘/민첩·추가 AB와 접촉 AC로 기본 명중률을 추정한다.
특수 무기 발사/공격 능력치 대체는 추측하지 않는다. 주문별 보너스, 거리/은폐/특수 회피는 미반영이다.
내성은 인내·반사·의지별 **대상의 내성 실패 확률**이다. 분기 안에서 발견한 내성은 후보이며
그 분기의 실행을 보장하지 않는다. 최신 게임 소유 CalculateParams 완료 결과의 DC를 읽고,
1500ms 초과나 시전자 상태/장비/메타매직/override 변경 때는 재사용하지 않는다.
DC가 없으면 임의 기본값을 만들지 않고 대기 사유를 표시한다. 조건부/사용자 지정 DC,
이전 내성 DC 재사용·버프 출처가 필요한 판정은 계산 불가다. Persistent의 성공 시 재굴림은 포함한다.
캐시 내성 보정의 조건부 대체/자동 성공/특수 재굴림은 미반영이다.

SR 관통은 저항 적용 여부와 별도로 표시한다. 기본 CL과 무조건 SR만 수치화하며,
주문 관통 피트/조건부 SR/특수 대체와 효과 면역은 확정하지 않는다.
따라서 이 표의 내성 실패는 디버프 최종 적용 확률이 아니며, 명중·SR·내성을 단순 곱하지 않는다.
내성 성공 후 반감/부분 효과, 매 라운드 재내성, 지속 장판/연쇄/합성 주문 전체 결과도 계산하지 않는다.

범위는 게임이 이미 호출한 AbilityData.WouldCurrentlyTarget의 반환값만 관측한다.
직접 기하 계산/대상 검사 재호출을 하지 않는다. 조준 위치/주문 변경은 목록을 지우며,
250ms 이상 갱신되지 않은 결과도 제외한다. 적·아군을 함께 표시하고 최대32명을 스크롤한다.
범위 증거가 없으면 커서 대상만 표시하며 사거리/포함 여부 미확인임을 명시한다.
패널 위로 커서를 옮기면 보관 중 표시로 멈추고 새 진단을 생성하지 않는다.

`spell-preview` 로그는 `quality=PartialEstimate`, `fullConditionsVerified=false`,
`observedTargetMembership`, `ability`, `checks`를 기록한다. checks는 실제 표시 문구와 같다.
주문별 새 보너스를 얻으려고 계산 함수를 직접 호출하거나 규칙/RNG를 실행하지 않는다.
