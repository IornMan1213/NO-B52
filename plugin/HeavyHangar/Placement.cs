using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace HeavyHangar
{
    /// <summary>
    /// Puts one heavy hangar at each land airbase with a long runway when a mission starts (server only; clients get
    /// it through the normal network spawn). It goes against one of the airbase's own taxiways: the apron's front edge
    /// overlaps the taxiway's edge, level with it, and the door faces it, so aircraft roll straight from the apron
    /// onto the taxi network (sites nearest a runway end first, so a heavy aircraft gets the full runway). The rest of
    /// the footprint must be bare, nearly flat terrain, clear of buildings and of the strip around every runway. Only
    /// if no taxiway site fits does it fall back to a site beside the runway, which has no paved link. Terrain
    /// colliders may not exist yet around distant airbases, so unplaced airbases are retried for a while.
    /// </summary>
    internal static class Placement
    {
        // Footprint in hangar-local metres (tools/blender_hangar.py): x +-57, z -39 (back wall) .. +85 (apron end).
        const float HalfW = 57f, Back = -39f, Front = 85f;
        const float MaxFloorRange = 5f;      // foundation skirt is 6 m deep
        const float MaxApronStep = 0.4f;     // fallback sites only: floor to ground at the apron edge (the lip covers 0.4 m)
        const float MaxTaxiStep = 0.3f;      // apron floor above the taxiway it meets (blender_hangar.py's 0.4 m lip ramps it down)
        const float TaxiOverlap = 3f;        // the apron's front edge reaches this far onto the taxiway
        const float TaxiSample = 30f;        // metres between candidate sites along a taxiway
        const float MaxTaxiHalfWidth = 60f;  // farther than this to grass: a big paved area, not a taxiway edge
        const float RunwayStrip = 75f;       // keep the building this far beyond a runway's edge, ends included
        const float RunwayEndClear = 300f;   //   ... and this far past each end, under the approach
        const int MaxTaxiTries = 250;        // full site checks per airbase per attempt
        const float MaxPathGrade = 0.10f;    // fallback sites: apron edge to runway, rise over run
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
            if (TryPlaceByTaxiway(airbase, runway, prefab)) return true;
            if (TryPlaceByRunway(airbase, runway, prefab)) return true;
            if (Plugin.VerboseLog.Value) Plugin.Log.LogInfo($"Heavy Hangar: no site yet at {airbase.name} (last: {reason})");
            return false;
        }

        /// <summary>Sites along the airbase's taxiways, nearest a runway end first. For each sample point on a taxiway
        /// the tarmac edge is found by walking out sideways until the ground is terrain; the apron then overlaps that
        /// edge by TaxiOverlap and the building stands on the grass behind it, door facing the taxiway.</summary>
        static bool TryPlaceByTaxiway(Airbase airbase, Airbase.Runway runway, GameObject prefab)
        {
            var net = airbase.GetTaxiNetwork();
            if (net == null || net.roads == null || net.roads.Count == 0) { reason = "no taxi network"; return false; }
            Vector3 ra = runway.Start.position, rb = runway.End.position;
            var sites = new List<(float score, Vector3 edge, Vector3 outward, float taxiY)>();
            foreach (var road in net.roads)
            {
                if (road == null || road.points == null) continue;
                for (int i = 0; i + 1 < road.points.Count; i++)
                {
                    Vector3 p0 = road.points[i].ToLocalPosition(), p1 = road.points[i + 1].ToLocalPosition();
                    var seg = Vector3.ProjectOnPlane(p1 - p0, Vector3.up); float len = seg.magnitude;
                    if (len < 1f) continue;
                    var along = seg / len;
                    for (float d = 0f; d <= len; d += TaxiSample)
                    {
                        var c = p0 + along * d;
                        foreach (var side in Sides)
                        {
                            var outward = Vector3.Cross(Vector3.up, along) * side;
                            if (!TaxiEdge(c, outward, out var edge, out float taxiY)) continue;
                            float score = Mathf.Min(Flat(edge - ra).magnitude, Flat(edge - rb).magnitude);
                            sites.Add((score, edge, outward, taxiY));
                        }
                    }
                }
            }
            if (sites.Count == 0) { reason = "no taxiway edge with grass beside it"; return false; }
            int tries = 0;
            foreach (var site in sites.OrderBy(x => x.score))
            {
                if (++tries > MaxTaxiTries) break;
                var apronEdge = site.edge - site.outward * TaxiOverlap;                 // front of the apron, on the tarmac
                var origin = apronEdge + site.outward * Front;
                var rot = Quaternion.LookRotation(-site.outward, Vector3.up);           // door faces the taxiway
                if (!CheckSite(origin, rot, airbase, site.taxiY, out float floorY))
                {
                    if (Plugin.VerboseLog.Value) Plugin.Log.LogInfo($"  {airbase.name} taxiway site {site.score:F0} m from a runway end: {reason}");
                    continue;
                }
                origin.y = floorY;
                Spawn(airbase, prefab, origin, rot, $"against a taxiway {site.score:F0} m from the runway end, apron {floorY - site.taxiY:F2} m above it");
                return true;
            }
            return false;
        }

        /// <summary>The old way, kept as a fallback: beside the runway with a strip of grass between apron and runway
        /// (no paved link). Sites near the runway ends first, so a heavy aircraft can use the whole runway.</summary>
        static bool TryPlaceByRunway(Airbase airbase, Airbase.Runway runway, GameObject prefab)
        {
            Vector3 a = runway.Start.position, b = runway.End.position;
            var dir = Flat(b - a); float len = dir.magnitude; dir /= len;
            var right = Vector3.Cross(Vector3.up, dir);
            float halfRunway = Mathf.Max(20f, RunwayWidth(runway) * 0.5f);

            foreach (var gap in new[] { 70f, 110f, 160f, 220f })
                foreach (var t in new[] { 0.15f, 0.85f, 0.3f, 0.7f, 0.5f })
                    foreach (var side in Sides)
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
                        Spawn(airbase, prefab, origin, rot, $"beside the runway, {gap:F0} m from its edge (no taxiway site fitted: aircraft cross grass to the runway)");
                        return true;
                    }
            return false;
        }

        static void Spawn(Airbase airbase, GameObject prefab, Vector3 origin, Quaternion rot, string where)
        {
            var hq = airbase.CurrentHQ;
            var unique = "HeavyHangar_" + (airbase.SavedAirbase?.UniqueName ?? airbase.name);
            NetworkSceneSingleton<Spawner>.i.SpawnBuilding(prefab, origin.ToGlobalPosition(), rot, hq, airbase, unique, false, null);
            Plugin.Log.LogInfo($"Heavy Hangar: placed at {airbase.name} ({(hq ? hq.name : "neutral")}), {where}");
        }

        static readonly float[] Sides = { 1f, -1f };
        static Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up);

        /// <summary>Walks out from a taxiway point until the ground is terrain: the tarmac edge, and the tarmac height
        /// just inside it.</summary>
        static bool TaxiEdge(Vector3 c, Vector3 outward, out Vector3 edge, out float taxiY)
        {
            edge = c; taxiY = 0f;
            if (!Ground(c, out float y0, out bool t0) || t0) return false;              // the road point itself must be paved
            taxiY = y0;
            for (float r = 2f; r <= MaxTaxiHalfWidth; r += 2f)
            {
                var p = c + outward * r;
                if (!Ground(p, out float y, out bool terrain)) return false;
                if (terrain) { edge = c + outward * (r - 1f); edge.y = taxiY; return true; }
                taxiY = y;
            }
            return false;
        }

        /// <summary>Taxiway sites: the footprint may only touch tarmac in the apron's front strip (where it overlaps
        /// the taxiway), must stay clear of every runway's strip, and its floor must come out level with the taxiway.
        /// The floor is the highest ground under the building (so nothing pokes through it); the skirt hides the
        /// low side.</summary>
        static bool CheckSite(Vector3 origin, Quaternion rot, Airbase airbase, float taxiY, out float floorY)
        {
            floorY = 0f;
            float lo = float.MaxValue, hi = float.MinValue;
            const float step = 8f;
            for (float x = -HalfW; x <= HalfW + 0.1f; x += HalfW / Mathf.Ceil(HalfW / step))
                for (float z = Back; z <= Front + 0.1f; z += (Front - Back) / Mathf.Ceil((Front - Back) / step))
                {
                    var p = origin + rot * new Vector3(x, 0, z);
                    if (NearRunway(airbase, p)) { reason = "too close to a runway"; return false; }
                    if (!Ground(p, out float y, out bool terrain)) return false;
                    bool frontStrip = z > Front - TaxiOverlap - 2f;
                    if (!terrain) { if (frontStrip) continue; reason = "tarmac/building in footprint"; return false; }
                    lo = Mathf.Min(lo, y); hi = Mathf.Max(hi, y);
                }
            if (hi == float.MinValue) { reason = "no ground"; return false; }
            floorY = Mathf.Max(hi, taxiY) + 0.02f;
            if (floorY - taxiY > MaxTaxiStep) { reason = $"ground {floorY - taxiY:F1} m above the taxiway"; return false; }
            if (floorY - lo > MaxFloorRange) { reason = $"too steep ({floorY - lo:F1} m)"; return false; }
            return Clear(origin, rot, floorY);
        }

        static bool NearRunway(Airbase airbase, Vector3 p)
        {
            foreach (var r in airbase.runways)
            {
                if (r == null || !r.Start || !r.End) continue;
                Vector3 a = r.Start.position, b = r.End.position;
                var dir = Flat(b - a); float len = dir.magnitude; if (len < 1f) continue; dir /= len;
                var rel = Flat(p - a);
                float t = Vector3.Dot(rel, dir), lateral = (rel - dir * t).magnitude;
                float half = Mathf.Max(20f, RunwayWidth(r) * 0.5f);
                if (t > -RunwayEndClear && t < len + RunwayEndClear && lateral < half + RunwayStrip) return true;
            }
            return false;
        }

        /// <summary>Nothing standing inside the building volume (vehicles, trees with colliders, scenery).</summary>
        static bool Clear(Vector3 origin, Quaternion rot, float floorY)
        {
            var centre = origin + rot * new Vector3(0, floorY - origin.y + 14f, (Back + Front) / 2f);
            foreach (var c in Physics.OverlapBox(centre, new Vector3(HalfW + 3f, 12f, (Front - Back) / 2f + 3f), rot, SolidMask, QueryTriggerInteraction.Ignore))
            {
                if (c.sharedMaterial == GameAssets.i.terrainMaterial) continue;
                if (c.gameObject.layer == PhysicsLayers.Water) continue;
                reason = "obstacle " + c.name; return false;
            }
            return true;
        }

        /// <summary>Fallback sites: ground samples over the footprint and along the path to the runway. Returns the
        /// floor height (highest ground under the building, so nothing pokes through the floor; the foundation skirt
        /// hides the low side).</summary>
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

            if (!Clear(origin, rot, floorY)) return false;

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
