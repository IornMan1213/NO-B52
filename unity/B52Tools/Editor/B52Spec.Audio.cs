using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52's own audio sources, nav lights and their housings (docs/DONOR_REPLACEMENT.md, phase 6), and the components
    /// kept as the game's on purpose (the pilot character, networking), each with its reason.
    /// </summary>
    public static partial class B52Builder
    {
        // Audio: each source is reset to Unity's defaults and then set for its role on a 56 m, eight-engine aircraft. The
        // clips and the mixer group stay the game's (shared by every aircraft). Distances in metres: full volume inside
        // minDistance, logarithmic fade out to maxDistance. The game drives the volume of the exhaust (from thrust), the tyres
        // (from load and wheel speed), the gear and the power hum, and the pitch of the fan whine (from N1); the whine's
        // volume and the bay doors' are the source's own, set here.
        struct Sound { public float min, max, volume, spread, doppler; public bool loop, awake; public int priority; }

        static Sound SoundFor(string go) =>
            go.StartsWith("engine")  ? new Sound { min = 30f, max = 4000f, volume = 0.9f, spread = 60f, doppler = 1f, loop = true, awake = true, priority = 64 }  // fan whine
          : go.StartsWith("nozzle")  ? new Sound { min = 50f, max = 7000f, volume = 0f, spread = 90f, doppler = 1f, loop = true, awake = true, priority = 48 }    // exhaust roar: carries furthest
          : go.StartsWith("wheel")   ? new Sound { min = 10f, max = 350f, volume = 0f, spread = 30f, doppler = 0.5f, loop = true, awake = false, priority = 128 } // tyres on the runway
          : go.StartsWith("bayDoor") ? new Sound { min = 10f, max = 500f, volume = 1f, spread = 45f, doppler = 0f, loop = false, awake = false, priority = 128 } // bay door hydraulics
          : new Sound { min = 3f, max = 60f, volume = 0f, spread = 0f, doppler = 0f, loop = true, awake = true, priority = 160 };                               // the power supply's hum (aircraft root)

        static void SpecAudioAndLights()
        {
            // ---------------------------------------------------------------- audio sources
            var blankGo = new GameObject("blank");
            var blank = blankGo.AddComponent<AudioSource>();
            int n = 0;
            foreach (var a in ourRoot.GetComponentsInChildren<AudioSource>(true))
            {
                var clip = a.clip; var mixer = a.outputAudioMixerGroup;
                EditorUtility.CopySerialized(blank, a);                     // Unity's defaults: nothing of the donor's left
                var s = SoundFor(a.gameObject.name);
                a.clip = clip; a.outputAudioMixerGroup = mixer;
                a.playOnAwake = s.awake; a.loop = s.loop; a.volume = s.volume; a.pitch = 1f;
                // One whine per pod (BuildEngines): the second engine of each pod (engine2, 4, 6, 8) is silent.
                if (a.gameObject.name.StartsWith("engine") && int.TryParse(a.gameObject.name.Substring(6), out var en) && en % 2 == 0) a.volume = 0f;
                a.spatialBlend = 1f; a.spread = s.spread; a.dopplerLevel = s.doppler; a.priority = s.priority;
                a.rolloffMode = AudioRolloffMode.Logarithmic; a.minDistance = s.min; a.maxDistance = s.max;
                a.reverbZoneMix = 1f; a.bypassEffects = false; a.bypassListenerEffects = false; a.bypassReverbZones = false;
                B52DonorAudit.Chose(a, "*");
                n++;
            }
            Object.DestroyImmediate(blankGo);
            Note($"Audio: {n} sources set for their roles");

            // ---------------------------------------------------------------- nav lights and their housings
            foreach (var side in new[] { "L", "R" })
            {
                float sgn = side == "R" ? 1f : -1f;
                var tip = Find(ourRoot, "wingtip_" + side);
                var light = Find(ourRoot, "navlight_" + side);
                if (!tip || !light) continue;
                // Housing: the B-52's wingtip nav light is a small clear dome on the tip fairing, about 0.2 x 0.14 x 0.32 m,
                // bulging outboard. Built here as half an ellipsoid on the tip's outermost point.
                var tipVerts = WorldVerts(tip);
                if (tipVerts.Length == 0) continue;
                var outer = tipVerts.OrderByDescending(p => p.x * sgn).First();
                light.SetPositionAndRotation(outer, ourRoot.rotation);
                var mesh = NavDome(sgn, 0.10f, 0.07f, 0.16f);
                var path = Gen + $"/B52_navlight_{side}.asset";
                AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
                var mf = light.GetComponent<MeshFilter>(); if (mf) { mf.sharedMesh = mesh; B52DonorAudit.Chose(mf, "*"); }
                var mr = light.GetComponent<MeshRenderer>();
                if (mr)
                {
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                    B52DonorAudit.Chose(mr, "*");                          // the game's lens materials (lit by NavLights)
                }
                // The light itself: aviation red (left) and green (right), a spot covering its 110 deg sector forward and
                // outboard, reaching the wing and the ground near the tip.
                var fx = Find(ourRoot, $"navlight_{side}_effects");
                var l = fx ? fx.GetComponent<Light>() : null;
                if (l)
                {
                    l.type = LightType.Spot; l.spotAngle = 110f; l.innerSpotAngle = 80f; l.range = 20f; l.intensity = 6f;
                    l.color = side == "L" ? new Color(1f, 0.06f, 0.03f) : new Color(0.1f, 1f, 0.35f);
                    l.shadows = LightShadows.None; l.renderMode = LightRenderMode.Auto;
                    fx.SetPositionAndRotation(outer + Vector3.right * sgn * 0.08f, Quaternion.LookRotation((ourRoot.forward + ourRoot.right * sgn).normalized, ourRoot.up));
                    B52DonorAudit.Chose(l, "*");
                    foreach (var c in fx.GetComponents<Component>()) if (c.GetType().Name == "UniversalAdditionalLightData") B52DonorAudit.Chose(c, "*");
                }
            }
            var nav = ourRoot.GetComponent(T("NavLights"));
            if (nav) Set(nav, "navLights", p =>
            {
                for (int i = 0; i < p.arraySize; i++)
                {
                    var e = p.GetArrayElementAtIndex(i);
                    var objs = e.FindPropertyRelative("objects"); if (objs != null) objs.arraySize = 0;     // no extra objects switched with the light
                    var cs = e.FindPropertyRelative("controlState"); if (cs != null && cs.propertyType == SerializedPropertyType.Enum) cs.enumValueIndex = 0;
                }
            });

            // ---------------------------------------------------------------- kept as the game's, on purpose
            // The crew are the game's pilot character (decided: no custom crew model): its body, skeleton, animation and
            // body collider stay as the game made them, at the B-52's stations (AddCrew).
            foreach (var pl in ourRoot.GetComponentsInChildren(T("Pilot"), true))
                foreach (var c in pl.GetComponentsInChildren<Component>(true))
                    if (c is Animator || c is SkinnedMeshRenderer || c is CapsuleCollider) B52DonorAudit.Chose(c, "*");
            // Networking: the game's own components that sync an aircraft between players; their settings must be the
            // game's for multiplayer to work.
            foreach (var c in ourRoot.GetComponents<Component>())
                if (c.GetType().Name == "NetworkIdentity" || c.GetType().Name == "AircraftNetworkTransform") B52DonorAudit.Chose(c, "*");
            // The cockpit's live tactical display is the game's UI prefab; its engine sounds list is the B-52's engines.
            var ck = Find(ourRoot, "cockpit_int") ? Find(ourRoot, "cockpit_int").GetComponent(T("Cockpit")) : null;
            if (ck) { B52DonorAudit.Chose(ck, "tacScreenUIPrefab"); B52DonorAudit.Chose(ck, "engineSources"); }
        }

        // Half an ellipsoid bulging towards +x (sgn 1) or -x (sgn -1), radii rx (outboard), ry (up), rz (along the chord).
        static Mesh NavDome(float sgn, float rx, float ry, float rz)
        {
            const int lat = 8, lon = 16;
            var v = new List<Vector3>(); var t = new List<int>();
            for (int i = 0; i <= lat; i++)
            {
                float th = Mathf.PI * 0.5f * i / lat;                       // 0 at the base, pi/2 at the tip of the dome
                for (int j = 0; j <= lon; j++)
                {
                    float ph = 2f * Mathf.PI * j / lon;
                    v.Add(new Vector3(sgn * rx * Mathf.Sin(th), ry * Mathf.Cos(th) * Mathf.Sin(ph), rz * Mathf.Cos(th) * Mathf.Cos(ph)));
                }
            }
            for (int i = 0; i < lat; i++)
                for (int j = 0; j < lon; j++)
                {
                    int a = i * (lon + 1) + j, b = a + lon + 1;
                    if (sgn > 0) t.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); else t.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b });
                }
            var m = new Mesh { name = sgn > 0 ? "B52_navlight_R" : "B52_navlight_L" };
            m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            var uv = v.Select(p => new Vector2(p.y / (2 * ry) + 0.5f, p.z / (2 * rz) + 0.5f)).ToList(); m.SetUVs(0, uv);
            return m;
        }
    }
}
