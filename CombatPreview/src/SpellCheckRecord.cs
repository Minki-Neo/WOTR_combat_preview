using System;
using System.Collections.Generic;

namespace CombatPreview
{
    public sealed class SpellCheckRecord
    {
        public string Kind, Type, Status, Source, Reason;
        public double? Probability;
        public double? AcOnlyProbability, VisibilityMiss;
        public double? HitBeforeImages, MirrorNearMiss;
        public int? MirrorImages;
        public bool AutomaticHit;
        public bool? PersistentSave;
        public string VisibilitySource;
        public Dictionary<string, object> D20Model;
        public int? Bonus, Threshold;
        public Dictionary<string, object> Data()
        {
            return new Dictionary<string, object> {
                { "kind", Kind }, { "type", Type }, { "status", Status }, { "source", Source }, { "reason", Reason },
                { "probability", Probability }, { "bonus", Bonus }, { "threshold", Threshold },
                { "acOnlyProbability", AcOnlyProbability }, { "visibilityMiss", VisibilityMiss }, { "visibilitySource", VisibilitySource },
                { "mirrorImages", MirrorImages }, { "hitBeforeImages", HitBeforeImages }, { "mirrorNearMiss", MirrorNearMiss },
                { "automaticHit", AutomaticHit }, { "persistentSave", PersistentSave },
                { "d20Model", D20Model },
                { "meaning", Kind == "save" ? "target-fails-save" : Kind == "attack" ? "attack-hits-after-modeled-miss-chance" : "SR-penetration-only" }
            };
        }
    }
}
