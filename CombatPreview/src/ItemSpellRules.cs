using System;
namespace CombatPreview
{
    internal sealed class ItemSpellInput
    {
        internal int Kind, Dc, Level, SpellLevel, Intelligence, Charisma, ItemLevelBonus, Mythic;
        internal bool Crafted, ScrollMastery, WandMastery, EldritchWandMastery, TricksterBoost;
        internal int? SpecializedLevel, PotionLevel;
    }
    internal static class ItemSpellRules
    {
        // ItemStatHelper/GetParamsFromItem precedence: crafted DC bypasses mastery;
        // ordinary specialized scroll CL replaces item CL; crafted CL uses max.
        internal static bool Calculate(ItemSpellInput input, out int dc, out int level)
        {
            dc = level = 0;
            if (input == null || input.SpellLevel < 0 || input.Level < 0) return false;
            long d = input.Dc, cl = input.Level;
            if (!input.Crafted)
            {
                if (input.Kind == 1 && input.WandMastery || input.Kind == 2 && input.ScrollMastery)
                    d = 10L + input.SpellLevel + input.Intelligence;
                else if (input.Kind == 1 && input.EldritchWandMastery) d = 10L + input.SpellLevel + input.Charisma;
            }
            if (input.Kind == 2 && input.SpecializedLevel.HasValue)
                cl = input.Crafted ? Math.Max(input.Level, input.SpecializedLevel.Value) : input.SpecializedLevel.Value;
            else if (!input.Crafted && input.Kind == 3 && input.PotionLevel.HasValue) cl = input.PotionLevel.Value;
            cl += input.ItemLevelBonus + (input.TricksterBoost ? (long)input.Mythic : 0);
            if (d < 0 || d > int.MaxValue || cl < 0 || cl > int.MaxValue) return false;
            dc = (int)d; level = (int)cl; return true;
        }
    }
}
