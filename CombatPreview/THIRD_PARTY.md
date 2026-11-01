# Provenance and licenses

Success Chance: https://github.com/sergusaev/pathfinder-mods
Pinned commit: dddd003f4205e8d0d78da110c7f868e99e477ab9 (2026-10-30).
Original version in Info.json: 1.2.2. MIT, Copyright (c) 2026 Sergey Usaev.
The full upstream license is reproduced in LICENSE and must travel with binaries.

Derived files: AttackChance.cs, Breakdown.cs, Checks.cs, D20.cs, DryRun.cs,
Main.cs, ManeuverChance.cs, SpellChance.cs, Spellstrike.cs, wotr/GameRules.cs.
D20Modifier.cs was extracted from DryRun.cs. HoverChance.cs was rewritten around
upstream hover selection and full-attack API usage. Namespace changed throughout.
New: DamageMath.cs, PreviewPanel.cs, build scripts, project files and tests.
New contributions are distributed under the same MIT terms; upstream copyright retained.

Changes include an opt-in experimental UI, disabled prediction logging hooks,
per-attack damage formulas and exact distribution moments, unconditional critical
expected-value calculation, full/first attack context, and stronger finally cleanup.
Unity RNG state is restored at each dry-run exit. This does not establish preservation
of other RNGs or arbitrary game/mod state.

Hit Chance Overlay was inspected, not copied:
https://github.com/brettNel/wotr-hit-chance-overlay
Pinned commit: e225c128f932617128406620b531209426442252.
Repository LICENSE: MIT, Copyright (c) 2026 Brett Nelson.
Nexus page: https://www.nexusmods.com/pathfinderwrathoftherighteous/mods/1238
Nexus asset permissions differ from the source license. No Nexus assets redistributed.

Game/Owlcat/Unity/UMM/Harmony assemblies are not included. Build against your own
installed game and UMM. The net472 reference-assemblies package is build-only.

0.2.0 research references (no code copied from these repositories):
TabletopTweaks-Core e76594f61b539c5243f74f150bb7afee604e1a48:
https://github.com/Vek17/TabletopTweaks-Core
Observed: per-type reduction, bypass/stacking, protection pool mutation and cleanup.
ToyBox-Wrath c4473258b2068d9eb0a577f490f62292fa962e2b:
https://github.com/xADDBx/ToyBox-Wrath
Observed: Kingmaker.GameVersion.GetVersion, compatibility checking pattern.
New original files: MitigationMath.cs, DefenseReader.cs, Compatibility.cs.
No remote incompatibility feed or automatic updater has been added.

0.2.1 original additions: DefenseContracts.cs, PredictionReport.cs,
PredictionSafety.cs, Diagnostics.cs, PassiveAttackDiagnostics.cs,
VanillaDefenseCapture.cs, contract tests and diagnostic documentation.
License remains MIT. Original upstream notices preserved.
The user's game assemblies were inspected locally to identify API shapes and
state-mutating paths; no game assembly or decompiled game source is redistributed.
ILSpy command-line 9.1.0.7988 was a development inspection tool, not bundled in the mod.

0.3.0 original additions: BetaModel.cs, BetaRuntime.cs, BetaTests.cs, rewritten
PreviewPanel.cs and read-only HoverChance.cs, beta comparison diagnostics and packaging.
These additions use MIT terms and preserve upstream notices. Local DLL metadata/IL
inspection informed API/field choices; no decompiled game source or reference DLL is redistributed.
The legacy replay code remains dormant and blocked; the beta does not restore/replay game events.

0.3.1: PanelPresentation.cs, original procedural UI gradients/frame, observed game
estimate scalar cache and layout/conditional-mean/component-comparison tests are
original MIT contributions. No game or other mod artwork/font files were extracted
or redistributed. The panel uses the already-loaded GUI font at runtime.
