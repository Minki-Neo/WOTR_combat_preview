using System;
using System.Linq;
using Kingmaker.Blueprints.Classes.Spells;
using Kingmaker.Designers.Mechanics.Facts;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.EntitySystem.Stats;
using Kingmaker.Enums;
using Kingmaker.UnitLogic.Abilities;

namespace CombatPreview
{
    internal static class ActiveD20Reader
    {
        static bool Cosmetic(Kingmaker.ElementsSystem.GameAction action) => action != null
            && (action.GetType().Name.StartsWith("ContextActionSpawnFx", StringComparison.Ordinal)
                || action.GetType().Name == "ContextActionPlaySound");
        static bool PassiveMarker(Kingmaker.ElementsSystem.GameAction action)
        {
            if (action?.GetType() != typeof(Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff)) return false;
            var reference = CombatFields.Read(action, "m_Buff");
            var loaded = LoadedBlueprintReader.Read(reference);
            // Audited empty vanilla cooldown markers. A loaded definition always
            // takes precedence, so added runtime components are never ignored.
            var blueprint = loaded as Kingmaker.UnitLogic.Buffs.Blueprints.BlueprintBuff;
            return D20ActionPolicy.PassiveMarker(CombatFields.ReferenceGuid(reference), BetaReader.Supported,
                loaded != null, blueprint != null, blueprint?.ComponentsArray?.Length ?? -1);
        }
        static bool SaveBonusBaseline(UnitEntityData actor, SavingThrowType save, Kingmaker.Enums.ModifierDescriptor descriptor, D20Modifier modifier)
        {
            var stat = save == SavingThrowType.Will ? actor.Stats.SaveWill : save == SavingThrowType.Fortitude
                ? actor.Stats.SaveFortitude : save == SavingThrowType.Reflex ? actor.Stats.SaveReflex : null;
            if (stat == null || stat.MoraleBonusesDisabled || stat.InsightBonusesDisabled
                || (int)descriptor == 4 || (int)descriptor == 41
                || !(CombatFields.Read(stat, "ModifierList") is System.Collections.IDictionary groups)) return false;
            modifier.SaveBonusDescriptor = (int)descriptor;
            foreach (System.Collections.DictionaryEntry group in groups)
            foreach (var existing in (System.Collections.IEnumerable)group.Value)
            {
                if (!(CombatFields.Read(existing, "ModDescriptor") is ModifierDescriptor existingDescriptor)) return false;
                if (existingDescriptor != descriptor) continue;
                int mode = CombatFields.Int(existing, "StackMode");
                if (mode == 1 || mode == 0 && existingDescriptor.IsStackable()) continue;
                if (mode < 0 || mode > 2 || !(CombatFields.Read(existing, "ModValue") is int value)) return false;
                modifier.SaveBonusBaselinePositive = Math.Max(modifier.SaveBonusBaselinePositive, value);
                modifier.SaveBonusBaselineNegative = Math.Min(modifier.SaveBonusBaselineNegative, value);
            }
            return true;
        }
        internal sealed class PartyRollSources { internal string[] Names; }
        static string[] WorldSources(UnitEntityData actor)
        {
            if (ScopedReadMemo<UnitEntityData, PartyRollSources>.TryGet(actor, out var cached)) return cached.Names;
            var names = new System.Collections.Generic.List<string>();
            var party = CombatFields.Read(Kingmaker.Game.Instance?.Player, "m_PartyAndPets") as System.Collections.Generic.List<UnitEntityData>;
            if (party?.Contains(actor) == true)
                foreach (var member in party)
                foreach (var fact in member.Facts.List.Where(fact => fact.Active))
                foreach (var runtime in fact.Components)
                    if (CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                        && CombatFields.Component(runtime)?.GetType() == typeof(TricksterKnowledgeWorldD20))
                        names.Add(CombatFields.Label(fact) + ": 자연 1을 20으로 치환");
            var result = names.ToArray();
            ScopedReadMemo<UnitEntityData, PartyRollSources>.Put(actor, new PartyRollSources { Names = result }); return result;
        }
        internal static D20RollModel Read(UnitEntityData actor, UnitEntityData target, string kind,
            SavingThrowType save = SavingThrowType.Unknown, AbilityData ability = null)
        {
            var key = (actor, target, kind, save, ability);
            if (ScopedReadMemo<(UnitEntityData, UnitEntityData, string, SavingThrowType, AbilityData), D20RollModel>.TryGet(key, out var cached)) return cached;
            var result = ReadCore(actor, target, kind, save, ability);
            ScopedReadMemo<(UnitEntityData, UnitEntityData, string, SavingThrowType, AbilityData), D20RollModel>.Put(key, result);
            return result;
        }
        static D20RollModel ReadCore(UnitEntityData actor, UnitEntityData target, string kind, SavingThrowType save, AbilityData ability)
        {
            var model = new D20RollModel();
            var world = WorldSources(actor);
            model.ReplaceOneWithMax = world.Length > 0; model.Notes.AddRange(world);
            int mask = kind == "attack" ? 1 : kind == "save" ? 4 : 32;
            foreach (var fact in actor.Facts.List.Where(fact => fact.Active))
            foreach (var runtime in fact.Components)
            {
                if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                var component = CombatFields.Component(runtime);
                if (component?.GetType() != typeof(ModifyD20)) continue;
                var rule = (ModifyD20)component;
                int flags = (int)rule.Rule;
                if (flags != 639 && (flags & mask) == 0) continue;
                if (kind == "save" && flags != 639)
                {
                    int types = CombatFields.Int(rule, "m_SavingThrowType");
                    if (save == SavingThrowType.Unknown || (types & (1 << ((int)save - 1))) == 0) continue;
                    if (rule.SpecificDescriptor && !(ability?.SpellDescriptor ?? SpellDescriptor.None).HasAnyFlag((SpellDescriptor)rule.SpellDescriptor)) continue;
                }
                if (rule.AgainstAlignment && !actor.Alignment.ValueRaw.HasComponent(rule.Alignment)
                    && (!rule.TargetAlignment || target == null || !target.Alignment.ValueRaw.HasComponent(rule.Alignment))) continue;
                string label = CombatFields.Label(fact);
                bool Scalar(Kingmaker.UnitLogic.Mechanics.ContextValue value, out int amount) => CombatFields.Scalar(value, fact, out amount);
                if (rule.WithChance && Scalar(rule.Chance, out int activationChance) && activationChance <= 0) continue;
                var modifier = new D20Modifier { Source = label, FactKey = BetaIdentity.Id(fact), Replace = rule.Replace, Rerolls = rule.RollsAmount,
                    TakeBest = rule.TakeBest, OnlyIfFailed = kind != "sr" && rule.RerollOnlyIfFailed,
                    OnlyIfSuccess = kind != "sr" && !rule.RerollOnlyIfFailed && rule.RerollOnlyIfSuccess };
                bool supported = true;
                if (rule.Replace) { supported &= Scalar(rule.RollResult, out int result); modifier.Roll = result; }
                if (!modifier.OnlyIfFailed && !modifier.OnlyIfSuccess && (int)rule.RollCondition != 0)
                { modifier.Compare = (int)rule.RollCondition; supported &= Scalar(rule.ValueToCompareRoll, out int compared); modifier.CompareValue = compared; }
                if (rule.WithChance) { supported &= Scalar(rule.Chance, out int chance); modifier.Chance = Math.Max(0, Math.Min(100, chance)); }
                if (kind == "save" && rule.AddSavingThrowBonus)
                {
                    bool known = Scalar(rule.Value, out int bonus);
                    modifier.SaveBonus = bonus;
                    if (!rule.ModifierDescriptor.IsStackable()) known &= SaveBonusBaseline(actor, save, rule.ModifierDescriptor, modifier);
                    supported &= known;
                    model.Notes.Add(label + (known ? ": 조건부 내성 보너스 중첩 기준 " : ": 조건부 내성 보너스 기준 미확인 ")
                        + rule.ModifierDescriptor + " · 기존 +" + modifier.SaveBonusBaselinePositive + "/" + modifier.SaveBonusBaselineNegative);
                }
                if (rule.AddBonus) supported = false;
                if (rule.ActionsToTriggerBeforeRoll?.Actions?.Any(action => !Cosmetic(action)) == true) supported = false;
                bool simpleFact = (CombatFields.Blueprint(fact) as Kingmaker.Blueprints.BlueprintScriptableObject)?.ComponentsArray
                    .All(part => part?.GetType() == typeof(ModifyD20) || part?.GetType() == typeof(SpellDescriptorComponent)) == true;
                foreach (var action in rule.ActionsToTrigger?.Actions ?? new Kingmaker.ElementsSystem.GameAction[0])
                {
                    if (Cosmetic(action)) continue;
                    if (simpleFact && action?.GetType() == typeof(Kingmaker.UnitLogic.Mechanics.Actions.ContextActionRemoveSelf))
                    { model.ConsumesEffect = true; modifier.ConsumeWhenApplied = true; }
                    else if (simpleFact && PassiveMarker(action)) model.Notes.Add(label + ": 후속 비수치 재사용 대기 표식");
                    else
                    {
                        supported = false;
                        var buffReference = CombatFields.Read(action, "m_Buff");
                        model.Notes.Add(label + ": 후속 효과 미지원 · " + (action?.GetType().Name ?? "null")
                            + (buffReference == null ? "" : " · buff=" + (CombatFields.ReferenceGuid(buffReference)?.ToString("N") ?? "unresolved"))
                            + " · simpleFact=" + simpleFact);
                    }
                }
                if (modifier.Rerolls < 0 || modifier.Rerolls > 16 || model.Modifiers.Count >= 8) supported = false;
                if (rule.DispellOn20 || rule.DispellOnRerollFinished)
                {
                    // The current snapshot can predict one roll. Later attacks /
                    // nested saves require tracking the consumed fact separately.
                    model.ConsumesEffect = true;
                    modifier.ConsumeWhenApplied |= rule.DispellOnRerollFinished;
                    modifier.ConsumeOn20 |= rule.DispellOn20;
                }
                if (modifier.SaveBonusDescriptor.HasValue && (modifier.ConsumeWhenApplied || modifier.ConsumeOn20) && !simpleFact)
                {
                    supported = false;
                    model.Notes.Add(label + ": 소모 시 함께 제거되는 다른 방어/특기 효과의 후속 내성 변화 미지원");
                }
                model.Complete &= supported;
                model.Notes.Add(label + (supported ? ": " + (modifier.Replace ? "고정 굴림" : modifier.TakeBest ? "유리 재굴림" : "불리 재굴림") : ": 동적/추가 효과 판독 필요"));
                if (model.Modifiers.Count < 8 && modifier.Rerolls >= 0 && modifier.Rerolls <= 16) model.Modifiers.Add(modifier);
            }
            if (kind == "sr" && actor.State.Features.AzataFavorableMagic)
            { model.AfterRoll.Add(1); model.Notes.Add("유리한 마법: 관통 추가 굴림"); }
            if (kind == "save" && target != null && ability != null && target.State.Features.AzataFavorableMagic)
            { model.AfterRoll.Add(-1); model.Notes.Add("시전자 유리한 마법: 대상 내성 추가 굴림"); }
            if (kind == "save" && target != null && ability != null)
                foreach (var fact in target.Facts.List.Where(fact => fact.Active))
                foreach (var runtime in fact.Components)
                {
                    if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")
                        || CombatFields.Component(runtime)?.GetType() != typeof(Kingmaker.UnitLogic.DemonSocothbenothAspect)) continue;
                    var rule = (Kingmaker.UnitLogic.DemonSocothbenothAspect)CombatFields.Component(runtime);
                    if (rule.UseSpellDescriptor && !ability.Blueprint.ComponentsArray.OfType<SpellDescriptorComponent>()
                        .Take(1).Any(component => component.Descriptor.HasAnyFlag((SpellDescriptor)rule.SpellDescriptor))) continue;
                    model.AfterRoll.Add(-1); model.Notes.Add(CombatFields.Label(fact) + ": 대상 내성 추가 굴림");
                }
            // Override and ReplaceOneWithMax use separate internal raw/override
            // fields. Do not guess their precedence without the full raw state.
            if (model.ReplaceOneWithMax && model.Modifiers.Any(modifier => modifier.Replace))
            { model.Complete = false; model.Notes.Add("고정 굴림과 자연 1 치환의 결합 미지원"); }
            if (model.AfterRoll.Count > 16) model.Complete = false;
            return model;
        }
        internal static double? SaveFailure(UnitEntityData target, UnitEntityData caster, AbilityData ability,
            SavingThrowType type, int bonus, int dc, bool persistent, out D20RollModel model)
        {
            model = Read(target, caster, "save", type, ability);
            return !model.Complete ? null : model.ConsumesEffect
                ? D20SaveSequence.Compute(model, bonus, dc, persistent)?.FirstFailure
                : (double?)model.SaveFailure(bonus, dc, persistent);
        }
    }
}
