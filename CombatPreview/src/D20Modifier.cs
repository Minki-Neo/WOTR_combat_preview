using System;
namespace CombatPreview
{
    // One d20 modifier as ModifyD20 sets it on a roll.
    class D20Modifier
    {
        public string Source;
        public bool Replace;
        public int Roll;
        public int Rerolls;
        public bool TakeBest;
        public bool OnlyIfFailed;
        // Added to every saving throw stat while the roll is made (AddSavingThrowBonus).
        public int SaveBonus;
        // 0..100, 100 when it always applies.
        public int Chance = 100;
        // WotR: "reroll only if succeeded", and a test of the first die against a value (ModifyD20.RollConditionType:
        // 1 more than, 2 less than, 3 less or equal, 4 more or equal, 5 equal).
        public bool OnlyIfSuccess;
        public int Compare;
        public int CompareValue;

        // Whether the modifier applies to a roll whose first die is f (ModifyD20.ShouldReroll).
        internal bool Applies(int f, Func<int, bool> failed)
        {
            if (OnlyIfFailed) return failed(f);
            if (OnlyIfSuccess) return !failed(f);
            switch (Compare)
            {
                case 1: return f > CompareValue;
                case 2: return f < CompareValue;
                case 3: return f <= CompareValue;
                case 4: return f >= CompareValue;
                case 5: return f == CompareValue;
                default: return true;
            }
        }
    }

}
