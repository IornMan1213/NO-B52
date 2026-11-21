using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// A B-52-like approach speed for AI-flown B-52s. The game's AI flies the pattern at the aircraft's corner speed plus
    /// 2 m/s per 100 m from the pattern point, and the final turn and approach at landing speed plus 1.5 m/s per 100 m
    /// from touchdown: about 200 m/s 10 km out. A light jet sheds that on short final; a clean 200 t B-52 at idle
    /// doesn't, so AI B-52s reached the runway at 400 km/h, porpoising, and broke up (tests/ingame/b52_ai.txt).
    /// After the game's AI has set its inputs, this resets the throttle of an AI B-52 to hold the real B-52's profile:
    /// at most ApproachCap in the pattern, easing from about 110 m/s 10 km out to landing speed at touchdown, and gear
    /// down once on final and slow enough (the real crew lowers it early for the drag). Only B-52s, only these three
    /// approach modes; the game's own touchdown, rollout and go-around logic is untouched.
    /// </summary>
    [HarmonyPatch(typeof(AIPilotLandingState), "RunMode")]
    internal static class AiApproach
    {
        internal const float ApproachCap = 125f, PerMetre = 0.004f, GearSpeed = 140f;
        private const int JoiningPattern = 0, TurningToFinal = 1, StabilizedApproach = 2;

        private static readonly FieldInfo ModeF = AccessTools.Field(typeof(AIPilotLandingState), "landingMode");
        private static readonly FieldInfo UsageF = AccessTools.Field(typeof(AIPilotLandingState), "runwayUsage");
        private static readonly FieldInfo SpeedF = AccessTools.Field(typeof(AIPilotLandingState), "adjustedLandingSpeed");
        private static readonly FieldInfo ParamsF = AccessTools.Field(typeof(AIPilotLandingState), "aircraftParameters");
        private static readonly FieldInfo AircraftF = AccessTools.Field(typeof(PilotBaseState), "aircraft");
        private static readonly FieldInfo InputsF = AccessTools.Field(typeof(PilotBaseState), "controlInputs");

        internal static float Target;   // last target speed, for the test driver
        private const float MinFinalThrottle = 0.08f;   // keeps the airbrake (the spoilers) shut on final

        private static bool OnFinalNow(AIPilotLandingState s, int mode) =>
            mode == StabilizedApproach || (mode == TurningToFinal && ReachedF != null && ReachedF.GetValue(s) is bool r && r);
        private static readonly FieldInfo ReachedF = AccessTools.Field(typeof(AIPilotLandingState), "reachedFinal");
        private static float nextLog;

        /// <summary>Approach speed at distance d (m) from touchdown, for a landing speed vLand.</summary>
        internal static float TargetSpeed(float vLand, float d) => Mathf.Min(vLand + PerMetre * Mathf.Max(d - 500f, 0f), ApproachCap);

        /// <summary>Set while an AI B-52 flies its final turn or approach, for <see cref="AimFlightPath"/>.</summary>
        internal static Aircraft Approaching;
        internal static bool OnFinal;                 // lined up (reachedFinal, or the stabilized approach)
        internal static GlobalPosition Touchdown;

        private static void Prefix(AIPilotLandingState __instance)
        {
            Approaching = null;
            if (ModeF == null || AircraftF == null || UsageF == null) return;
            var ac = AircraftF.GetValue(__instance) as Aircraft;
            if (!Plugin.IsB52(ac)) return;
            int mode = System.Convert.ToInt32(ModeF.GetValue(__instance));
            if (mode != JoiningPattern && mode != TurningToFinal && mode != StabilizedApproach) return;
            try { Touchdown = ((Airbase.Runway.RunwayUsage)UsageF.GetValue(__instance)).GetTouchdownPoint(); } catch { return; }
            OnFinal = mode == StabilizedApproach || (mode == TurningToFinal && ReachedF != null && ReachedF.GetValue(__instance) is bool r && r);
            Approaching = ac;
        }

        /// <summary>Debug (Plugin.LogAiApproach): where the pitching moment comes from. Centre of mass, attitude, angle of
        /// attack, the fly-by-wire's pitch-rate demand and the actual rate, and each aero part's moment about the CoM
        /// (nose-up positive), largest first.</summary>
        private static void LogPitchBalance(Aircraft ac)
        {
            var rb = ac.rb; var t = ac.transform;
            var com = rb.worldCenterOfMass;
            var lc = t.InverseTransformPoint(com);
            var v = t.InverseTransformDirection(rb.velocity);
            float aoa = Mathf.Atan2(-v.y, v.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            var fbw = ac.GetControlsFilter()?.GetFlyByWire();
            var parts = new System.Collections.Generic.List<(string n, float m)>();
            float total = 0f;
            foreach (var p in ac.GetComponentsInChildren<AeroPart>())
            {
                if (p.rb != rb) continue;
                var f = AeroCentres.Force(p);
                float m = Vector3.Dot(Vector3.Cross(AeroCentres.WorldAC(p) - com, f), -t.right);   // nose-up positive
                total += m;
                if (Mathf.Abs(m) > 2e4f) parts.Add((p.name, m));
            }
            parts.Sort((a, b) => Mathf.Abs(b.m).CompareTo(Mathf.Abs(a.m)));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < Mathf.Min(10, parts.Count); i++) sb.Append($" {parts[i].n} {parts[i].m / 1e6f:+0.00;-0.00}");
            Plugin.Log.LogInfo($"B-52J AI pitch: CoM z {lc.z:F2} y {lc.y:F2}, pitch {pitch:F1}, AoA {aoa:F1}, " +
                               $"rate {(fbw != null ? fbw.GetPitchAngVel() : 0f):F3} (want {(fbw != null ? fbw.GetTargetPitchAngVel() : 0f):F3}), " +
                               $"aero moment {total / 1e6f:+0.00;-0.00} MN.m:{sb}");
        }

        private static void Postfix(AIPilotLandingState __instance)
        {
            Approaching = null;
            if (ModeF == null || UsageF == null || AircraftF == null || InputsF == null) return;
            var ac = AircraftF.GetValue(__instance) as Aircraft;
            if (!Plugin.IsB52(ac) || ac.disabled) return;
            int mode = System.Convert.ToInt32(ModeF.GetValue(__instance));
            if (mode != JoiningPattern && mode != TurningToFinal && mode != StabilizedApproach) return;
            var inputs = InputsF.GetValue(__instance) as ControlInputs;
            var par = ParamsF?.GetValue(__instance) as AircraftParameters;
            if (inputs == null || par == null) return;

            float vLand = SpeedF != null ? (float)SpeedF.GetValue(__instance) : par.landingSpeed;
            float d = 20000f;
            try { d = FastMath.Distance(((Airbase.Runway.RunwayUsage)UsageF.GetValue(__instance)).GetTouchdownPoint(), ac.GlobalPosition()); }
            catch { }
            Target = mode == JoiningPattern ? ApproachCap : TargetSpeed(vLand, d);
            if (Plugin.LogAiApproach != null && Plugin.LogAiApproach.Value && Time.time >= nextLog)
            {
                nextLog = Time.time + 2f;
                float h = 0f;
                try { h = ac.GlobalPosition().y - ((Airbase.Runway.RunwayUsage)UsageF.GetValue(__instance)).GetTouchdownPoint().y; } catch { }
                Plugin.Log.LogInfo($"B-52J AI approach: mode {mode}, final {ReachedF?.GetValue(__instance)}, {d:F0} m to touchdown, {h:F0} m above it " +
                                   $"(3 deg path {0.05f * d:F0} m), {ac.speed:F0} m/s (target {Target:F0}), climb {ac.rb.velocity.y:F1} m/s, " +
                                   $"pitch input {inputs.pitch:F2}, throttle {inputs.throttle:F2}");
                LogPitchBalance(ac);
            }
            // only ever slower than the game asks for: never speed a B-52 up on the approach; and below the glide path
            // (under 70% of a 3 deg path's height) the game's own throttle stands, so a low, slow B-52 can power out
            float h0 = float.MaxValue;
            try { h0 = ac.GlobalPosition().y - ((Airbase.Runway.RunwayUsage)UsageF.GetValue(__instance)).GetTouchdownPoint().y; } catch { }
            bool low = mode != JoiningPattern && h0 < 0.7f * 0.05f * d;
            float fwd = Vector3.Dot(ac.transform.forward, ac.rb.velocity);
            float throttle = Mathf.Clamp((Target + 5f - fwd) * 0.1f, 0f, par.cruiseThrottle);
            if (!low) inputs.throttle = Mathf.Min(inputs.throttle, throttle);
            // On final, more than 400 m out: never quite idle, and power for the glide path. At exactly zero throttle the
            // game opens the airbrake, which on the B-52 is the spoilers: raised together they dump lift, and with full
            // nose-up stick it still came down at 11-14 m/s (a 10 deg glide) and hit 800 m short. A sink faster than the
            // 3 deg path (plus 2 m/s) adds power.
            // Short final (the game's own flare and touchdown, the last 400 m): it cuts the throttle to zero, which opens
            // the spoilers and dumps lift just before the wheels touch; the B-52 dropped onto the runway and broke its
            // wings. Until touchdown, keep the spoilers shut and add power against a sink over 3 m/s.
            if (mode == StabilizedApproach && d <= AimFlightPath.HandBack && ac.radarAlt > 0.5f)
            {
                float sinkExtra = Mathf.Clamp((-ac.rb.velocity.y - 3f) * 0.1f, 0f, 0.5f);
                inputs.throttle = Mathf.Clamp(Mathf.Max(inputs.throttle, MinFinalThrottle) + sinkExtra, 0f, par.cruiseThrottle);
            }
            bool wellHigh = h0 - 0.05f * d > AimFlightPath.HighBand;   // well above the path: the speedbrake may open
            if (mode != JoiningPattern && OnFinalNow(__instance, mode) && d > AimFlightPath.HandBack && !wellHigh)
            {
                float pathSink = ac.speed * Mathf.Tan(3f * Mathf.Deg2Rad) + 2f;
                float extra = Mathf.Clamp((-ac.rb.velocity.y - pathSink) * 0.06f, 0f, 0.6f);
                inputs.throttle = Mathf.Clamp(Mathf.Max(inputs.throttle, MinFinalThrottle) + extra, 0f, par.cruiseThrottle);
            }
            if (mode != JoiningPattern && ac.speed < GearSpeed && ac.gearState == LandingGear.GearState.LockedRetracted)
                ac.SetGear(deployed: true);
        }
    }

    /// <summary>
    /// The B-52's glide path. In its final turn the game's AI points the aircraft's nose at the glide path ahead (AutoAim
    /// with aimVelocity off); a slow B-52 with gear and flaps down flies 6-9 deg below where its nose points, so that put
    /// it in a 10-14 deg dive, stick held nose-up, into the ground 7 km short. On the stabilized approach the AI chases
    /// glide-path errors with full-stick corrections that a 200 t bomber answers slowly: it swung +-20 m/s and hit short
    /// (tests/ingame/b52_ai.txt). For a B-52 lined up on final, more than 400 m out, this aims the flight path (as the
    /// game does on the approach) along the runway course at a 3 deg descent, corrected gently for height error and kept
    /// between 4.5 deg down (about 7 m/s sink at approach speed) and 2 deg up: gently, because while the aircraft
    /// banks the game's AutoAim scales its pitch correction down to a tenth, so a dive begun in a turn can't be stopped. The game's flare and touchdown are untouched.
    /// </summary>
    [HarmonyPatch(typeof(AutopilotPlane), nameof(AutopilotPlane.AutoAim))]
    internal static class AimFlightPath
    {
        private const float Glide = 3f, MinGamma = -4.5f, MaxGamma = 2f, HighPerMetre = 0.015f, LowPerMetre = 0.05f;
        internal const float HandBack = 400f, HighBand = 80f;
        private const float SteepGamma = -6f;
        // The pattern's bank limit. The game lets the AI bank up to 135 deg in the pattern and the final turn; a B-52 rolls
        // slowly and overbanked, and pulling in a steep bank only tightened a spiral dive (full nose-up stick at 38 m/s
        // sink, into the ground 7 km out). Heavy bombers fly the pattern at 30 deg at most.
        private const float MaxBank = 30f;

        private static void Prefix(AutopilotPlane __instance, ref GlobalPosition destination, ref bool aimVelocity, ref float bankAllowed)
        {
            var ac = AiApproach.Approaching;
            if (ac == null || __instance.aircraft != ac) return;
            bankAllowed = Mathf.Min(bankAllowed, MaxBank);
            if (!AiApproach.OnFinal) return;                       // still turning in: the game's own aim
            aimVelocity = true;                                    // the flight path, not the nose
            var here = ac.GlobalPosition();
            Vector3 toTd = AiApproach.Touchdown - here;
            float d = new Vector2(toTd.x, toTd.z).magnitude;
            if (d < HandBack) return;
            float h = -toTd.y;                                     // height above the touchdown point
            float err = h - d * Mathf.Tan(Glide * Mathf.Deg2Rad);  // + : above the path
            // above the path: ease down gently; below it: level off quickly (60 m low = level flight)
            // well above the path (over 80 m) it may come down at up to 6 deg, with the speedbrake (AiApproach lets it open)
            float minG = err > HighBand ? SteepGamma : MinGamma;
            float gamma = Mathf.Clamp(-Glide - err * (err > 0f ? HighPerMetre : LowPerMetre), minG, MaxGamma);
            Vector3 course = destination - here; course.y = 0f;
            if (course.sqrMagnitude < 1f) return;
            course.Normalize();
            destination = here + (course + Vector3.up * Mathf.Tan(gamma * Mathf.Deg2Rad)) * 1000f;
        }
    }
}
