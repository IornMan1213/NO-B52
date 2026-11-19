using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using NuclearOption.SavedMission;
using UnityEngine;

namespace NODevDriverB52
{
    /// <summary>The B-52H tail turret (optional "Tail Turret" station, B52Weapons.MakeTailTurret) and the commands that
    /// test it: "loadout", "spawnhostile", "tailgun", "turretwatch".</summary>
    static class TailGun
    {
        static Aircraft Player() { var hud = SceneSingleton<CombatHUD>.i; return hud != null ? hud.aircraft : null; }
        static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        static readonly FieldInfo Traverse = typeof(Turret).GetField("traverseAngle", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo Elevation = typeof(Turret).GetField("elevationAngle", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo Manual = typeof(Turret).GetField("manual", BindingFlags.Instance | BindingFlags.NonPublic);

        /// <summary>loadout &lt;station name part&gt; &lt;mount jsonKey|none&gt;: change one station of the player's loadout and
        /// reload the weapons, as rearming with a new loadout does. Passes if the station now carries that mount.</summary>
        internal static void Loadout(string arg, ManualLogSource log, Action<bool, string> test)
        {
            var ac = Player(); var a = arg.Split(' ');
            if (ac == null || a.Length < 2) { test(false, "loadout: no aircraft, or not 'loadout <station> <mount|none>'"); return; }
            var sets = ac.weaponManager.hardpointSets;
            int i = Array.FindIndex(sets, s => s.name.IndexOf(a[0], StringComparison.OrdinalIgnoreCase) >= 0);
            if (i < 0) { test(false, $"loadout: no station '{a[0]}' (have: {string.Join(", ", sets.Select(s => s.name))})"); return; }
            WeaponMount m = null;
            if (a[1] != "none")
            {
                m = sets[i].weaponOptions.FirstOrDefault(o => o != null && o.jsonKey == a[1]);
                if (m == null) { test(false, $"loadout: {sets[i].name} has no option {a[1]} (have: {string.Join(", ", sets[i].weaponOptions.Where(o => o).Select(o => o.jsonKey))})"); return; }
            }
            var lo = new Loadout();
            var cur = ac.loadout;
            for (int k = 0; k < sets.Length; k++) lo.weapons.Add(k == i ? m : (cur != null && k < cur.weapons.Count ? cur.weapons[k] : null));
            ac.Networkloadout = lo;          // the host's hook respawns the weapons (WeaponManager.SpawnWeapons)
            log.LogInfo($"  loadout: {sets[i].name} = {(m ? m.mountName : "empty")}; stations now: " +
                        string.Join(", ", ac.weaponStations.Select(s => s.WeaponInfo ? $"{s.WeaponInfo.shortName} x{s.Ammo}" : "?")));
            test(sets[i].weaponMount == m, $"loadout: {sets[i].name} carries {(m ? m.jsonKey : "nothing")}");
        }

        /// <summary>spawnhostile &lt;jsonKey&gt; &lt;distance m&gt; &lt;bearing deg, 180 = dead astern&gt; [speed m/s] [loadout index]:
        /// one AI aircraft of the other side, at the player's height, flying the player's heading.</summary>
        internal static void SpawnHostile(string arg, ManualLogSource log)
        {
            var ac = Player(); var a = arg.Split(' ');
            if (ac == null || a.Length < 3) { log.LogWarning("  spawnhostile: no aircraft, or not 'spawnhostile <jsonKey> <m> <deg> [m/s] [loadout]'"); return; }
            var def = Resources.FindObjectsOfTypeAll<AircraftDefinition>().FirstOrDefault(d => d.jsonKey == a[0]);
            if (def == null) { log.LogWarning($"  spawnhostile: no aircraft {a[0]}; have: {string.Join(", ", Resources.FindObjectsOfTypeAll<AircraftDefinition>().Select(d => d.jsonKey))}"); return; }
            var hq = FactionRegistry.GetAllHQs().FirstOrDefault(h => h != null && h != ac.NetworkHQ);
            if (hq == null) { log.LogWarning("  spawnhostile: no other faction in this mission"); return; }
            float dist = F(a[1]), brg = F(a[2]), speed = a.Length > 3 ? F(a[3]) : 200f;
            int li = a.Length > 4 ? int.Parse(a[4]) : 1;
            var prm = def.aircraftParameters;
            var loadout = prm.loadouts[Mathf.Clamp(li, 0, prm.loadouts.Count - 1)];
            var fwd = Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up).normalized;
            var pos = ac.transform.position + Quaternion.Euler(0f, brg, 0f) * fwd * dist;
            var u = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, def.unitPrefab, loadout, 0.6f, default(LiveryKey), pos.ToGlobalPosition(),
                Quaternion.LookRotation(fwd), fwd * speed, null, hq, $"hostile_{a[0]}", 0.8f, 0.8f);
            log.LogInfo($"  spawnhostile: {a[0]} ({hq.faction?.factionName}) {dist:F0} m at {brg:F0} deg, {speed:F0} m/s, loadout {li}: {(u ? u.name : "failed")}");
        }

        static Turret Find(Aircraft ac) => ac ? ac.GetComponentsInChildren<Turret>().FirstOrDefault() : null;

        static string State(Turret t)
        {
            var ws = t.GetWeaponStation(); var tg = t.GetTarget();
            float dist = tg ? Vector3.Distance(tg.transform.position, t.transform.position) : -1f;
            return $"traverse {(float)Traverse.GetValue(t):F1} elev {(float)Elevation.GetValue(t):F1} manual {Manual.GetValue(t)} " +
                   $"target {(tg ? $"{tg.name} at {dist:F0} m" : "none")} on target {t.IsOnTarget()} rounds {(ws != null ? ws.Ammo : -1)}";
        }

        /// <summary>The nearest enemy aircraft: range, and its angle off the turret's aft axis.</summary>
        static string Nearest(Aircraft ac, Turret t)
        {
            Aircraft best = null; float bd = float.MaxValue;
            foreach (var a in UnityEngine.Object.FindObjectsOfType<Aircraft>())
            {
                if (a == ac || a.disabled || a.NetworkHQ == null || a.NetworkHQ == ac.NetworkHQ) continue;
                float d = Vector3.Distance(a.transform.position, t.transform.position);
                if (d < bd) { bd = d; best = a; }
            }
            if (best == null) return "no enemy aircraft";
            var v = best.transform.position - t.transform.position;
            return $"nearest enemy {best.name} {bd:F0} m, {Vector3.Angle(t.transform.forward, v):F0} deg off aft, {best.speed:F0} m/s";
        }

        /// <summary>tailgun [expect on|off]: the player's tail turret: where it hangs (aircraft frame), its station, and its
        /// aim; with on/off, passes if a turret is (not) fitted.</summary>
        internal static void Report(string arg, ManualLogSource log, Action<bool, string> test)
        {
            var ac = Player(); var t = Find(ac);
            if (t != null)
            {
                var p = ac.transform.InverseTransformPoint(t.transform.position);
                var aft = Vector3.Dot(t.transform.forward, -ac.transform.forward);
                var ws = t.GetWeaponStation();
                log.LogInfo($"  tailgun: at ({p.x:F2}, {p.y:F2}, {p.z:F2}), facing aft {aft:F2}, station {ws?.Number} {ws?.WeaponInfo?.weaponName}; {State(t)}");
                foreach (var r in t.GetComponentsInChildren<Renderer>()) log.LogInfo($"    renderer {r.name} {r.enabled} {r.bounds.size:F2}");
            }
            else log.LogInfo("  tailgun: none fitted");
            if (arg == "on") test(t != null && Vector3.Dot(t.transform.forward, -ac.transform.forward) > 0.99f, "tail turret fitted, facing aft");
            if (arg == "off") test(t == null, "no tail turret (a B-52J's default loadout)");
        }

        /// <summary>turretwatch &lt;seconds&gt;: log the tail turret each second; passes if it took a target and fired.</summary>
        internal static IEnumerator Watch(float seconds, ManualLogSource log, Action<bool, string> test)
        {
            var ac = Player(); var t = Find(ac);
            if (t == null) { test(false, "turretwatch: no tail turret"); yield break; }
            var ws = t.GetWeaponStation();
            int start = ws.Ammo; bool tracked = false; float maxTraverse = 0f, t0 = Time.time;
            Unit lastTarget = null;
            while (Time.time - t0 < seconds && t != null && ac != null && !ac.disabled)
            {
                var tg = t.GetTarget();
                if (tg) { tracked = true; lastTarget = tg; }
                maxTraverse = Mathf.Max(maxTraverse, Mathf.Abs((float)Traverse.GetValue(t)));
                log.LogInfo($"  turret {Time.time - t0:F0} s: {State(t)}; {Nearest(ac, t)}");
                yield return new WaitForSeconds(1f);
            }
            int fired = ws != null ? start - ws.Ammo : 0;
            test(tracked, $"turretwatch: the tail turret took a target{(lastTarget ? " (" + lastTarget.name + ")" : "")}");
            test(fired > 0, $"turretwatch: fired {fired} of {start} rounds; turned up to {maxTraverse:F0} deg");
            if (lastTarget is Aircraft h) log.LogInfo($"  target {h.name}: disabled {h.disabled}, parts hurt " +
                $"{h.GetComponentsInChildren<UnitPart>().Count(p => p.hitPoints < 100f)}/{h.GetComponentsInChildren<UnitPart>().Length}");
        }
    }
}
