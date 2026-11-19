using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52's vapour and contrails (docs/DONOR_REPLACEMENT.md, phase 6), and the last aircraft and part settings that
    /// are runtime state or wiring, each marked with its reason.
    ///
    /// VaporEffect feeds each emitter's particle system: emitted while the angle of attack is above a threshold that falls
    /// with airspeed (alphaThresholdVelocity), or around the speed of sound (transonic), or, for contrails, between 7.5 and
    /// 12.5 km. The donor had the wingtip vortices and a vapour cone, but no contrails: the B-52's eight trails are added.
    /// </summary>
    public static partial class B52Builder
    {
        static void SpecVapor()
        {
            var vapor = ourRoot.GetComponent(T("VaporEffect"));
            if (!vapor) { Note("  ! no VaporEffect"); return; }

            // Contrails: one soft trail per engine, starting about 0.8 m behind the core nozzle, spreading to about 24 m
            // across over 30 s. Emitted by VaporEffect (one particle per nozzle per tick, in the game's floating-origin
            // space); the game draws them between 7.5 and 12.5 km.
            var cgo = new GameObject("contrails");
            cgo.transform.SetParent(ourRoot, false);
            var ps = cgo.AddComponent<ParticleSystem>();
            var r = cgo.GetComponent<ParticleSystemRenderer>();
            var main = ps.main; var em = ps.emission;
            main.loop = true; main.playOnAwake = true; main.duration = 1f; main.maxParticles = 3000;
            main.startLifetime = 30f; main.startSpeed = 2f; main.startSize = 3f; main.startColor = Color.white;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World; main.gravityModifier = 0f;
            em.enabled = false;
            var sh = ps.shape; sh.enabled = false;
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Linear(0f, 1f, 0.2f, 3f, 1f, 8f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0.02f);
            r.renderMode = ParticleSystemRenderMode.Billboard; r.maxParticleSize = 2f;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(DoNotShip + "/Material/smoke_background_PLACEHOLDER.mat");
            var nozzles = Enumerable.Range(1, 8).Select(i => Find(ourRoot, "nozzle" + i)).Where(t => t).Select(t => Child(t, t.name + "_contrail", t.position - ourRoot.forward * 0.8f)).ToArray();

            Set(vapor, "emitters", p =>
            {
                int vortices = 0;
                for (int i = 0; i < p.arraySize; i++)
                {
                    var e = p.GetArrayElementAtIndex(i);
                    var particles = e.FindPropertyRelative("particles").objectReferenceValue as Component;
                    bool vortex = particles && particles.name.StartsWith("wingtipvortex");
                    if (vortex)
                    {
                        // Wingtip vortices made visible at high lift in moist air: the B-52 shows them on a heavy, slow
                        // approach (about 8 deg at 70 m/s), rarely at cruise.
                        e.FindPropertyRelative("emitFrequency").floatValue = 12f;
                        e.FindPropertyRelative("minSpeed").floatValue = 50f;
                        e.FindPropertyRelative("opacity").floatValue = 0.7f;
                        e.FindPropertyRelative("alphaThresholdVelocity").animationCurveValue = Linear(0f, 200f, 50f, 14f, 80f, 8f, 150f, 5f, 300f, 3f, 700f, 2f);
                        e.FindPropertyRelative("transonic").boolValue = false;
                        e.FindPropertyRelative("contrail").boolValue = false;
                        vortices++;
                    }
                    else
                    {
                        // The transonic vapour cone, sized for the B-52: its limit is about Mach 0.9, so it shows only at
                        // the edge of a dive. Five keys: the game moves the middle three to the speed of sound.
                        e.FindPropertyRelative("emitFrequency").floatValue = 20f;
                        e.FindPropertyRelative("minSpeed").floatValue = 200f;
                        e.FindPropertyRelative("opacity").floatValue = 0.5f;
                        e.FindPropertyRelative("alphaThresholdVelocity").animationCurveValue = Linear(0f, 90f, 200f, 90f, 260f, 40f, 300f, 10f, 340f, 40f);
                        e.FindPropertyRelative("transonic").boolValue = true;
                        e.FindPropertyRelative("contrail").boolValue = false;
                    }
                }
                p.arraySize++;
                var c = p.GetArrayElementAtIndex(p.arraySize - 1);
                c.FindPropertyRelative("particles").objectReferenceValue = ps;
                var et = c.FindPropertyRelative("emitTransforms"); et.arraySize = nozzles.Length;
                for (int i = 0; i < nozzles.Length; i++) et.GetArrayElementAtIndex(i).objectReferenceValue = nozzles[i];
                c.FindPropertyRelative("emitFrequency").floatValue = 6f;
                c.FindPropertyRelative("minSpeed").floatValue = 100f;
                c.FindPropertyRelative("opacity").floatValue = 0.6f;
                c.FindPropertyRelative("alphaThresholdVelocity").animationCurveValue = Linear(0f, 0f, 1000f, 0f);
                c.FindPropertyRelative("localSpace").boolValue = false;
                c.FindPropertyRelative("transonic").boolValue = false;
                c.FindPropertyRelative("contrail").boolValue = true;
                Note($"Vapour: {vortices} wingtip vortices, the transonic cone, and contrails from {nozzles.Length} nozzles");
            });
            // The vortex and cone emitters' effects and emit points are wiring (the B-52's own wingtip and cone objects);
            // whether each draws in local space is how the game's effect is built (kept).
            B52DonorAudit.Chose(vapor, "emitters");

            // ---------------------------------------------------------------- runtime state and wiring
            var ac = ourRoot.GetComponent(T("Aircraft"));
            if (ac)
            {
                // Filled in by the game when the aircraft spawns: its weapon and countermeasure stations, RCS, ECM level,
                // simple or complex physics (B52Systems.RigidAirframe), and the references to its autopilot and controls.
                foreach (var f in new[] { "weaponStations", "RCS", "countermeasureManager", "ecmIntensity", "simplePhysics",
                                          "relaxedStabilityController", "flightAssist", "autopilot" })
                    B52DonorAudit.Chose(ac, f);
                B52DonorAudit.Chose(ac, "scrapeSound");                 // the game's metal-on-runway scrape
                SetRef(ac, "obstacleTopTransform", null);               // no obstacle-top marker: the game uses the bounds
            }
            foreach (var ap in ourRoot.GetComponentsInChildren(T("AeroPart"), true))
            {
                // No part-level damage effects (the game has no smoke for bare structure; fire comes from the tanks and
                // engines); the damage-shading indices are written by SetLiveryTargets; parts without a lift normal of
                // their own (fuselage sections, pods) take lift and side force at their own origin.
                Set(ap, "damageEffects", p => p.arraySize = 0);
                B52DonorAudit.Chose(ap, "damageEffects");
                B52DonorAudit.Chose(ap, "damageMaterial.indices");
                B52DonorAudit.Chose(ap, "liftNormal");
            }
            var loc = Find(ourRoot, "nose_sensors") ? Find(ourRoot, "nose_sensors").GetComponent(T("RadarLocator")) : null;
            // The B-52's radar warning receiver hears airborne radars as well as surface ones.
            if (loc) Set(loc, "onlySurface", p => p.boolValue = false);
        }
    }
}
