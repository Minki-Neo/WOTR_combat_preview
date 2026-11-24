# 로컬 진단 사용법 — 0.5.61

## 0.5.68 진단 필드

`displayedInputs.attackLink`: commandId, globalAttackIndex, planIndex, weaponOrdinal, attackPenalty, exactRuleIdentity, status. 객체 동일성은 행 연결만 보장하며 동일 전투 조건을 보장하지 않는다.

`displayedInputs.inputAtAttackBegin`: 공격 시작 시 사용한 입력의 불변 복사. 이미 판독한 AB/AC/피해식/공격열/보정 출처/d20/은폐/거울상을 보존한다. 최종 실제 판정 기록과 대조한다.

`spellMetadata.automaticMetamagicSources`: 출처, 마스크, 적용 여부, 일회용 여부, 조건별 filters와 미확인 사유. 아이템은 itemParameters 안에 기록한다. `unavailableDiagnostics`는 짧은 UI 사유와 출처 타입/이름/미확인 판정을 보존한다. 실제 확률 0은 미확인과 구별한다.

분석기의 attackAssociationReview는 beta-comparison만 세어 observation과 중복 집계하지 않는다. HCO는 독립 참고값이며 실제 최종 입력/독립 주사위 계산이 검증 기준이다.

## 0.5.67 로그 기반 보정과 독립 검증

- [x] 음수 레벨: 활성 runtime.m_Data.Count를 읽어 CL/랭크 보정 -Count 적용. DC에 임의 감산하지 않고, 이미 캐시된 AB/내성 페널티를 다시 감산하지 않는다. Count를 파라미터 키와 negativeLevelSources에 보존한다.
- [x] 자연/비무장 등 장비 대상 강화: 활성 착용 장비의 EquipmentWeaponTypeEnhancement, 전체 공격 AllAttacksEnhancement, 일반/추가 강화 채널의 상한과 비중첩, 관통용 EnhancementTotal을 반영한다. 강화는 AB/피해 및 DR 판독에 함께 전달한다.
- [x] 주적: 등록된 CheckedFeatures를 현재 대상과 대조하여 가장 큰 일치 Bonus만 AB/피해에 적용한다. 중복 일치 항목은 합산하지 않는다.
- [x] 최신 환영 살인마 확률 미표시를 별도 항목으로 추가/수정: .66의 5957~5960행은 MetamagicRodMechanics 미지원으로 DC가 null. 활성 로드/이미 적용된 메타매직/주문 레벨 상한/화이트리스트/신속 행동/접촉 후속을 독립 판독하고 지속 메타매직을 유지한다.
- [x] 지속 메타매직 + 유리한 마법 + 의지/인내 2단계의 독립 주사위 확률 회귀 검증. HCO와 다른 결과를 HCO에 맞추지 않는다.
- [x] HCO 기록에 ReferenceOnly, probabilityEquivalenceVerified=false, 로컬 persistentSave 및 d20 모델을 함께 기록. 실제 최종 입력과 독립 열거를 검증 기준으로 삼는다.
- [x] 무기 갱신 묶음의 상태·파티 주사위 출처·착용 장비 판독을 공유하고 묶음 종료 시 폐기한다. 프레임 간 stale 캐시는 만들지 않는다.
- [x] 분석기에 retainedInputReview와 independentReferenceReview 추가. 조건이 바뀐 값의 차이는 확정 계산 오류로 승격하지 않는다.
- [ ] 새 DLL 적용 후 음수 레벨/자연무기/주적/로드 환영 살인마의 실제 DC·CL·강화·피해·분기 판정 대조.
- [ ] 533ms GC 동반 정지와 무기 갱신 16ms 초과 감소 여부 측정. 이번 변경으로 해결됐다고 단정하지 않는다.
- [ ] 임의 AutoMetamagic 조건, 전부 거절된 비교의 정확한 행/조건 연결, 추가 특수 공격·상황부 보정은 별도 검증이 남는다.


## 0.5.66 일반 행동 소모 시 제외 / 창 크기 복원

사용자 요청에 따라 .65의 합계 제외 공격 참고 행을 제거했다. 일반 행동이 Available이 아닌 것으로 확인되면 이동 행동이나 이전 공격 예측이 남아 있어도 합계에서 제외한다. 확실한 소모 상태는 새 예측 대기 중에도 제외하고, 일반 행동 상태 자체가 미확인이면 판독 대기로 둔다.

.64 소스 ZIP과 비교 결과 폭·배율·폰트·CompactHeight 산식은 .65에서 바뀌지 않았다. 참고 공격 행이 추가되어 자동 높이가 커진 경로를 제거했으며, 사용자의 별도 폭/배율 설정은 덮어쓰지 않는다. 주문서 파라미터 수정은 유지한다. 실제 화면은 새 DLL 적용 후 확인이 필요하다.


## 0.5.65 행동 제외 시 참고 표시와 아이템 주문

- [x] 기본 행동 Overused는 CPU 계산 한도가 아닌 선택한 행동 계획의 실행 실패임을 명확히 표시한다. 소모/제한과 선택 계획 불가를 구별한다.
- [x] 표준 행동 Available + 현재 사거리 + 이동 불필요 + 모순되는 공격 계획 없음인 경우, Overused와 별개로 일반 단일 공격을 추정한다. 실제 소모된 행동이나 실패한 접근을 실행 가능으로 바꾸지 않는다.
- [x] 확정적으로 합계 제외된 공격도 무기 참고 수치를 계산·표시한다. 실행 가능 합계, 체력 감소, 실제 판정 연결에는 포함하지 않는다.
- [x] ItemStatHelper/GetParamsFromItem 경로를 독립 모델로 구현: 아이템 DC/CL, 제작 파라미터, ScrollMastery, ScrollSpecialization, 완드 숙련 우선순위, 강화 물약, 아이템 유형별 CL 보너스, Trickster 신화 CL, 제작 메타매직/ExtendPotion. 일반 주문서 DC/CL 보정기를 아이템에 중복 적용하지 않는다.
- [x] itemParameters에 출처/사용 입력/결과를 기록하고 파라미터 캐시 키에 포함한다. HCO 값·Rulebook 재실행·실제 사용 후 관측을 기다리는 경로에 의존하지 않는다.
- [ ] 특수 AutoMetamagic 등록의 임의 조건식은 미지원 시 ItemAutoMetamagicPredicateUnresolved로 남긴다. 아이템에서 엔진이 건너뛰는 로드/다음 주문 메타매직 등록은 차단 사유로 삼지 않는다.
- [ ] 새 DLL 적용 후 행동 소모 상태의 참고 행 표시 및 실제 주문서 DC/CL/확률을 게임 로그와 대조한다.


## 0.5.64 실제 적용 전 진단 보강

한 번의 플레이: CompareBeta를 켜고 임시 HP/DR 대상 무기 공격과 피해·분기 주문을 사용한다. 15초 이상 플레이하거나 UMM에서 모드를 끄면 짧은 성능 구간도 저장한다. CombatPreview.diagnostics.jsonl 및 .prev를 보존한다. 실행 runtime.modVersion이 0.5.64.0인지 먼저 확인하며 이전 버전은 신규 기능 검증에 사용하지 않는다. 오류 발생 시 UMM/CombatPreview.log도 함께 보존한다.

- 무기 `healthProjection`: 표시한 전체 공격열의 본체/임시 HP, 각각의 기대 소모, 방어 후 기대 피해, 계산 프레임, 포함 previewIds, 계산 불가 사유, mean-state 근사임을 기록한다. 표시 입력을 불변 JSON으로 실제 판정에도 보존한다.
- `performance-window`: 15초 및 모드 해제/미리보기 객체 종료/수동 계측 초기화 직전에 짧은 구간을 기록한다. 전체 게임 프레임, 느린 프레임 수, 단계별 최대/평균/GC/할당량, 설정을 JSONL에 기록한다. 강제 프로세스 종료 시 마지막 15초 미만 구간은 보장하지 않는다.
- 성능 보고는 초당 4개의 별도 예산이며 preview 20개/실제 256개 예산을 소모하지 않는다. `reportRateLimitedRecords`를 별도 표시한다.
- 각 기록의 runtime에 modAssemblyMvid, nativeHealthIntegration=Removed, healthProjectionModel, diagnosticFeatures를 추가한다. 버전 숫자만 같은 다른 빌드도 구별할 수 있다.
- 분석기 healthProjectionReview/performanceReview로 누락·근사·자원 범위 오류·완료 체력 근거·느린 단계를 정리한다. 전체 공격열의 기댓값과 단일 타격은 직접 대조하지 않으며, 최종 상태만으로 실제 임시 HP 흡수량을 역산하지 않는다.
- 기존 명중/d20/피해식/방어 항목 대조, 주문 준비 지연/범위 대상 필터/판정 불일치 분석을 유지한다. 기본 게임 체력바 연동은 제거 상태다.


## 0.5.63 기본 게임 체력바 경로 제거

- 게임 Overtip/HP바 탐색, 오버레이 생성·파괴, LateUpdate 제어, native-HP-overlay 계측을 제거했다.
- NativeHealthProjection 설정 필드·토글·상태 표시 및 전용 게임 레이아웃 검사 4개를 제거했다. 오래된 설정 파일의 해당 값은 기능을 활성화하지 않는다.
- 모드 패널이 사용하는 방어 후 기대 피해 합산만 PreviewPanel로 이동했다. 임시 HP 때문에 차단하는 매개변수/조건도 제거했다.
- 모드 패널의 녹색 본체 HP·파란 임시 HP·노란 예상 소모 표시와 계산을 유지한다.
- 아래 이전 버전의 native HP 연동 설명과 성능 수치는 과거 기록이며 현행 기능이 아니다.


## 0.5.62 임시 체력과 예상 소모

- [x] 모드 체력바: 본체 HP 녹색, 임시 HP 파란색, 두 자원의 예상 소모 노란색. 막대 전체 기준은 최대 본체 HP + 현재 임시 HP이며 빈 본체 HP 구간도 유지한다.
- [x] 크리처의 기존 DR 우회 조건·에너지 저항/면역·피해 배율·소모성 보호막을 먼저 처리하고 최종 피해가 임시 HP, 본체 HP 순서로 흡수된다. 임시 HP에 DR을 추가로 적용하지 않는다.
- [x] 주문 공동 확률분포·공유 주사위·다중 타격·보호막 잔량·분기 확률에서 expectedTemporaryHpLoss를 보존. 즉사는 임시 HP 흡수로 세지 않는다. 구덩이 최초 낙하에도 적용.
- [x] 무기 체력바 및 기존 평균 상태 보호막 경로는 평균 피해 기반 근사이며 ≈ 표기. 분포가 계산되는 주문 경로의 기대 소모와 구별한다.
- [x] 주문 healthProjection과 sequence/branch/staged/pit 모델에 예상 임시 HP 소모를 기록. 완료된 실제 피해 기록은 healthAtCompletion에 최종 HP/임시 HP만 추가한다. 중간 스냅샷이나 Result로 흡수량 역산은 하지 않는다.
- [x] C# 26,383 assertions + 실제 주문 27-action topology, 게임 메타데이터 240항목, Python 분석 20 tests 통과. 빌드 성공(기존 Charge CS0649 경고 1개).
- [ ] 새 DLL을 적용한 실제 게임에서 임시 HP 버프·물리 DR 우회·에너지 저항·다중 공격 화면 및 로그 대조. 임의 외부 모드의 피해 가로채기/동적 방어 제거까지 보장하지 않는다.


## 0.5.61 최종 판정 원시 데이터

최종 판정의 rawInputs/traceVersion3/defenseAtCompletion/displayedInputs와 주문 frozenDisplayedInputs를 자동 기록합니다. 중간 방어 스냅샷은 수집하지 않습니다. 실제 관측256건/초와 미리보기20건/초 예산은 별도이며 rawRateLimitedRecords로 실제 누락을 확인합니다. omittedNonScalarFields/truncated/captureFailure는 완전 수집 실패를 뜻합니다. rawEvidenceCoverage는 보존 행의 자료 존재 여부만 집계합니다. 정확한 스키마는 소스의 docs/RAW_EVIDENCE.ko.md 참고. --expected-version 0.5.61로 런타임을 확인하세요.


## 0.5.60 HP 표시 준비

healthProjection.basis=mean-state-hp-display/approximate=true는 전체 기대 피해에서 임시HP를 차감해 상한을 적용한 평균 상태 표시이며 정확한 분포가 아닙니다. 유효한 전체 HP 분포가 있으면 우선합니다. areaScheduling.hoverChanged/parameterRevisionChanged로 재우선 계기를 확인할 수 있습니다. 양수 구간에는 최소1.5픽셀 표시 폭이 적용되며 숫자 캡션은 실제 계산값을 유지합니다. 0피해/미확인에는 억지 구간을 만들지 않습니다.


## 0.5.59 대상 제외/피해와 실제 주사위 검증

areaTargetFilter는 관측 후보/유효/제외와 excludedObservationSamples를 기록합니다. 후자는 반복 관측 누적이며 고유 아군 수가 아닙니다. 명확한 적전용/아군전용 전달만 관계로 제외하며 Any/미확인 범위의 아군 피해를 숨기지 않습니다.

무기 행의 meanOnNormalHitAfterDefense/meanOnCriticalHitAfterDefense는 일반/치명타 조건부 평균입니다. 분석기 weaponRollDamageReview는 실제d20/AB/AC·재굴림·은폐를 대조하고 피해 이벤트를 공격/대상별로 묶어 조건부 평균 이상치 후보를 출력합니다. expectedAfterDefense 같은 명중 확률 가중값에 실제 명중 피해를 바로 대조하지 않습니다. 후보는 상태/행 연결·추가 피해·방어·난이도 차이를 따로 분석해야 하며 곧바로 오류로 확정하지 않습니다. --expected-version 0.5.59로 실행도 확인하세요.


## 0.5.58 직접 판정/주변 효과 분리

secondaryTargetEffects.status=Unmodeled와 branchAnalysis.status=PartialSecondaryTargetEffects는 주변 무작위 효과가 남아 있음을 뜻합니다. 표의 확률은 그 이전의 직접 판정이며 전체 추가효과/HP 예측이 아닙니다. 해당 분기의 HP는 null로 유지합니다. UMM spell-school-inputs는 학파 스냅샷 생성 비용을 측정하며 배치 내 재사용에서는 다시 생성하지 않습니다.

분석기 parameterComparisonReview는 유효/일치/차이/미지원/누락/무효 표본을 나눕니다. DC/CL/rank의 일치가 피해/전체확률 일치는 아닙니다. secondaryTargetPreviewSamples도 확인할 수 있습니다. --expected-version 0.5.58로 실행을 확인하세요.


## 0.5.57 문맥 교체

일반 주문책에서 지원된 ReplaceAbilityParamsWithContext의 저장된 문맥값을 읽습니다. 문맥 기준 DC/CL에 현재의 별도 보너스를 더하며, 읽은 원본 RankBonus를 다시 더하지 않습니다. 메타매직 추가/출처누락/충돌은 ContextParametersMissingConflictingOrNewMetamagic으로 기록됩니다. 엄격한 캐시 키도 context-parameters 입력을 포함합니다. ContextConditionIsAlly를 추가했으며 무작위 주변 대상/치유량 전체는 별도입니다. --expected-version 0.5.57로 실행 버전을 확인하세요.


## 0.5.56 조건부 보정과 파생 능력

d20Model.modifiers에 saveBonusDescriptor/saveBonusBaselinePositive/saveBonusBaselineNegative를 기록합니다. 같은 유형은 활성화한 효과끼리 경쟁해 기존 보정 대비 증가분을 씁니다. complete=false인 복합 제거/억제/가상 기준 변경은 계산 성공이 아닙니다. inherited-source-parameters는 이미 저장된 원본 DC/CL/RankBonus를 읽은 출처입니다. 메타매직 불일치/미초기화 값은 InheritedParametersOrMetamagicUnavailable로 남습니다.

성공한 내성의 reason은 ActiveD20Model 또는 TrackedSingleSaveConsumption, 실패는 UnsupportedActiveD20Effects 또는 UnavailableSaveLifecycleOrBudget입니다. 분석기 conditionalSaveReview는 반복 표본과 출처를 집계하며 실제 수치 검증을 뜻하지 않습니다. --expected-version 0.5.56으로 런타임도 확인하세요.


## 0.5.55 학파/전문화

독립 매개변수 계산에 주문 전문화와 지원된 확장 학파 DC/CL 전이를 포함합니다. 기존 readonlyParameterComparison의 dcDelta/casterLevelDelta/rankBonusDelta로 관측 매개변수와 비교하세요. ExpandedSchoolEntriesUnavailable/ExpandedFocusDefinitionUnavailable/ExpandedMasteryDefinitionUnavailable/ContextParameterOverride는 각각 구분된 미지원 이유입니다. 비중첩 조건부 내성 보너스의 기존 보정과의 차이를 계산하지 못하면 d20Model.sources에 해당 유형을 기록합니다. 최신 런타임은 --expected-version 0.5.55로 별도 확인하세요.


## 0.5.54 불운 표식 판독

런타임 GUID로 후속 비수치 재사용 대기 표식을 인식합니다. 해당 불운 모형의 complete=true, saveSequence.status=Computed 및 TrackedSaveConsumption/ConditionalTrackedSaveConsumption을 확인하세요. 여전히 미지원이면 d20Model.sources에 후속 액션 종류·buff GUID·simpleFact가 추가됩니다. 실제 로드된 표식에 효과 컴포넌트가 추가됐거나 등록 순서/상태를 지원하지 못한 경우는 미확인을 유지합니다. SR 100% 경계의 반올림 초과도 정규화합니다.


## 0.5.53 실행 버전 확인

`python tools/analyze_diagnostics.py <CombatPreview.diagnostics.jsonl> --expected-version 0.5.53`

versionAssessment는 최신 세션의 런타임 버전/메타데이터 일치 여부를 별도로 보고합니다. RuntimeVersionMatched도 기능·예측 정확성·FPS 검증 완료는 아닙니다. 구버전 로그의 필드 부재/차이0/비교0을 개선 성공으로 간주하지 않습니다. 서술자 DC 및 능력치 교체 판독이 성공하면 기존 독립 매개변수 결과에 반영되며, 원소 교체/도메인/충돌은 구체적 미지원 이유를 유지합니다.


## 0.5.52 근사 보호막 결과

무기 rows.afterDefenseApproximate=true와 approximationAssumptions는 평균 잔량 근사 결과입니다. afterDefenseReason=approximate-shield-mean-state이며 이전에 수치를 비웠던 이유도 가정에 남습니다. UI의 행·합계는 ≈, 상세와 체력바 안내에는 보호막 근사를 표시합니다. 정상적인 기존 분포 결과에는 근사 표시를 붙이지 않습니다.

주문 spellDamage.sequenceProjection.status=ApproximateShieldMean, approximate=true, assumptions와 expectedApproximateTotal을 기록합니다. 이 경로의 killChanceFromDamage는 null입니다. branchPreview/outcomes/stagedOutcome/healthProjection의 approximate도 전달합니다. 이는 확률 자체를 임의로 채웠다는 뜻이 아니라 읽힌 확률과 평균 피해·평균 보호막 잔량을 결합했다는 뜻입니다. 소모 분기 상관·HP/임시 HP 상태와 소스 선택이 근사임을 감안해야 합니다.

분석기의 approximateShieldWeaponRows와 approximateShieldSpellPreviews는 반복 표본의 근사 사용 건수입니다. 실제 피해와의 일치 인증이나 고유 공격 횟수가 아닙니다. 근사값과 기존 분포/관측값을 구분해 대조합니다.

## 0.5.51 광역 성능 측정

UMM 성능 구간 spell-batch는 프레임별 처리 배치, spell-target은 대상 하나의 계산·표 생성·로그 처리를 포함합니다. 기존 spell-preview/first-target/refresh도 이제 배치 단위이므로 이전 버전의 전체 광역 처리 구간과 직접 같은 표본으로 비교하지 않습니다. 측정 구간은 겹치므로 합산하지 않습니다.

spellMetadata.areaScheduling에는 targetCount/frame/processedThisFrame/pendingAfterTarget/maximumPerFrame/softBudgetMs/computeMsBeforeLogging을 기록합니다. 마지막 시간은 대상 계산·표 준비까지이며 로그 직렬화/쓰기와 전체 게임 프레임을 포함하지 않습니다. 3ms는 다음 대상의 시작 여부 기준입니다. 단일 대상이 더 오래 걸릴 수 있습니다. hoverReadiness는 작업에 들어간 시점이 아니라 대기열에서 대상을 처음 취득한 시점부터 측정합니다.

분석기 spellAreaScheduling은 다중 대상 표본의 평균/최대 계산 시간·4/16ms 초과·대기 수를 요약합니다. 로그 제한 때문에 표본이 생략될 수 있으며 이 값만으로 FPS를 계산하지 않습니다. 실제 게임 전체 프레임 창과 spell-target/spell-batch를 함께 확인합니다.

## 0.5.50 행동 간 상태

무기 beta-preview의 clusteredPhysicalDR는 해당 무기에 활성 응집 컴포넌트가 있음을 뜻합니다. rows.afterDefenseReason=readonly-clustered-physical-DR이면 같은 대상의 지원된 전력 공격 순서에서 고정 물리 DR을 한 번 소모한 값입니다. 단일 공격과 미지원 보호막 조합은 이 결과와 구분합니다.

d20Model.modifiers의 factKey/consumeWhenApplied/consumeOn20는 제거할 출처와 조건입니다. saveSequence.status=Computed는 지원된 두 내성 트리에서 소모 상태를 전달한 경우입니다. firstFailure와 bothFailure는 SR 전 확률이며 secondFailureIfReached는 첫 실패 조건부 확률입니다. stagedOutcome과 결과 표/HP는 이 경로의 확률과 기존 분기의 방어 후 HP 감소를 결합합니다. 일반 branchAnalysis의 미확인 단계가 이 전문 경로에서 해결될 수 있으므로 saveSequence와 stagedOutcome.sources를 함께 읽습니다. HCO는 별도 비교 자료입니다.

## 0.5.49 무기 피해식

완료 피해 구성의 diceModified는 최종 보정 주사위, diceBase는 원래 주사위입니다. 분석기는 diceModified가 있으면 이를 우선 사용하고 과거 기록에서만 diceBase를 대체 근거로 사용합니다. 치명타로 이미 변한 주사위일 수도 있으므로 공격의 치명타 여부와 함께 대조합니다.

기본 무기 예측의 modifierNotes에는 원거리 피해 능력치 마법부여와 현재 유효 크기에 따른 주사위 변환을 기록합니다. nativeCacheStatus=NotObserved/ContextMiss/Expired는 같은 문맥의 게임 관측을 쓰지 못했다는 의미입니다. 폴백의 지원 범위 밖 효과와 실제 공격 모드 변경이 여전히 있을 수 있으므로 그 자체가 전체 정확도 인증은 아닙니다.

## 0.5.48 에너지 보정 진단

spellDamage.terms의 energyBonusPercent는 출처별 정수화 후 합산한 BonusPercent, energyVulnerability는 일치하는 에너지 취약의 최댓값입니다. energyScalingSources에 출처 ID·이름·컴포넌트·percent 또는 factor 및 weaponOnly를 기록합니다. targetDamageMultiplier에는 대상의 취약·범위 내구 배율만 들어가고 시전자 퍼센트는 별도 단계입니다. 같은 배율을 중복 곱하면 안 됩니다. 원시 평균에는 지원된 퍼센트가 들어가며 대상 방어는 방어 후 값과 HP 분포에서 적용합니다.

VulnerabilityHandlerOrderUnavailable, WeaponOnlyEnergyBonusContextUnavailable, LinkedEnergyScalingContextUnavailable, EnergyBonusPercentInvalid/Overflow 등은 수치 확정 실패 이유입니다. targetDamageMultiplier=null이면 이전 targetVulnerability로 되돌려 숫자를 만들지 않습니다. 분석기 spellTargetScaling의 energyBonusTerms/energyVulnerableTerms/energyHandlerOccurrences/unavailableReasons는 반복 호버 표본의 출처·누락 경로를 요약하며 실전 일치율이 아닙니다.

## 0.5.47 범위 피해 반감

spellDamage.terms의 isAreaDamage는 직접 피해 액션의 범위 여부입니다. targetVulnerability는 군체 취약, targetDurability는 범위 반감 출처들의 곱, targetDamageMultiplier는 둘의 곱입니다. areaHalfSources에는 적용한 활성 출처의 ID·이름·컴포넌트·0.5 배율을 기록합니다. 방어 전 원시 주사위 평균과 방어 후 평균/HP 감소를 구분해야 합니다. 여러 반감이 중첩되어도 최소 피해 처리와 저항/보호막을 거치므로 최종 피해가 정확히 절반씩 줄어든다고 가정하지 않습니다.

분석기 spellTargetScaling은 범위 항목·반감 항목·컴포넌트별 출현 횟수를 집계합니다. 같은 호버의 반복 기록을 포함하므로 고유 효과 수나 실제 피해 일치율이 아닙니다. 이전 로그에 필드가 없으면 0건이며 새 구현이 게임에서 검증됐다는 뜻이 아닙니다.

## 0.5.46 상태 수명 진단

projectedSaveStages의 targetProjectedBuffState/casterProjectedBuffState 안에 conditions를 기록합니다. 변경된 condition별 sources(남은 상태 출처 수), immune(면역 출처 유무), active(면역을 적용한 활성 여부), statusKind(생성된 상태 객체 종류)를 확인할 수 있습니다. 여러 출처 중 하나만 제거했거나 면역에 의해 잠시 억제된 상황을 구분합니다. buffChanges와 함께 상태 소유 버프의 제거를 추적합니다. ConditionTransitionAction, ConditionEffectOutsideSaveProjection, ConditionCountLayoutUnavailable 등의 이유는 확인할 수 없는 후속 내성을 뜻하며 0%가 아닙니다. 기존 실제 로그의 조건 분해를 소급 생성하지 않습니다.

## 0.5.45 버프 변화와 내성 보정

projectedSaveStages의 targetProjectedBuffState/casterProjectedBuffState는 변경된 버프의 source/blueprintId/caster/rank/originalRank/removed/live/contextDescriptor를 기록합니다. 서술자는 문맥 비트의 16진수 문자열입니다. addedStatModifiers의 initialValue는 해당 가상 항목을 처음 추가했을 때의 값이며, 랭크 변경 후 후속 내성에 적용한 최종 값은 같은 단계의 bonus를 확인합니다. 단순한 수치 합산으로 모든 내성 보정을 복원했다고 보지 않습니다.

실제 spell-observation의 내성 details.statModifiers는 엔진의 StatModifiersAtTheMoment를 최대 32개 복사합니다. 각 값·서술자·컴포넌트·출처 ID와 Blueprint GUID를 기록합니다. 기본 능력치와 별도 이벤트 보정 전체를 담는 목록은 아닙니다. 분석기는 출처별 기록 수를 요약합니다. effectiveMetamagic은 독립/관측 파라미터에서 사용하는 실제 선택 비트이며, 버프 랭크의 메타매직 처리에도 같은 비트를 전달합니다.

## 0.5.44 확률·준비·피해 출처

- 무기 beta-preview의 d20Model, 주문 checkDetails의 d20Model: 발동 출처, 유리/불리 횟수, 발동 확률·조건, 사후 재굴림 방향, 자연 1 치환, 소모/미지원 여부. 미확인 무기 행의 hit/crit/expected 값은 null이며 0%로 해석하지 않습니다.
- 실제 observation.visibility.d20, 주문 공격 details.visibility.d20와 내성/SR details.d20: 원본/유지 결과, history, signedRerollCount, sources의 이름/GUID/부호, replaceOneWithMax. 두 개의 눈만 보고 유리 보정이라고 단정하지 않습니다. 사후 방향 전환 때문에 전체 history의 단순 max/min과 결과가 다를 수 있습니다.
- parameterStatus=ReadOnlyCalculated는 관측 전 독립 계산입니다. Current와 출처를 구분합니다. parameterFallbackReason 및 parameterKeyScope, readonlyParameterComparison의 DC/CL/rankBonus 차이로 누락/차이를 검토합니다.
- hoverReadiness: 최초 대상 취득 프레임, 최초 파라미터/HP 기댓값 준비 시간과 역행 횟수. 준비 시간은 관측 대기·갱신 예약·재호버 사이 시간도 포함하므로 CPU 비용으로 해석하지 않습니다. CPU/GC는 UMM Performance의 별도 구간 측정을 사용합니다.
- damageTrace.traceVersion=2는 각 구성의 damageModifiers, ignoreModifiers, preRolledValue, empowerBonus를 추가합니다. modifiers는 일반/대상 보정이 함께 등록된 출처이며 Bonus와 BonusTargetRelated를 별도 기록합니다. 합계나 평균을 실제 주사위 눈과 직접 비교해 오류라고 단정하지 않습니다.
- 복합 즉사 모델의 hpProjectionSource는 후속 액션 판정 및 공통 방어 분포 사용 여부입니다. branchPreview.outcomes의 damageActionIds/killed로 분기를 대응시키며 미확인 분포를 방어 전 단순식으로 덮지 않습니다.
- UMM 성능 구간에 allocatedAvgKB/allocatedMaxKB가 추가됩니다. 런타임이 카운터를 제공하지 않으면 unavailable입니다. 구간은 서로 겹치므로 합산하지 않습니다. 수집을 위해 GC를 강제 실행하지 않습니다.

분석기는 위 출처·준비 타임라인·독립 파라미터 차이·피해 보정 출처를 요약합니다. 새 버전의 실전 확인은 설치 후 생성된 로그와 버전 메타데이터로 진행합니다.

## 0.5.43 주문 결과 표

`outcomeTable`은 화면의 확률 열 제목과 조건부 내성 안내, 각 행의 path/chance/displayedChance/effect/expectedHpLossContribution/lethal을 기록합니다. 기여도는 해당 최종 분기의 확률과 기존 분기 HP 감소량을 곱한 값입니다. 미확인 입력에서 임의 값을 만들지 않습니다. 여러 광선의 첫/타격당 확률은 전체 시전 분포와 다르므로 열 제목과 note를 함께 대조해야 합니다. 표가 없는 미지원 액션 트리는 기존 요약을 유지합니다.

## 0.5.42 군체

무기 미리보기의 `physicalDamageFactor`는 군체 반감 여부(0.5/1)를, `criticalImmune`는 등록된 기본 치명타 면역 여부를 기록합니다. 물리 무효는 기존 방어 면역과 `defenseNotes`의 군체 메모로 구분합니다. 주문의 각 피해 성분은 `targetVulnerability`에 직접 전달의 군체 범위 취약 배율을 기록하며, 공유 분포·분기 기댓값·체력바에도 적용합니다. 여러 배율을 기대 피해 합계에 단순 곱하지 않고 각 굴림과 보호막 잔량을 함께 계산합니다. 명중률은 피해 면역과 별개입니다.

## 0.5.41 가상 상태와 후속 내성

`projectedSaveStages`는 액션 ID·앞선 분기·내성 종류, 그 단계의 `bonus`/`dc`/`conditionalFailureChance`, `reachChancePerCast`를 기록합니다. 앞선 확률이 미정이면 도달 확률을 확정하지 않습니다. `targetProjectionReasons`/`casterProjectionReasons`는 `BuffActivation:컴포넌트명`, `BuffDeactivation:컴포넌트명`, `RankDecrementStatRefreshUnsupported`, `SubstitutionCycleOrBudget` 등 구체적 제한을 남깁니다. 같은 이유는 `branchPreview.outcomes`에도 기록됩니다. 버프 치환·제거 표시는 가상 상태 처리 결과이며 실제 적용 성공 관측과 구분해야 합니다.

## 0.5.40 복합 주문·보호막

`nestedSaves`는 조건부 DC 적용 전 `unadjustedDc`와 각 보정의 `definedAdjustment`를 추가 기록합니다. 액션 분기 해석기는 앞서 적용한 지원 버프 상태로 후속 조건과 내성을 다시 계산합니다. `spellDamage.sequenceProjection`에는 방어 후 `expectedDamage`, 임시 HP·초과 피해를 고려한 `expectedHpLoss`, `killChanceFromDamage`와 피해 성분별 `protectionSources`의 등록 순서·잔량·타격 상한을 기록합니다. 잔량 -1은 상시 방어입니다. 분포 계산 결과는 순수 입력이 같은 동안 재사용합니다. 실제 버프 적용·보호막 소모는 일어나지 않습니다. 게임 내 수치 및 FPS 검증은 이 버전을 설치한 뒤 생성한 로그로 구분해야 합니다.

## 0.5.1 실제 피해 구성 추적

기존의 **표시 모델과 실제 AB/AC/무기 구성의 차이를 로컬 로그에 기록** 설정으로 함께 동작합니다.
별도 실행·주사위·피해 적용 없이 실제 RuleDealDamage 완료 결과만 읽습니다.
일반 무기 공격에서 적을 1초 가리킨 뒤 공격하고 CombatPreview.diagnostics.jsonl과
CombatPreview.diagnostics.jsonl.prev를 함께 보관하세요. 각각 최대 8 MiB로 회전합니다.
이번 빌드는 진단 보강이며 미리보기의 반감/기대 피해 계산 자체를 수정하지 않습니다.

damage-observation.damageTrace에 다음을 추가합니다.

- previewId / previewRowIndex / previewComparisonStatus: 실제 표시한 무기 모델과 연결.
  10초 초과·설정 해제 시 연결을 버리고, 실제 피해 대상/공격자가 바뀌면 원래 방어 모델을 재사용하지 않습니다.
- components: 최대32개. rollResult는 실제 피해 주사위 합이며 개별 주사위 눈 목록은 아닙니다.
  rollAndBonusValue는 엔진의 해당 중간 값, valueWithoutReduction는 반감 등 앞 단계 처리 후
  DR/저항 차감 전 값입니다. 세 값의 차이를 모두 DR이라고 해석하면 안 됩니다.
- reduction / finalValue: 해당 구성의 엔진 감소량과 감소/상한 적용 후 값.
  resistance / reductionPenalty / hardness에는 값과 가능한 출처·식별 GUID를 남깁니다.
  reconstructedFinal은 max(0,min(cap,beforeReduction-reduction)) 독립 산술 재구성이며
  감소 규칙 전체가 맞는지 검증하는 값은 아닙니다.
- half / alreadyHalved / halfBecauseSavingThrow / declines / declineCached:
  구성별/이벤트별 반감 및 피해 감쇠 근거. cached 값은 읽기만 하며 재계산 getter를 호출하지 않습니다.
  내성 반감이 항상 0.5배인 것은 아니므로 플래그만으로 배율을 단정하지 않습니다.
- 치명타·암습/정밀 피해·피해 상한, 무기 피해 형태/강화/재질과 이벤트의 난이도 보정·분산 피해도 기록합니다.
  Stored 접미사 값은 엔진 저장 필드의 원시 값이며 바로 최종 배율로 해석하지 않습니다.
- previewUnmodeledAdjustments: 현재 미리보기에서 보장하지 못하는 반감/감쇠 등 관측된 효과.
  이 배열이 비어 있어도 누락이 없다는 보증은 아닙니다.

previewStaticDefenseReplay는 **실제 관측한 감소 직전 값**에 표시 당시 정적 DR/저항을 적용한 결과입니다.
실제 finalValue와 비교하므로 운 나쁜 굴림을 오류로 판정하지 않습니다. 표시 연결과 피해 구성이
비교 가능하고 동일 종류 구성의 중복이 없을 때만 시도합니다. 소모형 방어·상한·특수 배율·미지원 종류는
null과 이유를 남깁니다. 면역/반감 처리를 이미 거친 실제 값을 입력하므로 **일치하더라도 미리보기의
면역/반감 또는 기대 피해가 검증된 것은 아닙니다**.

분석 도구는 산술 차이, 정적 방어 재현 차이, 비교 불가 사유, 미반영 보정별 건수와
최대32개 사례의 이름 없는 수치 요약을 출력합니다. 원인은 원본 JSONL의 출처 필드로 조사합니다.
fullReplayVerified=false를 유지합니다. 무기 공격 굴림과 연결되지 않는 주문 피해는 이번 추적 범위 밖입니다.
개별 주사위 눈·실제 내성 굴림·모든 모드 핸들러 실행 순서·보호막 소모 전후·임시 HP 흡수량·
전체 기대 피해 분포 재현은 아직 제공하지 않습니다. 실제 게임 실행 검증도 필요합니다.
기존 로그 용량과 피해 총량만 기록한다는 아래 설명은 과거 버전 이력입니다.

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

## 0.5.8 action-state records

`action-plan` is emitted only when the actor/target/turn action input changes. `available` means a displayed
ordinary weapon attack/estimate, `excludedFromTotal` means confirmed action restriction, and `fullAttack`
selects single vs full weapon enumeration. `reason` identifies surprise, movement requirement, exhausted action,
unknown prediction or unsupported special action. `actionState` is the bounded scalar stamp documented in
VALIDATION.md. Missing data is not interpreted as spent or free actions. HP projection skips only known excluded
items, and keeps unknown items incomplete. No new game commands or prediction events are run.

## 0.5.11 주문 판정 추적
주문 UI는 공격 명중(AC만), 적 내성 실패, SR 관통을 각각 표시합니다. 확률을 임의로 곱하지 않습니다. checkDetails는 kind/type/status/source/reason/probability/bonus/threshold/meaning을 기록합니다. spellMetadata는 DC/시전자 레벨 관측 상태·경과시간, 전달 무기 확인 상태를 남깁니다. 오래된 DC로 숫자를 만들지 않습니다.
spell-observation은 완료된 공격·내성·SR 판정의 실제 수치를 별도로 남깁니다. sourceResolved와 castContextId로 출처를 확인하며 미리보기와 동일 조건임을 자동 보증하지 않습니다. 무기 beta-comparison의 abDelta/acDelta도 원시 수치 차이일 뿐 InputsChanged 등 비교 제외 사유를 무효화하지 않습니다.

## 0.5.12
spellMetadata.linkedSaveTypes/unloadedEffectReferences/effectScanTruncated를 추가했습니다. LinkedEffectBranchCandidateNotImmediateCast는 장판·버프의 후속 분기 후보이며 시전 즉시 최종 적용률이 아닙니다. DC 불확실성은 LinkedEffectDCOrConditionUnverified로 남깁니다.
UMM 로그에 Performance automatic window가 60초 주기로 출력됩니다. avgFPS/maxFrameMs/over33ms/over100ms는 전체 게임 프레임 측정입니다. 기능별 scope 시간은 중첩되므로 합산하지 않습니다.

## 0.5.13 — HitChanceOverlay 비교
현재 설치된 HCO 0.4.0 DLL(MVID 80ea8375-0d21-45f4-8533-0064c3011a00)의 이미 계산된 ForWeapon/ForAbility 결과를 관측합니다. HCO가 없으면 NotObserved, 다른 빌드면 UnsupportedOrPatchFailed로 동작하고 기본 미리보기는 계속됩니다. HCO는 필수 의존성이 아닙니다. 두 모드와 베타 비교/로컬 진단 옵션을 켜야 비교 기록을 수집합니다.
beta-preview.hcoReference 및 spell-preview.spellMetadata.hcoReference에 버전/MVID, 경과시간, 관측 문구, 개별 판정값 및 differences를 남깁니다. 상태 키·시전자·대상·능력/무기 일치 및 350ms 이내인 경우에만 부분 비교합니다. 이 상태 키는 전체 조건을 보증하지 않습니다.
differences의 bonusDelta/thresholdDelta/rawRollProbabilityDelta는 **우리 값 - HCO 값**입니다. 확률은 0~1 단위이므로 -0.10은 -10%p입니다. 공격 보너스/AC, 내성 보너스/DC, SR 관통 보정 등을 비교하며 자동 명중·은폐·미러 이미지·미반영 특수 규칙이 차이의 원인일 수 있습니다. HCO의 최종 효과 %를 기본 명중률이나 적 내성 실패율과 같은 값으로 간주하지 않습니다. finalEffectProbabilityDelta는 계산하지 않습니다. 여러 내성/공격 분기가 있으면 MultipleReferenceChecks로 수치 연결을 보류합니다.
referenceChecks는 내성 AutoPass/SuccessBonus/Precise와 SR Penetration/Precise, 공격 PendingToggles/MissPercent/MirrorImages/DamageImmune/MissNote도 담습니다. 내성 확률 자체가 없는 경우에도 원시 값과 HCO 문구로 누락 판정을 추적할 수 있습니다. HCO 계산 중 생성된 예측 이벤트는 실제 전투 기록과 관측 AB/AC 캐시에서 제외하고, DC 관측 출처는 parameterSource로 구분합니다. HCO와의 일치는 정확도 인증이 아닙니다.
분석기 hcoReferenceStatuses/hcoRawDifferences로 비교 제외 사유와 수치 차이를 집계합니다. 자동 성능 요약의 hco-capture-start/end/hco-compare는 추가 진단 비용입니다.

## 0.5.14 — 주문 피해 주사위/발사체
spellMetadata.spellDamage에 casterLevel, deliveryCount/deliveryKind, scanTruncated, basis, terms를 기록합니다. terms는 dice/sides/bonus/formula/damageType/meanPerApplicationRaw/repeats/branchCandidate/path/half/halfIfSaved/unavailableReason/totalRaw를 포함합니다. UI에서 피해 정보를 판정 상세보다 앞에 표시합니다.
시전자 레벨 기반 ContextRankConfig의 진행식·최소/최대 상한·Custom 구간·격화 상한 증가를 읽습니다. Shared/대상 속성/클래스 레벨 기반 등 미지원 값은 계산기를 실행하지 않고 미확인으로 남깁니다. 현재 CL 관측값이 없으면 CL 의존 주사위/발사체도 미확인입니다.
발사체 개수는 설정 배열 길이와 MaxProjectilesCountRank 중 작은 값이며, 선/원뿔은 대상별 한 번의 효과로 구분합니다. 단순한 고정 반복도 반영합니다. 조건 분기/무작위 반복은 확정 타수로 합산하지 않습니다. 분기 없는 피해식 하나, 알려진 타수, 강화/극대/반감 미적용인 경우에만 전부 적용 시 기본 평균을 표시합니다. 이는 명중·SR·내성·DR·저항·추가 피해 적용 전 값입니다. 강화/극대는 적용 전 주사위를 표시하고 따로 안내합니다.

## 0.5.15
주문 출처를 확인한 비무기 피해는 공격 굴림이 없어도 spell-observation의 checkKind=damage로 기록합니다. 시전 컨텍스트 ID, 공격 굴림 ID(있는 경우), 실제 피해 주사위·보정·반감·감소 추적을 포함합니다. 미리보기와의 동일 시전을 자동 확정하지 않습니다. UMM 시작 로그의 DLL assembly version으로 실제 로드된 버전을 확인할 수 있습니다.

## 0.5.17
spellDamage의 terms에 강화·극대 여부, 평균 반복 횟수, 후속 효과 경로, 등록 저항·면역 적용 시 평균을 기록합니다. 후속 효과는 이미 로드된 장판·버프 정의에서 읽습니다. 피해 합계는 직접 피해에 한합니다. 합성 주문은 compositeParts에 구성 주문별 기본 피해식을 기록합니다. 실제 주문 피해는 0.5.15부터 spell-observation의 damage 판정에 기록됩니다.

## 0.5.18
단일 직접 에너지 피해의 expectedSupportedModel 및 probabilityInputs에 명중(AC 기준), SR, 내성 분기별 피해와 등록 저항을 기록합니다. 기본 판정 확률을 독립으로 가정하는 부분 모델입니다. 별도 명중 보정, 은폐, 치명타 및 특수 피해 변환은 검증 범위 밖입니다. 단일 적용에 한해 확인된 소모형 에너지 보호막 잔량으로 저항 상한을 제한합니다. 클래스·캐릭터·신화 레벨 및 기본 공격 보너스 기반 랭크도 판독합니다.

## 0.5.26
Harmony 2.0.4의 위치 기반 인수로 HCO 결과 관측 패치를 연결합니다. 조준 중 동일한 주문 피해 미리보기는 1.5초 간격으로 다시 기록할 수 있으며 반복 기록은 초당 4건으로 제한합니다. 분석기는 같은 세션·시전자·대상·주문 인스턴스에 더해 턴이 일치하면 최대 4초 전의 미리보기를 대조 후보로 봅니다.

0.5.25.0 실제 로그 5,548건 중 주문 피해 199건을 다시 분석했습니다. 시전 컨텍스트 7개에서 피해 기록 13건이 후보로 연결됐으며, 주사위식은 13건 모두 일치했습니다. 186건은 최근 미리보기 후보가 없었습니다. 0.5.26의 HCO 관측은 게임에서 다시 확인해야 합니다.
# 0.5.39 추가 진단

- `branchAnalysis`: 일반 액션 분기의 계산 상태, 미지원 액션/조건 이름, 미확인 내성 단계 수입니다. 숫자 0으로 대신하지 않습니다.
- `branchPreview`: 현재 대상 조건으로 고른 분기, 시전당 확률, 피해식·상태 부여 시도, 기대 피해와 기대 HP 감소입니다. 후속 판정이 불명확해도 독립적인 피해는 유지합니다.
- `sequenceProjection`: 직접 주문 피해의 공동 굴림 분포, 임시 HP·거울상·내성 반영 결과와 계산량입니다.
- `casterLevelSource`: 관측한 최종 파라미터와 주문책에서 읽은 기본 CL을 구분합니다.
- 무기 `sequenceScope`: 여러 무기의 표시 순서를 가정한 거울상·물리 보호막 계산입니다. 실제 스케줄 순서를 인증하지 않습니다.
- 실제 주문 관측 `frozenPreviewId`, `association`, `stageOrdinalForCastTarget`: 시전·대상별 첫 관측 규칙에서 고정한 예측과 추적 입력 일치 여부입니다. 모든 게임 조건의 일치를 의미하지 않습니다.
- UMM `Spell panel:`: 주문 선택 경로와 표시/대기 상태입니다. `weapon-first-target`, `spell-first-target`, `*-refresh`, `over4ms`, `over16ms`로 최초 대상과 갱신 비용을 구분합니다.
