# 최종 판정 원시 데이터 — 0.5.61

진단이 활성화된 기존 경로에서 자동으로 기록한다. 별도의 대조표 생성은 필요하지 않다. 게임 규칙/주사위/액션을 다시 실행하지 않고 실제 판정 완료 시 이미 존재하는 결과와 필드를 복사한다. 중간 방어 상태를 매번 저장하지 않는다.

## 공격 완료: observation.rawInputs

- finalAB/finalAC: 실제 최종 명중 보정과 상대 AC.
- attackOutcomeFields: 공격 유형/무방비/자동 명중·실패/협공/치명타·확인 보정/은폐·거울상 등 실제 이벤트의 스칼라 필드. 미복사 참조는 omittedNonScalarFields에 표시한다.
- d20/criticalConfirmationD20: 실제 눈, 원시 눈, 재굴림 이력과 출처. 기존 visibility도 유지한다.
- armorClassRule: 엔진 RuleCalculateAC의 최종 Result, StatModifiersAtTheMoment, 규칙 가산과 임시 보정 목록.
- attackRule/baseAttackRule: 대상 보정과 기본 AB 규칙의 Result·당시 스탯 보정·규칙 가산.
- 각 스탯 보정: 값, 유형, StackMode, 원인 Fact 인스턴스/Blueprint ID와 이름, 컴포넌트명, 아이템 ID.
- weaponStatsRule/weaponDamageModifiers/weaponDiceFields/weaponDiceModifiers/weaponDamageDescriptions: 당시 무기 피해 보너스, 기준 주사위/수정 출처 및 피해 정의.
- targetACBaseValueNow/targetACCachedNow: 판정 완료 때 읽은 캐시값이다. 엔진이 보존한 AtTheMoment와 동일 시점이라고 가정하지 않는다.

## 피해 완료: damage-observation.damageTrace / spell-observation.details.damageTrace

traceVersion=3. 기존 최종 피해 자료와 함께 다음을 남긴다.

- 성분별 diceBase/diceModified, diceModifierFields/diceModifierSources.
- bonus/bonusTargetRelated와 damageModifiers의 값·유형·원인 Fact.
- 실제 rollResult/rollAndBonusValue/valueWithoutReduction/reduction/finalValue.
- 반감/내성 반감/이미 반감, decline/면역·비실체 소스, 강화·취약·내구 배율·최대값·치명타·정밀피해.
- resistance/reductionPenalty/hardness의 실제 값과 출처, 무기 형상·재질·강화/관통 강화.
- 이벤트 전체 결과/난이도 배율/추가 보정/리다이렉트/최소 HP 제한.
- defenseAtCompletion: 실제 판정 완료 시 DR part의 등록 순서, 각 Chunk의 Reduction/AppliedReduction/UsedForFirstDamage, 출처 인스턴스/Blueprint, 소모 잔량, 설정의 스칼라/참조/ContextValue 및 문맥 Rank/SharedValue.

주의: 등록된 DR 후보와 실제로 적용된 감소량은 구분한다. 최종 적용 감소는 각 실제 피해 성분의 reduction/resistance에 있고, 등록 목록을 그대로 적용 DR이라고 간주하면 안 된다. 완료 시점 잔량은 피해 직전 잔량이 아니다. Final damage도 임시 HP를 거친 최종 HP 감소와 같다고 가정하지 않는다.

## 표시 당시 값 보존

무기는 rawInputs.displayedInputs, beta-comparison.displayedInputs, damageTrace.displayedInputs에 원래 Beta 스냅샷을 함께 기록한다. 상태 변경/만료 때문에 정식 비교가 불가능해져도 원시 표시값을 버리지 않는다.

보존 대상: Preview ID, 행 연결 상태, 공격자/대상/무기 ID, AB/AC/명중·치명타 확률, 모든 행의 기대값, 주사위 성분과 고정 보너스, 예약 보정, 활성 D20 모형, 은폐/거울상, 방어 유형별 감소·면역·반감·보호막/소스 순서 및 판독 이유. 이는 기초 Beta 모형 스냅샷이며 별도의 수동 시나리오 표현을 인증하지 않는다.

주문은 UI가 계산한 최신 입력을 메모리의 불변 JSON으로 보존한다. 실제 주문 판정에는 frozenDisplayedInputs 또는 candidateDisplayedInputs로 **중첩 JSON 객체**를 붙인다. 512문자 문자열 필드로 저장하지 않는다. 단독 spell-preview 로그가 반복 제한으로 생략돼도 frozenPreviewLoggedSeparately=false를 통해 이를 구분하고 실제 판정에 입력 자체를 남길 수 있다. frozen은 같은 Cast/Target 첫 단계 연결, candidate는 최근 시점 후보이며 동일 조건 인증이 아니다.

## 누락/한도와 성능

- 판독 실패는 captureFailure, 미지원 비스칼라 참조는 omittedNonScalarFields, 한도 초과는 fieldsTruncated/truncated/chunksTruncated로 표시한다. null을0보정/0DR로 바꾸지 않는다.
- 일반 스칼라 필드64개, 스탯/규칙/주사위 보정64개, 무기 피해 정의32개, DR Chunk64개, 실제 피해 성분32개 등의 제한이 있다. 개별 설명 문자열에는 기존 길이 제한이 있다. 임의 모드 핸들러 내부나 모든 중간 상태가 완전히 수집되는 것은 아니다.
- 미리보기20건/초와 실제 관측·대조256건/초 예산을 분리했다. 초과 시 rawRateLimitedRecords를 증가시킨다. 이 값이 증가했으면 원시 기록이 빠진 구간이다. 파일 회전으로 과거 데이터가 사라질 수도 있다.
- raw-attack-evidence/raw-defense-evidence/raw-displayed-snapshot 측정 구간으로 수집 비용을 추적한다. 필드 메타데이터만 캐시하고 실제 값은 매 판정마다 읽는다. 새 버전의 실전 부하는 아직 측정하지 않았다.
- 분석기의 rawEvidenceCoverage는 자료가 존재하는 보존 행 수만 센다. 수치 정확성/출처 완전성의 인증이 아니며 오류·누락·한도 플래그도 같이 확인해야 한다.

이 버전에서 추가한 것은 대조에 필요한 수집 장치다. 일치/불일치/검증불가 대조표나 임의 미지원 계산식을 전부 구현했다는 뜻은 아니다. 설치 전 기록에는 새 원시 데이터가 소급해서 생기지 않는다.


## 0.5.62 임시 HP 필드

`healthAtCompletion`은 실제 RuleDealDamage 완료 시 본체 HP·최대 HP·남은 임시 HP의 읽기 전용 값이다. 별도 중간 스냅샷이 없으므로 실제 소모 임시 HP는 역산하지 않는다. `Result`는 최종 규칙 피해이며 HP 감소량과 동일하지 않다.

주문 `healthProjection.expectedTemporaryHpLoss`, `sequenceProjection.expectedTemporaryHpLoss`, `branchPreview.expectedTemporaryHpLoss`, `outcomes[].temporaryHpLossIfReached`, `stagedOutcome.expectedTemporaryHpLoss`, `pitPreview.expectedTemporaryHpLoss`는 표시 계산의 기대 임시 HP 소모다. 모르는 분기는 null이며 즉사는 임시 HP를 피해로 소모한 것으로 세지 않는다. 평균 상태 근사는 approximate/basis를 함께 확인한다.

게임 2.7.0x의 `RuleDealDamage.OnTrigger`는 최종 Result(방어/난이도/재지정 처리 후)를 `TemporaryHitPoints.HandleDamage`에 전달하고 반환된 잔여 피해만 본체 Damage에 더한다. `HandleDamage`는 임시 HP modifiers를 순서대로 소모한다. 임시 HP 자체의 존재로 별도 DR을 만들거나, 본체에 도달한 잔여 피해에 DR을 두 번 적용하지 않는다. 크리처별 DR 우회 및 에너지 방어는 기존 방어 모델이 담당한다.


## 0.5.64 표시 HP와 성능 보존

무기 beta-preview.healthProjection 및 displayedInputs.healthProjection은 전체 표시 공격열의 예상 본체/임시 HP 소모다. 실제 공격에 연결된 displayedInputs는 화면에 표시했을 때의 HP JSON을 고정하며, 공격 도중 캐시가 갱신돼도 바뀌지 않는다. 당시 표시 HP가 없었다면 이후 계산값으로 채우지 않는다. computedFrame은 계산 프레임이고 실제 렌더링 성공을 인증하는 필드는 아니다. 무기 모델의 basis=mean-state-hp-display와 approximate=true를 확인한다.

performance-window.performance는 같은 세션의 15초 구간이며 단계별 평균/최대 ms, 최대 프레임, GC 및 할당 샘플을 수록한다. 보고 기록은 초당4개 예산이며 reportRateLimitedRecords로 누락을 표시한다. 실제 이벤트 초당256개 예산은 독립이다. 구간 전체 게임 프레임은 모드 외 부하도 포함하고 중첩 단계는 합산하지 않는다.


## 0.5.67 파라미터/참조 검증 근거

spellMetadata.negativeLevelSources에는 활성 소스 ID·Count·CL 조정을 보존한다. rodMetamagic에는 활성 로드 마스크·적용 여부·레벨 상한·화이트리스트·신속 행동 상태를 기록한다. 이미 같은 메타매직이 있으면 parameterHandlerNoOp=true이며 중복 적용하지 않는다. checkDetails[].persistentSave와 d20Model을 함께 확인한다.

HCO authority=ReferenceOnly, probabilityEquivalenceVerified=false, verdict=ReferenceDifferenceOnly이다. 독립 재굴림·지속 내성·즉사 분기 계산을 HCO 값에 맞춰 바꾸지 않는다. 실제 규칙의 최종 입력과 독립 주사위 열거가 검증 기준이며 HCO raw delta는 오류 확정이 아니다.

retainedInputReview는 실제/표시 AB·AC 숫자가 다른지 구조화하지만 InputsChanged/이전 행 후보를 동일 조건 비교로 승격하지 않는다. 로그가 완전한 행/조건 연결을 제공하지 못한 구간은 검증 보류로 남긴다.
