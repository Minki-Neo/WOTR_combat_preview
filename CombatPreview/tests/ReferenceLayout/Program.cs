using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
class Program {
 static int count;
 static void Main(string[] args) {
  if (args.Length != 1) throw new ArgumentException("Supply the private WotR Managed directory; no game DLLs are distributed.");
  string folder = Path.GetFullPath(args[0]);
  AssemblyLoadContext.Default.Resolving += (context, name) => File.Exists(Path.Combine(folder, name.Name + ".dll")) ? context.LoadFromAssemblyPath(Path.Combine(folder, name.Name + ".dll")) : null;
  var a = AssemblyLoadContext.Default.LoadFromAssemblyPath(folder + "/Assembly-CSharp.dll");
  foreach(var pair in new[] {
   ("Kingmaker.UnitLogic.Abilities.AbilityData", "m_CasterRef"),
   ("Kingmaker.UnitLogic.Abilities.MetamagicData", "<MetamagicMask>k__BackingField"),
   ("Kingmaker.EntitySystem.Stats.ModifiableValueArmorClass", "<Touch>k__BackingField"),
   ("Kingmaker.UnitLogic.Parts.UnitPartSpellResistance", "m_SRs"),
   ("Kingmaker.UnitLogic.Mechanics.Actions.ContextActionSavingThrow", "m_ConditionalDCIncrease"),
   ("Kingmaker.EntitySystem.EntityFact", "<Blueprint>k__BackingField"), ("Kingmaker.EntitySystem.EntityFact", "Components"),
   ("Kingmaker.EntitySystem.EntityFactComponent", "<SourceBlueprintComponent>k__BackingField"), ("Kingmaker.EntitySystem.EntityFactComponent", "<IsSubscribedOnEventBus>k__BackingField"),
   ("Kingmaker.EntitySystem.EntityFactsProcessor", "<Manager>k__BackingField"), ("Kingmaker.EntitySystem.EntityFactsManager", "m_Facts"),
   ("Kingmaker.UnitLogic.Feature", "<Rank>k__BackingField"), ("Kingmaker.UnitLogic.Feature", "<Param>k__BackingField"),
   ("Kingmaker.UnitLogic.Buffs.Buff", "m_Context"), ("Kingmaker.UnitLogic.Buffs.Buff", "<Rank>k__BackingField"),
   ("Kingmaker.UnitLogic.Mechanics.MechanicsContext", "m_Ranks"), ("Kingmaker.UnitLogic.Mechanics.MechanicsContext", "m_SharedValues"),
   ("Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility", "m_IsOn"), ("Kingmaker.UnitLogic.ActivatableAbilities.ActivatableAbility", "<IsStarted>k__BackingField"),
   ("Kingmaker.UnitLogic.ActivatableAbilities.BlueprintActivatableAbility", "m_Buff"), ("Kingmaker.UnitLogic.Mechanics.Actions.ContextActionApplyBuff", "m_Buff"),
   ("Kingmaker.Blueprints.BlueprintReferenceBase", "<Cached>k__BackingField"), ("Kingmaker.Blueprints.BlueprintReferenceBase", "guid"),
   ("Kingmaker.UnitLogic.FactLogic.AddPhysicalImmunity", "m_PhysicalDamageForms"),
   ("Kingmaker.UnitLogic.Parts.UnitPartDamageReduction", "m_SourceFacts"), ("Kingmaker.UnitLogic.Parts.UnitPartDamageReduction", "m_Immunities"),
   ("Kingmaker.UnitLogic.Parts.UnitPartDamageReduction", "PenaltyEntries"), ("Kingmaker.UnitLogic.FactLogic.AddDamageResistancePhysical", "m_IsStackable"),
   ("Kingmaker.EntitySystem.Stats.ModifiableValue", "ModifierList"), ("Kingmaker.Items.ItemEntity", "m_Enchantments"),
   ("Kingmaker.RuleSystem.Rules.RuleCalculateAttackBonus", "m_InnerRule"), ("Kingmaker.RuleSystem.Rules.Modifier", "Value"),
   ("Kingmaker.RuleSystem.Rules.Damage.DamageTypeDescription+PhysicalData", "Enhancement"), ("Kingmaker.RuleSystem.Rules.Damage.DamageTypeDescription+PhysicalData", "EnhancementTotal")
  }) { var type = a.GetType(pair.Item1, true); FieldInfo f = null; for(var t = type; t != null && f == null; t=t.BaseType) f=t.GetField(pair.Item2, BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly); if(f == null) throw new Exception("Missing " + pair); count++; }
  foreach(var name in new[]{"RuleCalculateAttackBonus", "RuleCalculateAC", "RuleCalculateAttacksCount", "RuleCalculateAttackBonusWithoutTarget", "RuleCalculateWeaponStats"}) {
   if(a.GetType("Kingmaker.RuleSystem.Rules." + name, true).GetMethod("OnTrigger") == null) throw new Exception(name); count++;
  }
  var ability = a.GetType("Kingmaker.UnitLogic.Abilities.AbilityData", true);
  if (ability.GetMethod("CalculateParams", Type.EmptyTypes) == null || ability.GetMethod("WouldCurrentlyTarget") == null) throw new Exception("Spell observer methods missing");
  count += 2;
  Console.WriteLine("PASS: " + count + " supplied-game metadata layout checks; no game code executed");
 }
}
