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
