using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52's own values for the flight-model parts (docs/DONOR_REPLACEMENT.md, phase 1), in place of the donor
    /// bomber's. Every setting of every AeroPart is set here or in ApplyPhysics / Armour, each with its reason.
    /// </summary>
    public static partial class B52Builder
    {
        // Impact damage (damage model step 9). The game measures a contact as the impulse it delivers in one physics step,
        // in g of the whole aircraft: (impulse / dt / (m g) - threshold) x multiplier hit points. A part only gets it from
        // contacts it makes itself (gear wheels are raycasts, not collisions). For a 200 t aircraft settling onto its
        // belly that is about 5 g per m/s of sink. Sized so a belly landing at 1-1.5 m/s sink scrapes and dents, 3 m/s
        // badly damages what touches, and 5-6 m/s breaks it: the B-52's keel and fuselage take it best, the thin
        // nacelles, wingtips and control surfaces worst. The donor only had it on its root (40 g, x1: a light jet that
        // rarely touches anything but its gear).
        static readonly Dictionary<string, (float threshold, float multiplier)> Impact = new Dictionary<string, (float, float)>
        {
            { "B52", (4f, 3f) },                         // centre box, keel, bomb bay, main gear wells
            { "fuselage_F", (4f, 3f) }, { "fuselage_R", (4f, 3f) },
            { "cockpit", (3f, 4f) },                      // the nose: radome and crew compartment
            { "tail", (3f, 4f) }, { "hstab_L", (3f, 4f) }, { "hstab_R", (3f, 4f) },
            { "wingroot_L", (4f, 3f) }, { "wingroot_R", (4f, 3f) },
            { "wing1_L", (4f, 3f) }, { "wing1_R", (4f, 3f) },
            { "wing2_L", (3.5f, 3.5f) }, { "wing2_R", (3.5f, 3.5f) },
            { "wingtip_L", (3f, 4f) }, { "wingtip_R", (3f, 4f) },        // tip gear and the external tanks
            { "pod1_L", (3f, 5f) }, { "pod1_R", (3f, 5f) }, { "pod2_L", (3f, 5f) }, { "pod2_R", (3f, 5f) },   // nacelle strike
            { "rudder", (2f, 6f) }, { "elevator_L", (2f, 6f) }, { "elevator_R", (2f, 6f) },
            { "flap1_L", (2f, 6f) }, { "flap1_R", (2f, 6f) }, { "flap2_L", (2f, 6f) }, { "flap2_R", (2f, 6f) },
            { "spoilers_L", (2f, 6f) }, { "spoilers_R", (2f, 6f) },
        };

        // Blast armour: the game subtracts it from fragment damage and slows fragments passing through by a tenth of it.
        // Scaled from skin and structure thickness: the donor (a 30 t jet) uses 60 for structure, 30 for surfaces and
        // 20 for engines; the B-52's centre box and wing roots are its heaviest structure, its nacelles thin cowls.
        static readonly Dictionary<string, float> BlastArmour = new Dictionary<string, float>
        {
            { "B52", 75f }, { "wingroot_L", 70f }, { "wingroot_R", 70f }, { "wing1_L", 65f }, { "wing1_R", 65f },
            { "wing2_L", 60f }, { "wing2_R", 60f }, { "wingtip_L", 50f }, { "wingtip_R", 50f },
            { "fuselage_F", 60f }, { "fuselage_R", 60f }, { "cockpit", 55f }, { "tail", 55f }, { "hstab_L", 55f }, { "hstab_R", 55f },
            { "rudder", 30f }, { "elevator_L", 30f }, { "elevator_R", 30f },
            { "flap1_L", 30f }, { "flap1_R", 30f }, { "flap2_L", 30f }, { "flap2_R", 30f }, { "spoilers_L", 25f }, { "spoilers_R", 25f },
            { "pod1_L", 35f }, { "pod1_R", 35f }, { "pod2_L", 35f }, { "pod2_R", 35f },
        };

        // Drag area of the stub a part leaves when it breaks away (streamlining, m2): the section at the break, from the
        // B-52H's dimensions, x 0.6 (an aerofoil or rounded fuselage fills about that of its bounding rectangle) x 0.5 (a
        // torn face is partly closed by the structure left behind), drag coefficient about 1.
        //   wing at the wing-root break: chord ~10.6 m, depth ~1.3 m -> 4.1; at the inner/outer wing joins 8.5 x 1.0 -> 2.6,
        //   6.0 x 0.7 -> 1.3; at the tip 4.0 x 0.45 -> 0.5. Engine pylons ~4.5 x 0.5 -> 0.7. Fin root 8 x 0.6 -> 1.4,
        //   stabiliser root 4.5 x 0.4 -> 0.5. Fuselage ~3.0 m wide x 3.8 m deep -> 3.4 at the forward and aft breaks,
        //   2.8 x 3.0 -> 2.5 behind the nose. Flaps, spoilers, elevators and rudder hang on hinges and tracks: ~0.1-0.2.
        static readonly Dictionary<string, float> Stub = new Dictionary<string, float>
        {
            { "wingroot_L", 4.1f }, { "wingroot_R", 4.1f }, { "wing1_L", 2.6f }, { "wing1_R", 2.6f },
            { "wing2_L", 1.3f }, { "wing2_R", 1.3f }, { "wingtip_L", 0.5f }, { "wingtip_R", 0.5f },
            { "pod1_L", 0.7f }, { "pod1_R", 0.7f }, { "pod2_L", 0.7f }, { "pod2_R", 0.7f },
            { "tail", 1.4f }, { "hstab_L", 0.5f }, { "hstab_R", 0.5f },
            { "fuselage_F", 3.4f }, { "fuselage_R", 3.4f }, { "cockpit", 2.5f },
            { "flap1_L", 0.2f }, { "flap1_R", 0.2f }, { "flap2_L", 0.2f }, { "flap2_R", 0.2f },
            { "spoilers_L", 0.1f }, { "spoilers_R", 0.1f }, { "elevator_L", 0.1f }, { "elevator_R", 0.1f }, { "rudder", 0.1f },
            { "B52", 0f },   // the centre box is the root: nothing to break away from
        };

        // Critical parts: losing all of one kills the aircraft at once (UnitPart.criticalPart: hit points <= 0).
        // The crew compartment (cockpit) and the centre wing box (B52), through which both wings and the main gear carry.
        static readonly HashSet<string> Critical = new HashSet<string> { "cockpit", "B52" };

        static void SpecParts()
        {
            var apType = T("AeroPart");
            var metal = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(DoNotShip + "/PhysicMaterial/Metal_PLACEHOLDER.physicMaterial");
            int n = 0;
            foreach (Component ap in ourRoot.GetComponentsInChildren(apType, true))
            {
                var name = ap.name;
                var t = ap.transform;
                var (verts, mesh) = PartSkin(t);
                float structure = Phys.TryGetValue(name, out var ph) ? ph.mass : 1000f;
                float fuel = ph.fuel;

                // Buoyancy: the game lifts a part by 9.81 x buoyancy x its mass x how far it is under, so buoyancy is the
                // weight of water the part displaces over its own weight. Displaced volume from the part's own skin
                // (closed sections; the open joins make it a slight underestimate), its weight with full fuel: the hollow
                // fuselage and nacelles float high, the fuel-heavy inner wing about level (between 6 at most and 0.2).
                float volume = SkinVolume(mesh, t);
                float buoy = Mathf.Clamp(volume * 1000f / Mathf.Max(structure + fuel, 1f), 0.2f, 6f);
                SetF(ap, "buoyancy", buoy);

                // Streamlining: drag the parent gains where this part breaks away (game: added to the parent's drag area
                // when the part comes off; B52Systems.DetachFixup does it for the rigid airframe too). The torn stub is a
                // bluff face, drag coefficient about 1, the size of the part's section at its root.
                float stub = Stub.TryGetValue(name, out var sv) ? sv : 0f;
                SetF(ap, "streamlining", stub);

                // Airflow channelling turns the local flow towards a part's axis (the game's ducted intakes and nozzles).
                // No B-52 part is a duct: the fans' inflow is in the engine model (Turbofan), not the nacelle's lift.
                SetF(ap, "airflowChanneling", 0f);

                // Every lifting part uses the B-52's wing section (MakeDefinition: airfoil 0, B52_wing, the B-52's own
                // lift and drag curves); fuselage sections and pods have no wing area, so it only sets their side force.
                // The flap panels use the flapped section (airfoil 1, B52_flapped: the same up to 17 deg, then on to a
                // higher maximum, as a slotted flap does). The inner wing ahead of them switches to it only while the
                // flaps are down (B52Systems.FlappedAirfoil), so the clean wing still stalls as before.
                bool flapPanel = name.StartsWith("flap1") || name.StartsWith("flap2");
                Set(ap, "airfoil", p => p.intValue = flapPanel ? 1 : 0);

                // Impact damage (above).
                var imp = Impact.TryGetValue(name, out var iv) ? iv : (3f, 4f);
                SetF(ap, "impactDamage.threshold", imp.Item1);
                SetF(ap, "impactDamage.multiplier", imp.Item2);

                // Armour not in B52Builder.Armour: blast armour (above), fire armour (none: aluminium structure, the
                // fire tolerance in Armour sets how fast it burns), overpressure limit (only rotors use it; game default).
                SetF(ap, "armorProperties.blastArmor", BlastArmour.TryGetValue(name, out var ba) ? ba : 40f);
                SetF(ap, "armorProperties.fireArmor", 0f);
                SetF(ap, "armorProperties.overpressureLimit", 5f);

                Set(ap, "criticalPart", p => p.boolValue = Critical.Contains(name));

                // Fragmenting (hit points below the integrity threshold: the part's renderers are hidden and its
                // disintegration effects play). B-52 parts break off whole along their load paths (B52Systems.Structure)
                // instead, so they never fragment.
                SetF(ap, "integrityThreshold", float.MinValue);
                Set(ap, "disintegrationEffects", p => p.arraySize = 0); B52DonorAudit.Chose(ap, "disintegrationEffects");
                Set(ap, "disintegrateObjects", p => p.arraySize = 0); B52DonorAudit.Chose(ap, "disintegrateObjects");

                // Damage-shading threshold: not read by the game (the shading follows hit points directly).
                SetF(ap, "damageMaterial.threshold", 0f);

                // Centre of lift: set each step at runtime from the part's aerodynamic centre (B52Systems.AeroCentres).
                Set(ap, "centerOfLift", p => p.vector3Value = Vector3.zero);
                SetRef(ap, "connectedAnchor", null);

                // Surfaces without drag area of their own: their drag is the B-52 section's drag on their wing area.
                if (!Phys.ContainsKey(name) || ph.drag <= 0f) SetF(ap, "dragArea", 0f);

                // Joints (complex physics only; the B-52 flies as one rigid body): the game's metal tearing as they break,
                // no tensor or anchor (the joint is at the part's own origin, its root).
                Set(ap, "joints", p =>
                {
                    for (int i = 0; i < p.arraySize; i++)
                    {
                        var j = p.GetArrayElementAtIndex(i);
                        j.FindPropertyRelative("tensor").objectReferenceValue = null;
                        j.FindPropertyRelative("anchor").objectReferenceValue = null;
                    }
                });
                // The game's impact and tearing sounds, shared by every aircraft.
                B52DonorAudit.Chose(ap, "hitSound");
                B52DonorAudit.Chose(ap, "joints");

                // Colliders: the game's metal surface for scraping and sparks.
                var mc = t.GetComponent<MeshCollider>();
                if (mc && metal) mc.sharedMaterial = metal;
                n++;
                Note($"  part {name}: buoyancy {buoy:F2} ({volume:F1} m3), streamlining {stub:F2} m2, impact {imp.Item1} g x{imp.Item2}, blast armour {(BlastArmour.TryGetValue(name, out var b2) ? b2 : 40f)}{(Critical.Contains(name) ? ", critical" : "")}");
            }
            Note($"Parts spec: {n} parts");

            // The root rigidbody. Mass and inertia are worked out at runtime from the parts (B52Systems.MassSync), these are
            // the starting values: empty weight plus full fuel, no built-in drag (the aero model has it all), smooth motion
            // for the camera, and continuous collision against the ground so a fast touchdown can't tunnel through.
            var rb = ourRoot.GetComponent<Rigidbody>();
            if (rb)
            {
                float m = Phys.Values.Sum(v => v.mass + v.fuel);
                rb.mass = m; rb.drag = 0f; rb.angularDrag = 0.05f; rb.useGravity = true; rb.isKinematic = false;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.constraints = RigidbodyConstraints.None;
                foreach (var f in new[] { "m_Mass", "m_Drag", "m_AngularDrag", "m_CenterOfMass", "m_InertiaTensor", "m_InertiaRotation",
                                          "m_IncludeLayers", "m_ExcludeLayers", "m_ImplicitCom", "m_ImplicitTensor", "m_UseGravity",
                                          "m_IsKinematic", "m_Interpolate", "m_Constraints", "m_CollisionDetection" })
                    B52DonorAudit.Chose(rb, f);
                Note($"Rigidbody: {m:F0} kg to start (MassSync keeps it), continuous collision");
            }
        }

        static (Vector3[] verts, Mesh mesh) PartSkin(Transform t)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (!mf) { var vis = t.Find(t.name + "_visible"); if (vis) mf = vis.GetComponent<MeshFilter>(); }
            if (!mf || !mf.sharedMesh) return (new Vector3[0], null);
            var m = mf.sharedMesh;
            return (m.vertices.Select(v => t.InverseTransformPoint(mf.transform.TransformPoint(v))).ToArray(), m);
        }

        // Volume of the skin (signed tetrahedra from the origin), m3, in the part's frame; bounds x 0.35 if the skin isn't closed enough.
        static float SkinVolume(Mesh m, Transform t)
        {
            if (m == null) return 0f;
            var v = m.vertices; var tri = m.triangles; double vol = 0;
            var s = t.lossyScale;
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = Vector3.Scale(v[tri[i]], s), b = Vector3.Scale(v[tri[i + 1]], s), c = Vector3.Scale(v[tri[i + 2]], s);
                vol += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
            }
            var bs = Vector3.Scale(m.bounds.size, s);
            float box = bs.x * bs.y * bs.z;
            float abs = Mathf.Abs((float)vol);
            return abs > 0.05f * box && abs < box ? abs : 0.35f * box;
        }

    }
}
