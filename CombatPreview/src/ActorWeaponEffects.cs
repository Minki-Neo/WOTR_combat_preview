using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Kingmaker.EntitySystem;
using Kingmaker.EntitySystem.Entities;
using Kingmaker.Items;
using Kingmaker.UnitLogic.Parts;
using Kingmaker.UnitLogic.Mechanics.Components;
using Kingmaker.Designers.Mechanics.EquipmentEnchants;
using Kingmaker.Enums;
namespace CombatPreview
{
    internal static class ActorWeaponEffects
    {
        internal sealed class SourceSnapshot { internal EntityFact[] Facts; }
        static IEnumerable<EntityFact> Facts(UnitEntityData actor)
        {
            if (ScopedReadMemo<UnitEntityData, SourceSnapshot>.TryGet(actor, out var cached)) return cached.Facts;
            var result = new SourceSnapshot { Facts = ReadFacts(actor).ToArray() };
            ScopedReadMemo<UnitEntityData, SourceSnapshot>.Put(actor, result); return result.Facts;
        }
        static IEnumerable<EntityFact> ReadFacts(UnitEntityData actor)
        {
            var seen = new HashSet<EntityFact>();
            foreach (var fact in actor.Facts.List) if (fact.Active && seen.Add(fact)) yield return fact;
            var slots = CombatFields.Read(actor.Body, "EquipmentSlots") as IEnumerable;
            if (slots == null) yield break;
            int count = 0;
            foreach (var slot in slots)
            {
                if (++count > 64) throw new NotSupportedException("착용 장비 판독 한도");
                if (!CombatFields.Bool(slot, "m_Active")) continue;
                var item = BetaReader.SlotItem(slot); if (item == null) continue;
                foreach (var fact in ActiveCombatReader.Enchantments(item)) if (seen.Add(fact)) yield return fact;
            }
        }
        internal static void Enhance(UnitEntityData actor, ItemEntityWeapon weapon, WeaponTraits traits)
        {
            if (actor == null) return;
            var normal = new List<(int Value, bool Stacks)>(); var extra = new List<(int Value, bool Stacks)>();
            int total = 0;
            foreach (var fact in Facts(actor)) foreach (var runtime in fact.Components)
            {
                if (!CombatFields.Bool(runtime, "<IsSubscribedOnEventBus>k__BackingField")) continue;
                var component = CombatFields.Component(runtime);
                if (component?.GetType() == typeof(AllAttacksEnhancement))
                {
                    var rule = (AllAttacksEnhancement)component;
                    var context = CombatFields.Read(fact, "m_Context");
                    var source = CombatFields.Read(CombatFields.Read(CombatFields.Read(context, "m_CasterRef"), "m_Proxy"), "<Entity>k__BackingField") as UnitEntityData;
                    int fake = source?.State.Features.AdvancedFakeEnhancement == true ? 2 : source?.State.Features.FakeEnhancement == true ? 1 : 0;
                    int value = checked(rule.Bonus * CombatFields.Rank(fact) + fake);
                    normal.Add((value, rule.Descriptor.IsStackable())); total = checked(total + value);
                    traits.Notes.Add(CombatFields.Label(fact) + ": 전체 공격 강화 " + value);
                }
                else if (component?.GetType() == typeof(EquipmentWeaponTypeEnhancement))
                {
                    var rule = (EquipmentWeaponTypeEnhancement)component;
                    if (!(rule.AllNaturalAndUnarmed && (weapon.Blueprint.IsNatural || weapon.Blueprint.IsUnarmed))
                        && weapon.Blueprint.Category != rule.Category) continue;
                    normal.Add((rule.Enhancement, rule.IsStackable)); total = checked(total + rule.Enhancement);
                    int fake = actor.State.Features.AdvancedFakeEnhancement ? 2 : actor.State.Features.FakeEnhancement ? 1 : 0;
                    if (fake > 0) { extra.Add((fake, true)); total = checked(total + fake); }
                    traits.Notes.Add(CombatFields.Label(fact) + ": 장비의 공격 강화 " + rule.Enhancement + (fake > 0 ? " + 추가 " + fake : ""));
                }
            }
            traits.Enhancement = WeaponEffectMath.Enhancement(traits.Enhancement, normal, extra);
            traits.EnhancementTotal = checked(traits.EnhancementTotal + total);
        }
        internal static int FavoredEnemy(UnitEntityData actor, UnitEntityData target, List<string> notes)
        {
            var entries = CombatFields.Read(actor.Get<UnitPartFavoredEnemy>(), "Entries") as IEnumerable;
            if (entries == null) return 0;
            int result = 0, count = 0; object source = null;
            foreach (var entry in entries)
            {
                if (++count > 128) throw new NotSupportedException("주적 항목 판독 한도");
                var features = CombatFields.Read(entry, "CheckedFeatures") as IEnumerable;
                if (features == null) continue;
                bool match = false;
                foreach (var feature in features)
                    if (target.Facts.List.Any(f => f.Active && ReferenceEquals(CombatFields.Blueprint(f), feature))) { match = true; break; }
                int value = CombatFields.Int(entry, "Bonus");
                int next = WeaponEffectMath.FavoredStep(result, match, value);
                if (next != result) { result = next; source = CombatFields.Read(entry, "Source"); }
            }
            if (result > 0) notes.Add(CombatFields.Label(source) + ": 대상 주적 AB/피해 +" + result);
            return result;
        }
    }
}
