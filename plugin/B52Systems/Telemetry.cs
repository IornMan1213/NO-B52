using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Flight recorder for the local pilot's B-52J (off by default; config [Telemetry] Enabled). Every 0.5 s it writes
    /// one CSV row (state, pilot inputs, peak g over the interval, flap position, most-damaged part), and it logs
    /// events (loadout, part damaged or detached, gear, lift-off, touchdown with sink rate, weapon released, aircraft
    /// disabled) to BepInEx/B52_telemetry/&lt;time&gt;_&lt;aircraft&gt;.csv. Only the newest 20 logs are kept.
    /// Numbers are always written with a '.' decimal point, whatever the Windows language.
    /// </summary>
    public class Telemetry : MonoBehaviour
    {
        private const int KeepFiles = 20;
        private const float LocalWait = 10f;    // seconds to wait for the game to assign the pilot before giving up
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private Aircraft aircraft;
        private Rigidbody rb;
        private StreamWriter w;
        private float next, t0, spawnTime;
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
            spawnTime = Time.time;
        }

        /// <summary>Opens the log once the game has made this the local pilot's aircraft (that can lag the spawn).</summary>
        private void Begin()
        {
            rb = GetComponent<Rigidbody>();
            parts = GetComponentsInChildren<UnitPart>(true);
            string file;
            try
            {
                var dir = Path.Combine(Paths.BepInExRootPath, "B52_telemetry");
                Directory.CreateDirectory(dir);
                Prune(dir);
                file = Path.Combine(dir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{GetInstanceID()}.csv");
                w = new StreamWriter(file) { AutoFlush = true };
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("B-52J telemetry: can't write the flight log (" + e.Message + "); telemetry is off for this flight");
                Destroy(this);
                return;
            }
            w.WriteLine("t_s,kts,alt_ft,radar_alt_ft,vs_fpm,pitch_deg,roll_deg,aoa_deg,throttle,gear,fuel_frac,mass_kg," +
                        "pitch_in,roll_in,yaw_in,brake,g_max,g_min,flaps,hp_min,hp_min_part,beta_deg,yaw_rate_dps,roll_rate_dps,mach,event");
            t0 = Time.time;
            flaps = GetComponentsInChildren<HighLiftDevice>(true);
            foreach (var p in parts) hpBand[p] = 2;
            // Start from the current state so the first rows don't report a false lift-off, gear change or g spike.
            vPrev = rb ? rb.velocity : Vector3.zero;
            lastGear = aircraft.gearState;
            wasAirborne = aircraft.radarAlt > 3f;
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

        private static void Prune(string dir)
        {
            var old = new DirectoryInfo(dir).GetFiles("*.csv").OrderByDescending(f => f.LastWriteTimeUtc).Skip(KeepFiles - 1);
            foreach (var f in old)
                try { f.Delete(); } catch (Exception) { }
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

        private static string F(float v, string fmt) => v.ToString(fmt, Inv);

        /// <summary>CSV field: quoted when it holds a comma, quote or line break.</summary>
        private static string Csv(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
        }

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
            try
            {
                w.WriteLine(string.Join(",", new[]
                {
                    F(Time.time - t0, "F1"), F(kts, "F0"), F(altFt, "F0"),
                    F(aircraft.radarAlt * 3.28084f, "F0"), F(v.y * 196.85f, "F0"),
                    F(pitch, "F1"), F(roll, "F1"), F(aoa, "F1"),
                    F(inp.throttle, "F2"), aircraft.gearState.ToString(),
                    F(aircraft.GetFuelLevel(), "F2"), F(rb ? rb.mass : 0f, "F0"),
                    F(inp.pitch, "F2"), F(inp.roll, "F2"), F(inp.yaw, "F2"), F(inp.brake, "F2"),
                    F(gHi, "F2"), F(gLo, "F2"), F(flap, "F2"),
                    worst ? F(worst.hitPoints, "F0") : "", worst ? Csv(worst.name) : "",
                    F(vl.sqrMagnitude > 1f ? Mathf.Atan2(vl.x, vl.z) * Mathf.Rad2Deg : 0f, "F1"),   // + = air from the left
                    F(rb ? aircraft.transform.InverseTransformDirection(rb.angularVelocity).y * Mathf.Rad2Deg : 0f, "F1"),
                    F(rb ? -aircraft.transform.InverseTransformDirection(rb.angularVelocity).z * Mathf.Rad2Deg : 0f, "F1"),
                    F(aircraft.speed / LevelInfo.GetSpeedOfSound(aircraft.transform.position.GlobalY()), "F2"), Csv(ev)
                }));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("B-52J telemetry: write failed (" + ex.Message + "); stopping this log");
                w.Dispose(); w = null;
                return;
            }
            gMaxAll = Mathf.Max(gMaxAll, gHi); gMinAll = Mathf.Min(gMinAll, gLo);
            if (ev == "") { gHi = gLo = 1f; }
        }

        private void Update()
        {
            if (!aircraft) return;
            if (w == null)
            {
                if (parts != null) return;                                     // log was opened and has stopped
                if (GameManager.IsLocalAircraft(aircraft)) Begin();
                else if (Time.time - spawnTime > LocalWait) Destroy(this);     // AI or another player's B-52
                return;
            }
            foreach (var p in parts)
            {
                if (!p || detached.Contains(p)) continue;
                if (p.IsDetached()) { detached.Add(p); Event("DETACHED " + p.name); continue; }
                int band = p.hitPoints <= 0f ? 0 : p.hitPoints < 50f ? 1 : 2;
                if (band < hpBand[p]) { hpBand[p] = band; Event(FormattableString.Invariant($"DAMAGE {p.name} {p.hitPoints:F0} hp")); }
            }
            if (aircraft.disabled && !wasDisabled) { wasDisabled = true; Event("DISABLED"); }
            if (aircraft.gearState != lastGear) { Event("GEAR " + aircraft.gearState); lastGear = aircraft.gearState; }
            bool air = aircraft.radarAlt > 3f;
            if (air != wasAirborne)
            {
                float sink = rb ? -rb.velocity.y * 196.85f : 0f;
                Event(air ? FormattableString.Invariant($"LIFTOFF {aircraft.speed * 1.94384f:F0} kt {(rb ? rb.mass / 1000f : 0f):F0} t")
                          : FormattableString.Invariant($"TOUCHDOWN {aircraft.speed * 1.94384f:F0} kt sink {sink:F0} fpm"));
                wasAirborne = air;
            }
            if (Time.time >= next) { next = Time.time + 0.5f; Row(); }
        }

        private void OnDestroy()
        {
            if (aircraft && aircraft.weaponManager != null) aircraft.weaponManager.OnStationFired -= OnFired;
            if (w == null) return;
            FlushHeld();
            try
            {
                w.WriteLine(FormattableString.Invariant(
                    $"# summary: max {maxKts:F0} kt, max {maxAltFt:F0} ft, g {gMinAll:F1}..{gMaxAll:F1}, {fired} releases, {detached.Count} parts detached"));
            }
            catch (Exception) { }
            w.Dispose(); w = null;
        }
    }

}
