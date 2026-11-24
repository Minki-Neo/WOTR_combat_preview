using System;
using System.Collections.Generic;

namespace CombatPreview
{
    internal sealed class PreviewReadiness
    {
        readonly double start;
        readonly int frame;
        double? parametersReady, hpReady;
        bool hadParameters, hadHp;
        int regressions;
        internal PreviewReadiness(double now, int frame) { start = now; this.frame = frame; }
        internal Dictionary<string, object> Observe(double now, bool parameters, bool hp)
        {
            if (now < start) return new Dictionary<string, object> { { "status", "ClockRegression" } };
            double elapsed = Math.Max(0, (now - start) * 1000);
            if (parameters && !parametersReady.HasValue) parametersReady = elapsed;
            if (hp && !hpReady.HasValue) hpReady = elapsed;
            if (hadParameters && !parameters || hadHp && !hp) regressions++;
            hadParameters = parameters; hadHp = hp;
            return new Dictionary<string, object> { { "firstHoverFrame", frame },
                { "firstParametersReadyMs", parametersReady }, { "firstHpProjectionReadyMs", hpReady },
                { "parametersReadyNow", parameters }, { "hpProjectionReadyNow", hp }, { "readinessRegressions", regressions },
                { "scope", "first panel acquisition to result; includes game readiness, refresh scheduling and time between rehovers, not calculation CPU time" } };
        }
    }
}
