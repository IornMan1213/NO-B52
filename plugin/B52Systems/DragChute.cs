using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52's landing drag chute (44 ft / 13.4 m canopy). Deploys on the runway when the throttle is at idle and
    /// the wheel brakes are applied below 165 kt; jettisons below 10 m/s or when power comes back up. Drag
    /// 0.5 rho V^2 Cd A (Cd 0.55, A 141 m2: ~230 kN at 135 kt) pulls from the tail along the airflow, inflating over
    /// 1.5 s. One chute per landing; it re-arms after the next lift-off. The canopy is a simple procedural cup.
    /// Every client runs the same deploy/jettison logic from the synced pilot inputs (throttle and brake reach other
    /// players), so everyone sees the chute; only the owner applies its drag. A released chute drifts back and settles
    /// on the runway before it is removed.
    /// </summary>
    public class DragChute : MonoBehaviour
    {
        private const float Diameter = 13.4f, Cd = 0.55f, LineLength = 32f;
        private const float MaxDeploySpeed = 85f, JettisonSpeed = 10f, InflateTime = 1.5f;
        private const float Kts = 1.94384f;
        private static readonly Vector3 TailLocal = new Vector3(0f, 0.6f, -21.3f);   // aft end of fuselage_R
        private static bool hintShown;                                               // once per game session

        private Aircraft aircraft;
        private Rigidbody rb;
        private bool deployed, used;
        private float inflate;
        private GameObject canopy;
        private LineRenderer lines;
        private Mesh mesh;                      // built once per aircraft, shared by every canopy it deploys
        private Material mat, lineMat;
        private Texture2D goreTex;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            rb = aircraft ? aircraft.rb : null;
            if (!aircraft || !aircraft.networked) { Destroy(this); return; }
        }

        private void FixedUpdate()
        {
            if (!aircraft || !rb) return;
            var inp = aircraft.GetInputs();
            bool onGround = aircraft.radarAlt < 2f;
            if (aircraft.radarAlt > 20f) used = false;                               // re-arm after lift-off

            bool inSpeedRange = aircraft.speed > JettisonSpeed * 2f && aircraft.speed < MaxDeploySpeed;
            if (!deployed && !used && onGround && inSpeedRange && inp.throttle <= 0.01f)
            {
                if (inp.brake > 0.3f) Deploy();
                else if (!hintShown && GameManager.IsLocalAircraft(aircraft))
                {
                    hintShown = true;
                    HudReadout.Flash("Hold the wheel brakes to stream the drag chute", 4f);
                }
            }
            if (deployed && (aircraft.speed < JettisonSpeed || inp.throttle > 0.3f || aircraft.radarAlt > 30f))
                Jettison();
            if (!deployed) return;

            inflate = Mathf.Min(1f, inflate + Time.fixedDeltaTime / InflateTime);    // on every client: drives the canopy
            if (!aircraft.LocalSim) return;
            var v = rb.velocity;
            float area = Mathf.PI * Diameter * Diameter * 0.25f;
            float drag = 0.5f * aircraft.airDensity * v.sqrMagnitude * Cd * area * inflate * inflate;
            rb.AddForceAtPosition(-v.normalized * drag, transform.TransformPoint(TailLocal));
        }

        private void LateUpdate()
        {
            if (!deployed || !canopy || !rb) return;
            var tail = transform.TransformPoint(TailLocal);
            var back = rb.velocity.sqrMagnitude > 1f ? -rb.velocity.normalized : -transform.forward;
            float s = Mathf.Lerp(0.15f, 1f, inflate);
            canopy.transform.position = tail + back * LineLength * Mathf.Lerp(0.3f, 1f, inflate) + Vector3.up * 1.5f * inflate;
            canopy.transform.rotation = Quaternion.LookRotation(-back, Vector3.up) *
                                        Quaternion.Euler(Mathf.Sin(Time.time * 2.1f) * 3f, Mathf.Sin(Time.time * 1.7f) * 3f, 0f);
            canopy.transform.localScale = new Vector3(s, s, Mathf.Lerp(0.6f, 1f, inflate));
            lines.SetPosition(0, tail);
            lines.SetPosition(1, canopy.transform.position - canopy.transform.forward * 0.2f);
        }

        private void Deploy()
        {
            deployed = true; used = true; inflate = 0f;
            BuildVisual();
            Plugin.Log.LogInfo($"B-52J drag chute deployed at {aircraft.speed * Kts:F0} kt");
            GetComponent<Telemetry>()?.Note($"CHUTE deployed {aircraft.speed * Kts:F0} kt");
            if (GameManager.IsLocalAircraft(aircraft)) HudReadout.Flash("CHUTE OUT");
        }

        private void Jettison()
        {
            deployed = false;
            if (canopy)
            {
                // Let go of it: it keeps some of its speed, falls and collapses on the runway, then is removed.
                var drop = canopy.AddComponent<ChuteDrop>();
                drop.Begin(rb ? rb.velocity * 0.6f : Vector3.zero, lines ? lines.gameObject : null);
                canopy = null; lines = null;
            }
            GetComponent<Telemetry>()?.Note($"CHUTE jettisoned {aircraft.speed * Kts:F0} kt");
            if (GameManager.IsLocalAircraft(aircraft)) HudReadout.Flash("CHUTE RELEASED");
        }

        private void OnDestroy()
        {
            if (canopy) Destroy(canopy);
            if (lines) Destroy(lines.gameObject);
            if (mesh) Destroy(mesh);
            if (mat) Destroy(mat);
            if (lineMat) Destroy(lineMat);
            if (goreTex) Destroy(goreTex);
        }

        /// <summary>A shallow cup (open toward the aircraft) with a vent, white/orange gores, and one riser line.</summary>
        private void BuildVisual()
        {
            if (!mesh) BuildAssets();
            canopy = new GameObject("B52_DragChute");
            canopy.AddComponent<MeshFilter>().sharedMesh = mesh;
            canopy.AddComponent<MeshRenderer>().sharedMaterial = mat;
            var lgo = new GameObject("B52_DragChuteRiser");
            lines = lgo.AddComponent<LineRenderer>();
            lines.positionCount = 2; lines.startWidth = 0.15f; lines.endWidth = 0.6f; lines.sharedMaterial = lineMat ? lineMat : mat;
        }

        private void BuildAssets()
        {
            const int seg = 20, rings = 5; float r = Diameter * 0.5f, depth = Diameter * 0.35f;
            int n = (rings + 1) * (seg + 1);
            // Two copies of the vertices, one per side, so each side gets its own normals for lighting.
            var verts = new Vector3[n * 2];
            var cols = new Color[n * 2];
            var uvs = new Vector2[n * 2];
            for (int i = 0; i <= rings; i++)
            {
                float t = (float)i / rings, ang = Mathf.Lerp(0.12f, 1f, t) * Mathf.PI * 0.5f;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j * Mathf.PI * 2f / seg;
                    int k = i * (seg + 1) + j;
                    verts[k] = verts[k + n] = new Vector3(Mathf.Cos(a) * Mathf.Sin(ang) * r, Mathf.Sin(a) * Mathf.Sin(ang) * r, Mathf.Cos(ang) * depth);
                    cols[k] = cols[k + n] = (j / 2) % 2 == 0 ? new Color(0.95f, 0.95f, 0.92f) : new Color(0.95f, 0.45f, 0.1f);
                    uvs[k] = uvs[k + n] = new Vector2((j + 0.5f) / seg, t);
                }
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a0 = i * (seg + 1) + j, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                    tris.AddRange(new[] { a0, b0, a1, a1, b0, b1 });                         // inside
                    tris.AddRange(new[] { a0 + n, a1 + n, b0 + n, a1 + n, b1 + n, b0 + n }); // outside
                }
            mesh = new Mesh { name = "B52_DragChute", vertices = verts, colors = cols, uv = uvs, triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            // Lit canopy: URP/Lit with a gore texture, so it shades with the scene (no glowing at night). Falls back to
            // the vertex-coloured unlit particle shader if the game build has no URP/Lit.
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit)
            {
                goreTex = new Texture2D(seg, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, name = "B52_DragChuteGores" };
                for (int j = 0; j < seg; j++) goreTex.SetPixel(j, 0, (j / 2) % 2 == 0 ? new Color(0.92f, 0.92f, 0.88f) : new Color(0.93f, 0.42f, 0.08f));
                goreTex.Apply(false, true);
                mat = new Material(lit) { name = "B52_DragChute" };
                mat.SetTexture("_BaseMap", goreTex);
                mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.15f);
            }
            else
            {
                var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
                mat = sh ? new Material(sh) { color = Color.white, name = "B52_DragChute" } : null;
            }
            var unlit = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (unlit) lineMat = new Material(unlit) { color = new Color(0.75f, 0.75f, 0.72f), name = "B52_DragChuteRiser" };
        }
    }

    /// <summary>A released drag chute: drifts back with the speed it had, sinks to the ground, collapses, then goes.</summary>
    public class ChuteDrop : MonoBehaviour
    {
        private const float Life = 5f;
        private Vector3 vel;
        private GameObject riser;
        private float t, groundY;
        private Vector3 scale0;

        internal void Begin(Vector3 velocity, GameObject riserObj)
        {
            vel = velocity; riser = riserObj; scale0 = transform.localScale;
            groundY = Physics.Raycast(transform.position + Vector3.up * 20f, Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y : transform.position.y - 5f;
            if (riser) Destroy(riser);
        }

        private void Update()
        {
            t += Time.deltaTime;
            float dt = Time.deltaTime;
            vel = Vector3.MoveTowards(vel, Vector3.zero, 18f * dt);              // air drag on an open canopy
            var p = transform.position + vel * dt;
            p.y = Mathf.MoveTowards(p.y, groundY + 0.3f, 2.5f * dt);
            transform.position = p;
            float k = Mathf.Clamp01(t / 2.5f);
            transform.localScale = new Vector3(scale0.x * Mathf.Lerp(1f, 0.7f, k), scale0.y * Mathf.Lerp(1f, 0.25f, k), scale0.z * Mathf.Lerp(1f, 0.15f, k));
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized + Vector3.down * 0.8f, Vector3.up), 1.5f * dt);
            if (t > Life) Destroy(gameObject);
        }
    }

}
