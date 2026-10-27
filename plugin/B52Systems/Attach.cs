using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Adds the B-52's runtime systems to every B-52J as it is created (player, AI, other players' and hangar previews;
    /// each system decides for itself what to do on each). One patch, so the B-52 check runs once per aircraft.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachSystems
    {
        private static void Postfix(Aircraft __instance)
        {
            if (!Plugin.IsB52(__instance)) return;
            var go = __instance.gameObject;
            if (Plugin.RigidAirframe.Value) Add<MassSync>(go);
            Add<GearSystem>(go);
            Add<DragChute>(go);
            Add<SpoilerDriver>(go);
            Add<MachDrag>(go);
            Add<Instruments>(go);
            Add<ExteriorLights>(go);
            Add<EngineFans>(go);
            if (Plugin.TelemetryEnabled.Value) Add<Telemetry>(go);
        }

        private static void Add<T>(GameObject go) where T : Component
        {
            if (!go.GetComponent<T>()) go.AddComponent<T>();
        }
    }
}
