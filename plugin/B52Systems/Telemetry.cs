using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Flight recorder for B-52J test flights. Every 0.5 s it writes one CSV row (state, pilot inputs, peak g over
    /// the interval, flap position, most-damaged part), and it logs events (loadout, part damaged or detached, gear,
    /// lift-off, touchdown with sink rate, weapon released, aircraft disabled) to
    /// BepInEx/B52_telemetry/&lt;time&gt;_&lt;aircraft&gt;.csv. Config: [Telemetry] Enabled.
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
        private Vector3 vPrev; private float gHi = 1f, gLo = 1f, gMaxAll = 1f, gMinAll = 1f;
        private readonly Dictionary<UnitPart, int> hpBand = new Dictionary<UnitPart, int>();
        private HighLiftDevice[] flaps;
        private bool wasDisabled;
        private int fired;
        private static readonly AccessTools.FieldRef<HighLiftDevice, float> FlapPos = AccessTools.FieldRefAccess<HighLiftDevice, float>("position");

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
            w.WriteLine("t_s,kts,alt_ft,radar_alt_ft,vs_fpm,pitch_deg,roll_deg,aoa_deg,throttle,gear,fuel_frac,mass_kg," +
                        "pitch_in,roll_in,yaw_in,brake,g_max,g_min,flaps,hp_min,hp_min_part,beta_deg,yaw_rate_dps,roll_rate_dps,mach,event");
            t0 = Time.time;
            flaps = GetComponentsInChildren<HighLiftDevice>(true);
            foreach (var p in parts) hpBand[p] = 2;
            var wm = aircraft.weaponManager;
            if (wm != null)
            {
                wm.OnStationFired += OnFired;
                var loadout = new List<string>();
                foreach (var st in aircraft.weaponStations)
                    if (st != null && st.WeaponInfo != null) { loadout.Add($"{st.WeaponInfo.weaponName} x{st.Ammo}"); ammoSeen[st] = st.Ammo; }
                Event("LOADOUT " + (loadout.Count > 0 ? string.Join(" + ", loadout) : "empty"));
            }
            Plugin.Log.LogInfo("B-52J telemetry -> " + file);
        }

        // OnStationFired is raised for every trigger pull, also when nothing leaves the aircraft (on the ground, bay doors
        // still opening): one Oct 5 flight logged 7,751 of them with the count unchanged. Log a row only when a
        // station's count drops (it has already dropped when the event fires); pulls that release nothing are summed
        // into one NO RELEASE row, written before the next release or at the end of the flight.
        private readonly Dictionary<WeaponStation, int> ammoSeen = new Dictionary<WeaponStation, int>();
        private int heldPulls;
        private string heldWeapon;

        private void OnFired()
        {
            var st = aircraft ? aircraft.weaponManager.currentWeaponStation : null;
            if (st == null || st.WeaponInfo == null) return;
            int was = ammoSeen.TryGetValue(st, out var n) ? n : st.Ammo;
            ammoSeen[st] = st.Ammo;
            if (st.Ammo >= was) { heldPulls++; heldWeapon = st.WeaponInfo.weaponName; return; }
            FlushHeld();
            fired += was - st.Ammo;
            Event($"FIRED {st.WeaponInfo.weaponName} x{was - st.Ammo} ({st.Ammo} left)");
        }

        private void FlushHeld()
        {
            if (heldPulls > 0) Event($"NO RELEASE {heldWeapon} x{heldPulls} trigger pulls");
            heldPulls = 0;
        }

        private void FixedUpdate()
        {
            if (!rb || w == null) return;
            var a = (rb.velocity - vPrev) / Time.fixedDeltaTime; vPrev = rb.velocity;
            float g = Vector3.Dot(a + Vector3.up * 9.81f, transform.up) / 9.81f;
            gHi = Mathf.Max(gHi, g); gLo = Mathf.Min(gLo, g);
        }

        private void Event(string e) => Row(e);
        internal void Note(string e) => Event(e);              // other systems (drag chute) log events here

        private void Row(string ev = "")
        {
            if (w == null || !aircraft) return;
            var e = aircraft.transform.eulerAngles;
            float pitch = -(e.x > 180f ? e.x - 360f : e.x);                  // + = nose up
            float roll = -(e.z > 180f ? e.z - 360f : e.z);                   // + = right wing down
            Vector3 v = rb ? rb.velocity : Vector3.zero;
            var inp = aircraft.GetInputs();
            float flap = 0f; foreach (var f in flaps) if (f) flap = Mathf.Max(flap, FlapPos(f));
            UnitPart worst = null; foreach (var p in parts) if (p && !detached.Contains(p) && (!worst || p.hitPoints < worst.hitPoints)) worst = p;
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
                aircraft.GetFuelLevel().ToString("F2"), (rb ? rb.mass : 0f).ToString("F0"),
                inp.pitch.ToString("F2"), inp.roll.ToString("F2"), inp.yaw.ToString("F2"), inp.brake.ToString("F2"),
                gHi.ToString("F2"), gLo.ToString("F2"), flap.ToString("F2"),
                worst ? worst.hitPoints.ToString("F0") : "", worst ? worst.name : "",
                (vl.sqrMagnitude > 1f ? Mathf.Atan2(vl.x, vl.z) * Mathf.Rad2Deg : 0f).ToString("F1"),   // + = air from the left
                (rb ? aircraft.transform.InverseTransformDirection(rb.angularVelocity).y * Mathf.Rad2Deg : 0f).ToString("F1"),
                (rb ? -aircraft.transform.InverseTransformDirection(rb.angularVelocity).z * Mathf.Rad2Deg : 0f).ToString("F1"),
                (aircraft.speed / LevelInfo.GetSpeedOfSound(aircraft.transform.position.GlobalY())).ToString("F2"), ev
            }));
            gMaxAll = Mathf.Max(gMaxAll, gHi); gMinAll = Mathf.Min(gMinAll, gLo);
            if (ev == "") { gHi = gLo = 1f; }
        }

        private void Update()
        {
            if (!aircraft || w == null) return;
            foreach (var p in parts)
            {
                if (!p || detached.Contains(p)) continue;
                if (p.IsDetached()) { detached.Add(p); Event("DETACHED " + p.name); continue; }
                int band = p.hitPoints <= 0f ? 0 : p.hitPoints < 50f ? 1 : 2;
                if (band < hpBand[p]) { hpBand[p] = band; Event($"DAMAGE {p.name} {p.hitPoints:F0} hp"); }
            }
            if (aircraft.disabled && !wasDisabled) { wasDisabled = true; Event("DISABLED"); }
            if (aircraft.gearState != lastGear) { Event("GEAR " + aircraft.gearState); lastGear = aircraft.gearState; }
            bool air = aircraft.radarAlt > 3f;
            if (air != wasAirborne)
            {
                float sink = rb ? -rb.velocity.y * 196.85f : 0f;
                Event(air ? $"LIFTOFF {aircraft.speed * 1.94384f:F0} kt {(rb ? rb.mass / 1000f : 0f):F0} t"
                          : $"TOUCHDOWN {aircraft.speed * 1.94384f:F0} kt sink {sink:F0} fpm");
                wasAirborne = air;
            }
            if (Time.time >= next) { next = Time.time + 0.5f; Row(); }
        }

        private void OnDestroy()
        {
            if (w == null) return;
            if (aircraft && aircraft.weaponManager != null) aircraft.weaponManager.OnStationFired -= OnFired;
            FlushHeld();
            w.WriteLine($"# summary: max {maxKts:F0} kt, max {maxAltFt:F0} ft, g {gMinAll:F1}..{gMaxAll:F1}, " +
                        $"{fired} releases, {detached.Count} parts detached");
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
