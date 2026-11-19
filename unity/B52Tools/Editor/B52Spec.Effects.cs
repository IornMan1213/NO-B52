using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52's own particle effects (docs/DONOR_REPLACEMENT.md, phase 6): the eight engines' heat haze, the six gear
    /// legs' dust and the two nav-light glows. Each effect is reset to Unity's defaults and then set for the B-52, keeping
    /// only the game's material (its shared smoke, haze and light textures) and the object the game's scripts point at
    /// (JetNozzle drives the haze's size, speed, life, opacity and rate from its own settings; LandingGear plays and stops
    /// the dust). The wingtip vortices, contact sparks and vapour cone are fed by the game's own emitters (floating-origin
    /// and ribbon-trail setups) and stay as the game configures them.
    /// </summary>
    public static partial class B52Builder
    {
        static void SpecEffects()
        {
            var blankGo = new GameObject("blank");
            var blankPs = blankGo.AddComponent<ParticleSystem>();
            var blankR = blankGo.GetComponent<ParticleSystemRenderer>();
            int haze = 0, dust = 0, glow = 0;
            foreach (var ps in ourRoot.GetComponentsInChildren<ParticleSystem>(true))
            {
                var go = ps.gameObject; var n = go.name;
                string kind = n.StartsWith("heat_haze") ? "haze" : n.StartsWith("wheel_") ? "dust" : n.EndsWith("_effects") && n.StartsWith("navlight") ? "glow" : null;
                if (kind == null) continue;
                var r = go.GetComponent<ParticleSystemRenderer>();
                var mat = r ? r.sharedMaterial : null;
                EditorUtility.CopySerialized(blankPs, ps);
                if (r) { EditorUtility.CopySerialized(blankR, r); r.sharedMaterial = mat; }
                var main = ps.main; var em = ps.emission; var sh = ps.shape;
                main.loop = true; main.duration = 1f; main.gravityModifier = 0f;
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                if (r) { r.renderMode = ParticleSystemRenderMode.Billboard; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }

                if (kind == "haze")
                {
                    // Behind each F130's core nozzle (about 0.8 m across, inside the fan duct), blowing aft. JetNozzle sets
                    // size, speed, life, opacity and rate every frame from B52Spec.Engines' heat-haze values; these are
                    // the idle starting values. The haze leaves at the aircraft's speed plus the exhaust's, slows in the
                    // air, grows and fades.
                    go.transform.rotation = Quaternion.LookRotation(-ourRoot.forward, ourRoot.up);
                    main.playOnAwake = false; main.maxParticles = 200; main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startLifetime = 1.2f; main.startSpeed = 15f; main.startSize = 1.1f; main.startColor = new Color(1f, 1f, 1f, 0.35f);
                    em.rateOverTime = 8f;
                    sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 3f; sh.radius = 0.35f;
                    var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Linear(0f, 0.6f, 1f, 1.6f));
                    var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0.1f);
                    var inh = ps.inheritVelocity; inh.enabled = true; inh.mode = ParticleSystemInheritVelocityMode.Initial; inh.curve = 1f;
                    var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1000f; lim.drag = 0.6f; lim.multiplyDragByParticleSize = false;
                    if (r) r.maxParticleSize = 2f;
                    haze++;
                }
                else if (kind == "dust")
                {
                    // Dust thrown up by the tyres on unpaved ground (LandingGear plays it above 2 m/s): the B-52's main
                    // trucks (dual 56 in tyres, ~60 t each) throw big, slow, tan clouds; the outriggers small ones.
                    bool outrigger = n.StartsWith("wheel_F");
                    main.playOnAwake = false; main.maxParticles = 250; main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f);
                    main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
                    main.startSize = outrigger ? new ParticleSystem.MinMaxCurve(2f, 3.5f) : new ParticleSystem.MinMaxCurve(4f, 7f);
                    main.startColor = new Color(0.72f, 0.65f, 0.52f, 0.45f);
                    main.gravityModifier = -0.03f;
                    em.rateOverTime = outrigger ? 6f : 12f;
                    sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = outrigger ? 0.4f : 0.8f;
                    var sol = ps.sizeOverLifetime; sol.enabled = true; sol.size = new ParticleSystem.MinMaxCurve(1f, Linear(0f, 0.4f, 1f, 1.5f));
                    var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(0.05f);
                    var inh = ps.inheritVelocity; inh.enabled = true; inh.mode = ParticleSystemInheritVelocityMode.Initial; inh.curve = 0.3f;
                    var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 1000f; lim.drag = 0.2f; lim.multiplyDragByParticleSize = false;
                    if (r) r.maxParticleSize = 1f;
                    dust++;
                }
                else
                {
                    // The nav light's glow: one particle that stays, the size of the light's halo seen from a distance
                    // (a 56 m aircraft's lights are spotted further off than a fighter's), coloured by its material.
                    main.playOnAwake = true; main.maxParticles = 1; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.startLifetime = 1e6f; main.startSpeed = 0f; main.startSize = 0.8f; main.startColor = Color.white;
                    main.startRotation = 0f;
                    em.rateOverTime = 10f;
                    sh.enabled = false;
                    if (r) r.maxParticleSize = 10f;
                    glow++;
                }
                B52DonorAudit.Chose(ps, "*");
                if (r) B52DonorAudit.Chose(r, "*");
            }
            Object.DestroyImmediate(blankGo);
            Note($"Effects: {haze} heat haze, {dust} tyre dust, {glow} nav-light glows set for the B-52");
        }

        // Fade in over the first part of the life, out to nothing at the end.
        static ParticleSystem.MinMaxGradient Fade(float fadeIn)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, fadeIn), new GradientAlphaKey(0f, 1f) });
            return new ParticleSystem.MinMaxGradient(g);
        }
    }
}
