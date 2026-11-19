using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52J's engines (docs/DONOR_REPLACEMENT.md, phase 2): eight Rolls-Royce F130s (the BR725 family, 17,000 lbf,
    /// bypass ratio about 4.4), two per pod, each with its exhaust nozzle. Every Turbofan and JetNozzle setting is set here
    /// or in BuildEngines (thrust, its curves, fuel flow, the pod as critical part, core armour), with its reason.
    /// </summary>
    public static partial class B52Builder
    {
        // The game's engine: RPM runs from idle (minRPM) to maxRPM; thrust is staticThrust x (RPM - 0.85 idle) / (max - idle).
        // RPM is in % N1 here. Ground idle of a big high-bypass fan is about 28% N1, which gives 0.15 x 28 / 72 = 6% of
        // full thrust at idle, as on a real turbofan.
        const float N1Idle = 28f, N1Max = 100f;
        // Spool-up: certification asks for 15% to 95% thrust within 5 s; a BR725-class fan does it in about 6 s from
        // flight idle, i.e. 72% N1 in 6 s.
        const float N1SpoolRate = 12f;
        // Engine start, from rest to idle: about 25 s on the ground (air start the same in the game).
        const float N1StartRate = N1Idle / 25f;
        // The engine runs while the air is denser than this (kg/m3): 0.16 is about 16 km, a little above the F130's
        // certified ceiling (51,000 ft on the Gulfstream G650) and the B-52's service ceiling (50,000 ft).
        const float MinDensity = 0.16f;

        static void SpecEngine(Component tf, Component jn, int n, bool silent, int podIndex)
        {
            SetF(tf, "minRPM", N1Idle);
            SetF(tf, "maxRPM", N1Max);
            SetF(tf, "spoolRate", N1SpoolRate);
            SetF(tf, "startupRate", N1StartRate);
            SetF(tf, "minDensity", MinDensity);
            // Fixed-geometry nozzles and no thrust vectoring; the throttle maps straight to N1; no split throttle on yaw.
            Set(tf, "thrustVectoring", p => p.vector3Value = Vector3.zero);
            Set(tf, "thrustVectoringGain", p => p.vector3Value = Vector3.one);
            SetF(tf, "thrustVectoringMaxAirspeed", 0f);
            Set(tf, "throttleRemap", p => p.vector2Value = new Vector2(0f, 1f));
            SetF(tf, "splitThrustFactor", 0f);
            // Thrust starts to fall once the engine is below 70 hp (fan and core damage: the game scales thrust with
            // condition below this threshold, and the engine fails at 0).
            SetF(tf, "damageThreshold", 70f);
            // Core armour beyond B52Builder's SetArmor: the fan case's containment ring stops light fragments; the fire
            // zone has extinguishers (a little fire armour); overpressure: game default (only rotors use it).
            SetF(tf, "armorProperties.blastArmor", 10f);
            SetF(tf, "armorProperties.fireArmor", 2f);
            SetF(tf, "armorProperties.overpressureLimit", 5f);
            // The B-52 numbers its engines 1 to 8 from the left wingtip.
            Set(tf, "failureMessage", p => p.stringValue = $"ENGINE {n} FIRE");
            B52DonorAudit.Chose(tf, "failureMessageAudio");      // the game's engine-fire call-out
            // Turbine whine: the fan's pitch at full N1, lower than a small jet's; a little different per pod (+-6%) so the
            // four sound sources don't phase. The second engine of each pod is silent (BuildEngines: sixteen loops of one
            // sound were hollow and far too loud); its pitch is set all the same.
            SetF(tf, "turbineMaxPitch", 1.2f * (0.94f + 0.04f * podIndex));   // +-6%: apart even at idle N1

            // Nozzle. One per engine, so it takes all the thrust; no vectoring on pitch or roll.
            SetF(jn, "thrustProportion", 1f);
            SetF(jn, "pitchThrust", 0f);
            SetF(jn, "rollThrust", 0f);
            SetF(jn, "thrustMaxVolume", silent ? 0f : 0.55f);    // eight engines: each quieter than a twin's
            // Infrared: a high-bypass fan mixes most of its flow cold, so each F130's plume is far cooler than the donor's
            // low-bypass jets (1-4): about half. Eight of them still make the B-52 a large IR target.
            SetF(jn, "IRMin", 0.5f);
            SetF(jn, "IRMax", 2.0f);
            // Heat haze behind the core nozzle (about 0.8 m across, inside the fan duct): smaller, fainter and shorter
            // than the donor's, thinning with airspeed.
            B52DonorAudit.Chose(jn, "heatHaze");                 // every value below set for the F130
            Set(jn, "heatHaze", p =>
            {
                p.FindPropertyRelative("sizeMin").floatValue = 1.1f; p.FindPropertyRelative("sizeMax").floatValue = 1.9f;
                p.FindPropertyRelative("speedMin").floatValue = 15f; p.FindPropertyRelative("speedMax").floatValue = 60f;
                p.FindPropertyRelative("opacityMin").floatValue = 0.35f; p.FindPropertyRelative("opacityMax").floatValue = 0.22f;
                p.FindPropertyRelative("lifeMin").floatValue = 1.2f; p.FindPropertyRelative("lifeMax").floatValue = 0.3f;
                p.FindPropertyRelative("airspeedLifeFactor").floatValue = 0.01f;
                p.FindPropertyRelative("rateMin").floatValue = 8f; p.FindPropertyRelative("rateMax").floatValue = 40f;
            });
            SetRef(jn, "glow", null);                            // no afterburner, nothing glows
            B52DonorAudit.Chose(jn, "failureEffect");            // the game's turbine fire
            B52DonorAudit.Chose(jn, "priority");                 // runtime
            B52DonorAudit.Chose(jn, "turbojet");                 // none: the engine is a Turbofan (set in BuildEngines)
        }
    }
}
