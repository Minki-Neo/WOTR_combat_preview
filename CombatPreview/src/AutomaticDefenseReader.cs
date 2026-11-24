using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.Designers.Mechanics.Buffs;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem;
using Kingmaker.Enums.Damage;
using Kingmaker.Items;
using Kingmaker.UnitLogic;
using Kingmaker.UnitLogic.FactLogic;
using Kingmaker.UnitLogic.Parts;

namespace CombatPreview
{
    internal static class AutomaticDefenseReader
    {
        internal static DefenseProfile Read(UnitEntityData target, ItemEntityWeapon weapon, WeaponTraits traits, out List<string> notes, bool energyOnly = false, bool readEnergyPools = false,
            bool singleApplication = false, DamageKind? incomingKind = null, UnitEntityData actor = null)
        {
            var details = new List<string>(); notes = details;
            var missing = new List<string>();
            var poolIssues = new HashSet<string>();
            void PoolIssue(string reason) { missing.Add(reason); poolIssues.Add(reason); }
            var profile = new DefenseProfile { Complete = true, Source = "readonly-active-vanilla-DR/constant-resistance", UnavailableReason = "" };
            if (actor != null && weapon != null)
                foreach (var fact in actor.Facts.List.Where(fact => fact.Active))
                foreach (var runtime in fact.Components)
                    if (CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                        && CombatFields.Component(runtime)?.GetType() == typeof(AddClusteredAttack))
                    {
                        var rule = (AddClusteredAttack)CombatFields.Component(runtime);
                        if (rule.AttackType == AddClusteredAttack.Type.RangedWeapon && weapon.Blueprint.IsRanged
                            || rule.AttackType == AddClusteredAttack.Type.Unarmed && weapon.Blueprint.IsUnarmed)
                        { profile.ClusteredPhysicalDR = true; details.Add("응집 공격: 전력 공격의 같은 대상에 물리 DR 누적 1회"); }
                    }
            var part = target.Get<UnitPartDamageReduction>();
            var registeredOrder = new Dictionary<object, int>();
            if (readEnergyPools && CombatFields.Read(part, "m_Chunks") is IList chunks && chunks.Count <= 256)
            {
                for (int index = 0; index < chunks.Count; index++)
                {
                    var source = CombatFields.Read(chunks[index], "<DR>k__BackingField");
                    if (source != null && !registeredOrder.ContainsKey(source)) registeredOrder[source] = index;
                }
            }
            var penalties = CombatFields.Read(part, "PenaltyEntries") as IList;
            if (penalties != null && penalties.Count > 0) missing.Add("DR 감소/취약 효과");
            var facts = new HashSet<EntityFact>(target.Facts.List);
            var registered = CombatFields.Read(part, "m_SourceFacts") as IEnumerable;
            if (registered != null) foreach (var value in registered)
            { var f = value as EntityFact; if (f != null) facts.Add(f); else missing.Add("알 수 없는 등록 방어 소스"); }
            var immunities = CombatFields.Read(part, "m_Immunities") as IEnumerable;
            if (immunities != null) foreach (var value in immunities)
            { var f = CombatFields.Read(value, "SourceFact") as EntityFact; if (f != null) facts.Add(f); else missing.Add("면역 등록 소스 미확인"); }
            int physicalImmunityForms = 0;
            bool completeImmunity = false;
            int budget = 0;
            foreach (var fact in facts)
            {
                if (!fact.Active) continue;
                foreach (var runtime in fact.Components)
                {
                    if (++budget > 4096) { missing.Add("방어 판독 한도 초과"); break; }
                    var component = CombatFields.Component(runtime);
                    if (component?.GetType() == typeof(AddImmunityToCriticalHits)
                        && CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField"))
                    { profile.CriticalImmune = true; details.Add("치명타 면역"); continue; }
                    if (component?.GetType() == typeof(SwarmDamageResistance))
                    {
                        if (!energyOnly && CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField"))
                            SwarmDefenseReader.Weapon((SwarmDamageResistance)component, actor, weapon, traits, profile, details, missing);
                        continue;
                    }
                    if (energyOnly && component is AddIncorporealDamageDivisor)
                    {
                        profile.IncorporealEnergyDivisor = true;
                        continue;
                    }
                    if (!energyOnly && weapon == null && component is AddIncorporealDamageDivisor)
                    { missing.Add("비실체 대상 비무기 물리 피해"); continue; }
                    if (component is CompleteDamageImmunity)
                    {
                        if (component.GetType() != typeof(CompleteDamageImmunity)) { missing.Add("미지원 전체 면역 파생 효과"); continue; }
                        completeImmunity = true;
                        SupportedCombatRules.AddCompleteImmunity(profile);
                        details.Add(CombatFields.Label(fact) + ": 전체 피해 면역");
                        continue;
                    }
                    if (component is AddPhysicalImmunity)
                    {
                        if (energyOnly) continue;
                        if (component.GetType() != typeof(AddPhysicalImmunity)) { missing.Add("미지원 물리 면역 파생 효과"); continue; }
                        var forms = CombatFields.Read(component, "m_PhysicalDamageForms");
                        if (!(forms is PhysicalDamageForm)) { missing.Add("물리 면역 형태 미확인"); continue; }
                        physicalImmunityForms |= (int)(PhysicalDamageForm)forms;
                        details.Add(CombatFields.Label(fact) + ": 물리 면역 형태 " + forms);
                        continue;
                    }
                    var resistance = component as AddDamageResistanceBase;
                    var immunity = component as AddEnergyDamageImmunity;
                    // A fire-only protection must not make Holy damage unavailable.
                    // Filter before reading its pool, priority or dynamic magnitude.
                    if (incomingKind.HasValue && component is AddDamageResistanceEnergy typedResistance
                        && Enum.TryParse(typedResistance.Type.ToString(), out DamageKind resistanceKind)
                        && resistanceKind != incomingKind.Value) continue;
                    if (incomingKind.HasValue && immunity != null
                        && Enum.TryParse(immunity.EnergyType.ToString(), out DamageKind immunityKind)
                        && immunityKind != incomingKind.Value) continue;
                    if (immunity != null)
                    {
                        if (component.GetType() != typeof(AddEnergyDamageImmunity)) { missing.Add("알 수 없는 면역 파생 효과"); continue; }
                        if (target.State.HasCondition(UnitCondition.SuppressedEnergyImmunity)) { details.Add("에너지 면역 억제 중"); continue; }
                        DamageKind kind;
                        if (!Enum.TryParse(immunity.EnergyType.ToString(), out kind) || kind == DamageKind.Unknown) { missing.Add("미지원 면역 유형 " + immunity.EnergyType); continue; }
                        profile.Immunities.Add(kind); details.Add(CombatFields.Label(fact) + ": " + kind + " 면역");
                        continue;
                    }
                    if (resistance == null || energyOnly && component.GetType() == typeof(AddDamageResistancePhysical)) continue;
                    // The vanilla ResistEnergy component inherits the energy
                    // resistance calculation and bypass checks without overrides.
                    var componentType = component.GetType();
                    bool supportedPhysical = componentType == typeof(AddDamageResistancePhysical);
                    bool protection = componentType == typeof(ProtectionFromEnergy);
                    bool supportedEnergy = componentType == typeof(AddDamageResistanceEnergy) || componentType == typeof(ResistEnergy) || protection;
                    if (supportedEnergy && target.State.HasCondition(UnitCondition.SuppressedEnergyResistance))
                    {
                        details.Add("에너지 저항 억제 중");
                        continue;
                    }
                    if (energyOnly && resistance.IsHardness)
                    {
                        missing.Add("별도 경도 처리: " + CombatFields.Label(fact));
                        continue;
                    }
                    int? remainingPool = null;
                    if (resistance.UsePool)
                    {
                        if ((supportedEnergy || supportedPhysical)
                            && CombatFields.Read(runtime, "m_RemainPool") is int remaining && remaining >= 0)
                        {
                            remainingPool = remaining;
                            if (!supportedPhysical) profile.HasConsumablePool = true;
                        }
                        else
                        {
                            profile.HasConsumablePool = true;
                            PoolIssue("소모형 보호막/DR 풀: " + CombatFields.Label(fact));
                            continue;
                        }
                    }
                    int amount;
                    if (protection && remainingPool.HasValue) amount = remainingPool.Value;
                    else if (!CombatFields.Scalar(resistance.Value, fact, out amount)) { missing.Add("동적 DR 값: " + CombatFields.Label(fact)); continue; }
                    var physical = resistance as AddDamageResistancePhysical;
                    var energy = resistance as AddDamageResistanceEnergy;
                    if (supportedPhysical)
                    {
                        if (CombatFields.Bool(physical, "m_IsStackable")) { missing.Add("중첩형 DR: " + CombatFields.Label(fact)); continue; }
                        if ((int)physical.ValidWeaponFact == 1 && weapon == null
                            || (int)physical.ValidWeaponFact == 2 && weapon != null
                            || weapon != null && (((int)physical.ValidWeaponAttackTypes & (1 << (int)weapon.Blueprint.AttackType)) == 0)) continue;
                        bool? bypass = PhysicalBypass(physical, target, weapon, traits);
                        if (!bypass.HasValue) { missing.Add("미확인 DR 관통 조건: " + CombatFields.Label(fact)); continue; }
                        details.Add(CombatFields.Label(fact) + ": 물리 DR " + amount + (bypass.Value ? " 관통" : " 적용 후보") + " (" + (physical.Or ? "OR" : "AND") + ")");
                        if (bypass.Value) continue;
                        profile.PhysicalSequenceSources.Add((registeredOrder.TryGetValue(runtime, out int physicalOrder) ? physicalOrder : -1,
                            remainingPool ?? -1, amount));
                        if (!remainingPool.HasValue) Add(profile, DamageKind.Physical, amount, missing);
                        else if (remainingPool.Value > 0 && amount > 0)
                        {
                            if (singleApplication) Add(profile, DamageKind.Physical, Math.Min(remainingPool.Value, amount), missing);
                            else if (profile.PhysicalPool.HasValue && readEnergyPools) profile.HasConsumablePool = true;
                            else if (profile.PhysicalPool.HasValue || amount > 1000000)
                                PoolIssue("중첩 또는 범위 밖 물리 보호막");
                            else
                            {
                                profile.PhysicalPool = (remainingPool.Value, amount);
                                details.Add(CombatFields.Label(fact) + ": 물리 보호막 잔량 " + remainingPool.Value);
                            }
                        }
                    }
                    else if (supportedEnergy)
                    {
                        int factor = 1;
                        if (!protection && energy.UseValueMultiplier && !CombatFields.Scalar(energy.ValueMultiplier, fact, out factor)) { missing.Add("동적 저항 배율"); continue; }
                        DamageKind kind;
                        if (!Enum.TryParse(energy.Type.ToString(), out kind) || kind == DamageKind.Unknown) { missing.Add("미지원 저항 유형 " + energy.Type); continue; }
                        if (!protection) amount = checked(amount * factor * CombatFields.Rank(fact));
                        {
                            if (!profile.EnergySequenceSources.TryGetValue(kind, out var sequence))
                                profile.EnergySequenceSources[kind] = sequence = new List<(int Order, int Remaining, int PerHit)>();
                            sequence.Add((registeredOrder.TryGetValue(runtime, out int order) ? order : -1,
                                remainingPool ?? -1, amount));
                        }
                        if (remainingPool.HasValue)
                        {
                            if (profile.EnergyPools.ContainsKey(kind)) profile.MultipleEnergyPools.Add(kind);
                            profile.EnergyPools[kind] = (remainingPool.Value, amount);
                            amount = Math.Min(amount, remainingPool.Value);
                        }
                        else
                        {
                            profile.ConstantEnergyResistance.TryGetValue(kind, out int old);
                            profile.ConstantEnergyResistance[kind] = Math.Max(old, amount);
                        }
                        Add(profile, kind, amount, missing); details.Add(CombatFields.Label(fact) + ": " + kind + " 저항 " + amount);
                    }
                    else missing.Add("미지원 방어 효과 " + component.GetType().Name);
                }
            }
            if (!completeImmunity && !energyOnly)
            {
                bool? physicalImmune = SupportedCombatRules.PhysicalImmunity(physicalImmunityForms, traits.Form, traits.Known);
                if (!physicalImmune.HasValue) missing.Add("물리 면역 판정에 필요한 무기 피해 형태 미확인");
                else if (physicalImmune.Value) { profile.Immunities.Add(DamageKind.Physical); details.Add("현재 무기 피해 형태에 물리 면역 적용"); }
            }
            if (profile.PhysicalPool.HasValue && profile.Reduction.TryGetValue(DamageKind.Physical, out int constantDr) && constantDr > 0
                && !(readEnergyPools && profile.PhysicalSequenceSources.All(source => source.Order >= 0)))
                PoolIssue("물리 보호막과 상시 DR의 처리 순서 미확인");
            profile.Complete = missing.Count == 0;
            profile.PoolApproximationAllowed = missing.All(poolIssues.Contains)
                && (profile.PhysicalSequenceSources.Any(source => source.Remaining >= 0)
                    || profile.EnergySequenceSources.Values.Any(sources => sources.Any(source => source.Remaining >= 0)));
            profile.UnavailableReason = string.Join("; ", missing.Distinct());
            details.AddRange(missing.Select(s => "미확인: " + s));
            if (profile.Immunities.Count > 0)
                details.Add("적용 면역: " + string.Join(", ", profile.Immunities.OrderBy(k => k)));
            if (profile.Complete)
                details.Add("적용 최대값: " + (profile.Reduction.Count == 0 ? (profile.Immunities.Count == 0 ? "DR/저항 없음" : "DR/저항 없음 (면역은 별도 적용)") : string.Join(", ", profile.Reduction.Select(p => p.Key + " " + p.Value))));
            return profile;
        }
        static void Add(DefenseProfile profile, DamageKind kind, int amount, List<string> missing)
        { if (amount < 0 || amount > 1000000) missing.Add("음수/범위 밖 방어 값"); else SupportedCombatRules.AddReduction(profile, kind, amount); }
        static bool? PhysicalBypass(AddDamageResistancePhysical dr, UnitEntityData target, ItemEntityWeapon weapon, WeaponTraits t)
        {
            var clauses = new List<bool?>();
            if (dr.BypassedByForm) clauses.Add(SupportedCombatRules.MaskBypass((int)dr.Form, t.Form, t.Known));
            if (dr.BypassedByMagic) clauses.Add(t.Known ? t.EnhancementTotal >= dr.MinEnhancementBonus : (bool?)null);
            if (dr.BypassedByMaterial)
            {
                clauses.Add(SupportedCombatRules.MaterialBypass((int)dr.Material, t.Materials, t.Enhancement,
                    (int)(PhysicalDamageMaterial.ColdIron | PhysicalDamageMaterial.Silver), (int)PhysicalDamageMaterial.Adamantite, t.Known));
            }
            if (dr.BypassedByAlignment) clauses.Add(SupportedCombatRules.AlignmentBypass((int)dr.Alignment, t.Alignment, t.EnhancementTotal, t.Known));
            if (dr.BypassedByWeaponType) clauses.Add(weapon != null && CombatFields.Reference(CombatFields.Read(dr, "m_WeaponType"), weapon.Blueprint.Type));
            if (dr.BypassedByMeleeWeapon) clauses.Add(weapon?.Blueprint.IsMelee == true);
            // Epic AND semantics and reality-changing handlers need additional
            // adapters. A deciding OR/AND clause can still resolve the outcome.
            if (dr.BypassedByReality || dr.BypassedByEpic) clauses.Add(null);
            return SupportedCombatRules.Bypass(dr.Or, clauses);
        }
    }
}
