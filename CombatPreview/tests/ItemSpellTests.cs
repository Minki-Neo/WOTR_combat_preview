using System;
using CombatPreview;
static class ItemSpellTests
{
    public static void Run()
    {
        int checks = 0;
        void Check(ItemSpellInput input, int dc, int level, string reason)
        {
            checks++;
            if (!ItemSpellRules.Calculate(input, out int actualDc, out int actualLevel) || actualDc != dc || actualLevel != level)
                throw new Exception(reason + ": " + actualDc + "/" + actualLevel);
        }
        ItemSpellInput Scroll() => new ItemSpellInput { Kind = 2, Dc = 16, Level = 7, SpellLevel = 4, Intelligence = 7, Charisma = 3 };
        var x = Scroll(); Check(x, 16, 7, "ordinary scroll uses item DC/CL, not caster spellbook");
        x.ScrollMastery = true; Check(x, 21, 7, "scroll mastery replaces DC with intelligence-based item spell DC");
        x.SpecializedLevel = 13; Check(x, 21, 13, "scroll specialization supplies caster's specialized book CL");
        x.Level = 15; Check(x, 21, 13, "ordinary scroll specialization replaces, not maximizes, item CL");
        x.Crafted = true; Check(x, 16, 15, "crafted scroll preserves stored DC and takes maximum CL");
        x.Level = 7; Check(x, 16, 13, "crafted scroll specialized CL may raise stored CL");
        x.ItemLevelBonus = 2; x.TricksterBoost = true; x.Mythic = 3;
        Check(x, 16, 18, "item-specific CL bonuses and trickster mythic boost apply after selection");
        x = Scroll(); x.Kind = 1; x.WandMastery = true; x.EldritchWandMastery = true;
        Check(x, 21, 7, "wand mastery takes precedence over eldritch charisma mastery");
        x.WandMastery = false; Check(x, 17, 7, "eldritch wand mastery uses charisma");
        x.Kind = 2; Check(x, 16, 7, "wand-only mastery does not affect scroll DC");
        x.Kind = 3; x.PotionLevel = 12; Check(x, 16, 12, "enhance potion uses stored class level");
        x.Crafted = true; Check(x, 16, 7, "crafted potion ignores enhance-potion replacement");
        x = Scroll(); x.Level = int.MaxValue; x.ItemLevelBonus = 1;
        checks++; if (ItemSpellRules.Calculate(x, out _, out _)) throw new Exception("CL overflow must be unavailable");
        x = Scroll(); x.SpellLevel = -1;
        checks++; if (ItemSpellRules.Calculate(x, out _, out _)) throw new Exception("invalid item spell level refused");
        Console.WriteLine("PASS: " + checks + " independent item spell parameter / precedence assertions");
    }
}
