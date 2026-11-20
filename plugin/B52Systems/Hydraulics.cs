using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52's damaged systems (docs/DAMAGE_MODEL.md, step 7).
    ///
    /// - Spoilers: the B-52 rolls with spoilers only, and they are hydraulic (its elevators and rudder are moved by
    ///   aerodynamic control tabs through cables, so they keep working without hydraulic power). Modelled as: each wing's
    ///   spoilers are powered by pumps on that wing's engines, so they work while at least one engine on that wing runs.
    ///   Without pressure (or with the spoiler panels shot through to 0 hp) they go flush and stay there
    ///   (ControlSurface.SetLocked). The real aircraft's plumbing is more involved (six systems); this keeps what matters to
    ///   the pilot: an aircraft that has lost a wing's engines loses roll control into that side.
    /// - Landing gear: a gear leg whose mounting part is shot through (0 hit points) collapses as soon as it carries load.
    ///   The game already breaks a leg that is over-compressed or side-loaded beyond its spring.
    ///
    /// - Wheel brakes: anti-skid. The game's brakes pull the full tyre grip (load x 0.75 on dry concrete), which stopped
    ///   the B-52 at 1 g and stood it on its nose. The real aircraft's anti-skid and brake heat hold a maximum-effort stop
    ///   to about 0.35 g: above taxi speed (15 m/s) each leg's brake is held to 45% of full grip. Slower, for taxiing,
    ///   turning on the brakes and holding at full thrust, they work as the game made them.
    ///
    /// Runs where the aircraft is flown (controls and gear are simulated there). Its brake limit runs after the gear in each
    /// physics step, on the B-52's own gear only.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    internal class Hydraulics : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<Turbofan, float> Rpm = AccessTools.FieldRefAccess<Turbofan, float>("currentRPM");
        private static readonly AccessTools.FieldRef<Turbofan, float> MinRpm = AccessTools.FieldRefAccess<Turbofan, float>("minRPM");
        private static readonly AccessTools.FieldRef<Turbofan, bool> Operable = AccessTools.FieldRefAccess<Turbofan, bool>("operable");
        private static readonly AccessTools.FieldRef<LandingGear, AeroPart> GearPart = AccessTools.FieldRefAccess<LandingGear, AeroPart>("attachedPart");
        private static readonly AccessTools.FieldRef<LandingGear, float> Compression = AccessTools.FieldRefAccess<LandingGear, float>("compressionDistance");
        private static readonly AccessTools.FieldRef<LandingGear, float> BrakeStrength = AccessTools.FieldRefAccess<LandingGear, float>("brakeStrength");
        private const float AntiSkidSpeed = 15f;
        // The gear eases its brake towards the pedal by a tenth each step (Lerp at 5/s, 50 steps a second); held here at
        // 0.39, it brakes at 0.39 + 0.1 x (1 - 0.39) = 0.45 of full grip.
        private const float AntiSkidHold = 0.39f;
        private static readonly MethodInfo BreakWheel = AccessTools.Method(typeof(LandingGear), "BreakWheel");

        private Aircraft aircraft;
        private readonly Turbofan[][] engines = new Turbofan[2][];
        private readonly ControlSurface[] spoilers = new ControlSurface[2];
        private readonly UnitPart[] spoilerParts = new UnitPart[2];
        private readonly bool[] locked = new bool[2];
        internal bool SideLocked(int side) => locked[side];   // that wing's spoilers have lost their hydraulics (Annunciators)
        private LandingGear[] gear;
        private readonly HashSet<LandingGear> broken = new HashSet<LandingGear>();
        private float next;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            var all = GetComponentsInChildren<Turbofan>(true);
            string[] side = { "_L", "_R" };
            for (int i = 0; i < 2; i++)
            {
                engines[i] = all.Where(e => e.GetComponentInParent<AeroPart>() is AeroPart p && p.name.EndsWith(side[i])).ToArray();
                var sp = GetComponentsInChildren<AeroPart>(true).FirstOrDefault(p => p.name == "spoilers" + side[i]);
                spoilerParts[i] = sp;
                spoilers[i] = sp ? sp.GetComponent<ControlSurface>() : null;
            }
            gear = GetComponentsInChildren<LandingGear>(true);
        }

        private void FixedUpdate()
        {
            if (!aircraft || !aircraft.LocalSim || gear == null) return;
            foreach (var g in gear)
            {
                if (!g || !g.enabled || BrakeStrength(g) <= AntiSkidHold) continue;
                var part = GearPart(g);
                if (part && part.rb && Vector3.ProjectOnPlane(part.rb.GetPointVelocity(g.transform.position), Vector3.up).magnitude > AntiSkidSpeed)
                    BrakeStrength(g) = AntiSkidHold;
            }
        }

        private void Update()
        {
            if (!aircraft || !aircraft.LocalSim || Time.time < next) return;
            next = Time.time + 0.25f;
            for (int i = 0; i < 2; i++)
            {
                if (!spoilers[i]) continue;
                // A pump turns with a working engine still on the wing: a failed engine, or one torn away with its pod,
                // gives no pressure even while it is still spinning down.
                bool pressure = engines[i].Any(e => e && Operable(e) && !(e.GetComponentInParent<AeroPart>() is AeroPart p && p.IsDetached())
                                                    && Rpm(e) > 0.5f * MinRpm(e));
                bool panels = spoilerParts[i] && spoilerParts[i].hitPoints > 0f;
                bool lockNow = !pressure || !panels;
                if (lockNow != locked[i])
                {
                    locked[i] = lockNow;
                    spoilers[i].SetLocked(lockNow);
                    Plugin.Log.LogInfo($"B-52J {aircraft.name}: {(i == 0 ? "left" : "right")} spoilers {(lockNow ? (pressure ? "shot through, flush" : "lost hydraulic pressure (no engine on that wing), flush") : "powered again")}");
                }
            }
            foreach (var g in gear)
            {
                if (!g || !g.enabled || broken.Contains(g)) continue;
                var part = GearPart(g);
                if (part && part.hitPoints <= 0f && Compression(g) > 0.02f)
                {
                    broken.Add(g);
                    BreakWheel?.Invoke(g, null);
                    g.enabled = false;
                    Plugin.Log.LogInfo($"B-52J {aircraft.name}: {g.name} collapsed ({part.name} shot through, {part.hitPoints:F0} hp)");
                }
            }
        }
    }
}
