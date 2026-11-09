using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace HeavyHangar
{
    /// <summary>
    /// Fallback for airbases without a heavy hangar: an oversize aircraft spawned by a stock land hangar appears on the
    /// ground just outside its door, facing out, instead of inside a building it doesn't fit (wings through the walls,
    /// fin through the roof). Ship hangars (moving rigidbodies) and heavy hangars are left alone.
    /// </summary>
    [HarmonyPatch(typeof(Hangar), "SpawnAircraft")]
    internal static class OutsideSpawn
    {
        private static readonly AccessTools.FieldRef<Hangar, Transform> SpawnTransform = AccessTools.FieldRefAccess<Hangar, Transform>("spawnTransform");
        private static readonly AccessTools.FieldRef<Hangar, GameObject> SpawnedObject = AccessTools.FieldRefAccess<Hangar, GameObject>("spawnedObject");

        private static bool Prefix(Hangar __instance, Player player, AircraftDefinition definition, Loadout loadout, float fuelLevel, LiveryKey livery)
        {
            if (Plugin.IsHeavy(__instance) || !Plugin.IsOversize(definition)) return true;
            var unit = __instance.attachedUnit;
            if (unit == null || unit.rb != null) return true;                  // carriers and other moving hosts
            var sp = SpawnTransform(__instance);
            if (sp == null) return true;

            // Clear of the building: half the hangar's depth, half the aircraft, plus 10 m; somewhere else nearby if an
            // aircraft (an earlier spawn), vehicle or building is already there.
            float hangarDepth = unit.definition != null ? unit.definition.length : 45f;
            var p = FindSpot(sp, hangarDepth, definition, out var fwd, out bool clear);
            if (!clear) LogNote($"no clear spot outside {unit.definition?.unitName} at {__instance.parentAirbase?.name}; spawning {definition.unitName} at the door anyway");

            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            var pos = p + Vector3.up * definition.spawnOffset.y + fwd * definition.spawnOffset.z;
            var aircraft = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(player, definition.unitPrefab, loadout, fuelLevel, livery,
                pos.ToGlobalPosition(), rot * Quaternion.Euler(definition.restRotation), Vector3.zero, __instance, unit.NetworkHQ, null, 1f, 0.5f);
            if (loadout == null)
                aircraft.Networkloadout = aircraft.weaponManager.SelectAIAircraftWeapons(__instance.parentAirbase);
            SpawnedObject(__instance) = aircraft.gameObject;
            Plugin.Log.LogInfo($"Heavy Hangar: {definition.unitName} spawned outside {unit.definition?.unitName} at {__instance.parentAirbase?.name} (no heavy hangar there)");
            return false;
        }
        /// <summary>First clear spot outside the door: straight out, further out, then to either side. Clear means no
        /// collider but terrain in the aircraft's box (lifted half a metre, so the ground and aprons don't count).
        /// Falls back to the first spot.</summary>
        private static Vector3 FindSpot(Transform sp, float hangarDepth, AircraftDefinition d, out Vector3 fwd, out bool clear)
        {
            fwd = Vector3.ProjectOnPlane(sp.forward, Vector3.up).normalized;
            var side = Vector3.Cross(Vector3.up, fwd);
            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            float outward = hangarDepth * 0.5f + d.length * 0.5f + 10f;
            var offsets = new[] { fwd * outward, fwd * (outward + d.length + 10f), fwd * outward + side * (d.width + 10f), fwd * outward - side * (d.width + 10f) };
            var first = Vector3.zero; clear = false;
            for (int i = 0; i < offsets.Length; i++)
            {
                var p = Grounded(sp.position + offsets[i], sp.position.y);
                if (i == 0) first = p;
                if (IsClear(p, rot, d)) { clear = true; return p; }
            }
            return first;
        }

        private static bool IsClear(Vector3 p, Quaternion rot, AircraftDefinition d)
        {
            var half = new Vector3(d.width * 0.5f + 2f, d.height * 0.5f, d.length * 0.5f + 2f);
            foreach (var c in Physics.OverlapBox(p + Vector3.up * (half.y + 0.5f), half, rot, ~(1 << PhysicsLayers.IgnoreRaycast), QueryTriggerInteraction.Ignore))
                if (c.sharedMaterial != GameAssets.i.terrainMaterial) return false;
            return true;
        }

        private static Vector3 Grounded(Vector3 p, float fallbackY)
        {
            if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out var hit, 120f, ~(1 << PhysicsLayers.IgnoreRaycast), QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Unit>() == null)
                p.y = hit.point.y;
            else
                p.y = fallbackY;
            return p;
        }

        private static void LogNote(string s) => Plugin.Log.LogWarning("Heavy Hangar: " + s);
    }
}
