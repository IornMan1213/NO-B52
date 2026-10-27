using BepInEx.Bootstrap;
using HarmonyLib;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52 (56 m span, 12 m tall) doesn't fit any stock hangar. Heavy Hangar (a separate mod) gives airbases a
    /// hangar it fits in and spawns oversize aircraft outside the door elsewhere. For players without Heavy Hangar,
    /// this does the second part for the B-52 alone: spawned by a stock land hangar, it appears on the ground just
    /// outside the door, facing out, instead of inside a building with its wings through the walls. When Heavy Hangar
    /// is installed this stays out of the way and Heavy Hangar handles it (same logic: plugin/HeavyHangar/OutsideSpawn.cs).
    /// </summary>
    [HarmonyPatch(typeof(Hangar), "SpawnAircraft")]
    internal static class OutsideSpawn
    {
        private const string HeavyHangarGuid = "com.ironman1213.heavyhangar";
        private static readonly AccessTools.FieldRef<Hangar, Transform> SpawnTransform = AccessTools.FieldRefAccess<Hangar, Transform>("spawnTransform");
        private static readonly AccessTools.FieldRef<Hangar, GameObject> SpawnedObject = AccessTools.FieldRefAccess<Hangar, GameObject>("spawnedObject");
        private static bool? heavyHangarLoaded;

        private static bool Prefix(Hangar __instance, Player player, AircraftDefinition definition, Loadout loadout, float fuelLevel, LiveryKey livery)
        {
            if (definition == null || definition.jsonKey != Plugin.JsonKey) return true;
            if (heavyHangarLoaded == null) heavyHangarLoaded = Chainloader.PluginInfos.ContainsKey(HeavyHangarGuid);
            if (heavyHangarLoaded.Value) return true;
            var unit = __instance.attachedUnit;
            if (unit == null || unit.rb != null) return true;                  // carriers and other moving hosts
            var sp = SpawnTransform(__instance);
            if (sp == null) return true;

            // Clear of the building: half the hangar's depth, half the aircraft, plus 10 m.
            float hangarDepth = unit.definition != null ? unit.definition.length : 45f;
            var fwd = Vector3.ProjectOnPlane(sp.forward, Vector3.up).normalized;
            var p = sp.position + fwd * (hangarDepth * 0.5f + definition.length * 0.5f + 10f);
            if (Physics.Raycast(p + Vector3.up * 50f, Vector3.down, out var hit, 120f, ~(1 << PhysicsLayers.IgnoreRaycast), QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Unit>() == null)
                p.y = hit.point.y;
            else
                p.y = sp.position.y;

            var rot = Quaternion.LookRotation(fwd, Vector3.up);
            var pos = p + Vector3.up * definition.spawnOffset.y + fwd * definition.spawnOffset.z;
            var aircraft = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(player, definition.unitPrefab, loadout, fuelLevel, livery,
                pos.ToGlobalPosition(), rot * Quaternion.Euler(definition.restRotation), Vector3.zero, __instance, unit.NetworkHQ, null, 1f, 0.5f);
            if (loadout == null)
                aircraft.Networkloadout = aircraft.weaponManager.SelectAIAircraftWeapons(__instance.parentAirbase);
            SpawnedObject(__instance) = aircraft.gameObject;
            Plugin.Log.LogInfo($"B-52J spawned outside {unit.definition?.unitName} at {__instance.parentAirbase?.name} (install Heavy Hangar for a hangar it fits in)");
            return false;
        }
    }
}
