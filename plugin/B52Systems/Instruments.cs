using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Drives the B-52J flight deck's standby instruments: airspeed (0-500 kt), altimeter (100 ft per mark),
    /// VSI (+-4000 fpm, zero at the 4 o'clock rest position) and the ADI card (roll about the panel normal,
    /// pitch by scrolling the card texture). The panel leans back 12 deg, so needles turn about its normal.
    /// </summary>
    public class Instruments : MonoBehaviour
    {
        private Aircraft aircraft;
        private Transform asi, alt, vsi, adi;
        private Quaternion asi0, alt0, vsi0, adi0;
        private Material adiMat;
        private Rigidbody rb;
        // Panel normal toward the crew, in cockpit_int space (panel tilted back 12 deg).
        private static readonly Vector3 Normal = new Vector3(0f, Mathf.Sin(12f * Mathf.Deg2Rad), -Mathf.Cos(12f * Mathf.Deg2Rad));

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            rb = GetComponent<Rigidbody>();
            asi = Find("needle_asi"); alt = Find("needle_alt"); vsi = Find("needle_vsi"); adi = Find("adi_card");
            if (asi) asi0 = asi.localRotation;
            if (alt) alt0 = alt.localRotation;
            if (vsi) vsi0 = vsi.localRotation;
            if (adi) { adi0 = adi.localRotation; var r = adi.GetComponent<Renderer>(); if (r) adiMat = r.material; }
        }

        private Transform Find(string n)
        {
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == n) return t;
            return null;
        }

        private void LateUpdate()
        {
            if (!aircraft) return;
            float kts = aircraft.speed * 1.94384f;
            float ft = aircraft.transform.position.GlobalY() * 3.28084f;
            float fpm = rb ? rb.velocity.y * 196.85f : 0f;
            if (asi) asi.localRotation = Quaternion.AngleAxis(Mathf.Clamp(kts, 0f, 500f) / 500f * 300f, Normal) * asi0;
            if (alt) alt.localRotation = Quaternion.AngleAxis(Mathf.Repeat(ft, 1000f) / 1000f * 300f, Normal) * alt0;
            if (vsi) vsi.localRotation = Quaternion.AngleAxis(120f + Mathf.Clamp(fpm / 1000f, -4f, 4f) * 30f, Normal) * vsi0;
            if (adi)
            {
                var e = aircraft.transform.eulerAngles;
                float pitch = e.x > 180f ? e.x - 360f : e.x;      // Unity: positive x = nose down
                float roll = e.z > 180f ? e.z - 360f : e.z;
                adi.localRotation = Quaternion.AngleAxis(roll, Normal) * adi0;
                if (adiMat)
                {
                    var off = new Vector2(0f, Mathf.Clamp(pitch, -30f, 30f) * (4f / 512f));
                    if (adiMat.HasProperty("_BaseMap")) adiMat.SetTextureOffset("_BaseMap", off);
                    if (adiMat.HasProperty("_EmissionMap")) adiMat.SetTextureOffset("_EmissionMap", off);
                }
            }
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachInstruments
    {
        private static void Postfix(Aircraft __instance)
        {
            if (Plugin.IsB52(__instance) && !__instance.GetComponent<Instruments>())
                __instance.gameObject.AddComponent<Instruments>();
        }
    }
}
