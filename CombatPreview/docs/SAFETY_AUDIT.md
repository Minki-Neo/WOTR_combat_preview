> 0.5.45 extends detached buff context descriptors, Replace/Rank lifetime transitions and supported AddContextStatBonus values. It reads the stored RestrictionCalculator/PropertyCalculator shape but does not invoke their predicates, ContextValue.Calculate or lifecycle methods. Unknown dependencies remain unavailable. Real save diagnostics copy the engine's captured modifier list only. Pure numeric rank/stat changes never write back to live facts or modifiers.

> 0.5.44 reads active reroll/parameter/damage settings and completed dice/modifier records; distribution evaluation uses detached numbers and never RNG or a component handler. Parameter fallback excludes unknown/special sources, and only audited dependencies may omit geometry from the parameter key. Synchronous memoization ends with the callback. Rank changes and descriptor removals modify detached ledgers/modifiers only. No buff, action, rule, resource or protection is executed or changed. The optional allocation counter does not force GC. HCO output remains comparison-only; its observed numbers never populate the independent model.

> 0.5.42 reads subscribed swarm/critical-immunity components and existing attacker facts, weapon category/form and delivery components. It does not invoke swarm event handlers, add damage declines or modify game damage bundles. Vulnerability is part of the detached distribution/cache input. The target's displayed size/name is not used to infer swarm rules; HCO remains comparison-only.

> 0.5.40 projects supported buff stat/condition changes into detached value collections. It never applies a buff, executes its actions/handlers or writes a live modifier/pool. Existing stat caches, loaded mechanics buff references and registered DR order are read. Missing definitions or unsupported components invalidate the dependent projection. Protected distribution memoization retains only numeric/string keys and detached numerical results, bounded to 32 entries. HCO results remain comparison-only inputs.

> 0.3.3 adds a third explicit completed-rule postfix for attack counts. Legacy replay remains blocked; see the final section for current grip/count/defense boundaries.

> 0.3.2 current policy: legacy replay/suppression patches stay blocked. Two opt-in, explicitly installed postfix observers now read completed AB/AC rules. Historical “no Harmony patches” statements below describe earlier versions. See the final section.

> Current 0.3.0: legacy replay remains blocked. A separate opt-in read-only basic-weapon partial model is described in the final section. Earlier blanket hover-block statements describe 0.2.x.

# State mutation audit — 0.2.1

Date: 2026-11-01 (Asia/Seoul). Static inspection and compiled source only; no in-game preservation claim.
Baseline: original 0.2.0 ZIP SHA-256 `4ad6fa8d567883140588f28c985ae341d6e79a28baf5781d376696ee3176c4c7`.

## Findings and policy

0.2.0 is not a read-only sandbox. Before-event handlers are arbitrary code. A new rule object or a new
rule context still refers to live units, equipment, facts, buffs and component runtimes. Unity RNG restoration
cannot restore System.Random, Owlcat/mod RNGs, pools, action state, resource counters or arbitrary handler state.
The correct fallback is unavailable, not an opt-in assertion of safety.

0.2.1 permanently rejects the legacy path. Main does not install any Harmony patches or subscribe the legacy
AttackCheck/EarlyPredictions/SpellChecks. Settings.Logging is always false. Compatibility API presence cannot
set CanPreview true because PredictionSafety.CanReplayEvents is false. Hover series, AttackChance.Predict,
ManeuverChance.Predict and SpellChance entry points reject before creating new game rules. DryRun.Run,
AtEnd and Rerolls reject without touching context, callbacks, temporary modifiers or RNG. No settings or
version-string bypass exists. Dormant legacy helper code remains for provenance and investigation.

| Path | Input / live references | Possible mutations in 0.2.0 | Recovery evidence | 0.2.1 policy |
|---|---|---|---|---|
| Hover → EnumerateFullAttack → rule constructors | selected unit, hands, target | iterator/constructor behavior not certified | none | gate before enumeration |
| DryRun.Run → OnEventAboutToTrigger | rule + live units/facts | arbitrary handlers, toggles, pools, RNG | partial known-handler cleanup only | unconditional rejection |
| Rulebook.Trigger(weapon stats/AC/AB/parry/CMB/CMD) | live context and stats | full event lifecycle, arbitrary mod callbacks | isolated history != isolated game state | unreachable through registered entry points |
| WeaponAttackStart allowlisted handlers | shared target fact runtimes | temporary stat/runtime changes | cleanup registered after handler calls; partial-throw risk | never replay |
| shallow/copy rules, CopyModifiersFrom, new AbilityData | existing runtime object references | aliasing, context-dependent caches | deep cloning never established | copy != proof; legacy entries blocked |
| OnAttackBonus/context values/ability params | live mechanic contexts | virtual evaluators and conditional game calls | none for arbitrary component | no live preview execution |
| old cleanup loop/OnDidTrigger/context pop | partially initialized nested context | cleanup throws, shared state overwrite | original outer try began after context/depth setup; callbacks may throw before cleanup registration | no legacy scope entered; no claimed rollback proof |
| old d20 interception | ModifyD20/fact context | evaluator calls; some rolls suppressed | Unity-only restoration | patches never installed; pure math rejects >8 modifiers |
| patch installation | global method dispatch | incomplete PatchAll installation/rollback | runtime not exercised | no PatchAll at all in current startup |
| defense AllSources getter | UnitPartDamageReduction | calls TryInitialize, appends chunks and calculates values | confirmed in supplied DLL analysis | forbidden for capture |
| ApplyDamageReduction/SpendPool/Cleanup/EstimateDamage/Bypassed | live damage, pool, virtual DR methods | consumes pools, updates reduction state, may remove buffs | not reversible generally | forbidden for capture/prediction |
| VanillaDefenseCapture | existing part, raw private fields/list entries | no writes/callbacks in our code; Get<T>/runtime access still needs game instrumentation | field-layout static inspection + full compilation | evidence-only, incomplete snapshot; never effective damage |
| passive rule handlers | already-completed RuleAttackRoll/RuleDealDamage | our code copies scalars; no extra rule/dice | actual runtime bus ordering/interop untested | opt-in diagnostic only; errors disable it |
| logging | local JSONL and ordinary mod log | local filesystem writes | recorder handles sink faults/reentrancy, byte rotation | no network, bounded diagnostic files |

## Failure and nested-call checks

Pure tests verify repeated legacy permission rejection, recorder reentrancy, duplicate events, disabled mode,
clock regression, missing/changed conditions, write failure, and bounded log rotation. They do NOT execute game
contexts, cleanup callbacks, Harmony installation or Unity lifecycle. Since no legacy scopes/patches are entered,
mocked cleanup tests would not validate the game. Runtime exception/partial-registration, nested attack events,
UMM disable/enable, load/scene changes and state/RNG equivalence remain required acceptance tests.
Main attempts EventBus.Unsubscribe if its passive subscription fails part-way. Unsubscribe failure is logged and
Settings.Enabled remains false. No subscription rollback has been verified in the game.

## Evidence needed to enable hover again

Implement a predictor operating on fully copied immutable observations, with an audited getter/call graph.
No permission flag should enable event replay. Pin the actual game/mod configuration and inspect every remaining
getter/iterator/evaluator. Instrument HP/temp HP, buff duration/rank, resources/toggles, actions, pools, runtime
facts, context/history and ALL RNGs; compare identical-save OFF/ON paths and subsequent actual die sequences.
Test 1,000 hover changes, nested events, injected exceptions, scene/load transitions and each active mod.
API compilation and the fixture suite are prerequisites, not evidence that these tests passed.

## 0.2.2 follow-up

The user's 0.2.1 log confirms a limited run of observation callbacks, not noninterference. The updated capture reads
UsePool directly from the blueprint field reached via SourceBlueprintComponent's backing field, avoiding its getters.
A positive m_RemainPool is no longer evidence of a pool, since non-pool components initialize it too. IsFake rolls
and damage are excluded; unassociated rolls are labeled separately and cannot consume a prediction. Session IDs use
OS Guid generation, never Unity/game RNG. Legacy prediction replay remains permanently blocked. Runtime metadata is
copied on observer creation and included on each line. No new game rules, evaluator calls or state restoration were added.

## 0.3.0 basic-weapon beta read graph

The new path does not call the legacy predictor. Compatibility requires game assembly MVID
`90a9869c-2792-4c7b-bfb7-5a8b33da7c82` and no detected TabletopTweaks assembly.
No Harmony patches are installed. No Rulebook.Trigger, new gameplay rules/dice, event replay, damage application,
virtual defense evaluator, pool spending, cleanup, or RNG restoration is used by this path.

Static inspection of the supplied DLL established these routes:

- Selected-unit/turn cached action state and existing hover/highlight determine whether to show the panel.
  `GetActionsStates` and `GetEnabledFullAttack` read stored turn state; they do not enumerate attacks.
- Read `ModifiableValue.<ModifiedValue>k__BackingField` for BAB, ability, extra AB/damage, AC, maximum HP
  and temporary HP. Current HP is max(0, cached maximum minus UnitDescriptor.m_Damage), displayed separately.
  HP reads never apply healing/damage or invoke a health rule. A missing maximum stays unavailable.
- Current primary slot uses existing ItemSlot.m_ItemRef → EntityRef.m_Proxy → Proxy.Entity backing field.
  Do not call MaybeWeapon/EmptyHandWeapon (can create an unarmed weapon), or EntityRef.Entity (can refresh proxy).
  Fake slots/items being removed are rejected.
- Existing item blueprint field and preloaded type reference only. Check BlueprintReferenceBase.Cached backing
  field before type-dependent getters, avoiding the lazy GetBlueprint load/cache write when data is absent.
- Copy base weapon scalars to immutable BetaInput. Position, HP damage and sorted fact identity/Active plus
  known scalars form a limited fingerprint. This is not a complete game-state or rule condition revision.
- UnitAttack.EnumerateFullAttack triggers attack-count/limb rules in the supplied game; it is deliberately
  not called. The pure model builds only BAB iteratives, and labels extra attacks/conditional corrections unsupported.
- A subscribed before-event hook reads inputs at the start of an actual RuleAttackWithWeapon's handler phase.
  The passive actual-roll observer consumes that association and reads completed roll/weapon-stat scalars.
  Unmodeled/changed/expired input produces a skip status. Root completion cleans abandoned pending entries.

Only a model actually painted by IMGUI is marked displayed. Association is bounded by display age (2s),
completion age (10s), known input identity, and 32 pending events. Comparison never certifies full conditions:
`fullConditionsVerified=false`. Only deterministic AB/AC/dice/bonus/component differences are logged as
partial-model differences, not proven defects. A single damage result is never tested against expected damage.
Actual DR/resistance/pool mitigation remains unavailable. Local logs and optional throttled toast are owned state.

Pure tests, syntax parsing and net472 compilation passed; these do not execute Unity lifecycle, render the UI,
verify callback ordering in the live mod configuration, or prove complete state/RNG preservation.
The actual 0.3.0 opt-in UI, HP refresh, comparison association, transitions and noninterference remain unrun in game.

## 0.3.1 UI and observed estimate copies

PanelPresentation is pure formatting/placement arithmetic. Active PredictionPanelPCView/ActionBarPCView
RectTransform world corners and Canvas camera determine placement; no native UI objects or transforms are
modified. A screen-relative anchor is the fallback. Original procedural textures/styles are owned by the mod,
with GUI matrix/color restored after drawing and texture cleanup on destruction. No game/third-party art is bundled.

An optional passive copy reads an already completed externally created IsFake RuleAttackRoll (game or another mod) for the selected primary
weapon, zero attack penalty and current turn. It never constructs/triggers/replays the event or calls CreateDamage.
The completed rule's cached AB/AC and DamageDescription scalars are copied. ModifiedDiceFormula reads the last
stored formula modification; IL inspection found no evaluator/state update there. EvaluatedBonus is refused rather
than executed. DamageDescription.Bonus already contains its stored modifiers (CreateDamage subtracts their total
before reapplying them; inspected only, never invoked). First component uses base critical scaling; further components
are modeled nonmultiplying. Critical overrides, extra attacks, concealment and true mitigation remain unverified.

The copied estimate has no game-object references. Reuse requires matching known-input fingerprint, actor/target/
weapon/turn/mode and age <=2s. Expiry, changed input or absent events falls back to the basic model. Invalidation
clears the cache. This limited fingerprint still does not certify every condition, and the observed estimate is never
counted as an actual hit/damage or a successful comparison. All beta records keep fullConditionsVerified=false.
Native callback ordering and the new UI have not been executed in the game; static inspection/build/tests do not
prove complete state/RNG noninterference. Existing legacy replay remains blocked.

## 0.3.2 mount graph, retained UI, completed-rule observers

Existing UnitPartRider.m_SaddledUnitRef / UnitPartSaddled.m_RiderRef are resolved through raw
EntityPartRef.m_Ref → EntityRef.m_Proxy → cached Proxy.Entity. The reciprocal live relationship is required;
no EntityRef.Entity refresh, Ensure, mount action, pet creation or evaluator is called. Existing primary item
can fall back to UnitBody.m_EmptyHandWeapon (raw field only); already existing natural secondary/additional
limb slots are copied. Polymorph list selection uses its stored flag. Type-reference Cached is checked before
natural/type getters. Natural slots get one model row, never manufactured BAB iteratives.
Turn m_RiderActionState/m_MountActionState and m_AttackMode/m_MountAttackMode are read directly to avoid
GetRider/GetMount entity-reference getter refresh during UI reads.

PassiveRuleResults explicitly patches only RuleCalculateAttackBonus.OnTrigger and RuleCalculateAC.OnTrigger
with Priority.Last postfix observers under owner CombatPreview.PassiveRuleResults. They do not change args,
Result, original execution, contexts, rules, RNG or cleanup. No PatchAll or legacy patch class is installed.
This covers completed rules even if another caller directly invokes OnTrigger without fake-roll bus events.
Installation attempts owner-only rollback on failure. Preview/observed-source/mod disable removes that owner's
postfixes; partial startup cleanup also attempts removal. Callback failures stop the observer and are caught.
These lifecycle paths are compiled but not exercised inside the game.

Result copies require selected actor or reciprocal mount, weapon, target, turn, position/HP/fact state and
basic ability/BAB identity, with age<=1.5s. Current extra AB/AC fields are deliberately excluded from that
LIMITED native key because real rule scopes may temporarily change them and clean up after OnTrigger.
This does not certify unchanged buffs/ranks, modifiers, actions or every condition; every record remains
fullConditionsVerified=false. Critical AC results and nonordinary attack types are excluded. Manufactured
iterative penalties are not learned as a first attack. Natural results stay per weapon, including global
nonzero attack indices. Displaying mount data does not replace the rider's tracker entry. No new game rule
is ever constructed or triggered by these observers; they do not create a prediction on their own.

F6 toggles details; F7 pins a known target. Pointer travel has a 1.2s grace; over-panel/pin can retain the
panel only for the same attacker/turn and a living visible existing target. Away from target, stored UI
is explicitly labeled held and is not republished as a fresh prediction. Return to target refreshes.
An owned transparent Canvas/Image/GraphicRaycaster blocks underlying world clicks within the panel.
No new EventSystem is created. Only owned objects/styles/textures are changed/destroyed.
In-game interaction, action eligibility, patch ordering and all-state/all-RNG noninterference remain unrun.

Selecting either member can show its reciprocal counterpart. Action-state choice follows the existing
GetActionsStates rider-vs-mount branch with cached references; full-mode reads also respect selected member
and the original noncontrollable/nonselected-rider fallback. The native result key includes attack mode.

## 0.3.3 grip, known extra attacks and compact UI

EquipmentPreviewReader reads current HandsEquipmentSet.m_GripType and existing primary/secondary cached item
references. Explicit One/TwoHanded is honored. Ordinary Auto checks an empty secondary slot, already loaded
CanTakeTwoHands blueprint flags, FighterOnHandedGrip and existing Magus.SpellCombat state. It does not call
GetPreferredAutoGripType/MaybeWeapon/IsGripAllowed or mutate equipment. Positive strength is multiplied1.5
and truncated; penalties are not multiplied. Ranged/natural fallback limitations remain partial.

For an active unit fact, only already-instantiated subscribed component fields are read. Known
BuffExtraAttackWeaponSpecific range filters and constant BuffExtraAttack Number/Haste/Penalized fields
are interpreted into copied penalty arrays. No component handler, ContextValue, delegate/evaluator, fact
initialization or attack enumeration is invoked. Effect labels come from the already-held fact blueprint.
Unknown component classes are not assumed zero certified extras. Every report remains incomplete.

The third owner-scoped postfix observes existing RuleCalculateAttacksCount.OnTrigger results. It refuses
forced-primary/filter/forced-natural contexts, checks a loaded manufactured primary weapon and bounded
MainAttacks/PenalizedAttacks/Flawless fields, and stores a copied pattern for<=1.5s with actor/weapon/turn/mode/
position/HP/fact/BAB identity. Native patterns override the known-component fallback; they are never added
on top of it. Natural per-slot modeling remains separate. Arrays are defensively copied and capped at24rows.
No rule is constructed or triggered. Installation rollback/removal remains owner-only and unverified in game.

Static energy type and physical bypass flags are copied into defense evidence; cached reduction and registered
immunity are not promoted into a complete effective-defense model. No ByPassed/Apply/CalcValue/SpendPool or
lazy weapon-type blueprint resolution is called. Geometry is compact/left-anchored in owned UI only; F6/F7 and
held-model freshness rules remain. New grip/count adapter correctness and layout need user-game validation.

## 0.4.0 shared engine output and scoped automatic defense

Added explicit read-only postfixes for completed `RuleCalculateAttackBonusWithoutTarget` and `RuleCalculateWeaponStats` outputs
alongside the three existing result observers. The mod never constructs/triggers these rules, changes their arguments/results,
skips originals, or activates a feat/buff. This is a general result bridge: effects implemented by the game or another mod in
these rules are copied without per-feat enumeration. Other mods' post-handler/patch ordering and all combat conditions remain uncertified.
Target-independent AB is not labeled as observed target AC. Expired/mismatched toggle, rank, cached context, equipment/grip,
ability-stat and turn stamps invalidate the general weapon cache. Native totals replace fallback active bonuses rather than adding them.

Deferred attack-start effects are a separate, partial static projection of already loaded blueprint references/direct apply-buff actions.
No ContextValue evaluator, context construction/rank recalculation, resource/restriction evaluator, action executor or reference.Get is called.
Unloaded references, unsupported conditions/actions and dynamic future values remain explicit unknown notes. Existing active buff blueprints
are excluded from the deferred set; pending AB/damage remains labeled projected even when the base output came from the engine.

AutomaticDefenseReader copies existing active/registered sources and stored scalar context values. It does not initialize parts, call
AllSources, call virtual GetCurrentValue/Bypassed/Hit, spend pools, remove buffs or clean up reductions. It projects only supported constant
nonstacking DR/resistance and immunity/suppression with audited bypass masks/thresholds. Unsupported pools, stacking, hardness, vulnerabilities,
epic/reality clauses and multiple physical grouping remain unavailable where unresolved. Complete means this limited constant model,
not all rules/modifiers certified. The old strict DefenseObservation providers remain unchanged; fullConditionsVerified stays false.

The supplied assembly's field/method layout was checked by reflection without executing game methods (33 checks). This is metadata validation,
not an in-game Harmony installation, UI, correctness or state/RNG invariance test. No new live execution evidence is claimed for 0.4.0.

### 0.5.0 spell UI observers (local test)

Two additional explicit Priority.Last postfixes observe AbilityData.CalculateParams and
AbilityData.WouldCurrentlyTarget. Neither receives ref/out inputs or replaces results.
The former copies DC/CL/metamagic scalars after a game-owned calculation; the latter copies
already-computed inclusion for the currently selected ability. No preview path invokes either method,
CanTarget, ContextValue.Calculate, action handlers, DryRun or Rulebook.Trigger. Loaded entity proxies
and cached blueprint references are read directly. Parameter evidence is bounded to64 entries,
1500ms and a caster/equipment/metamagic/override context stamp; targeting evidence is bounded to128
entries/250ms and invalidated on selection/aim changes. Display caps32 visible live units.

Probabilities are conditional baseline d20 values, not a certification of full spell outcomes.
Conditional/custom save DC and unrecognized delivery paths are refused. Effect immunity, save
replacement/auto-pass, conditional bonuses and special rerolls are not certified. No combined debuff
success percentage is produced. The legacy SpellChance/DryRun predictor remains blocked.

## 0.5.1 completed damage trace audit

The existing real RuleDealDamage did-trigger observer copies ResultList (32 components maximum), stored
half/decline sources, damage scalar fields and bounded resistance modifiers. It never calls BaseDamage.Decline,
rule handlers, RNG, damage application, defense initialization, pool spending, or cleanup. Audited DamageValue
Reduction/FinalValue getters perform only arithmetic on completed source fields. The computed component final
is independently reconstructed in pure DTO code. Unknown kinds are not inferred through arbitrary virtual Type getters.
CompletedDamageTrace is gated on the existing supported game/mod configuration. A trace failure produces an
explicit missing-evidence record and preserves the older AB/AC logger. Preview associations expire after 10 seconds,
are bounded to 128, and are cleared on diagnostic reset; redirected attacker/target pairs do not reuse the original model.
No new Harmony patch is added. All full-game state/RNG preservation assertions remain unverified in-game.

## 0.5.7 native HP UI ownership

The only new UI mutation is a mod-owned Image child under an existing native HP Image. It has no raycast target,
no cloned game component/shader, and no event subscription. Existing game Image fields are read but not modified.
A pooled controller must still point to the preview target before display; visibility is checked each LateUpdate.
Host searches are throttled and no additional combat calculation is performed. Live UI and lifecycle verification
remain outstanding, so this is a test integration and the panel HP display stays available as fallback.

## 0.5.11 주문 추적
추가 Rulebook 실행이나 난수 생성 없이 완료된 판정을 구독합니다. 전달 무기는 이미 로드된 Blueprint 캐시만 비차단 잠금으로 조회하며 참조 Get/Load를 호출하거나 캐시를 변경하지 않습니다. 게임이 생성한 가상 접촉 공격 값은 동일 선택 능력·대상·상태 및 0.25초 이내에서만 활용합니다. 내성 DC 관측 유효기간은 유지합니다.

## 0.5.12
장판/버프 탐색은 기존 로드된 참조와 ActionList 필드만 읽습니다. 조건/액션을 실행하지 않습니다. 순환 방지와 탐색 한도를 두며 한도 초과 시 후보 DC의 수치를 확정하지 않습니다. 100ms 공유는 정적 효과 정의에 한하며 상태/DC/대상 확률 캐시가 아닙니다. 자동 성능 요약은 60초 주기로 로컬 UMM 로그에만 기록합니다.

## 0.5.13 — 선택적 외부 예측 결과 관측
HCO DLL을 메타데이터/IL로 조사했습니다. 조건부 보너스·은폐·미러 이미지·내성/SR·효과 분기를 처리하며 자체 예측 규칙을 실행하는 구조였습니다. 구현 코드를 복제하거나 그 계산 함수를 호출하지 않습니다. 기존 호출의 입력 식별자와 반환 필드만 관측합니다.
기존 규칙 결과 관측 정책에서 선택적 HCO 어댑터의 prefix/finalizer를 추가했습니다. prefix는 우리 쪽 스레드별 출처 스택만 시작하고 인수/게임 상태/실행 여부를 바꾸지 않습니다. postfix는 반환 필드만 읽으며 finalizer는 원래 예외를 그대로 반환하고 스택을 정리합니다. 하드 종속 참조나 HCO DLL 배포는 없습니다. 알고 있는 MVID에서만 패치합니다.
HCO 예측 이벤트를 실제 전투 및 독립적 게임 관측으로 분류하지 않습니다. DC는 출처를 표시합니다. 최근 기록은 128개, 하위 판정은 24개, 표시 문구는 16개로 제한하며 무기 로그 비교는 매 프레임 대신 150ms로 제한합니다. 추가 비용은 성능 로그에 따로 기록합니다. 게임 내 Harmony 상호작용과 FPS는 실제 실행으로 검증해야 합니다.

## 0.5.14 — 피해식 정적 해석
ContextValue.Calculate/ContextDiceValue.Calculate/ContextRankConfig.GetValue/액션/Rulebook를 호출하지 않습니다. 관측된 최신 CL과 메타매직, 정적 정의 필드로 기본식을 해석합니다. 장판/버프의 지속 피해를 시전 즉시 피해로 합산하지 않습니다. 탐색 깊이 8/예산 256/순환 방지, 표시 12개, 반사 필드 캐시 128개로 제한합니다. 정적 효과 경로는 250ms 동안 범위 대상 사이에 공유하고 CL/대상 확률을 캐시하지 않습니다. spell-damage-preview 시간은 자동 성능 요약에 포함합니다.
## 0.5.39

현재 턴 CurrentAbility는 m_LastAbility를 반환하는 단순 getter임을 확인했습니다. 강조 대상, 주문책 CL 구성 값과 조건 판독은 기존 필드/컬렉션을 읽으며 게임의 조건 검사·파라미터 계산기·액션·Rulebook를 실행하지 않습니다. 조건 분기는 모드 소유 자료에만 저장하고, 액션 탐색 및 분포 계산에 별도 한도를 둡니다. HCO 결과의 수치 주입은 없습니다.

HUD BindViewImplementation postfix는 모드 소유 RectTransform 후보 목록만 갱신합니다. UI 바인딩 훅 실패는 선택적 기능으로 격리하고 기존 탐색을 유지합니다. 보호막·거울상은 모드 소유 확률 분포만 변경하며 실제 잔량·교전 목록을 수정하지 않습니다. 시전 로그의 사전 입력은 약한 이벤트 키에 저장하고 완료 이벤트에서만 대조 정보로 사용합니다. 입력 일치 표시는 추적된 필드에 한정됩니다.


## 0.5.68 read-only adapters
AttackCommandReader reads existing command/plan/hand fields and compares rule/weapon object identity. No command, slot creation, fake weapon getter or attack execution is invoked. AutoMetamagicReader reconstructs the audited base predicates from registered tuples, blueprint components, existing item/book/class fields and loaded blueprint references. It never calls ShouldApplyTo/IsSuitableAbility, consumes a buff, writes game caches, or loads blueprints. Unknown derived predicates remain unavailable. Batch memoization is scoped to one refresh; item parameter failures retain auto-source evidence.
