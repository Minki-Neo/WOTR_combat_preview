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
