# Validation — diagnostic development history

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
