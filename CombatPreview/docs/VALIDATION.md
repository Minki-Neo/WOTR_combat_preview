# Validation — diagnostic development history

## 0.5.68 적용 전 진단·자동 메타매직 보강

- [x] 현재 공격 명령의 AttackHandInfo.Rule과 실제 RuleAttackWithWeapon의 객체 동일성, 무기, 페널티를 대조한다. 전체 공격 번호를 보조 무기 행 번호로 오용하지 않고 같은 무기의 순서를 사용한다. 연결 실패/목록 제한/페널티 변화는 UnmodeledAttack으로 남긴다.
- [x] 공격 시작 시 이미 판독한 입력을 inputAtAttackBegin 불변 JSON으로 보존한다. 표시 입력·실제 최종 판정과 함께 사후 대조할 수 있다. 별도 중간 방어 판정을 실행하거나 기록하지 않는다.
- [x] 정확한 행 연결에도 기존 입력 서명·대상·무기·턴·공격 모드·유효 기간 검사를 유지한다. InputsChanged를 검증 성공으로 바꾸지 않는다.
- [x] 미확인 판정은 짧은 사유와 원인 효과를 표시하고 unavailableDiagnostics에 코드/출처/필요 판정을 남긴다. 알려진 0%는 미확인으로 바꾸지 않는다. 패널 폭·배율·폰트·높이 산식은 유지한다.
- [x] 기본 AutoMetamagic의 허용 주문 종류, 지정 주문, 속성, 학파, 최대 레벨, 지정 주문 목록, 포함/제외 클래스 조건과 부모/변환 주문의 분기를 독립 판독한다. 활성 등록 목록만 사용하며 일회용 효과를 소모하거나 게임 조건 평가기를 호출하지 않는다.
- [x] 일반 주문과 주문서/물약의 자동 메타매직에 공통 경로를 사용하고 동일 갱신 묶음에서 결과를 공유한다. 출처·적용 여부·조건별 판독 결과를 로그에 남긴다. 아이템에서는 엔진처럼 로드/다음 주문 등록을 건너뛴다.
- [x] 로드 최대 레벨 검사에 선택 메타매직의 슬롯 비용을 포함한다. 자동으로 추가된 마스크를 선택 슬롯 비용으로 다시 계산하지 않는다.
- [x] 분석기 attackAssociationReview로 정확한 행 연결, 조건 변화로 거절된 연결, 입력 보존 수와 실패 사유를 집계한다.
- [ ] 임의 파생 AutoMetamagic의 재정의 조건, MagicHackData 속성/학파, 읽히지 않는 주문 레벨/부모/주문 목록, MetamagicOnNextSpell의 별도 조건은 여전히 명시적 미확인으로 남긴다. 모든 자동 메타매직 지원 완료를 의미하지 않는다.
- [ ] 새 DLL 적용 후 실제 행 연결률·미확인 감소·최종 판정 및 FPS를 검증한다. 게임 내 동작과 성능 개선은 아직 확인하지 않았다.

검증 결과: C# 26,493 assertions와 27-action 주문 회귀, 게임 메타데이터 291항목, Python 분석 26 tests. Release 빌드 성공(기존 Charge CS0649 경고 1개). 게임 코드는 테스트에서 실행하지 않는다.


## 0.5.67 로그 기반 보정과 독립 검증

검증: C# 26,447 assertions + 27-action 주문 회귀, 제공 게임 메타데이터 269항목, Python 분석 25 tests 통과. Release 빌드 성공(기존 Charge CS0649 경고 1개). 실제 게임의 새 수치/FPS 개선은 아직 검증하지 않았다.

공통 무기 결과 캐시는 AttackWithWeapon이 있는 대상 종속 결과를 받지 않도록 보강했다. 대상 없는 엔진 기준에 주적 AB/피해를 각각 한 번 추가하며 다른 대상으로 보너스가 새지 않는 회귀를 포함한다.

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

검증: C# 26,421 assertions + 27-action 주문 회귀 통과, Release 빌드 성공(기존 CS0649 경고 1개). .64 ZIP의 Main.cs(크기/배율 설정) 및 PanelPresentation.cs(크기 산식)와 현행 소스가 줄 단위로 동일함을 확인했다. 제외된 공격은 무기 계산 전에 빠지는 기존 경로로 복원했다.

사용자 요청에 따라 .65의 합계 제외 공격 참고 행을 제거했다. 일반 행동이 Available이 아닌 것으로 확인되면 이동 행동이나 이전 공격 예측이 남아 있어도 합계에서 제외한다. 확실한 소모 상태는 새 예측 대기 중에도 제외하고, 일반 행동 상태 자체가 미확인이면 판독 대기로 둔다.

.64 소스 ZIP과 비교 결과 폭·배율·폰트·CompactHeight 산식은 .65에서 바뀌지 않았다. 참고 공격 행이 추가되어 자동 높이가 커진 경로를 제거했으며, 사용자의 별도 폭/배율 설정은 덮어쓰지 않는다. 주문서 파라미터 수정은 유지한다. 실제 화면은 새 DLL 적용 후 확인이 필요하다.


## 0.5.65 행동 제외 시 참고 표시와 아이템 주문

검증: C# 26,418 assertions + 27-action 주문 회귀, 제공 게임의 메타데이터 249항목 통과. Release 빌드 성공(기존 Charge CS0649 경고 1개). 아이템 우선순위 14개와 행동 가용성 21개 사례를 포함한다. 실제 게임의 새 화면/주문서 판정 대조는 적용 후 확인이 필요하다.

- [x] 기본 행동 Overused는 CPU 계산 한도가 아닌 선택한 행동 계획의 실행 실패임을 명확히 표시한다. 소모/제한과 선택 계획 불가를 구별한다.
- [x] 표준 행동 Available + 현재 사거리 + 이동 불필요 + 모순되는 공격 계획 없음인 경우, Overused와 별개로 일반 단일 공격을 추정한다. 실제 소모된 행동이나 실패한 접근을 실행 가능으로 바꾸지 않는다.
- [x] 확정적으로 합계 제외된 공격도 무기 참고 수치를 계산·표시한다. 실행 가능 합계, 체력 감소, 실제 판정 연결에는 포함하지 않는다.
- [x] ItemStatHelper/GetParamsFromItem 경로를 독립 모델로 구현: 아이템 DC/CL, 제작 파라미터, ScrollMastery, ScrollSpecialization, 완드 숙련 우선순위, 강화 물약, 아이템 유형별 CL 보너스, Trickster 신화 CL, 제작 메타매직/ExtendPotion. 일반 주문서 DC/CL 보정기를 아이템에 중복 적용하지 않는다.
- [x] itemParameters에 출처/사용 입력/결과를 기록하고 파라미터 캐시 키에 포함한다. HCO 값·Rulebook 재실행·실제 사용 후 관측을 기다리는 경로에 의존하지 않는다.
- [ ] 특수 AutoMetamagic 등록의 임의 조건식은 미지원 시 ItemAutoMetamagicPredicateUnresolved로 남긴다. 아이템에서 엔진이 건너뛰는 로드/다음 주문 메타매직 등록은 차단 사유로 삼지 않는다.
- [ ] 새 DLL 적용 후 행동 소모 상태의 참고 행 표시 및 실제 주문서 DC/CL/확률을 게임 로그와 대조한다.


## 0.5.64 실제 적용 전 진단 보강

검증: C# 26,399 assertions + 27-action 주문 회귀, 게임 메타데이터 236항목, Python 분석 23 tests 통과. Release 빌드 성공(기존 Charge CS0649 경고 1개). 보고 예산 분리·쓰기 실패 격리·표시 프레임 고정·추후 캐시 갱신 차단을 회귀 검증했다.

설치 전 구 로그 5,357행 분석: 최신 실행은 0.5.57.0, 구 주문 체력 표시 371건 중 임시 HP 포함 90건이며 새 임시 HP 소모 필드는 없다. 구조화된 성능 구간은 0건이다. 새 버전 수치 정확도/FPS 향상을 검증한 결과로 취급하지 않는다. 원시 분석 출력은 작업 outputs/CombatPreview-0.5.64-preinstall-baseline.json에 보존했다.

- 무기 `healthProjection`: 표시한 전체 공격열의 본체/임시 HP, 각각의 기대 소모, 방어 후 기대 피해, 계산 프레임, 포함 previewIds, 계산 불가 사유, mean-state 근사임을 기록한다. 표시 입력을 불변 JSON으로 실제 판정에도 보존한다.
- `performance-window`: 15초 및 모드 해제/미리보기 객체 종료/수동 계측 초기화 직전에 짧은 구간을 기록한다. 전체 게임 프레임, 느린 프레임 수, 단계별 최대/평균/GC/할당량, 설정을 JSONL에 기록한다. 강제 프로세스 종료 시 마지막 15초 미만 구간은 보장하지 않는다.
- 성능 보고는 초당 4개의 별도 예산이며 preview 20개/실제 256개 예산을 소모하지 않는다. `reportRateLimitedRecords`를 별도 표시한다.
- 각 기록의 runtime에 modAssemblyMvid, nativeHealthIntegration=Removed, healthProjectionModel, diagnosticFeatures를 추가한다. 버전 숫자만 같은 다른 빌드도 구별할 수 있다.
- 분석기 healthProjectionReview/performanceReview로 누락·근사·자원 범위 오류·완료 체력 근거·느린 단계를 정리한다. 전체 공격열의 기댓값과 단일 타격은 직접 대조하지 않으며, 최종 상태만으로 실제 임시 HP 흡수량을 역산하지 않는다.
- 기존 명중/d20/피해식/방어 항목 대조, 주문 준비 지연/범위 대상 필터/판정 불일치 분석을 유지한다. 기본 게임 체력바 연동은 제거 상태다.


## 0.5.63 기본 게임 체력바 경로 제거

검증: Release 빌드 성공, C# 26,383 assertions 및 27-action 주문 회귀 통과, 게임 메타데이터 236항목 통과. src/tests의 기본 HP 연동 심벌 잔존 검색 결과 0건. 실제 게임 화면은 미검증.

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


## 2026-11-23 — 0.5.61 final ruling raw evidence

C#25,667 assertions plus topology fixture: changed-input displayed AB/dice/fixed bonus/raw DR retained without certifying the association; missing defense stays null; frozen nested JSON is immutable and exceeds512 characters safely; preview exhaustion cannot consume the separate actual-event budget and raw truncation is counted. Supplied-game metadata240 checks cover captured engine fields. Python20 tests include raw coverage and missing counters. Build passed with existing CS0649 Charge warning.

Primary evidence: work/actual-roll-inputs-061.txt, work/rule-modifiers-061.txt, work/defense-runtime-fields-061.txt and existing dice/stat field audits. Captures use completed real events plus already retained displayed DTOs; no rule dispatch, ContextValue evaluation, reference loading or live stat recalculation. No intermediate defense snapshots are collected. Registered completion-time DR/pool candidates remain distinct from actual applied per-component reduction. Unknown non-scalar state and collection caps are explicit, not exhaustive provenance claims. Reflection metadata is cached, values are copied anew. No live game capture/schema/FPS integration verification is claimed; game files were not replaced.


## 2026-11-23 — 0.5.60 health display readiness

Build and C#25,655 assertions plus topology fixture passed;16 new assertions cover completed-hover refresh, retained other work, excluded/duplicate protection, missing stage vs valid full-branch HP, mean-state temporary HP/caps, unknown death/collateral protection and positive subpixel visibility without a zero-damage marker. Metadata224/Python19 checks retained. Existing CS0649 Charge warning. Tests do not render Unity UI or measure live hover latency.

Known exact HP results precede mean-state estimates; mean-only display is explicitly labeled and cannot invent unknown final probabilities. Weapon temporary-HP approximation is restricted to the mod panel, preserving native overlay guard behavior. Same-batch target budget remains unchanged. Known hover targets sort before the cap; already completed targets may refresh on hover or parameter revision. See LOG_REVIEW_0.5.60.ko.md for limitations.


## 2026-11-23 — 0.5.59 target eligibility and actual-roll/outlier review

Build passed with existing CS0649 Charge warning. C#25,639 assertions plus topology fixture;10 new eligibility/queue checks cover enemies, allied healing, friendly fire, self/direct flags, unknown delivery retention and exclusion before caps/priority. Metadata224 checks include area selector type/conditions. Python19 tests cover conditional-vs-hit-weighted means, multiple damage events, legacy/new critical means, reroll history and concealment. No live UI/FPS validation is claimed.

Audited AbilityTargetsAround.Select and local Angel Bolt/Storm blueprints. Eligibility filters observations and display candidates before capacity/queue/fallback; unknown semantics remain candidates. Added normal/critical post-defense means and candidate-only damage/roll review. Early snapshot669 actual-roll checks and later rotated snapshot404 checks yielded no unexplained basic AC arithmetic cases. Later128 associated conditional-mean groups yielded27 heuristic outliers, not confirmed errors; many candidate associations have changed state. Actual source/dice/bonus/DR gaps remain for follow-up. See LOG_REVIEW_0.5.59.ko.md.


## 2026-11-23 — 0.5.58 direct branches with unresolved secondary targets

Build passed (existing CS0649 Charge warning). C#25,629 assertions plus topology fixture, including5 new outcome table checks for retained direct probabilities, null HP and explicit secondary scope. Metadata222 checks include random action target count/list; Python17 tests include parameter comparison coverage that rejects bool/NaN/missing deltas. Whole game branch adapters and FPS are not exercised by these tests.

Latest0.5.57 session042f8db5d37148e68fda219b5951d32f final snapshot5,066 rows/303 spell previews:186 valid independent parameter comparisons all match DC/CL/rank;117 readonly calculations have no comparison. This is tracked-state parameter evidence, not total damage/odds validation. Typed bonus8 checks remain incomplete due to compound blessing removal. AoE82 samples max16.7691ms, one over16ms; full-frame FPS is not inferred.

Known earlier direct branches survive unmodeled random collateral actions with explicit scope. Full HP/damage for affected branches remains null and projected state is invalidated before later calculations. No random selection or collateral event is executed. Added missing SchoolInputs batch scope and school-input timing; reuse remains synchronous and ends with the batch. No measured0.5.58 performance improvement is claimed. See LOG_REVIEW_0.5.58.ko.md.


## 2026-11-23 — 0.5.57 context replacement and ally conditions

Build and C#25,624 assertions plus topology fixture passed; 11 new context snapshot tests cover nearest-parent/own precedence, missing/cyclic inputs, identical vs conflicting overrides, rejected-state preservation, meta union and invalid source levels. Metadata220 checks include MechanicsContext.m_Params/ParentContext and AbilityParams.SpellLevel. Python16 checks retained. Existing CS0649 Charge warning remains. No live adapter/UI/FPS validation is claimed.

Audited ReplaceAbilityParamsWithContext handler, RuleCalculateAbilityParams.OnTrigger, MechanicsContext.Params and ContextConditionIsAlly. Saved context parameters replace baseline DC/CL; supported rule bonuses are then added, with RankBonus derived only from the current CL bonuses. Context mutation dependencies are retained even in the strict cache path. Context getters, reference Get, events and RNG are not invoked. Unsupported meta changes, conflicts and specialized sources keep explicit uncertainty. Source OverrideParams and context event replacement remain separate.

Latest reviewed runtime0.5.54, 3,079 retained session rows; no actual0.5.55–57 samples available. AoE69 sampled target timings max8.772ms, no samples over16ms, max2 processed per frame; these exclude logging and do not certify FPS. See LOG_REVIEW_0.5.57.ko.md for evidence and remaining work.


## 2026-11-22 — 0.5.56 conditional save stacking and inherited parameters

Build passed (existing CS0649 Charge warning). C#25,613 assertions plus topology fixture; 303 new typed-save assertions include294 direct die/chance/activation oracles, grouped positive/negative stacking, no activation, different descriptors, immutable baselines and a consumed effect across two saves. Four inherited-parameter boundary/metamagic checks added. Metadata217 checks; Python16 tests. Source snapshots and conditional bonus fields are not game handlers. Live UI/adapter/FPS validation remains pending.

Latest observed runtime0.5.54.0, session8c338f6669a84f41a2c70980394308c3, reviewed3,874 rows. AoE scheduling86 samples, maximum target11.0325ms and no target sample over16ms; timing is rate limited and excludes logging, so it does not establish full-frame FPS improvement. Existing numerical comparison coverage remains absent. Complex Luck/Resolve buff consumption, suppressed stat caches and changed projected stat baselines retain explicit uncertainty. Inherited source parameters are used only with matching known metamagic, valid DC/CL and no item/conversion priority conflict. Parameter values are included in cache dependencies and provenance.

Primary IL evidence: ModifyD20 temporary stat modifiers and AbilityData.CalculateParams returning Fact.OverrideParams before rule calculation; new AbilityParams metadata audit in work/ability-override-056.txt. See LOG_REVIEW_0.5.56.ko.md. No game files were replaced.


## 2026-11-22 — 0.5.55 school features and specialization

Build passed, existing CS0649 Charge warning only. C#25,306 assertions plus topology fixture; 160 new school transfer/mythic focus assertions cover donor grouping vs cross-school sum, own-school exclusion, empty/negative donors, order invariance and current expanded runtime context. Supplied-game metadata209 checks, including expanded-school entries/lists, FeatureParam fields, parent reference and context replacement reference. Python15 tests unchanged. Tests do not execute live game parameter adapters.

Primary evidence: local game IL for SpellFocusParametrized, SpellSpecializationParametrized, SchoolMasteryParametrized, ExpandedArsenalMagicSchools, UnitPartExpandedArsenal, component event context and feature collection. Expanded donor GetBonusDC reads Param from the currently running arsenal component event context; calculations preserve that behavior rather than substituting donor Param. No game handler, Ensure, reference Get or blueprint load is invoked by these adapters. Feature/school snapshots are scoped to one synchronous batch; cache dependency stamps include selected feature parameters and expanded-school entries.

Latest actual runtime0.5.54.0; its seq397 logs Computed save-consumption and staged HP prediction for Phantasmal Killer. Other seq394/395 failures are typed conditional saving-throw bonuses from Luck/Resolve blessing, not the old missing cooldown Guid. These remain unsupported with explicit notes. New0.5.55 actual DC/CL/numeric damage/UI/FPS validation is pending. See LOG_REVIEW_0.5.55.ko.md.


## 2026-11-22 — 0.5.54 runtime blueprint identities / Misfortune

Build passed with existing CS0649 Charge warning. C# 25,146 assertions plus topology fixture. Regression tests cover the actual production field traversal with serialized guid=null, authoritative runtime/empty identities, stale text, no mutation, cooldown policy with loaded definitions taking precedence, modified/nonbuff/unloaded unknown rejection, supported build restriction, exact SR probability boundaries and the seq242 two-save persistent lifecycle/HP calculation. The numerical expectations were independently enumerated from raw dice. Existing save-consumption oracle remains in place. No live game component handler was executed by tests.

Supplied-game metadata checks: 201, including BlueprintReferenceBase.deserializedGuid and BlueprintGuid.m_Guid. Python analysis tests: 15. Local IL confirms OnAfterDeserialize clears serialized guid and GetBlueprint reads deserializedGuid. Affected readers formerly relied solely on cleared text. Passive cooldowns and read-only loaded-blueprint lookup now share authoritative runtime identity; no reference Get/Load/write is introduced. Loaded modified definitions retain precedence and unsupported followup actions retain uncertainty with better provenance. D20 probability sums clamp to [0,1] to avoid negative SR-blocked outcomes from 1+epsilon.

Latest observed game runtime is 0.5.53.0 in session ab19ad9bdbbc4790a30e75af9013a397. Game UI, state preservation in live use and exact numerical application of 0.5.54 still require installation and fresh observation. No game files were replaced.


## 2026-11-22 — 0.5.53 parameter fallback and release review

Build passed with the existing CS0649 Charge warning. C# suite: 25,120 assertions plus the 27-action topology fixture. New checks cover descriptor masks (including bit63), nonmatching descriptors, same/conflicting/unsupported stat replacements and rejected-state preservation. Existing DC bonus stacking tests remain in place. Supplied-game metadata: 199 checks. Python analyzer: 15 tests including version normalization, old/mixed/missing runtime rejection and invalid requested version input. git diff --check passed (one existing CRLF normalization notice).

Audited IncreaseSpellDescriptorDC, ReplaceAbilitiesStat, RuleCalculateAbilityParams.OnTrigger and UnitPartChangeSpellElementalDamage IL from supplied Assembly-CSharp.dll. The mutating elemental descriptor helper is deliberately not called. New adapters read snapshots only, and retain unsupported reasons for conflicting/domain replacements and elemental-conversion contexts. Tests do not execute the live parameter adapters or validate in-game numbers/UI/FPS.

Current + rotated diagnostics: 2,825 records, both sessions runtime 0.5.48.0; installed Info.json 0.5.48. Latest session 1,871 rows, 236 damage components with no arithmetic reconstruction differences but zero comparable static-defense replays. --expected-version 0.5.53 reports RuntimeVersionUnverifiedOrDifferent. No runtime validation claim is made for 0.5.49–53. Full findings and remaining work: LOG_REVIEW_0.5.53.ko.md. Game files were not replaced.


## 2026-11-22 — 0.5.52 shield approximation

C# suite: 25,099 assertions plus topology fixture, including 788 mean-shield/exact-priority checks. Covers clustered plus pooled DR in visible Beta rows, exact-path preservation, constant/pool tie selection, input immutability, probabilistic residual budgets, source identity across mixed damage, known SR/save gates, conditional HP caps, temporary HP and long sequences above the exact delivery limit. Approximate values are tested for nonnegative/finite/raw-damage bounds; these are not claims of exact expectation for stochastic depletion. Unknown probabilities/types and non-pool uncertainty remain unavailable. Metadata layout: 194 checks; Python analyzer: 14 tests, including approximation provenance counts.

Build uses supplied WotR references, with the existing CS0649 Charge warning. Local IL audits confirm ProtectionFromEnergy.CalculateValue calls CalculateRemainingPool and its runtime RemainPool getter. Exact successful results remain preferred; approximate fallback allocates/updates its own mean ledger only when needed, with bounded source/component/delivery counts and no game RNG or pool mutation. In-game display, actual error magnitude and FPS have not been measured on this build.

## 2026-11-22 — 0.5.51 bounded AoE work

C# suite: 24,311 assertions plus topology fixture, including 146 queue/log-budget checks. A 32-target workload is simulated with 1ms and 5ms target costs: all targets finish exactly once in 16 or 32 frames, cursor target first, with membership changes and hard-count/soft-time limits. Rate-limited preview payload enumeration is verified to stay untouched, and the next log window restores serialization. Supplied-game metadata layout remains unchanged (194 checks). Python analyzer: 13 tests, including per-target timings versus full-frame FPS semantics.

Code audits establish the previous 32-target synchronous loop and pre-rate-limit JSON serialization. Current/rotated game logs reviewed: 3,282 rows, latest runtime 0.5.48.0, 767 spell previews, maximum per-second distinct-target union 15, no new timing samples. This is not a simultaneous target count, a CPU profile, an FPS benchmark or an in-game integration test. The queue can delay completion of the entire area preview and cannot preempt one expensive target. Per-target results are refreshed at different frames; the panel displays progress rather than dropping targets or inventing zero damage.

## 2026-11-22 — 0.5.50 stateful features

C# suite: 24,165 assertions plus topology fixture, including 162 new clustered-DR/consumed-save checks. Independent die enumeration verifies natural-20-only one-use misfortune with Persistent; the two-save input fixture gives first failure .721125 and joint failure .6209325. Constant DR depletion is enumerated across hit/miss and d3 damage branches, preserving input/copy independence. Existing 24,003 assertions also pass after the D20 lifecycle output change. Supplied-game metadata: 194 checks. Python analyzer: 12 tests. Release builds against the supplied game references, with existing CS0649 Charge warning.

Local IL evidence: work/clustered-d20-audit.txt (AddClusteredAttack, UnitPartClusteredAttack, ModifyD20); asset metadata: work/misfortune-blueprint-audit.json and empty Fortune/Misfortune cooldown definitions in the user-provided local blueprints.zip. Latest spell-744 on 0.5.48 reports missing save probabilities with a consuming ModifyD20 and Persistent, while SR .8/DC25/Will15/Fort12 are available. No gameplay method, RNG or buff action was invoked to obtain the corrected model numbers. Runtime adapter matching, actual UI/HP rendering, arbitrary after-roll side effects, clustered DR plus consumable protection and FPS remain unverified/unsupported explicitly.

## 2026-11-22 — 0.5.49 weapon fallback

C# suite: 24,003 assertions plus topology fixture, including 15 new fallback/input-gap checks. Supplied-game metadata: 190 checks. Python analyzer: 12 tests, including modified-dice precedence over blueprint base dice. Actual 0.5.48 beta-378 review is documented in LOG_REVIEW_2026-11-22.ko.md; 1d8+14 versus 3d6+22 is an input discrepancy, not a statistical comparison against random damage. Pure tests cover ability multiplier signs, size clamp/sign quirks, copied-row lookup, missing destinations and typed pending bonus deltas. IL audits are workspace work/weapon-stats-audit.txt, work/weapon-size-handlers-audit.txt, work/weapon-size-data-audit.txt, work/damage-stat-override-audit.txt and work/base-ab-rule-audit.txt. New runtime adapter behavior, full damage accuracy, DR/bypass, critical changes, action mode and FPS need new game evidence.

## 2026-11-22 — 0.5.48 energy scaling

Pure C# suite: 23,988 assertions plus the 27-action topology fixture, including 5,412 new energy scaling checks. Independent 2d4 enumeration covers signed percentage/minimum/Empower/Maximize/save/resistance combinations, single-precision integer boundaries, HP caps, temporary HP, constant resistance versus consumable-pool selection and cache identity. Unknown scales are checked through plain, capped and repeated-hit means without producing a numeric NaN result. Supplied-game metadata: 180 layout checks; Python analyzer: 11 tests. Release compilation uses the local game references with the existing CS0649 Charge warning only.

Local IL audits verify EnergyDamageBonus is an initiator handler; AddEnergyVulnerability is a target handler; BonusPercent uses per-source truncation/addition; set_Vulnerability uses Max; DamageBundle.WeaponDamage requires a weapon. Audits are workspace work/energy-scaling-audit.txt, work/damage-percent-handlers-audit.txt, work/base-damage-audit.txt and work/damage-bundle-audit.txt. These tests do not execute the game or prove every runtime reader path. Mixed swarm/energy vulnerability handler ordering, uncertain weapon-only bonuses, linked contexts and actual UI/FPS remain unverified or explicitly unresolved.

## 2026-11-22 — 0.5.47 area damage defenses

C# suite: 18,576 assertions plus the 27-action topology fixture, including 971 new target-scaling assertions. Independent 2d3 enumeration covers stacked area halves, swarm vulnerability, Empower/Maximize, save multipliers, resistance, shared consumable protection, temporary HP and HP caps. Metadata: 176 supplied-game layout checks. Python: 10 tests, including area handler statistics and null damage metadata. Release build uses supplied game references; only the existing CS0649 Charge warning remains. Local IL audits confirm the target handlers multiply Durability and confirm ContextActionDealDamage's IsAoE/cache/TargetsAround predicate. Source files: work/area-damage-audit.txt, work/damage-modifier-handlers-audit.txt and work/damage-calculation-audit.txt in the workspace.

Current and rotated logs reviewed: 6,062 rows, latest identified mod version 0.5.42.0; no new target scaling fields or named area-half handler evidence. This does not verify 0.5.47 gameplay, UI or FPS. Linked area/buff contexts and arbitrary target defense changes during a spell remain outside this direct-action implementation.

## 2026-11-22 — 0.5.46 detached condition lifetime

C# suite: 17,605 assertions plus the 27-action spell topology fixture, including 6,924 new condition-lifetime assertions. An independent source-token oracle enumerates overlapping condition/immunity sources and three-step add/remove sequences. Tests also cover copied live inputs/sibling branches, invalid/overflow counters, self-status activation guards, and exact owned-buff removal. Supplied-game metadata: 174 layout checks; Python analyzer: 9 tests. Release build against the supplied WotR references passes with the pre-existing CS0649 Charge warning. Game IL was inspected for UnitState, status buffs, fatigue, helplessness, and condition components. These checks do not execute the game, certify every runtime adapter path, or establish UI/FPS results. Arbitrary condition-trigger actions and unsupported status handlers remain unresolved explicitly.

## 2026-11-21 — 0.5.45 buff contexts and lifetime

Release compiles against the supplied WotR 2.7.0x references. Pure C# suite: 10,681 assertions plus the 27-action spell topology fixture. Buff transition/stat coverage is 1,521 assertions, including parent/own descriptor inheritance, snapshot selection versus first-current-buff removal, sibling independence, context multiplier/minimum/Powerful Change operation order, pending modifier removal, Replace source changes, Rank caps and pending/live rank contributions. Metadata layout: 164 supplied-game checks. Python analyzer: 9 tests, including actual save modifier source summaries. No game method executes in these tests; runtime parameter provenance, restriction predicates, reactivation ordering, UI numbers and FPS remain unverified until new game evidence is available.

## 2026-11-21 — 0.5.43 outcome tables

Added 24 presentation assertions, including a sanitized real Phantasmal Killer outcome fixture from 0.5.42 record 16877. Checks retain all final branches, per-cast probability sums, lethal/damage HP contributions, unknown versus zero, generic blocked deliveries, combined status/damage, per-ray scope and mirror-depletion first-hit scope. Total C# assertions: 9,277 plus topology checks. Python analyzer: 8 tests. The Unity panel uses cached GUIContent and width-dependent row heights, wrapping Korean text and keeping outcome details behind F6. Builds validate Unity API references; these are not in-game visual/FPS tests. General reroll and parameter-readiness issues identified in LOG_REVIEW_2026-11-21.ko.md are not fixed by this display change.

## 2026-11-21 — 0.5.42 swarm support

Audited the supplied 2.7.0x assembly's SwarmDamageResistance, SwarmAoeVulnerability, AddImmunityToCriticalHits, RuleCalculateDamage transformation order and SingleTarget spell immunity. Added 186 pure assertions covering subtype/form masks, attacker and kinetic exceptions, non-weapon physical damage, decline before DR/pool absorption, energy damage remaining after physical immunity, area/save/SR/temporary-HP distributions, unchanged hit probability with critical immunity and basic panel labels. Total: 9,253 numerical/policy assertions plus the 27-action topology fixture. Supplied DLL metadata checks: 144. These tests do not execute game rules or certify UI/FPS in the running game. No name-matching swarm records were found in the current/rotated diagnostic logs; this is not proof that a swarm never appeared under another localized name. Installed mod is still 0.5.36.

## 2026-11-21 — 0.5.41

Added 795 value-only assertions for buff transitions: first-match removal, caster filtering, rank-preserving identity and final rank removal, live/projected insertion order, independent branches and removal tombstones, descriptor substitution masks, immunity exceptions with unknown facts, restoring suppressed typed bonuses and the unfiltered cap of morale/insight caches. Total C# numerical/policy assertions: 9,067, plus the existing 27-action topology fixture. Game DLL layout checks: 141; Python diagnostics suite: 6 tests. Release build uses the supplied game references; the tests do not execute game logic. Runtime substitution handler ordering, UI values and FPS are not certified by these tests. Installed mod remains 0.5.36.

## 2026-11-20 — 0.5.40

Release built against the user's game 2.7.0x references. Pure C# suite: 8,272 assertions plus the real BoltOfJustice topology regression (27 actions). New coverage includes 343 mixed/protected spell distribution assertions, 166 projected-stat/next-save assertions, source-selection-before-roll-capping, changed-input cache invalidation and defense snapshot independence. Supplied DLL metadata checks: 130; Python diagnostic analysis: 6 tests. No game methods execute in these tests. Installed Info.json still reports 0.5.36, so current logs cannot establish 0.5.40 UI accuracy or FPS improvement. Full buff substitution, exotic handlers, continued area effects and actual multi-attacker scheduling remain open.

Latest: 0.4.0 shared-engine/automatic-DR cloud validation is recorded at the end. Earlier sections are historical results, including user-supplied 0.2.x runtime evidence.

Date: 2026-11-01 (Asia/Seoul). This records commands actually executed in the cloud instance.
No game executable, Unity asset data, user save or interactive game session was supplied/run.

## Inputs and environment

- OS: Debian GNU/Linux 13 (trixie), Linux x86_64.
- .NET SDK: 8.0.408. SDK archive SHA-512 matched the official release metadata during setup.
- Python 3.12; tree-sitter 0.25.2; tree-sitter-c-sharp 0.23.1.
- Original source ZIP SHA-256: `4ad6fa8d567883140588f28c985ae341d6e79a28baf5781d376696ee3176c4c7` (matches handoff).
- Empty GitHub checkout was populated from that exact ZIP, retaining original LICENSE and THIRD_PARTY notices.
  No Git commit, push, PR or external publication was performed. Original attachment and baseline extraction remain intact.
- User reports ModFinder management and no TTT. Full installed mod list/settings are not known.

Uploaded DLLs were inspected with metadata readers and ILSpy 9.1.0.7988; they were not executed as applications.
No game or decompiled source is included in the source/UMM ZIPs.

| Input | Metadata / identity | SHA-256 |
|---|---|---|
| Assembly-CSharp.dll | assembly 0.0.0.0; MVID 90a9869c-2792-4c7b-bfb7-5a8b33da7c82 | `2cb7160b7154d4ffacc77b9c51b1eb26199e1294300f04fdfc073367b2ef8953` |
| game 0Harmony.dll | 2.0.4.0; MVID 88b084d2-77f3-44aa-9c29-04c640976875 | `de50cd610ea22854c8930ec5433276713aee1bb0ecd785d6b15a0661c2fa9fd5` |
| UnityModManager.dll | 0.33.3 / informational 0.33.3+c41c0787062ebb1def95239c9647191073f35500 | `e0b97c482ec2bf66cb97e0a671a562bbe8501b6ed22df2aa03222137f16a35aa` |
| ModFinder.dll | 1.5.3 / .NETCoreApp 8.0 / Windows WPF references | `77c4435a3cb711d543fd8d02d3936805674dc111d2fbe8ffd3a2c9286b9c088e` |

The game display version cannot be established from the AssemblyVersion (0.0.0.0).
Static inspection shows GameVersion.GetVersion loads a Unity `Version` resource. Runtime logging is needed to
identify the user's actual display version. No release is declared supported merely because it is called latest.
UMM references Harmony 2.3.6.0, while the supplied game Managed folder contains Harmony 2.0.4.0. Compilation
succeeded against the supplied set; the actual loaded/runtime binding still needs verification. No DLL was replaced.

## Commands and outcomes

Working directory: `/workspace/WOTR_combat_preview/CombatPreview`.
Shell activation: `export PATH=/workspace/tooling/dotnet:$PATH; export DOTNET_CLI_HOME=/workspace/tooling/dotnet-home`.
Python checks used `/workspace/tooling/python/bin/python`.

| Command | Actual outcome | Exit |
|---|---|---|
| `dotnet run --project tests/CombatPreview.Tests.csproj -c Release` on original sources | 6,699 assertions passed (baseline, now actually executed) | 0 |
| same command on updated sources | 6,699 original math assertions + 109 defense/quality/diagnostic/safety assertions = **6,808** passed | 0 |
| `python tests/reference_check.py` | 509 independent math/manifest checks; 2 project XML files parsed | 0 |
| `python tests/syntax_check.py` | WOTR parse for 25 source/test C# files; generated bin/obj excluded | 0 |
| `dotnet build CombatPreview.csproj -c Release -p:ManagedDir=/workspace/game-references/Managed -p:UmmDir=/workspace/game-references/UMM` | full net472 mod build using actual provided game/Unity/Harmony/UMM references; 0 errors; one existing unused Charge-field warning on compilation | 0 |
| `dotnet run --project tests/CombatPreview.Tests.csproj -c Release -- --diagnostic-fixture docs/examples/diagnostic-fixture.jsonl` | generated explicitly labeled fixture report, not game observations | 0 |
| `python3 tools/package.py --managed-dir /workspace/game-references/Managed --umm-dir /workspace/game-references/UMM` | full build + exact 6-file diagnostic-only UMM ZIP; CRC/allowlist verification | 0 |
| `bash /workspace/tooling/refresh.sh` | saved reusable dependency/test/full-build procedure executed successfully | 0 |

Initial full build without game references failed at CheckGameReferences as expected. After the user's Managed.zip
and UMM were supplied, that prerequisite was resolved and the actual full build succeeded. No game stubs were used.
The original test fixture compilation has unused D20Modifier-field warnings because it excludes game-linked handlers;
these are warnings, not test failures. Required assertions did execute; no tests were disabled/skipped to pass.
PowerShell packaging was updated for fresh temporary staging but not executed in this Linux environment; the Python
build/packaging path above was executed. ModFinder itself was not launched (Windows application).

## Coverage and concrete behavior

- Original deterministic dice distributions, unconditional hit/crit weighting and static typed mitigation.
- Fixture DR maximum vs explicit stacking, AND/OR and enhancement-threshold bypass, typed immunity, snapshot copies.
- Incomplete observation, unknown rules/types/traits, suppression, unsupported pools/modifiers and mixed bypass refusal.
- Aggregate unavailable/missing/mixed rows do not produce a full-attack number; partial quality remains partial.
- Diagnostic OFF, complete-condition deterministic comparisons, state/turn/TTL/clock invalidation, manual refusal,
  duplicate suppression, roll-to-damage IDs, valid Unicode JSONL, rate limiting, byte rotation, reentrancy and disk failure.
- Legacy replay gate and d20 >8/invalid-input refusal.

These tests exercise original and new pure C# code. They do NOT instantiate game targets or validate live adapters.
The manual model `1d8+15 physical + 1d6 fire`, DR10/fire5, Hit=.75/Crit=.075 still yields **8.7125**.
One random actual damage sample is never compared with that expectation to infer mismatch.

## Explicitly unverified / outstanding

- UMM load/event subscription/partial failure/disable/enable, ModFinder installation and in-game diagnostic output.
- No in-game numerical accuracy, UI rendering/input, target/buff/turn changes or full state/RNG preservation proof.
- Hover preview is unconditionally blocked; no live prediction comparison or automatic effective target defense support.
- Vanilla cached-field capture is a compiled draft; observed cached numbers are incomplete evidence only.
- TTT has fixture contracts only, no live reader or activation verification. User currently does not use TTT.
- Sneak/conditional on-hit effects, mount damage totals, vulnerability/difficulty semantics and sequential pools remain unsupported.
- No game session or remote publication. Environment draft persistence is separate from publication/new-task restoration.

Next input: an actual game test environment/session with a user-approved test save, full current mod configuration,
and runtime UMM/CombatPreview logs (including exact game display version and loaded Harmony/MVID).
Existing provided DLLs are retained privately under `/workspace/game-references`; they do not need to be supplied again
in this workspace. Follow RUNTIME_TEST_PLAN.ko.md; do not turn a blocked feature into a passed test.

## 0.2.2 diagnostic regression update — 2026-11-01

User-supplied 0.2.1 runtime evidence now confirms event observation in one actual game session:
50 roll records (35 associated with weapon attacks, 15 unassociated), 14 associated hits and 14 damage events,
all damage linked to its roll, 22 full-attack roll rows and one confirmed critical. No error entries in the supplied
CombatPreview.log. These do not prove predictor accuracy or state/RNG preservation. Raw personal gameplay logs
are retained outside Git; only aggregate findings are recorded in RUNTIME_LOG_REVIEW.ko.md.

0.2.2 fixes false protection-pool classification from an unused positive cache, separates unassociated rolls,
filters IsFake events, records session/runtime identity on every row, and preserves structured defense details.
ProtectionPoolSnapshot and recorder regression fixtures exercise those cases. Game-linked capture reads the
blueprint backing field and its static UsePool field, without invoking virtual defense methods or evaluators.
The original 6,699 math assertions and 138 contract/diagnostic/safety assertions (**6,837 total**) pass.
Independent Python checks: 509; WOTR syntax: 25 source/test files. Whole net472 compilation uses the same real
provided game/UMM references. 0.2.2 itself has not yet run in the user's game.

### Subsequent 0.2.2 user execution evidence

A second supplied JSONL appends 11 schema-2 rows after the exact previous 64 schema-1 rows. The new session
contains 9 associated weapon rolls (6 full-attack roll rows), 2 hits and 2 linked damage records; no missing links,
duplicate IDs, unassociated roll rows or clipped evidence. Record sequences are contiguous 1–11 and runtime
metadata agrees across all rows: game **2.7.0x**, mod 0.2.2.0, UMM 0.33.3.0, Harmony 2.0.4.0, game MVID
90a9869c-2792-4c7b-bfb7-5a8b33da7c82, TTT assembly detection False. This confirms those versions loaded
together for the observed callbacks, not all possible Harmony/UMM APIs or gameplay behavior.

Both new damage targets had no existing reduction part. Nonempty DR/energy/pool extraction and all state/RNG
acceptance tests remain unverified. The analyzer now separates cumulative vs per-session results. It was exercised
on the supplied old/new files, a cross-session duplicate object-ID case, a record-sequence gap and interleaved session
ordering. No runtime C# code, DLL, installer or version was changed for this analysis-tool/documentation update.

## 0.3.0 basic-weapon beta — 2026-11-01 Asia/Seoul

- Original pure math: 6,699 assertions; contracts/diagnostics/safety: 138; new beta math/association/logging:
  250. **7,087 assertions passed** using the real C# implementations, not game execution.
- Independent Python checks: 509; two project XML files parsed. WOTR syntax: 28 C# source/test files.
- Whole net472 mod build against the supplied Managed/UMM references: 0 errors, one existing unused
  AttackPrediction.Charge warning. No reference/game DLL or decompiled code is packaged.
- New beta tests cover independent 400-pair attack/confirmation enumeration over AB/AC thresholds,
  basic iterative rows and raw damage moments, incomplete/unknown damage, before/after associations,
  changed input/weapon/target/turn/mode, expiration/clock regression, extra attacks, cancellation,
  no comparable fields, typed JSON rows and roll links. A random damage sample differing from mean
  never becomes an expectation mismatch. No tests were disabled to pass.
- Target HP display reads cached maximum and damage, with temporary HP separate; it does not enter
  the model's deterministic difference fields. Static IL inspection and full compilation cover the field
  shape; actual HP/buff/heal refresh and UI rendering require the runtime plan.

This beta intentionally displays a partial base-primary-weapon model. Attack count, enhancements, feats,
ability substitution and conditional behavior are not fully modeled. Actual mitigation remains unavailable.
Every beta comparison says fullConditionsVerified=false. Passing tests do not establish full combat accuracy.
The 0.3.0 UI/hook association and all-state/all-RNG noninterference have **not been run in the game**.
Prior 0.2.2 observation evidence is not evidence for these newly enabled paths. PowerShell packaging and
ModFinder itself were not run on Linux. See RUNTIME_TEST_PLAN.ko.md for the first current-save test.

Beta fixture generation and the analyzer were executed: one deliberate AB model difference, zero missing
preview/roll references. Python packaging executed a successful full build and verified ZIP CRC and six-file
allowlist; shell release publisher syntax passed bash -n. Distribution payload and source ZIP are checked
separately for version, CRC, byte-identical built DLL and excluded proprietary/binary artifacts.

## 0.3.1 UI and component update — 2026-11-01 Asia/Seoul

C# pure tests: **6,699 original +138 contracts +281 beta =7,118 assertions passed**.
Independent Python:509; project XML:2; WOTR syntax:29 C# files. New checks exercise conditional
hit means including criticals (21.45 for the original example), nonmultiplying extra fire dice,
line-separated formulas, basic full-attack sums, unknown totals, viewport placement, immutable copies,
fresh/expired/changed-input observed-estimate reuse, cache clear/weapon refusal, all-component
comparison/JSON, and explicitly manual DR expectations. 1d8+4 physical +1d6 fire at hit.75/crit.075
has normal mean12, critical mean20.5, raw expectation9.6375, conditional mean12.85. Manual DR4
changes only the labeled scenario expectation to6.6375 and conditional mean8.85, never actual defense availability.

0.3.0 user evidence confirms 29 displayed models,10 comparison links,28 real weapon rolls and12 linked damage
records; four comparable rows all differ in AB and one actual extra attack was outside the basic model.
A screenshot confirms the old panel and HP text rendered. These are neither 0.3.1 tests nor proof of numerical
accuracy/HP refresh/state preservation. See RUNTIME_LOG_REVIEW.ko.md for aggregate evidence.

0.3.1 still needs in-game validation of action-bar bounds, Korean font/row wrapping, IMGUI lifecycle, dragging/
scaling/migration, availability/ordering of game-created estimates, new component comparisons, and complete
state/RNG equivalence. The game executable/UI session is not available in this cloud workspace. Static field
inspection and compilation do not establish any of these. Actual DR/energy/pool mitigation and full advanced
attack enumeration remain unavailable. PowerShell packaging/ModFinder application were not run on Linux.

The final net472 build succeeded against supplied references with zero errors and the existing unused Charge
warning. A sandboxed restore initially reported NU1900 (NuGet advisory endpoint unreachable); an escalated
restore through the inherited proxy succeeded without disabling TLS/signatures/audit, and subsequent packaging
completed without that advisory warning. Python packaging validates CRC and the exact six-file mod-only allowlist.

## 0.3.2 — 2026-11-01 Asia/Seoul

Executed C# assertions: **6699 original +138 contracts +306 beta =7143** passed. New pure checks cover
mounted rider/mount/limb simultaneous tracker entries, natural nonzero global attack indices without BAB
iteratives, combined sums/unknown mount refusal, hand-checked d20 thresholds and integer bounds, pointer
travel/over-panel/pin/target/turn/death invalidation, and completed-result identity/age/clock/reset behavior.
Independent Python math/manifest checks:509, two XML projects; syntax parser sees31 C# source/test files.
The actual game-reference net472 build succeeds with zero errors and the existing unused Charge warning.

User0.3.1 evidence confirms the old UI rendered but all80 models used basic inputs and all5 comparable rows
still differed in AB. No in-game claim for the new observer is inferred from those records. The cloud cannot
run Unity/the game. Two targeted Harmony postfix installation/removal, invocation ordering, reciprocal mount
refs, existing natural slots, conditional AC/AB accuracy, F6/F7/input-raycast behavior and all-state/RNG
preservation are **not yet tested in game**. No tests were disabled to pass. No proprietary binaries or raw user
logs are included in the distribution. Earlier no-patch statements describe earlier versions; legacy replay
and suppression patches remain blocked. See SAFETY_AUDIT.md for the new passive-observer boundary.

Python packaging completed a full build and verified the six-file allowlist/CRC. The initial sandbox restore
reported NU1900 while reaching the NuGet audit endpoint; an escalated restore through the inherited proxy
succeeded, preserving TLS/signature/audit checks. Final packaging is repeated with that restored metadata.
PowerShell packaging/ModFinder itself remain unrun on Linux. Source and installer ZIPs are inspected separately.

## 0.3.3 — 2026-11-01 Asia/Seoul

C# assertions: **6699 original +138 contracts +326 beta =7163** passed. New checks cover positive/odd/
negative strength under two-hand grip; BAB2 bow flurry0/0 vs a single attack; independent BAB iteratives,
haste, flawless/native pattern shape, both flurry rows/combined expectation and second-row actual comparison;
1d8+6 normal mean10.5 / conditional mean11.55; left placement and height<=420; count cause/grip JSON.
Python:509 independent checks/two project XML files; WOTR syntax:33 C# files. Full net472 mod compilation
against supplied references succeeds with zero errors and the existing unused Charge warning.

Initial adapter compilation found CountableFlag has implicit bool rather than Active; corrected using the
inspected game getter/operator. Assertions were not disabled. Source shaping and static field inspection are
not evidence of Unity interaction/adapter accuracy. Actual Auto grip, fact activation/range applicability,
postfix installation/removal and three-method interop, flurry/haste behavior and compact UI remain game-unrun.

Newly supplied.prev does confirm the0.3.2 observer Installed, AB/AC callback counters and10 models using
completed-rule source. Its97 newest rows are all previews, so no0.3.2 accuracy comparison is inferred.
Earlier sessions include24 defended damage records (physical2/5, energy5/10, Electricity immunity caches).
These are incomplete evidence, not proof of bypass/stacking/effective damage or complete state/RNG preservation.

## 0.4.0 — 2026-11-01 UTC

- Completed shared engine AB-without-target/weapon-stat result bridge; target AB no longer depends on simultaneous AC availability.
  Final engine totals replace active fallback contributions. Deferred attack-start contributions are copied/projected separately and marked estimated.
- Build-specific read-only static DR/resistance/immunity adapter; conditional bypass masks and enhancement thresholds, maximum nonstacking reduction,
  explicit unsupported reasons, immutable profile snapshot, row/UI/JSONL integration and effective-defense cache invalidation.
- `dotnet build` against the supplied private Managed/UMM references, net472: pass; existing unused `AttackPrediction.Charge` warning remains.
- Pure C# suite: 6699 original +138 contract +326 beta +146 new native/active/automatic-defense assertions =7309 passed.
  New cases cover PA BAB/handedness/mythic/rank math, AND/OR/unknown/material/alignment thresholds, max DR, crit-vs-DR order,
  mixed resistance/immunity, unsupported pools/physical grouping, immutable copies, pending/native duplicate avoidance, cache invalidation and logged-row equality.
- Private reflection-only probe: 33 field/method layout checks passed against the supplied Assembly-CSharp, without executing game methods.
- Separate Python mathematical oracle and C# tree-sitter syntax verification recorded with the release package checks below.
- New game hooks/activation projection/automatic defense have NOT been run in the actual game. Previous 0.3.2 user's runtime evidence does not validate these changes.
  Request current diagnostics JSONL + .prev and F6 OFF/ON/DR observations for the next runtime check.
- Release checks: independent Python509 checks/2 project XML files; tree-sitter39 production/main-test C# files.
  The separate ReferenceLayout executable also compiled and passed33 metadata checks. Package build after successful NuGet audit restore:0 warnings/0 errors.
  Installation archive allowlist/CRC/current-DLL/doc/version checks and source archive dependency/raw-log exclusion were verified before publication.

## Local cache/diagnostics fix — 2026-11-02 Asia/Seoul (unpublished)

Fixed a completed weapon-stat callback being discarded solely because a fresh AB-bearing entry existed.
Only identical damage, critical and bypass traits retain that entry; changed weapon results replace it without
inheriting the older AB. Added cache rejection status and comparison input-change categories with separate
start/display and attack-duration clocks. Comparison gates and cache TTL remain unchanged.

Executed: 6699 original +138 contracts +330 beta +152 native assertions =7319; Python509 checks;
39-file WOTR syntax parse; full net472 build with supplied private Managed/UMM references, zero errors
and one existing unused Charge-field warning. Regression cases exercise changed dice replacing stale data,
identical callback lifetime, context miss/clock regression, and structured diagnostic JSON serialization.
Repository packaging verifies six-file mod-only allowlist and ZIP CRC. No game execution or new release publication.
The logged natural-weapon AB4→6 and1d3→1d4 discrepancy is not proven resolved in-game by these tests;
new diagnostics are needed to establish the source of that case and remaining fallback/comparison exclusions.

## Physical/complete immunity fix — 2026-11-03 Asia/Seoul (unpublished)

The automatic reader previously handled AddEnergyDamageImmunity but silently ignored
AddPhysicalImmunity and CompleteDamageImmunity. It now reads the active physical masks
and combines them using all-form coverage, and includes complete immunity for supported damage types.
Unknown derived components or missing physical-form metadata produce unavailable results.
The provided private game assembly was inspected statically to establish mask aggregation/coverage
and complete-damage decline semantics; no rule handlers or game code were executed.
Private decompilation outputs remain outside Git and distribution archives.

Executed pure checks: 6699 original +138 contracts +330 beta +234 native =7401 assertions;
Python509 checks;39 WOTR syntax files. Added exhaustive three-bit physical-form mask coverage,
unknown-form refusal, physical normal/critical zero, preserved extra fire, each supported complete-immune
damage type and immunity cache-stamp invalidation. Full net472 build succeeded with the existing unused
Charge-field warning; reference metadata probe passed34 checks including the physical immunity mask field.
The new user log contains263 previews/75 attacks/46 damage records; immunity defense notes are absent.
Zero reported damage alone was not used to infer a specific immunity source or validate the fix.
Game UI, installed buffs, attack-specific immunity bypass and end-to-end damage remain unverified in cloud.

## 0.5.0 spell and AoE preview — 2026-11-04 Asia/Seoul (unpublished)

Added a selected-ability panel with baseline touch/ranged-touch hit estimates, target save-failure
probabilities from observed DC and cached saves, Persistent success reroll, and baseline caster-level
SR checks for unconditional registered resistance. Attack/save natural1/20 differs intentionally from
SR checks. Spell DC/CL are passively copied from game-owned CalculateParams completions, with bounded
freshness and context checks. Custom/conditional save DC and special projectile delivery are refused.
AoE membership is passively copied from game-owned WouldCurrentlyTarget results, with selection/aim
invalidation, expiry, ally inclusion and a32-row display cap. Hover fallback explicitly lacks membership
certification. Numeric values are conditional/partial and never presented as final debuff application rates.

Executed:7401 existing assertions plus27 spell probability/JSON/target-window assertions =7428 total;
509 independent Python checks;43 C# syntax files. Full net472 mod compiled against private supplied
references with zero errors and the existing Charge-field warning. Metadata probe passed41 checks,
including spell observer methods, cached touch AC, caster/metamagic/SR/save fields. Packaging checks
CRC, six-file mod-only allowlist, current DLL identity and version0.5.0. No game UI execution was possible.

Outstanding in-game checks: parameter observation availability while aiming; cancellation/switching
and TTL waits; touch/ray vs normal attack classification; a direct Will debuff and Reflex area save;
Persistent/conditional DC refusal; selected AoE circle/cone/line membership and ally rows; scrolling/held
panel; conditional SR/immunity and no-certification labels; postfix installation/removal and save/RNG
noninterference. No tests were disabled, no proprietary references or raw logs are packaged, and no
GitHub release/push/publication was performed.

## 0.5.1 completed damage trace — 2026-11-05 (Asia/Seoul)

Local Windows development against the user's installed game references; no game was launched or played.
.NET SDK 8.0.408 was downloaded into the task workspace. No proprietary references are packaged.

Executed successfully:
- C# Release test executable: 6,699 original + 138 contracts + 330 beta + 234 native defense + 27 spell + 19 trace = 7,447 assertions.
- Python reference_check.py: 509 independent checks and 2 project XML parses.
- Python damage_trace_analysis_test.py: 3 tests for deterministic reduction discrepancy counts and legacy compatibility.
- ReferenceLayout metadata-only executable: 52 layout checks against the local Assembly-CSharp.dll; no game methods executed.
- Full net472 Release build against local game and UMM DLLs: success, one pre-existing CS0649 warning for AttackPrediction.Charge.
  Installed UMM indirectly references a net48 Harmony build; the normal resolve rejected that reference.
  The build used -p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true to resolve the installed assemblies.
  This is compile-time evidence, not proof of in-game loader compatibility.
- syntax_check.py was attempted but tree_sitter is absent. Full C# compilation above performed the syntax/type/API check.

Core commands (SDK executable located outside the repository under task work/dotnet):
```
dotnet run --project CombatPreview/tests/CombatPreview.Tests.csproj -c Release
dotnet run --project CombatPreview/tests/ReferenceLayout/ReferenceLayout.csproj -c Release -- PRIVATE_MANAGED_PATH
dotnet build CombatPreview/CombatPreview.csproj -c Release -p:GameDir=PRIVATE_GAME_PATH -p:ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true
python CombatPreview/tests/reference_check.py
python CombatPreview/tests/damage_trace_analysis_test.py
```

Static inspection of the local game IL confirmed DamageValue.Reduction and FinalValue only read existing
source values and perform arithmetic. The trace avoids BaseDamage.Decline, whose getter updates cached state.
The actual source Type virtual getter is also not invoked for unknown damage kinds.
No prediction rule, RNG, ApplyDamageReduction, pool spending, game state update or new Harmony patch is introduced.

Outstanding: actual 0.5.1 callback/JSONL/loader verification and controlled game cases (DR, incorporeal decline,
saving throw half, resistance, immunity, critical, redirection, caps). The preview expected-value model has
not been fixed or certified by this diagnostic change. Static replay uses actual post-multiplier damage,
so a match cannot certify the preview's earlier multipliers or immunity handling.

## 0.5.2 UI placement — 2026-11-05 (Asia/Seoul)

The left anchor now follows the top of active party portrait RectTransforms (UnitPortraitPartView / PartyCharacterPCView),
with a 16 logical pixel gap; fallback portrait boundary is 78% of screen height. Height is bounded above this region.
Layout migration version 3 selects the lower-left anchor and width 820. Dragging still disables automatic anchoring.
AB and target AC are always visible beside hit chance and dice; details show additional provenance as before.
Body/numeric text increased 14 to 16, table rows 13 to 15, title 16 to 18, muted labels 12 to 13.
The preceding 0.5.1 damage trace is included unchanged.

Full net472 game-reference build passed (same reference-resolution override as 0.5.1); C# 7,449 assertions passed,
including portrait clearance and small-screen placement. Existing unused-field warnings remain.
Actual game rendering, Korean font fallback and live portrait bounds need in-game confirmation.
No game session was started and the installed mod was not changed.

## 0.5.3 UI screenshot feedback — 2026-11-05 (Asia/Seoul)

The supplied 0.5.2 screenshot showed the panel above portraits but overlapping the taller action bar.
Left placement now uses the earliest of portrait top, action-bar top and a conservative 70%-screen boundary,
with existing clearance and height bounds. Layout migration 4 restores automatic left anchoring once.
Actor/weapon names occupy their own wrapping full-width heading, while each attack row contains only its index.
Hit chance has 14% of the usable width and a 19px centered bold style (previously 9%, 16px).
Labels reset inherited fixedHeight, add vertical padding and use measured minimum heights to accommodate Korean glyphs.
Actual installed-game-reference net472 build passed with the previously documented reference-resolution override.
The game was not launched; live font rendering and UI clearance remain user-verifiable.

## 0.5.4 lower-left pocket layout — 2026-11-05 (Asia/Seoul)

Hit chance is now the rightmost column, retaining 19px bold emphasis. The automatic left anchor is constrained
horizontally before the first party portrait's left boundary (with a conservative 30%-screen fallback and margin).
This allows the panel to occupy the empty space left of the action bar, above the lower-left fist controls,
instead of raising it above the entire action bar. Bottom clearance uses portrait top and an 88%-height cap.
Layout migration 5 restores the automatic anchor once. Existing trace functionality is preserved.
Full local game-reference net472 compile and installation ZIP integrity checks passed.
No in-game rendering or exact fist-control geometry has been observed by this build; the supplied screenshot
informs the conservative fallback boundaries. Live verification remains required.

## 0.5.5 flush-left and HP projection — 2026-11-05 (Asia/Seoul)

Left anchor x is zero. Horizontal portrait clearance is increased to 52 logical pixels, replacing 36,
and layout migration 6 restores automatic placement once. A 26px HP bar sits below the target heading.
The HP snapshot is read alongside the displayed attack snapshot and remains held with that snapshot.
Green represents current HP minus the visual mean-damage segment, orange is min(current HP, expected damage),
and the dark region is missing HP. This is a mean-damage illustration, not a confidence interval,
kill probability, or mathematically exact E[remaining HP]. All displayed attacks need available after-defense
expected values; manual scenarios and temporary HP suppress the projection with an explicit reason.
No raw damage fallback is silently used. Header HP numbers remain visible even when projection is unavailable.
Full local game-reference build passed. C# 7,454 assertions passed, including HP clipping, unknown expected
values, temporary HP suppression, and invalid max HP. ZIP allowlist/CRC/version/DLL checks passed.
Actual game rendering of the bar and border clearance remain unverified.

## 0.5.6 hover CPU audit — 2026-11-05 (Asia/Seoul)

Static hot-path findings (not measured FPS): the completed AB observer performed full buff/defense captures
before excluding iterative weapon penalties; State/PairStamp/CountStamp rebuilt and sorted identical fact
signatures repeatedly inside a single read; Field metadata lookup repeated for each scalar; Update scanned all
scene units every frame; dice strings were rebuilt for every row on IMGUI passes; the blocker RectTransform
was assigned on every pass even when unchanged. These are plausible contributors, not a proven sole cause.

Changes: filter excluded non-natural iterative AB before Capture; cache FieldInfo metadata (not live values),
reuse fact-state signatures only inside a single synchronous read scope, isolate nested scopes and discard on
exit/exception, prefer hover events with a 100ms fallback scan, cache dice text with its immutable snapshot,
and avoid unchanged blocker transforms. No cached state survives callbacks/frames; existing observed-value
TTL and 200ms panel refresh are unchanged. Damage/UI formulas and layout migration are unchanged.

UMM settings display per-stage call count/mean/max milliseconds and allow reset or explicit local log output.
Stages include preview-update, capture-input, panel-OnGUI, AfterAB/AC/BaseAB/Weapon/Count, game-estimate,
preview-log and damage-diagnostics. Scopes overlap and must not be summed. IMGUI can run multiple times/frame.
Counters are bounded by fixed stage names and do not generate automatic per-frame disk writes.

Full installed-game-reference net472 build passed with the prior documented UMM reference-resolution option.
C# tests: 7,461 assertions, including scoped read lifetime, nested isolation/restoration, exception cleanup and
no cached state in a later callback. No gameplay/FPS/GC profiler run occurred. Performance improvement and
actual callback timing require user game measurements; this build supplies those counters.

## 0.5.7 visible DR and native enemy HP ruler — 2026-11-05 (Asia/Seoul)

Each weapon heading now includes target physical DR candidate evidence (including bypass) and the effective
physical DR for that weapon. Unknown/incomplete/pool defenses remain explicitly unknown rather than zero.
Native HP integration reads the existing PC OvertipController.Unit, m_OvertipStandartHitPoints, m_HitsBar,
and IsShowed fields. Binding scans are limited to every 500ms while no target host has been found, and stop
once bound. A mod-owned, raycast-disabled Image child occupies the lower 35% of that HP image rectangle.
It represents a LINEAR HP ruler from mean-predicted remaining HP to current HP. The game's original HP fill
uses an animation correction curve and shader, so it is not replaced or represented as pixel-identical to our ruler.
No game HP, original Image fill/material/color, controller state, or damage rule is changed.
Cursor departure, held snapshots, spell selection, toggling, pooling target mismatch and hidden native bars
hide the ruler. Owned objects are destroyed on owner destruction or host replacement. The existing panel HP
bar remains available; native binding failure does not disable the preview. Unknown defense/temporary HP/manual
scenarios suppress the native damage segment. The new native-HP-overlay stage is included in performance counters.

Local game-reference net472 compile passed. C# 7,464 assertions and 56 metadata layout checks passed.
The native host's fields and correction-curve implementation were inspected from local DLL metadata/IL.
Actual native UI attachment/rendering, masks, pooled lifecycle and FPS cost have NOT been verified in-game.

## 0.5.8 surprise/remaining-action policy — 2026-11-05 (Asia/Seoul)

Static game IL review showed ActionsState.IsAttack checks predicted WillBeUsed on Move/Standard; it does not
answer whether an unused standard attack is possible. This was previously used as an absolute availability gate.
New data-only policy reads existing current/predicted attack activity, surprise flags, cached target identity,
range/approach geometry, prediction-pending and movement distance. No command, pathfinder, rule event, action
consumption, or game prediction update is invoked. Actor/rider/mount state sources remain separate.

Ordinary surprise attacks use one primary attack; UI full-attack selection cannot create iterative/natural extras.
A currently available standard action with matching, fresh cached in-range geometry can produce a labelled single-
attack estimate even when the cursor has not marked an attack WillBeUsed. Movement plus ordinary attack in a
surprise round is excluded. Ordinary rounds can use a remaining standard single attack after movement. Engine
selected special attack modes (e.g. SpellCombat) remain unsupported instead of being recast as normal attacks.
Unknown/mismatched/pending state is not zero damage: it remains a waiting/unknown item. Known blocked actions
are labelled excluded and removed from expected/raw totals and both HP projections. If all are blocked the total
is zero with a no-executable-weapon-attack label. Unknown items still prevent a misleading complete total.

Action state is included in the preview condition signature. A deduplicated action-plan JSONL record captures
availability, exclusion, full/single mode, reason and the input state stamp. Stamp order is defined by
AttackActionInput.Stamp: Known, Surprise, Overused, OutOfRange, TargetMatches, PredictionsPending,
PredictedAttack, RequestedFull, MovementRequired, RangeKnown, InCurrentRange, StandardCurrent,
StandardPredicted, MoveCurrent, MovePredicted, PlannedType. ActivityState values: Available=0, WillBeUsed=1,
WillBeLost=2, Unable=3, Lost=4, Used=5. PlannedType follows game UsageType (SingleAttack=2, FullAttack=3).

Full installed-game-reference net472 build passed with the prior documented reference-resolution override.
C# 7,480 assertions passed, including 16 action cases and a BAB16 surprise single-attack regression.
66 metadata-only layout checks passed against local game DLLs. Actual surprise-round gameplay, mounted action
exceptions, reach/LOS/path edge cases and temporary action-state timing remain unverified in-game.
Reduced charge and special action abilities are not generalized from the ordinary surprise weapon policy.

## 0.5.9 initial-hover UI breakdown — 2026-11-05 (Asia/Seoul)

The installed user's log was still 0.5.7. Its newest summary reports panel-OnGUI max 32.987ms vs capture-input
max 1.514ms and native-HP-overlay max 0.001ms. The broad timer establishes an outlier inside the UI call,
not its source or exact relationship to first hover, FPS, or another mod's work. No matching runtime exception
was found in the inspected UMM summary.

Replaced repeated string/assembly type resolution for party portrait bounds with supported-build typeof references.
HUD-bound search is warmed by Update before enemy hover while the mod is active; the same throttle remains.
This moves that preparation off first draw, not off the main thread or out of the overall CPU budget.
Added ui-style-init, ui-portrait-search, ui-window-layout/repaint/input, ui-content-layout/repaint/input and
ui-input-blocker timers. Each maximum records the game frame and Gen0 collection-count delta so nested
outliers can be correlated. This does not measure native code/GPU time independently or prove a GC cause.
Includes all 0.5.8 action-policy changes. Full installed-game-reference net472 build passed with the documented
reference-resolution override. The 0.5.8 C# regression suite (7,480 assertions) passed again. Live initial-hover
and FPS validation remain outstanding; this is not claimed to eliminate the 33ms spike.

## 0.5.10 measured HUD scan and UI allocations — 2026-11-08 (Asia/Seoul)

Read the user's latest UMM log (modified 2026-11-08 05:27:44 KST); loaded version 0.5.9.
Observed ui-portrait-search: 2,791 calls, mean 11.855ms, max 19.991ms, GC0 at max 0.
The existing implementation rescanned all portrait components every second even with valid cached transforms.
AfterAB max 246.985ms, AfterCount 234.078ms, AfterAC 221.640ms, content-layout 226.500ms,
content-repaint 224.770ms and capture-input 188.956ms all reported GC0 at max 1, on different frames.
These are GC-coincident elapsed-time stalls, not exclusive CPU attribution or proof that this mod alone caused GC.
The plain panel-OnGUI wrapper max was 12.985ms; Unity may dispatch window content callbacks separately from
that wrapper, so callback and parent maxima must not be treated as a simple nested call-tree accounting.
BubbleBuffs repeatedly logged NullReferenceException and WrathPatches logged a Harmony patch failure.
No unrelated mod files/settings were changed; their contribution cannot be quantified from this log.

Changes:
- Reuse valid portrait transforms indefinitely; rebind when invalid or resolution changes, with a 10-second
  backoff when no HUD exists. Cached top/left geometry is still read live, preserving movement/layout tracking.
  No periodic whole-scene search is performed while the cached transforms remain valid.
- Bounded (512 entries) value/style/width cell cache reuses GUIContent, layout-option arrays and measured heights.
- Bounded (256 rows) immutable-row presentation cache reuses formatted hit/AB/AC/mean/expected strings.
  Manual scenarios bypass row-text caching to avoid stale scenario results; cell keys include actual text/width/style.
- Identity token strings are created once per object; live object state is still re-read. Removed a per-fact
  capturing delegate from state signatures. No TTL extension or cross-callback live-state cache was introduced.
- Performance output now distinguishes maxNoGC and GC intervals from GC-coincident maxima.

Full installed-game-reference net472 build passed with the previously documented resolution override.
Existing 7,480 C# assertions passed. Package CRC/version/allowlist/exact DLL validation passed.
Actual 0.5.10 FPS, GC frequency, cache invalidation at scene/HUD changes and Korean layout need in-game verification.

## 2026-11-08 — 0.5.11 주문 명중/내성/SR 진단
설치된 0.5.9 로그에서 산성 분사 전달 무기 확인 실패 후 후속 확률 표시, 악의 눈 DC 대기를 확인했습니다. 주문 67건은 기존 설명 문자열 중심이었으며 무기 비교 216건은 모두 상태/시간/모드/대상 변경으로 제외되었습니다. 비교 제외 규칙을 완화하지 않았습니다.
공격·적 내성 실패·SR 확률을 분리하고 출처 및 불가 사유, 실제 완료된 판정 수치를 추가했습니다. 이미 로드된 전달 무기 조회와 선택된 접촉 주문의 게임 관측 수치 활용을 추가했습니다. 특수 조건부 DC·재굴림·은폐 등 전체 주문 효과를 보장하지 않습니다.
검증: 게임 참조 DLL로 Release 빌드, C# 회귀 테스트, 제공 DLL 메타데이터 필드 74개 검사, Python 분석기 테스트. 게임 코드는 테스트에서 실행하지 않았으며 실제 UI·명중률·성능은 재측정이 필요합니다. 로컬 UMM 참조 호환 빌드 옵션 ResolveAssemblyReferenceIgnoreTargetFrameworkAttributeVersionMismatch=true 사용.

## 2026-11-09 — 0.5.12
실제 0.5.11 로그 5,105건(현재/회전 합산) 중 주문 미리보기 133건: 기름막 56, 반짝이가루 43. 직접 내성 미발견은 총 114건. 새 구현은 장판/버프의 로드된 효과 참조를 깊이 10/예산 256/방문 집합으로 탐색하고 후속 내성 후보를 따로 표시합니다. 조건부/버프 전용 DC는 수치 확정을 피합니다. 동일 주문의 범위 대상 사이에는 정적 효과 탐색 결과만 100ms 공유하며 대상 능력치/DC는 공유하지 않습니다. 실제 출처 추적은 원인 버프의 기존 컨텍스트도 확인합니다.
이번 UMM 로그에 성능 요약은 없으므로 0.5.9 대비 FPS/초상화 검색 개선 여부를 확인할 수 없었습니다. 60초마다 실제 프레임 평균 FPS·최대 프레임·33/100ms 초과 횟수와 구간 시간을 자동 기록하도록 보강했습니다. 전체 게임 프레임 수치는 다른 모드/로딩/메뉴 영향을 포함하며 구간 시간은 중첩됩니다.
검증: Release 빌드, C# 회귀 검증, 참조 DLL 필드 검사, Python 분석기 테스트, ZIP CRC/버전/DLL 일치. 실제 주문 데이터의 후속 판정 및 새 성능 수집은 게임 실행 검증이 필요합니다.

## 2026-11-09 — 0.5.13 HCO 참조 진단
설치된 HitChanceOverlay 0.4.0 메타데이터/IL에서 OddsCalculator.ForWeapon/ForAbility/CalculateAttack, RulePreview.SavingThrow/SpellPenetration의 서명과 반환 필드를 확인했습니다. 정확한 MVID로 어댑터를 제한합니다. 최초 로딩 순서 차이는 최대 10초 간격의 설치 재시도로 처리합니다.
일반 무기의 첫 공격 및 주문 판정 원시 값/표시 문구를 기존 미리보기 로그에 첨부합니다. 조건·유효시간·무기/공격 순서가 맞지 않거나 판정 분기가 여러 개인 경우 수치 차이를 정확도 오류로 판정하지 않습니다. 아직 모든 HCO 효과/면역/조건부 규칙을 자체 엔진으로 재구현하지 않았습니다. 이 버전은 누락/오차 원인을 식별하는 비교 진단을 보강합니다.
검증: Release 빌드, 기존 회귀 검증 및 참조 매칭 경계/로그 직렬화 검증 11개 추가, Python 분석기의 비교 제외 검증 추가, 게임 참조 필드 81개 검사. HCO 런타임 패치와 실제 수치 수집은 게임에서 재검증해야 합니다. HCO 일치율이나 FPS 향상은 아직 측정하지 않았습니다.

## 2026-11-10 — 0.5.14
제공 게임 DLL의 ContextRankConfig.ApplyProgression/ApplyMinMax 및 AbilityDeliverProjectile의 전달 반복 IL을 조사해 CL 증가식/상한/발사체 제한 규칙을 확인했습니다. 임의 실행 없이 기본 피해 주사위, 발사체/고정 반복, 피해 유형 및 기본 평균을 표시하고 로그에 구조화했습니다.
회귀 검증에 피해/성장식 18개를 추가했습니다: CL 상한, 매 2레벨 발사체 증가, 레벨 7/11 광선 증가, 배열 길이 제한, 미확인 타수, 5D6 평균 및 광선/발사체 기본 합계 등. 예제는 기본 수학 검증이며 실제 해당 주문의 런타임 Blueprint 추출을 인증하지 않습니다. 제공 게임 메타데이터 필드 검사를 확장했습니다. Release 빌드 및 패키지 CRC/DLL 일치 검증을 수행하며 게임 내 결과는 재측정이 필요합니다. 특수 랭크 보너스/상한 해제/조건부 반복/메타매직 최종 피해/방어 후 최종 기댓값은 이번 정적 기본식 범위 밖입니다.
## 2026-11-20 — 0.5.39 공통 주문 판독

제공 게임 참조로 Release 빌드 성공. 수학 6699, 계약 138, 베타 359, 네이티브 방어 243, 주문 109, 공동 피해 분포 169, 피해 추적 26, 행동 가용성 16개 검증 통과. 정의의 번개에서 추출한 27개 액션의 구조로 깊이 4 탐색 실패와 공통 깊이 12 탐색 완료를 재현했습니다. 기본 피해와 독립적인 미확인 후속 내성, 공유 굴림의 상관관계, 여러 성분의 임시 HP 소모, 거울상, 큰 HP 대상의 계산 범위 축소를 검증했습니다. 서로 다른 무기·관통 무기 사이의 거울상 및 단일 물리 보호막 소모도 전수 계산 가능한 사례로 검증했습니다.

메타데이터 필드 117개와 Python 피해 로그 분석기 테스트 6개 통과. 이는 Unity UI·Harmony 패치의 게임 실행 검증이 아닙니다. 현재 설치 및 새 로그는 0.5.36이며, 0.5.39를 실제 게임에서 실행한 정확도/FPS 결과는 없습니다. 미지원 항목은 OPEN_ITEMS.ko.md, 정의의 번개 누락 근거는 SPELL_COMMON_PIPELINE.ko.md에 구분했습니다.
## 2026-11-21 — 0.5.44

Release built successfully against the supplied WotR 2.7.0x / MVID 90a9869c-2792-4c7b-bfb7-5a8b33da7c82 references. Pure C# suite: 9,958 assertions plus the real 27-action topology fixture; this includes 678 active reroll/readiness assertions and 798 buff transition/identity assertions. The reroll oracle independently enumerates dice for attack, save and SR semantics, conditional first-roll tests, persistent saves, post-roll signed-count changes, fixed overrides and natural-one transformation. Unknown consuming effects cannot advance a subsequent weapon's mirror/pool distribution. Rank replacement tests check competing typed modifiers, live penalties and snapshot independence. Supplied-game metadata layout: 158 checks. Python diagnostic analyzer: 9 tests. git diff --check passed (line-ending notices only).

These checks compile/read metadata and evaluate detached models; they do not execute game handlers or validate a running game. The installed Info.json was rechecked and still reads 0.5.42. The 0.5.44 UI, first-hover latency/FPS, read-only DC/CL against actual game events, new bonus source adapters and descriptor/rank changes require new runtime evidence. The frozen 0.5.42 logs establish the original symptoms, not validation of the replacement DLL. HCO remains an optional comparison with different probability semantics, not a numeric source or a correctness oracle.
