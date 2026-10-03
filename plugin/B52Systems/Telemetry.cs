using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Flight recorder for B-52J test flights. Every 0.5 s it writes one CSV row, and it logs events (part
    /// detached, gear state change, lift-off, touchdown) to BepInEx/B52_telemetry/&lt;time&gt;_&lt;aircraft&gt;.csv.
    /// Config: [Telemetry] Enabled.
    /// </summary>
    public class Telemetry : MonoBehaviour
    {
        private Aircraft aircraft;
        private Rigidbody rb;
        private StreamWriter w;
        private float next, t0;
        private UnitPart[] parts;
        private readonly HashSet<UnitPart> detached = new HashSet<UnitPart>();
        private LandingGear.GearState lastGear;
        private bool wasAirborne;
        private float maxKts, maxAltFt;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            if (!aircraft || !aircraft.networked) { Destroy(this); return; }   // hangar preview
            rb = GetComponent<Rigidbody>();
            parts = GetComponentsInChildren<UnitPart>(true);
            var dir = Path.Combine(Paths.BepInExRootPath, "B52_telemetry");
            Directory.CreateDirectory(dir);
            var file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{GetInstanceID()}.csv");
            w = new StreamWriter(file) { AutoFlush = true };
            w.WriteLine("t_s,kts,alt_ft,radar_alt_ft,vs_fpm,pitch_deg,roll_deg,aoa_deg,throttle,gear,fuel_frac,mass_kg,event");
            t0 = Time.time;
            Plugin.Log.LogInfo("B-52J telemetry -> " + file);
        }

        private void Event(string e) => Row(e);

        private void Row(string ev = "")
        {
            if (w == null || !aircraft) return;
            var e = aircraft.transform.eulerAngles;
            float pitch = -(e.x > 180f ? e.x - 360f : e.x);                  // + = nose up
            float roll = -(e.z > 180f ? e.z - 360f : e.z);                   // + = right wing down
            Vector3 v = rb ? rb.velocity : Vector3.zero;
            Vector3 vl = aircraft.transform.InverseTransformDirection(v);
            float aoa = vl.sqrMagnitude > 1f ? Mathf.Atan2(-vl.y, vl.z) * Mathf.Rad2Deg : 0f;
            float kts = aircraft.speed * 1.94384f;
            float altFt = aircraft.transform.position.GlobalY() * 3.28084f;
            maxKts = Mathf.Max(maxKts, kts); maxAltFt = Mathf.Max(maxAltFt, altFt);
            w.WriteLine(string.Join(",", new[]
            {
                (Time.time - t0).ToString("F1"), kts.ToString("F0"), altFt.ToString("F0"),
                (aircraft.radarAlt * 3.28084f).ToString("F0"), (v.y * 196.85f).ToString("F0"),
                pitch.ToString("F1"), roll.ToString("F1"), aoa.ToString("F1"),
                aircraft.GetInputs().throttle.ToString("F2"), aircraft.gearState.ToString(),
                aircraft.GetFuelLevel().ToString("F2"), (rb ? rb.mass : 0f).ToString("F0"), ev
            }));
        }

        private void Update()
        {
            if (!aircraft || w == null) return;
            foreach (var p in parts)
                if (p && !detached.Contains(p) && p.IsDetached()) { detached.Add(p); Event("DETACHED " + p.name); }
            if (aircraft.gearState != lastGear) { Event("GEAR " + aircraft.gearState); lastGear = aircraft.gearState; }
            bool air = aircraft.radarAlt > 3f;
            if (air != wasAirborne) { Event(air ? "LIFTOFF" : "TOUCHDOWN"); wasAirborne = air; }
            if (Time.time >= next) { next = Time.time + 0.5f; Row(); }
        }

        private void OnDestroy()
        {
            if (w == null) return;
            w.WriteLine($"# summary: max {maxKts:F0} kt, max {maxAltFt:F0} ft, {detached.Count} parts detached");
            w.Dispose(); w = null;
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachTelemetry
    {
        private static void Postfix(Aircraft __instance)
        {
            if (Plugin.IsB52(__instance) && Plugin.TelemetryEnabled.Value && !__instance.GetComponent<Telemetry>())
                __instance.gameObject.AddComponent<Telemetry>();
        }
    }
}
