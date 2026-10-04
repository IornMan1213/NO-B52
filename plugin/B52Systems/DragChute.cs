using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52's landing drag chute (44 ft / 13.4 m canopy). Deploys on the runway when the throttle is at idle and
    /// the wheel brakes are applied below 165 kt; jettisons below 10 m/s or when power comes back up. Drag
    /// 0.5 rho V^2 Cd A (Cd 0.55, A 141 m2: ~230 kN at 135 kt) pulls from the tail along the airflow, inflating over
    /// 1.5 s. One chute per landing; it re-arms after the next lift-off. The canopy is a simple procedural cup.
    /// </summary>
    public class DragChute : MonoBehaviour
    {
        private const float Diameter = 13.4f, Cd = 0.55f, LineLength = 32f;
        private const float MaxDeploySpeed = 85f, JettisonSpeed = 10f, InflateTime = 1.5f;
        private static readonly Vector3 TailLocal = new Vector3(0f, 0.6f, -21.3f);   // aft end of fuselage_R

        private Aircraft aircraft;
        private Rigidbody rb;
        private bool deployed, used;
        private float inflate;
        private GameObject canopy;
        private LineRenderer lines;

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
            if (!onGround && aircraft.radarAlt > 20f) used = false;                  // re-arm after lift-off

            if (!deployed && !used && onGround && inp.throttle <= 0.01f && inp.brake > 0.3f
                && aircraft.speed > JettisonSpeed * 2f && aircraft.speed < MaxDeploySpeed)
                Deploy();
            if (deployed && (aircraft.speed < JettisonSpeed || inp.throttle > 0.3f || !onGround && aircraft.radarAlt > 30f))
                Jettison();
            if (!deployed || !aircraft.LocalSim) return;

            inflate = Mathf.Min(1f, inflate + Time.fixedDeltaTime / InflateTime);
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
            Plugin.Log.LogInfo($"B-52J drag chute deployed at {aircraft.speed * 1.944f:F0} kt");
            GetComponent<Telemetry>()?.Note($"CHUTE deployed {aircraft.speed * 1.944f:F0} kt");
        }

        private void Jettison()
        {
            deployed = false;
            if (canopy) Destroy(canopy);
            if (lines) Destroy(lines.gameObject);
            GetComponent<Telemetry>()?.Note($"CHUTE jettisoned {aircraft.speed * 1.944f:F0} kt");
        }

        private void OnDestroy() { if (canopy) Destroy(canopy); if (lines) Destroy(lines.gameObject); }

        /// <summary>A shallow cup (open toward the aircraft) with a vent, white/orange gores, and one riser line.</summary>
        private void BuildVisual()
        {
            var mat = FindMaterial();
            canopy = new GameObject("B52_DragChute");
            const int seg = 20, rings = 5; float r = Diameter * 0.5f, depth = Diameter * 0.35f;
            var verts = new Vector3[(rings + 1) * (seg + 1)];
            var cols = new Color[verts.Length];
            var uvs = new Vector2[verts.Length];
            for (int i = 0; i <= rings; i++)
            {
                float t = (float)i / rings, ang = Mathf.Lerp(0.12f, 1f, t) * Mathf.PI * 0.5f;
                for (int j = 0; j <= seg; j++)
                {
                    float a = j * Mathf.PI * 2f / seg;
                    int k = i * (seg + 1) + j;
                    verts[k] = new Vector3(Mathf.Cos(a) * Mathf.Sin(ang) * r, Mathf.Sin(a) * Mathf.Sin(ang) * r, Mathf.Cos(ang) * depth);
                    cols[k] = (j / 2) % 2 == 0 ? new Color(0.95f, 0.95f, 0.92f) : new Color(0.95f, 0.45f, 0.1f);
                    uvs[k] = new Vector2(0.5f, 0.5f);
                }
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < rings; i++)
                for (int j = 0; j < seg; j++)
                {
                    int a0 = i * (seg + 1) + j, a1 = a0 + 1, b0 = a0 + seg + 1, b1 = b0 + 1;
                    tris.AddRange(new[] { a0, b0, a1, a1, b0, b1, a0, a1, b0, a1, b1, b0 });   // both sides
                }
            var mesh = new Mesh { name = "B52_DragChute", vertices = verts, colors = cols, uv = uvs, triangles = tris.ToArray() };
            mesh.RecalculateNormals();
            canopy.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = canopy.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat;
            var lgo = new GameObject("B52_DragChuteRiser");
            lines = lgo.AddComponent<LineRenderer>();
            lines.positionCount = 2; lines.startWidth = 0.15f; lines.endWidth = 0.6f; lines.sharedMaterial = mat;
        }

        private Material FindMaterial()
        {
            // A vertex-colour-capable material if the game has one loaded, else whatever the airframe uses.
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Sprites/Default");
            if (sh) return new Material(sh) { color = Color.white };
            var r = GetComponentInChildren<MeshRenderer>();
            return r ? r.sharedMaterial : null;
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachDragChute
    {
        private static void Postfix(Aircraft __instance)
        {
            if (Plugin.IsB52(__instance) && !__instance.GetComponent<DragChute>())
                __instance.gameObject.AddComponent<DragChute>();
        }
    }
}
