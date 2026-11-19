using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52's structure: every part can break off, along the real load paths, from overload or damage.
    ///
    /// The game breaks aircraft parts off only through physics joints, and only while the aircraft runs complex
    /// physics. The B-52 flies as one rigid body (RigidAirframe), so it has no joints and nothing could ever come off.
    /// This puts the joints back as numbers. Each part has a root (where it hangs on its parent part) and an ultimate
    /// strength there, worked out at spawn from the B-52's design cases:
    ///   - wing: +2.0 g limit, 3.0 g ultimate, at full fuel and at zero fuel (wing fuel relieves the bending), with
    ///     the lift split the game's aero model really produces (LiftShare, measured clean and slow with flaps down),
    ///     and the ground case (full fuel, no lift) for bending the other way;
    ///   - engine mounts: 4.5 g of the pod's weight plus 1.5 x full thrust;
    ///   - fuselage sections: 3.0 g of their weight plus what they carry;
    ///   - tail surfaces, flaps and spoilers, and the structure carrying them: the air load at the design dive speed
    ///     (400 kt EAS, normal-force coefficient 1.2) x 1.5;
    ///   - negative g at half the positive strength.
    /// Loads are taken apart along each root's structural axes: main bending (about the level axis across the spar,
    /// per sense), torsion (about the spar), chordwise bending, and shear. The fly-by-wire and the AI both hold 2.5 g, so
    /// normal flying stays inside the 3.0 g ultimate; at 1 g the wing roots run at about 30% of it.
    /// Every physics step it works out the real force and bending moment at each root: aerodynamic force (at the
    /// part's aerodynamic centre), thrust, gravity and inertia (the rigid body's acceleration, rotation and spin at each
    /// part) of the part and everything outboard of it. Damage weakens a root the way the game weakens its joints:
    /// strength x (hp - structuralThreshold) / (100 - structuralThreshold); damage to the parent weakens it half as
    /// much. When the load passes the strength, or the part's hit points pass its structural threshold, the part comes
    /// off through Unit.DetachPart, which the server sends to every client, taking everything outboard with it.
    ///
    /// Runs where the aircraft is simulated (the owner's machine, or the server for AI). Every client fixes up a
    /// detached part (DetachFixup below).
    /// </summary>
    public class Structure : MonoBehaviour
    {
        private const float G = 9.81f;
        private const float QDive = 0.5f * 1.225f * 205f * 205f;   // design dive speed 400 kt EAS
        private const float CnMax = 1.2f;
        private const float Tau = 0.06f;                           // load filter: physics steps carry contact spikes

        private static readonly AccessTools.FieldRef<UnitPart, float> BaseMass = AccessTools.FieldRefAccess<UnitPart, float>("baseMass");
        private static readonly AccessTools.FieldRef<UnitPart, float> StructThreshold = AccessTools.FieldRefAccess<UnitPart, float>("structuralThreshold");
        private static readonly AccessTools.FieldRef<AeroPart, float> WingAreaRef = AccessTools.FieldRefAccess<AeroPart, float>("wingArea");

        internal static readonly Dictionary<Aircraft, Structure> ByAircraft = new Dictionary<Aircraft, Structure>();

        private class Node
        {
            public AeroPart part;
            public Node parent;
            public readonly List<Node> subtree = new List<Node>();   // the part and everything outboard
            public Vector3 massLocal;                                // mass centre, part frame
            public Vector3 fuelLocal;                                // the fuel's centre (its tanks), part frame
            public FuelTank[] tanks;                                 // the part's own tanks (child objects at their fuel)
            public Vector3 rootLocal;                                // attachment point, part frame
            public Vector3 toParentLocal;                            // from the part towards its parent, part frame
            public Turbofan[] engines;
            public LandingGear[] gears;                              // gear legs mounted on the part
            public Vector3 acLocal;                                  // aerodynamic centre (where design lift acts), part frame
            public Vector3 bendLocal, sparLocal, upLocal;            // structural axes at the root, part frame
            public Vector3 forceAxisLocal;                           // shear direction in flight, part frame
            public float strengthUp, strengthDown, strengthTorsion, strengthChord, strengthF, strengthFDown, strengthFCross, threshold;
            public Vector3 loadM, loadF;                             // filtered bending moment and shear force, world
            public float peakRatio;
            public string why = "";
            public bool gone, requested;
            public bool primary;                                     // losing it means the aircraft can't fly
        }

        private Aircraft aircraft;
        private Rigidbody rb;
        private readonly List<Node> nodes = new List<Node>();
        private readonly Dictionary<UnitPart, Node> byPart = new Dictionary<UnitPart, Node>();
        private Vector3 vPrev, wPrev;
        private bool havePrev, built;
        private float ejectAt = -1f, startTime;

        private void Start()
        {
            startTime = Time.time;
            aircraft = GetComponent<Aircraft>();
            rb = aircraft ? aircraft.rb : null;
            if (!rb) { Destroy(this); return; }
            ByAircraft[aircraft] = this;
            Build();
        }

        private void OnDestroy()
        {
            if (aircraft is object && ByAircraft.TryGetValue(aircraft, out var s) && s == this) ByAircraft.Remove(aircraft);
        }

        // ------------------------------------------------------------------ design strengths

        private void Build()
        {
            var root = aircraft.transform;
            foreach (var p in aircraft.GetComponentsInChildren<AeroPart>(true))
            {
                if (p.parentUnit != aircraft) continue;
                var n = new Node { part = p, threshold = StructThreshold(p) };
                n.massLocal = Centroid(p);
                n.tanks = System.Array.FindAll(aircraft.GetComponentsInChildren<FuelTank>(true), t => FuelTanks.Part(t) == p);
                n.fuelLocal = n.massLocal;
                if (n.tanks.Length > 0)
                {
                    var fc = Vector3.zero;
                    foreach (var t in n.tanks) fc += t.transform.position;
                    n.fuelLocal = p.transform.InverseTransformPoint(fc / n.tanks.Length);
                }
                n.acLocal = AeroCentres.Compute(p) ?? n.massLocal;
                // Only the part's own engines (those on parts outboard of it are in their own nodes, in its subtree).
                n.engines = System.Array.FindAll(p.GetComponentsInChildren<Turbofan>(true), e => e.GetComponentInParent<AeroPart>() == p);
                n.gears = System.Array.FindAll(aircraft.GetComponentsInChildren<LandingGear>(true), g => GearPart(g) == p);
                nodes.Add(n); byPart[p] = n;
            }
            foreach (var n in nodes)
            {
                var t = n.part.transform.parent;
                n.parent = t ? (t.GetComponentInParent<AeroPart>() is AeroPart q && byPart.TryGetValue(q, out var qn) ? qn : null) : null;
            }
            foreach (var n in nodes)
                for (var a = n; a != null; a = a.parent) a.subtree.Add(n);

            // Design weights: structure and full fuel.
            float structure = 0, fuel = 0;
            foreach (var n in nodes)
            {
                structure += Struct(n); fuel += Fuel(n);
            }

            var sb = new StringBuilder($"B-52J structure: {nodes.Count} parts, {structure / 1000f:F1} t structure, {fuel / 1000f:F1} t fuel\n");
            foreach (var n in nodes)
            {
                if (n.parent == null) continue;
                var pt = n.part.transform;
                Vector3 cP = Mass(n), cQ = Mass(n.parent);
                var d = (cQ - cP).normalized;
                float reach = 0;
                foreach (var v in Verts(n.part)) reach = Mathf.Max(reach, Vector3.Dot(v - cP, d));
                var rootW = cP + d * reach;
                n.rootLocal = pt.InverseTransformPoint(rootW);
                n.toParentLocal = pt.InverseTransformDirection(d);
                string name = n.part.name;
                n.primary = name.StartsWith("wingroot") || name.StartsWith("wing1") || name.StartsWith("fuselage") || name == "cockpit";

                // Design cases at 1 g: manoeuvre at full and at zero fuel, with each of the game's two lift splits
                // (LiftShare: clean, and slow with flaps down), lift acting at each surface's aerodynamic centre; and
                // the ground case (full fuel, no lift: taxiing and landing, 2.0 g limit, the same 3.0 ultimate).
                // Case 2 is the ground case.
                const int Cases = 5;
                var cases = new Vector3[Cases]; var forces = new Vector3[Cases];
                float subMass = 0; var subCentre = Vector3.zero;
                for (int c = 0; c < Cases; c++)
                {
                    bool full = c != 1 && c != 4, slow = c >= 3;
                    float W = c == 2 ? 0 : (structure + (full ? fuel : 0)) * G;
                    foreach (var s in n.subtree)
                    {
                        float ms = Struct(s), mf = full ? Fuel(s) : 0, m = ms + mf;
                        float lift = W * LiftShare(s.part.name, slow);
                        // what the root must supply, as in FixedUpdate: hold the weight up, the lift down
                        var fw = root.up * (ms * G); var ff = root.up * (mf * G); var fl = -root.up * lift;
                        forces[c] += fw + ff + fl;
                        cases[c] += Vector3.Cross(Mass(s) - rootW, fw) + Vector3.Cross(FuelAt(s) - rootW, ff)
                                  + Vector3.Cross(s.part.transform.TransformPoint(s.acLocal) - rootW, fl);
                        if (c == 0) { subMass += m; subCentre += Mass(s) * ms + FuelAt(s) * mf; }
                    }
                }
                subCentre /= Mathf.Max(1f, subMass);

                // Structural axes at the root: the spar runs from the root towards what it carries (level); the main
                // bending is about the level axis across it, torsion about the spar, chordwise bending about the vertical.
                // A wing bends up in flight and down on the ground, and is built for each, so the main bending strength
                // is kept per sense.
                var spar = Vector3.ProjectOnPlane(subCentre - rootW, root.up);
                spar = spar.magnitude > 0.2f ? spar.normalized : root.forward;
                var bend = Vector3.Cross(root.up, spar).normalized;
                float nUlt = name.StartsWith("pod") ? 4.5f : 3.0f;
                float flightMain = 0;
                foreach (int c in new[] { 0, 1, 3, 4 })
                    if (Mathf.Abs(Vector3.Dot(cases[c], bend)) > Mathf.Abs(flightMain)) flightMain = Vector3.Dot(cases[c], bend);
                if (Mathf.Abs(flightMain) < 1f) flightMain = -Vector3.Dot(cases[2], bend);
                if (flightMain < 0) bend = -bend;                       // + = the flight sense
                float up = 0, down = 0, tors = 0;
                for (int c = 0; c < Cases; c++)
                {
                    float a = Vector3.Dot(cases[c], bend);
                    up = Mathf.Max(up, nUlt * a); down = Mathf.Max(down, -nUlt * a);
                    tors = Mathf.Max(tors, nUlt * Mathf.Abs(Vector3.Dot(cases[c], spar)));
                }
                // Shear the same way: along the vertical, in the flight sense and the reverse one.
                var flightF = Vector3.zero;
                foreach (int c in new[] { 0, 1, 3, 4 }) if (forces[c].magnitude > flightF.magnitude) flightF = forces[c];
                var fu = flightF.magnitude > 1f ? flightF.normalized : -forces[2].normalized;
                float sF = 0, sFDown = 0;
                for (int c = 0; c < Cases; c++)
                {
                    float b = Vector3.Dot(forces[c], fu);
                    sF = Mathf.Max(sF, nUlt * b); sFDown = Mathf.Max(sFDown, -nUlt * b);
                }

                // Negative g: -1.0 g limit, i.e. half the positive manoeuvre strength.
                down = Mathf.Max(down, 0.5f * up); sFDown = Mathf.Max(sFDown, 0.5f * sF);

                // Air-load floor for the surfaces whose load isn't set by the g limit (tail, control surfaces, flaps,
                // spoilers): the dive-speed normal force on each, all in one direction, x 1.5. The wing's own lift is
                // already the manoeuvre case (a CN of 1.2 at dive speed would be some 12 g).
                // A wing root's own strength comes from the manoeuvre and ground cases (its flaps' and spoilers' lift is
                // in them); the floor is for the surfaces themselves and the structure carrying them (the rear fuselage).
                float aM = 0, aF = 0;
                bool wingRoot = Lifting(n) && !name.StartsWith("flap") && !name.StartsWith("spoilers");
                foreach (var s in n.subtree)
                {
                    if (wingRoot || Lifting(s) && !s.part.name.StartsWith("flap") && !s.part.name.StartsWith("spoilers")) continue;
                    float N = QDive * CnMax * (WingAreaRef(s.part) + s.part.dragArea);
                    aF += N; aM += N * (Mass(s) - rootW).magnitude;
                }
                up = Mathf.Max(up, 1.5f * aM); down = Mathf.Max(down, 1.5f * aM);
                sF = Mathf.Max(sF, 1.5f * aF); sFDown = Mathf.Max(sFDown, 1.5f * aF);
                // Torsion: a closed wing or fuselage box is stiff in twist, at least half its bending strength.
                // Chordwise bending and fore-aft or side shear: the box is far deeper that way than it is thick, so at
                // least as strong as in its main bending.
                tors = Mathf.Max(tors, Mathf.Max(1.5f * aM, 0.5f * up));
                float chord = Mathf.Max(1.5f * aM, up), sFCross = Mathf.Max(1.5f * aF, sF);

                // Engine mounts also carry 1.5 x full thrust, on each axis by as much as thrust loads it.
                foreach (var s in n.subtree)
                    foreach (var e in s.engines)
                    {
                        var T = e.transform.forward * (1.5f * e.staticThrust);
                        var TM = Vector3.Cross(e.transform.position - rootW, T);
                        float b = Mathf.Abs(Vector3.Dot(TM, bend));
                        up += b; down += b; tors += Mathf.Abs(Vector3.Dot(TM, spar)); chord += Mathf.Abs(Vector3.Dot(TM, root.up));
                        sFCross += T.magnitude;
                    }
                // Gear mountings are built for their leg's share of the aircraft's weight at the 2 g landing limit, x 1.5:
                // each of the four main trucks about a quarter of it, each wingtip outrigger about 1% (a ton or two). Without
                // this a part carrying a gear leg (the forward fuselage its trucks) was built for flight loads alone and
                // broke under the aircraft's own weight on the runway.
                foreach (var s in n.subtree)
                    foreach (var g in s.gears)
                    {
                        float share = g.name.Contains("_O") ? 0.01f : 0.25f;
                        float Fmax = 3.0f * share * (structure + fuel) * G;
                        var at = g.transform.position;
                        var Fv = root.up * Fmax; var Fd = -root.forward * (0.75f * Fmax);
                        var Mv = Vector3.Cross(at - rootW, Fv); var Md = Vector3.Cross(at - rootW, Fd);
                        // either sense: a part's own weight and the gear's push can leave its root loaded either way
                        float b = Mathf.Abs(Vector3.Dot(Mv, bend)) + Mathf.Abs(Vector3.Dot(Md, bend));
                        up += b; down += b;
                        tors += Mathf.Abs(Vector3.Dot(Mv, spar)) + Mathf.Abs(Vector3.Dot(Md, spar));
                        chord += Mathf.Abs(Vector3.Dot(Mv, root.up)) + Mathf.Abs(Vector3.Dot(Md, root.up));
                        sF += Fmax; sFDown += Fmax; sFCross += 0.75f * Fmax;
                    }
                n.bendLocal = pt.InverseTransformDirection(bend); n.sparLocal = pt.InverseTransformDirection(spar);
                n.upLocal = pt.InverseTransformDirection(root.up);
                n.forceAxisLocal = pt.InverseTransformDirection(fu);
                n.strengthUp = up; n.strengthDown = down; n.strengthTorsion = tors; n.strengthChord = chord;
                n.strengthF = sF; n.strengthFDown = sFDown; n.strengthFCross = sFCross;
                sb.AppendLine($"  {name} -> {n.parent.part.name}: carries {n.subtree.Count} parts, root {root.InverseTransformPoint(rootW):F1}, " +
                              $"ultimate bending {up:E2} / {down:E2} reverse, torsion {tors:E2}, chordwise {chord:E2} Nm, shear {sF:E2} / {sFDown:E2} / {sFCross:E2} N, " +
                              $"structural threshold {n.threshold:F0}");
            }
            built = true;
            Plugin.Log.LogInfo(sb.ToString().TrimEnd());
        }

        /// <summary>
        /// Each part's share of the aircraft's lift in the game's aero model, measured in game (tests/ingame/b52_loads.txt,
        /// "lift shares", 14 Nov), per side. Clean: 165 m/s, flaps up. Slow: 96 m/s, flaps down. They are not the area
        /// split: the centre section lifts almost nothing, and slow with flaps down the inner wing carries a third of its
        /// clean share while the flaps carry more than the wing between them. Re-measure if wing areas or airfoils change.
        /// </summary>
        private static float LiftShare(string part, bool slow)
        {
            string k = part.EndsWith("_L") || part.EndsWith("_R") ? part.Substring(0, part.Length - 2) : part;
            float clean, low;
            switch (k)
            {
                case "B52":      clean = 4;  low = -1; break;
                case "hstab":    clean = 2;  low = -1; break;
                case "wingroot": clean = 32; low = 7; break;
                case "flap1":    clean = 6;  low = 8; break;
                case "wing1":    clean = 23; low = 16.5f; break;
                case "flap2":    clean = 6;  low = 9; break;
                case "spoilers": clean = 1;  low = -0.5f; break;
                case "wing2":    clean = 15; low = 11; break;
                case "wingtip":  clean = 7;  low = 1.5f; break;
                default: return 0;
            }
            return slow ? low / 102f : clean / 188f;   // each split's total
        }

        private static bool Lifting(Node n)
        {
            var s = n.part.name;
            return s == "B52" || s.StartsWith("wing") || s.StartsWith("flap") || s.StartsWith("spoilers");
        }

        private static float Struct(Node n) => BaseMass(n.part) > 0 ? BaseMass(n.part) : n.part.mass;

        private static float Fuel(Node n)
        {
            float f = 0;
            foreach (var t in n.tanks) f += t.FuelCapacity;
            return f;
        }

        private static float FuelNow(Node n)
        {
            float f = 0;
            foreach (var t in n.tanks) if (t) f += t.fuelMass;
            return f;
        }

        private static readonly AccessTools.FieldRef<LandingGear, AeroPart> GearPart = AccessTools.FieldRefAccess<LandingGear, AeroPart>("attachedPart");
        private static readonly AccessTools.FieldRef<LandingGear, float> GearSpring = AccessTools.FieldRefAccess<LandingGear, float>("compressionForce");
        private static readonly AccessTools.FieldRef<LandingGear, float> GearDamping = AccessTools.FieldRefAccess<LandingGear, float>("dampingForce");
        private static readonly AccessTools.FieldRef<LandingGear, float> GearFriction = AccessTools.FieldRefAccess<LandingGear, float>("frictionCoef");
        private static readonly AccessTools.FieldRef<LandingGear, float> GearBrake = AccessTools.FieldRefAccess<LandingGear, float>("brakeStrength");
        private static readonly AccessTools.FieldRef<LandingGear, RaycastHit> GearHit = AccessTools.FieldRefAccess<LandingGear, RaycastHit>("hit");

        // A gear leg's force on the airframe while its wheels are on the ground: the strut's spring and damper along the
        // ground's normal, and the brakes' drag (the game's law: load x grip x brake), at the contact point.
        private bool GearForce(LandingGear g, out Vector3 at, out Vector3 force)
        {
            at = Vector3.zero; force = Vector3.zero;
            if (!g || !g.enabled) return false;
            float load = GearSpring(g) + GearDamping(g);
            if (load <= 0f) return false;
            var hit = GearHit(g);
            at = hit.point;
            // the contact is cached from the gear's last step: after the aircraft is moved (a reset or teleport) it is
            // still where the wheel was, far away; only a contact under the wheel counts
            if ((at - g.transform.position).sqrMagnitude > 25f) return false;
            force = hit.normal * load;
            var slide = Vector3.ProjectOnPlane(rb.GetPointVelocity(at), hit.normal);
            if (slide.sqrMagnitude > 1f) force -= slide.normalized * load * GearFriction(g) * Mathf.Clamp01(GearBrake(g) + 0.02f);
            return true;
        }

        private static Vector3 FuelAt(Node n) => n.part.transform.TransformPoint(n.fuelLocal);

        private static Vector3 Mass(Node n) => n.part.transform.TransformPoint(n.massLocal);

        /// <summary>The part's own skin (its own mesh and its control-surface "_visible" copy), world space.</summary>
        private static List<Vector3> Verts(AeroPart part)
        {
            var t = part.transform;
            var list = new List<Vector3>();
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                if (mf.transform != t && mf.name != part.name + "_visible") continue;
                foreach (var v in mf.sharedMesh.vertices) list.Add(mf.transform.TransformPoint(v));
            }
            if (list.Count == 0) list.Add(t.position);
            return list;
        }

        private static Vector3 Centroid(AeroPart part)
        {
            var c = Vector3.zero;
            var vs = Verts(part);
            foreach (var v in vs) c += v;
            return part.transform.InverseTransformPoint(c / vs.Count);
        }

        // ------------------------------------------------------------------ loads, every physics step

        private float settleUntil;
        private Vector3 pPrev;

        private void FixedUpdate()
        {
            if (!built || !rb) return;
            if (ejectAt > 0 && Time.time > ejectAt)
            {
                ejectAt = -1f;
                if (aircraft.IsServer && aircraft.Player == null) aircraft.StartEjectionSequence();
            }
            if (!aircraft.LocalSim) return;

            float dt = Time.fixedDeltaTime;
            Vector3 v = rb.velocity, w = rb.angularVelocity;
            if (!havePrev) { vPrev = v; wPrev = w; havePrev = true; return; }
            Vector3 aCm = (v - vPrev) / dt, alpha = (w - wPrev) / dt;
            vPrev = v; wPrev = w;
            // moved without flying there (the game resetting it, a teleport): as a velocity jump
            var pos = rb.position;
            bool jumped = (pos - pPrev - v * dt).sqrMagnitude > 1f && pPrev != Vector3.zero;
            pPrev = pos;
            // Spawning, the game resetting the aircraft, or a kinematic hold set the velocity directly: no real airframe
            // sees 30 g or 20 rad/s2 and flies on (a crash that does is handled by the game's impact damage). After such a
            // jump the aero model still carries the forces of the old motion for a step or two (its job runs a step behind),
            // and nothing balances them: e.g. a flying aircraft reset onto the runway read its flight lift with no weight
            // against it and snapped the nose and wing roots. So hold off for half a second and start the loads afresh.
            if (rb.isKinematic || jumped || aCm.sqrMagnitude > 900f * G * G || alpha.sqrMagnitude > 400f)
            {
                settleUntil = Time.time + 0.5f;
                foreach (var n in nodes) { n.loadM = Vector3.zero; n.loadF = Vector3.zero; }
                return;
            }
            if (Time.time - startTime < 3f || Time.time < settleUntil) return;
            var com = rb.worldCenterOfMass;
            float k = Mathf.Clamp01(dt / Tau);

            foreach (var n in nodes)
            {
                if (n.parent == null || n.gone) continue;
                var rootW = n.part.transform.TransformPoint(n.rootLocal);
                Vector3 F = Vector3.zero, M = Vector3.zero;
                foreach (var s in n.subtree)
                {
                    if (s.gone) continue;
                    var p = s.part;
                    // what the structure must supply: the part's own mass at its centre, its fuel at the tanks
                    float mf = Mathf.Min(FuelNow(s), p.mass);
                    var c = Mass(s); var cf = FuelAt(s);
                    var acc = aCm + Vector3.Cross(alpha, c - com) + Vector3.Cross(w, Vector3.Cross(w, c - com));
                    var accF = aCm + Vector3.Cross(alpha, cf - com) + Vector3.Cross(w, Vector3.Cross(w, cf - com));
                    var inertial = (p.mass - mf) * (acc - Physics.gravity);
                    var inertialF = mf * (accF - Physics.gravity);
                    F += inertial + inertialF; M += Vector3.Cross(c - rootW, inertial) + Vector3.Cross(cf - rootW, inertialF);
                    var aero = AeroCentres.Force(p);
                    F -= aero; M -= Vector3.Cross(AeroCentres.WorldAC(p) - rootW, aero);
                    foreach (var e in s.engines)
                    {
                        var T = e.transform.forward * e.GetThrust();
                        F -= T; M -= Vector3.Cross(e.transform.position - rootW, T);
                    }
                    // On the ground the gear holds the aircraft up (and brakes it): its force on the part it's mounted on.
                    foreach (var g in s.gears)
                        if (GearForce(g, out var at, out var Fg)) { F -= Fg; M -= Vector3.Cross(at - rootW, Fg); }
                }
                n.loadM += (M - n.loadM) * k;
                n.loadF += (F - n.loadF) * k;

                float f = Strength(n);
                float ratio = Ratio(n, out var mode) / Mathf.Max(f, 1e-6f);
                if (f <= 0f) ratio = float.PositiveInfinity;
                if (ratio > n.peakRatio && !float.IsInfinity(ratio)) n.peakRatio = ratio;
                if (ratio < 1f || n.requested) continue;

                n.why = f <= 0f ? $"destroyed (hp {n.part.hitPoints:F0}, threshold {n.threshold:F0})"
                    : $"{mode} at {ratio * f:P0} of its ultimate with {f:P0} strength left (hp {n.part.hitPoints:F0})";
                Fail(n, rootW);
            }
        }

        /// <summary>Load as a share of the undamaged ultimate strength, in the worst direction.</summary>
        private static float Ratio(Node n, out string mode)
        {
            var t = n.part.transform;
            float a = Vector3.Dot(n.loadM, t.TransformDirection(n.bendLocal));
            float rB = a >= 0 ? a / Mathf.Max(1f, n.strengthUp) : -a / Mathf.Max(1f, n.strengthDown);
            float rT = Mathf.Abs(Vector3.Dot(n.loadM, t.TransformDirection(n.sparLocal))) / Mathf.Max(1f, n.strengthTorsion);
            float rC = Mathf.Abs(Vector3.Dot(n.loadM, t.TransformDirection(n.upLocal))) / Mathf.Max(1f, n.strengthChord);
            var fu = t.TransformDirection(n.forceAxisLocal);
            float b = Vector3.Dot(n.loadF, fu), side = (n.loadF - b * fu).magnitude;
            float rF = Mathf.Max(b >= 0 ? b / Mathf.Max(1f, n.strengthF) : -b / Mathf.Max(1f, n.strengthFDown),
                                 side / Mathf.Max(1f, n.strengthFCross));
            float r = Mathf.Max(Mathf.Max(rB, rT), Mathf.Max(rC, rF));
            mode = r == rB ? (a >= 0 ? "bending, flight sense" : "bending, reverse sense") : r == rT ? "torsion" : r == rC ? "chordwise bending" : "shear";
            return r;
        }

        /// <summary>Share of design strength left at this part's root: its own damage, and half of its parent's.</summary>
        private static float Strength(Node n)
        {
            float own = Share(n);
            if (n.parent != null) own = Mathf.Min(own, 0.5f + 0.5f * Share(n.parent));
            return own;
        }

        private static float Share(Node n) => Mathf.Clamp01((n.part.hitPoints - n.threshold) / (100f - n.threshold));

        private void Fail(Node n, Vector3 rootW)
        {
            n.requested = true;
            Plugin.Log.LogInfo($"B-52J {aircraft.name}: {n.part.name} broke off: {n.why}; {aircraft.speed:F0} m/s, alt {aircraft.radarAlt:F0} m");
            aircraft.DetachPart(n.part.id, rb.GetPointVelocity(rootW), n.part.transform.position - n.parent.part.transform.position);
        }

        // ------------------------------------------------------------------ after a part comes off (every client)

        internal void OnDetached(AeroPart part)
        {
            if (!byPart.TryGetValue(part, out var n) || n.gone) return;
            if (n.parent != null)
            {
                var pt = part.transform;
                BreakStubs.Make(part, n.parent.part, pt.TransformPoint(n.rootLocal), pt.TransformDirection(n.toParentLocal), Verts(part), Verts(n.parent.part));
            }
            foreach (var s in n.subtree) { s.gone = true; s.requested = true; }
            if (n.primary && ejectAt < 0) ejectAt = Time.time + 2f;      // AI crews bail out of an aircraft that can't fly
        }

        /// <summary>One line per part: its load now, its peak, and its strength left (for the test driver).</summary>
        public static string Report(Aircraft a)
        {
            if (!a || !ByAircraft.TryGetValue(a, out var s)) return "no B-52J structure";
            var sb = new StringBuilder();
            // Each part's share of the aircraft's weight carried by its own aero force (vertical), against its share
            // of the lifting area: the design cases assume lift goes by wing area.
            float wsum = 0, area = 0;
            foreach (var n in s.nodes) { if (!n.gone) wsum += n.part.mass; if (Lifting(n)) area += WingAreaRef(n.part); }
            var lifts = new StringBuilder("lift shares (aero up-force / weight, vs area share):");
            foreach (var n in s.nodes)
            {
                if (n.gone) continue;
                float up = Vector3.Dot(AeroCentres.Force(n.part), a.transform.up) / (wsum * G);
                if (Mathf.Abs(up) > 0.005f || Lifting(n))
                    lifts.Append($" {n.part.name} {up:P0}/{(Lifting(n) ? WingAreaRef(n.part) / area : 0):P0}");
            }
            sb.AppendLine(lifts.ToString());
            foreach (var n in s.nodes)
            {
                if (n.parent == null) continue;
                float f = Strength(n), r = Ratio(n, out var mode);
                var tt = n.part.transform;
                float main = Vector3.Dot(n.loadM, tt.TransformDirection(n.bendLocal)), tors = Vector3.Dot(n.loadM, tt.TransformDirection(n.sparLocal));
                sb.AppendLine($"{n.part.name}: {(n.gone ? "OFF " + n.why : "on")}, load {r:P0} of ultimate ({mode}), " +
                              $"peak {n.peakRatio:P0} of what's left, strength {f:P0}, hp {n.part.hitPoints:F0}; " +
                              $"bending {main:E2} (ultimate {n.strengthUp:E2}), torsion {tors:E2} ({n.strengthTorsion:E2}) Nm");
            }
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    /// A part that comes off a rigid B-52 gets its own rigidbody (AeroPart.Detach), but the parts outboard of it keep
    /// pointing at the aircraft's: their weight, lift and thrust stayed on the aircraft. They follow the piece they're
    /// attached to, and the structure stops checking them.
    /// </summary>
    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.Detach))]
    internal static class DetachFixup
    {
        private static readonly AccessTools.FieldRef<AeroPart, float> Streamlining = AccessTools.FieldRefAccess<AeroPart, float>("streamlining");

        private static void Postfix(AeroPart __instance)
        {
            if (!(__instance.parentUnit is Aircraft a) || !Plugin.IsB52(a) || !Plugin.RigidAirframe.Value) return;
            var body = __instance.rb;
            int moved = 0;
            foreach (var p in __instance.GetComponentsInChildren<UnitPart>(true))
                if (p != __instance && p.rb != body) { p.rb = body; moved++; }
            if (Structure.ByAircraft.TryGetValue(a, out var s)) s.OnDetached(__instance);
            // The torn stub's drag on the part it came off (the game does this only for joint breaks in complex physics).
            var parent = __instance.transform.parent ? __instance.transform.parent.GetComponentInParent<AeroPart>() : null;
            float stub = Streamlining(__instance);
            if (parent && stub > 0f) parent.ModifyDrag(stub);
            if (a.rb && MassSync.ByBody.TryGetValue(a.rb, out var sync) && sync) sync.Sync();
            Plugin.Log.LogInfo($"B-52J {a.name}: {__instance.name} is off ({(body ? body.mass : 0):F0} kg with {moved} parts outboard)");
        }
    }
}
