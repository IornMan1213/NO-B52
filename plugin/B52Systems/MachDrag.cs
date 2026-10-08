using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Transonic drag rise for the B-52's thick, 35-degree swept wing. The game's own compressibility term adds at most
    /// 15 % drag around Mach 1 (fine for fighters), so in a dive the B-52 reached Mach 1.3. This adds Lock's
    /// fourth-power wave drag above the critical Mach number: dCd = 50 (M - 0.80)^4 on the 371 m2 wing, capped at 0.10.
    /// Cruise (Mach 0.84) is untouched (dCd 0.0001); drag divergence (dCd 0.002) is at Mach 0.88, just under the real
    /// B-52's limit (Mmo 0.90). Estimated top speeds at 132 t: 30-degree dive Mach 0.98, 65-degree dive at 5,000 ft
    /// Mach 0.96, vertical about Mach 1.0 (it reached 1.3 before).
    /// Above Mach 0.88 the airframe buffets (the game's own shake).
    /// </summary>
    public class MachDrag : MonoBehaviour
    {
        private const float WingArea = 371f, Mcrit = 0.80f, K = 50f, MaxDcd = 0.10f, BuffetMach = 0.88f;

        private Aircraft aircraft;
        private Rigidbody rb;

        /// <summary>Extra drag coefficient (on wing area) at Mach m.</summary>
        internal static float WaveDragCoef(float m) => m <= Mcrit ? 0f : Mathf.Min(K * Mathf.Pow(m - Mcrit, 4f), MaxDcd);

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            rb = aircraft ? aircraft.rb : null;
            if (!aircraft) Destroy(this);
        }

        private void FixedUpdate()
        {
            if (!rb || !aircraft.LocalSim || aircraft.disabled) return;
            var v = rb.velocity;
            float speed = v.magnitude;
            float mach = speed / LevelInfo.GetSpeedOfSound(transform.GlobalPosition().y);
            if (mach <= Mcrit) return;
            float drag = 0.5f * aircraft.airDensity * speed * speed * WingArea * WaveDragCoef(mach);
            rb.AddForce(-v / speed * drag);
            if (mach > BuffetMach)
                aircraft.ShakeAircraft(0.2f * Mathf.Clamp01((mach - BuffetMach) / 0.07f), 0.15f * aircraft.airDensity);
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachMachDrag
    {
        private static void Postfix(Aircraft __instance)
        {
            if (Plugin.IsB52(__instance) && !__instance.GetComponent<MachDrag>())
                __instance.gameObject.AddComponent<MachDrag>();
        }
    }
}
