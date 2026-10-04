using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace HeavyHangar
{
    /// <summary>
    /// Puts one heavy hangar beside a long runway at each land airbase when a mission starts (server only; clients get
    /// it through the normal network spawn). The site must be bare terrain (no tarmac, buildings or water), nearly
    /// flat, with a clear, gently sloping path from the apron to the runway edge. Terrain colliders may not exist yet
    /// around distant airbases, so unplaced airbases are retried for a while.
    /// </summary>
    internal static class Placement
    {
        // Footprint in hangar-local metres (tools/blender_hangar.py): x +-57, z -39 (back wall) .. +85 (apron end).
        const float HalfW = 57f, Back = -39f, Front = 85f;
        const float MaxFloorRange = 5f;      // foundation skirt is 6 m deep
        const float MaxApronStep = 1.5f;     // floor to ground at the apron edge
        const float MaxPathGrade = 0.10f;    // apron edge to runway, rise over run
        const int RetryCount = 20; const float RetryInterval = 15f;

        static readonly AccessTools.FieldRef<Airbase.Runway, float> RunwayWidth = AccessTools.FieldRefAccess<Airbase.Runway, float>("width");
        static readonly AccessTools.FieldRef<Airbase, Unit> AttachedUnit = AccessTools.FieldRefAccess<Airbase, Unit>("attachedUnit");

        static readonly HashSet<Airbase> done = new HashSet<Airbase>();
        static Coroutine running;
        static string reason;

        internal static void Start()
        {
            done.Clear();
            if (running != null && Runner.I) Runner.I.StopCoroutine(running);
            running = Runner.Get().StartCoroutine(Loop());
        }

        static IEnumerator Loop()
        {
            for (int attempt = 0; attempt < RetryCount; attempt++)
            {
                yield return new WaitForSeconds(attempt == 0 ? 2f : RetryInterval);
                if (!NetworkManagerNuclearOption.i || !NetworkManagerNuclearOption.i.Server.Active) yield break;
                int pending = 0;
                foreach (var airbase in FactionRegistry.airbaseLookup.Values.ToList())
                {
                    if (airbase == null || done.Contains(airbase)) continue;
                    if (!Eligible(airbase, out var runway)) { done.Add(airbase); continue; }
                    if (TryPlace(airbase, runway)) done.Add(airbase);
                    else pending++;
                }
                if (pending == 0) yield break;
                if (attempt == RetryCount - 1) Plugin.Log.LogInfo($"Heavy Hangar: {pending} airbase(s) had no suitable site");
            }
        }

        static bool Eligible(Airbase airbase, out Airbase.Runway best)
        {
            best = null;
            if (AttachedUnit(airbase) != null) return false;                               // carriers and other ships
            if (airbase.buildings.Any(b => b && b.definition && b.definition.jsonKey == Plugin.JsonKey)) return false;   // already has one
            if (airbase.runways == null) return false;
            best = airbase.runways.Where(r => r != null && r.Takeoff && r.Start && r.End && r.Length >= Plugin.MinRunway.Value)
                                  .OrderByDescending(r => r.Length).FirstOrDefault();
            return best != null;
        }

        static bool TryPlace(Airbase airbase, Airbase.Runway runway)
        {
            if (!Encyclopedia.i.TryGetPrefab(Plugin.JsonKey, out var prefab))
            {
                Plugin.Log.LogWarning("Heavy Hangar: building '" + Plugin.JsonKey + "' is not loaded (is Heavy Hangar_x.y.z.nobp installed?)");
                return true;   // nothing to retry
            }
            Vector3 a = runway.Start.position, b = runway.End.position;
            var dir = Vector3.ProjectOnPlane(b - a, Vector3.up); float len = dir.magnitude; dir /= len;
            var right = Vector3.Cross(Vector3.up, dir);
            float halfRunway = Mathf.Max(20f, RunwayWidth(runway) * 0.5f);

            foreach (var gap in new[] { 70f, 110f, 160f, 220f })
                foreach (var t in new[] { 0.5f, 0.35f, 0.65f, 0.25f, 0.75f })
                    foreach (var side in new[] { 1f, -1f })
                    {
                        // Door faces the runway; the apron ends `gap` metres from the runway edge.
                        var along = a + dir * (len * t);
                        var origin = along + right * side * (halfRunway + gap + Front);
                        var rot = Quaternion.LookRotation(-right * side, Vector3.up);
                        if (!CheckSite(origin, rot, along + right * side * halfRunway, out float floorY))
                        {
                            if (Plugin.VerboseLog.Value) Plugin.Log.LogInfo($"  {airbase.name} t={t} side={side} gap={gap}: {reason}");
                            continue;
                        }
                        origin.y = floorY;
                        var hq = airbase.CurrentHQ;
                        var unique = "HeavyHangar_" + (airbase.SavedAirbase?.UniqueName ?? airbase.name);
                        NetworkSceneSingleton<Spawner>.i.SpawnBuilding(prefab, origin.ToGlobalPosition(), rot, hq, airbase, unique, false, null);
                        Plugin.Log.LogInfo($"Heavy Hangar: placed at {airbase.name} ({(hq ? hq.name : "neutral")}), runway {runway.Length:F0} m, " +
                                           $"{gap:F0} m from the runway edge");
                        return true;
                    }
            if (Plugin.VerboseLog.Value) Plugin.Log.LogInfo($"Heavy Hangar: no site yet at {airbase.name} (last: {reason})");
            return false;
        }

        /// <summary>Ground samples over the footprint and along the taxi path. Returns the floor height (highest ground
        /// under the building, so nothing pokes through the floor; the foundation skirt hides the low side).</summary>
        static bool CheckSite(Vector3 origin, Quaternion rot, Vector3 runwayEdge, out float floorY)
        {
            floorY = 0f;
            float lo = float.MaxValue, hi = float.MinValue, apronLo = float.MaxValue;
            for (float x = -HalfW; x <= HalfW + 0.1f; x += HalfW / 3f)
                for (float z = Back; z <= Front + 0.1f; z += (Front - Back) / 8f)
                {
                    var p = origin + rot * new Vector3(x, 0, z);
                    if (!Ground(p, out float y, out bool terrain)) return false;
                    if (!terrain) { reason = "tarmac/building in footprint"; return false; }
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                    if (z > Front - 1f) apronLo = Mathf.Min(apronLo, y);
                }
            if (hi - lo > MaxFloorRange) { reason = $"too steep ({hi - lo:F1} m)"; return false; }
            floorY = hi + 0.05f;
            if (floorY - apronLo > MaxApronStep) { reason = "apron edge drop"; return false; }

            // Nothing standing inside the building volume (vehicles, trees with colliders, scenery).
            var centre = origin + rot * new Vector3(0, floorY - origin.y + 14f, (Back + Front) / 2f);
            foreach (var c in Physics.OverlapBox(centre, new Vector3(HalfW + 3f, 12f, (Front - Back) / 2f + 3f), rot, SolidMask, QueryTriggerInteraction.Ignore))
            {
                if (c.sharedMaterial == GameAssets.i.terrainMaterial) continue;
                if (c.gameObject.layer == PhysicsLayers.Water) continue;
                reason = "obstacle " + c.name; return false;
            }

            // Taxi path from the apron edge to the runway edge: any surface, but no buildings and a gentle grade.
            var start = origin + rot * new Vector3(0, 0, Front);
            float dist = Vector3.ProjectOnPlane(runwayEdge - start, Vector3.up).magnitude;
            float prev = floorY;
            for (float s = 15f; s < dist; s += 15f)
            {
                var p = Vector3.MoveTowards(start, runwayEdge, s);
                if (!Ground(p, out float y, out _)) return false;
                if (Mathf.Abs(y - prev) > 15f * MaxPathGrade) { reason = "rough taxi path"; return false; }
                prev = y;
            }
            return true;
        }

        static readonly int SolidMask = ~((1 << PhysicsLayers.IgnoreRaycast) | (1 << PhysicsLayers.Effects) | (1 << PhysicsLayers.ExclusionZones)
                                         | (1 << PhysicsLayers.IgnoreCollisions) | (1 << PhysicsLayers.UI));

        static bool Ground(Vector3 p, out float y, out bool terrain)
        {
            y = 0f; terrain = false;
            var top = new Vector3(p.x, p.y + 400f, p.z);
            if (!Physics.Raycast(top, Vector3.down, out var hit, 1200f, SolidMask, QueryTriggerInteraction.Ignore)) { reason = "no ground collider (terrain not loaded?)"; return false; }
            if (hit.collider.gameObject.layer == PhysicsLayers.Water || hit.point.y < Datum.LocalSeaY + 1f) { reason = "water"; return false; }
            if (hit.collider.GetComponentInParent<Unit>() != null) { reason = "unit " + hit.collider.name; return false; }
            y = hit.point.y;
            terrain = hit.collider.sharedMaterial == GameAssets.i.terrainMaterial;
            return true;
        }
    }

    /// <summary>Host for the retry coroutine.</summary>
    internal class Runner : MonoBehaviour
    {
        internal static Runner I;
        internal static Runner Get()
        {
            if (!I) { var go = new GameObject("HeavyHangarRunner"); DontDestroyOnLoad(go); I = go.AddComponent<Runner>(); }
            return I;
        }
    }

    [HarmonyPatch(typeof(MissionRunner), nameof(MissionRunner.OnMissionStart))]
    internal static class PlaceOnMissionStart
    {
        private static void Postfix()
        {
            if (!Plugin.AutoPlace.Value || GameManager.gameState == GameState.Editor) return;
            Placement.Start();
        }
    }
}
