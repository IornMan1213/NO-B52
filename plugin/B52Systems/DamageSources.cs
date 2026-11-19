using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52's fuel tanks (docs/DAMAGE_MODEL.md, step 5).
    ///
    /// Each tank is its own object at the centroid of its fuel (the builder's "tank_&lt;part&gt;"), so a leak trails from
    /// the tank, a fire burns there and the tank fire's heat reaches what is around the tank. Two game paths assume
    /// a tank sits on its part's own object, and are routed here:
    /// - the fire / rupture message (Unit.RpcFuelTankStatus) finds the tank by part;
    /// - a crash impact is a collision message, which arrives on the aircraft's rigidbody: TankImpacts passes it to
    ///   the tanks of the part that hit.
    ///
    /// Both only touch the B-52's own tanks ("tank_&lt;part&gt;"), and the game's handler still runs after, so tanks other
    /// mods hang on the aircraft (the Aryx weapons pack's drop tanks) work as they do on any aircraft.
    ///
    /// Fuel order: see FuelTransfer.
    /// </summary>
    internal static class FuelTanks
    {
        internal static readonly AccessTools.FieldRef<FuelTank, UnitPart> Part = AccessTools.FieldRefAccess<FuelTank, UnitPart>("part");
        private static readonly AccessTools.FieldRef<FuelTank, float> LastCollision = AccessTools.FieldRefAccess<FuelTank, float>("lastCollision");

        /// <summary>One of the B-52's own tanks (built by B52Builder as "tank_&lt;part&gt;" on that part), not another mod's.</summary>
        internal static bool Own(FuelTank t, Aircraft a)
        {
            var p = t ? Part(t) : null;
            return p && p.parentUnit == a && t.name == "tank_" + p.name;
        }

        internal static void ImpactOn(UnitPart part)
        {
            if (!(part.parentUnit is Aircraft a)) return;
            foreach (var t in a.GetFuelTanks())
                if (Own(t, a) && Part(t) == part) LastCollision(t) = Time.timeSinceLevelLoad;
        }

        [HarmonyPatch]
        private static class StatusMessage
        {
            private static MethodBase TargetMethod() =>
                typeof(Unit).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .First(m => m.Name.StartsWith("UserCode_RpcFuelTankStatus"));

            private static bool Prefix(Unit __instance, byte partID, bool ruptured, bool onFire)
            {
                if (!(__instance is Aircraft a) || !Plugin.IsB52(a)) return true;
                foreach (var t in a.GetFuelTanks())
                    if (Own(t, a) && Part(t).id == partID) t.UpdateStatus(ruptured, onFire);
                return true;   // the game's handler finds no tank on the part itself, but another mod's might be there
            }
        }

    }

    /// <summary>
    /// The B-52's fuel transfer (docs/DAMAGE_MODEL.md, step 5): the fuselage tanks empty first, then the centre wing,
    /// then the wing tanks from the root out. Fuel out in the wing weighs against the lift and eases the bending at
    /// the wing root, so the wing keeps its fuel to the end.
    ///
    /// The fuel draw itself is left to the game (or to whatever mod orders it: the Aryx weapons pack draws its drop
    /// tanks first, then the internal tanks in proportion). After each physics step this pumps fuel between the
    /// B-52's own tanks: whatever the engines took from a tank later in the order is made up from the earliest tanks
    /// that still have fuel, as the real aircraft's transfer pumps do. It never touches another mod's tanks, never
    /// pumps into a tank that is leaking or on a part that has come off, and runs only where the aircraft is flown
    /// (the game's tanks run there too).
    /// </summary>
    internal class FuelTransfer : MonoBehaviour
    {
        private static readonly string[][] Order =
        {
            new[] { "fuselage_F", "fuselage_R" },
            new[] { "B52" },
            new[] { "wingroot_L", "wingroot_R" },
            new[] { "wing1_L", "wing1_R" },
            new[] { "wing2_L", "wing2_R" },
            new[] { "wingtip_L", "wingtip_R" },
        };
        private const float MaxRate = 100f;   // kg/s the pumps move at most (far above the engines' burn)

        private static readonly AccessTools.FieldRef<FuelTank, float> LeakRate = AccessTools.FieldRefAccess<FuelTank, float>("leakRate");

        private Aircraft aircraft;
        private readonly List<(FuelTank tank, int group)> tanks = new List<(FuelTank, int)>();
        private readonly Dictionary<FuelTank, float> held = new Dictionary<FuelTank, float>();
        private bool logged;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            if (!aircraft) { enabled = false; return; }
            foreach (var t in aircraft.GetFuelTanks())
            {
                if (!FuelTanks.Own(t, aircraft)) continue;
                var name = FuelTanks.Part(t).name;
                int g = System.Array.FindIndex(Order, o => o.Contains(name));
                if (g >= 0) tanks.Add((t, g));
            }
        }

        private void FixedUpdate()
        {
            if (!aircraft || !aircraft.LocalSim || tanks.Count == 0) return;
            // The earliest group in the order that still has fuel feeds the rest.
            int source = -1;
            foreach (var (t, g) in tanks)
                if (Feeds(t) && t.fuelMass > 0.01f && (source < 0 || g < source)) source = g;
            // Each later tank is held at its level: the engines draw in bursts (about once a second), and the pumps
            // make that up over the following steps. A tank refuelled, leaking, or on a part that came off is held
            // where it is now.
            float want = 0f;
            var refill = new List<(FuelTank, float)>();
            foreach (var (t, g) in tanks)
            {
                // A drop far larger than the engines' burst (8 engines take about 12 kg a second) is the tank being set
                // to a new level (spawn, rearm), not fuel to pump back.
                if (!held.TryGetValue(t, out var h) || t.fuelMass > h || h - t.fuelMass > 200f || g <= source || !Feeds(t) || LeakRate(t) > 0f)
                { held[t] = t.fuelMass; continue; }
                float lost = h - t.fuelMass;
                if (lost > 0f) { refill.Add((t, lost)); want += lost; }
            }
            if (want <= 0f || source < 0) return;
            float avail = 0f;
            foreach (var (t, g) in tanks) if (g == source && Feeds(t)) avail += t.fuelMass;
            float move = Mathf.Min(want, avail, MaxRate * Time.fixedDeltaTime);
            if (move <= 0f) return;
            foreach (var (t, g) in tanks) if (g == source && Feeds(t)) t.UseFuel(move * t.fuelMass / avail);
            foreach (var (t, lost) in refill) t.UseFuel(-move * lost / want);   // UseFuel moves the part's mass too
            if (!logged) { logged = true; Plugin.Log.LogInfo($"B-52J fuel transfer: feeding from {string.Join(" ", Order[source])}"); }
        }

        private static bool Feeds(FuelTank t)
        {
            var p = t ? FuelTanks.Part(t) : null;
            return p && !p.IsDetached();
        }
    }

    /// <summary>Passes the B-52's ground and crash impacts to the fuel tanks of the part that hit.</summary>
    internal class TankImpacts : MonoBehaviour
    {
        private void OnCollisionEnter(Collision c)
        {
            if (c.body != null || c.contactCount == 0) return;
            var part = c.GetContact(0).thisCollider ? c.GetContact(0).thisCollider.GetComponentInParent<UnitPart>() : null;
            if (part) FuelTanks.ImpactOn(part);
        }
    }

    /// <summary>
    /// The lower deck's downward ejection seats (docs/DAMAGE_MODEL.md, step 6).
    ///
    /// The B-52's radar navigator and navigator sit on the lower deck and eject downward through hatches in the
    /// belly. The game's seat always fires along the cockpit's up axis, so for these two seats this runs the rail
    /// itself: 2.2 m down (from the seat, clear of the belly skin), driven by the seat's catapult at 60% of the
    /// upward seats' thrust, then the crewman falls free with the seat and the game's own sequence takes over
    /// (seat separation and parachute below 1000 m). The seat counts as on its rail meanwhile, so other clients
    /// run the same rail locally instead of applying network positions.
    /// </summary>
    internal static class DownwardSeats
    {
        private static readonly HashSet<string> Lower = new HashSet<string> { "RadarNav", "Navigator" };
        private static readonly HashSet<EjectionSeat> active = new HashSet<EjectionSeat>();
        private const float Rail = 2.2f, ThrustShare = 0.6f;

        private static readonly AccessTools.FieldRef<EjectionSeat, PilotDismounted> Dismounted = AccessTools.FieldRefAccess<EjectionSeat, PilotDismounted>("pilotDismounted");
        private static readonly AccessTools.FieldRef<EjectionSeat, Rigidbody> PilotRB = AccessTools.FieldRefAccess<EjectionSeat, Rigidbody>("pilotRB");
        private static readonly AccessTools.FieldRef<EjectionSeat, Rigidbody> CockpitRB = AccessTools.FieldRefAccess<EjectionSeat, Rigidbody>("cockpitRB");
        private static readonly AccessTools.FieldRef<EjectionSeat, UnitPart> CockpitPart = AccessTools.FieldRefAccess<EjectionSeat, UnitPart>("cockpitPart");
        private static readonly AccessTools.FieldRef<EjectionSeat, Collider> SeatCollider = AccessTools.FieldRefAccess<EjectionSeat, Collider>("seatCollider");
        private static readonly AccessTools.FieldRef<EjectionSeat, float> Thrust = AccessTools.FieldRefAccess<EjectionSeat, float>("thrust");
        private static readonly AccessTools.FieldRef<EjectionSeat, float> RailPosition = AccessTools.FieldRefAccess<EjectionSeat, float>("railPosition");
        private static readonly AccessTools.FieldRef<EjectionSeat, bool> Firing = AccessTools.FieldRefAccess<EjectionSeat, bool>("firing");
        private static readonly MethodInfo FireEffects = AccessTools.Method(typeof(EjectionSeat), "FireEffects");

        [HarmonyPatch(typeof(EjectionSeat), nameof(EjectionSeat.Fire))]
        private static class Fire
        {
            private static bool Prefix(EjectionSeat __instance, UnitPart unitPart)
            {
                var pd = Dismounted(__instance);
                if (!pd || !unitPart || !(unitPart.parentUnit is Aircraft a) || !Plugin.IsB52(a)) return true;
                if (a.pilots == null || pd.pilotNumber >= a.pilots.Length || !Lower.Contains(a.pilots[pd.pilotNumber].name)) return true;
                active.Add(__instance);
                CockpitPart(__instance) = unitPart;
                CockpitRB(__instance) = unitPart.rb;
                Firing(__instance) = true;
                RailPosition(__instance) = 0f;
                FireEffects?.Invoke(__instance, null);
                __instance.StartCoroutine(Run(__instance, pd, unitPart));
                Plugin.Log.LogInfo($"B-52J {a.pilots[pd.pilotNumber].name} ejects downward");
                return false;
            }
        }

        [HarmonyPatch(typeof(EjectionSeat), "LateUpdate")]
        private static class Late
        {
            private static bool Prefix(EjectionSeat __instance) => !active.Contains(__instance);
        }

        private static IEnumerator Run(EjectionSeat seat, PilotDismounted pd, UnitPart cockpit)
        {
            var prb = PilotRB(seat);
            var crb = cockpit.rb;
            var ct = cockpit.transform;
            var offset = ct.InverseTransformPoint(prb.position);
            var relRot = Quaternion.Inverse(ct.rotation) * prb.rotation;
            var sc = SeatCollider(seat);
            if (sc) sc.enabled = false;
            float rail = 0f, speed = 0f, thrust = Thrust(seat) * ThrustShare;
            while (rail < Rail && seat && prb && ct)
            {
                float dt = Time.fixedDeltaTime;
                speed += thrust / prb.mass * dt;
                var pos = ct.TransformPoint(offset) - ct.up * rail;
                prb.Move(pos, ct.rotation * relRot);
                prb.velocity = (crb ? crb.GetPointVelocity(pos) : prb.velocity) - ct.up * speed;
                prb.angularVelocity = crb ? crb.angularVelocity : Vector3.zero;
                rail += speed * dt;
                RailPosition(seat) = 1.5f * Mathf.Min(rail / Rail, 0.999f);
                yield return new WaitForFixedUpdate();
            }
            if (!seat) yield break;
            RailPosition(seat) = 1.5f;
            active.Remove(seat);
            if (sc) sc.enabled = true;
            if (pd) pd.SetCollidable(enabled: true);
        }
    }
}

namespace B52Systems
{
    /// <summary>
    /// Impact damage to the part that hit (docs/DAMAGE_MODEL.md, step 9).
    ///
    /// The game damages a part from its own collision message, by the impulse of the contact in g of the aircraft. With
    /// the B-52 flying as one rigid body, the message reaches the part that owns the rigidbody (the centre section), which
    /// then took the damage for every contact: a nacelle strike or a tail scrape hurt the centre box. This hands the
    /// damage to the part whose collider actually touched, with that part's own threshold and multiplier
    /// (B52Builder.Impact), and keeps the receiving part's own damage out of it; the dust and sparks still play. A contact
    /// is only counted once per physics step and part, whichever way Unity delivers it.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(AeroPart), "OnCollisionEnter")]
    internal static class ImpactRouting
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<UnitPart, ImpactDamage> Impact =
            HarmonyLib.AccessTools.FieldRefAccess<UnitPart, ImpactDamage>("impactDamage");
        private static readonly System.Collections.Generic.Dictionary<AeroPart, (float time, float impulse)> last =
            new System.Collections.Generic.Dictionary<AeroPart, (float, float)>();

        private static void Prefix(AeroPart __instance, UnityEngine.Collision collision, out float __state)
        {
            __state = -1f;
            if (collision == null || collision.contactCount == 0 || !(__instance.parentUnit is Aircraft a) || !Plugin.IsB52(a)) return;
            var col = collision.GetContact(0).thisCollider;
            var hit = col ? col.GetComponentInParent<AeroPart>() : null;
            if (!hit || hit.parentUnit != a) return;
            float impulse = collision.impulse.magnitude;
            bool seen = last.TryGetValue(hit, out var l) && UnityEngine.Mathf.Approximately(l.time, UnityEngine.Time.fixedTime) && UnityEngine.Mathf.Abs(l.impulse - impulse) < 1f;
            var own = Impact(__instance);
            if (hit != __instance || seen)
            {
                // Not this part's contact (or already counted): keep the game's own handler from damaging this part.
                if (own != null) { __state = own.threshold; own.threshold = 0f; }
            }
            if (seen) return;
            last[hit] = (UnityEngine.Time.fixedTime, impulse);
            if (hit == __instance) return;                                     // the game's handler damages it as usual
            var imp = Impact(hit);
            if (imp == null || !(imp.threshold > 0f) || !a.LocalSim || !hit.rb) return;
            float dmg = (impulse / UnityEngine.Time.fixedDeltaTime / (hit.rb.mass * 9.81f) - imp.threshold) * imp.multiplier;
            if (dmg <= 0f) return;
            if (a.IsServer) hit.TakeDamage(0f, 0f, 1f, 0f, dmg, PersistentID.None);
            else a.Damage(hit.id, new DamageInfo(0f, 0f, 0f, dmg));
            Plugin.Log.LogInfo($"B-52J {a.name}: {hit.name} hit something ({impulse / UnityEngine.Time.fixedDeltaTime / (hit.rb.mass * 9.81f):F1} g): {dmg:F0} damage");
        }

        private static void Postfix(AeroPart __instance, float __state)
        {
            if (__state >= 0f) { var own = Impact(__instance); if (own != null) own.threshold = __state; }
        }
    }
}
