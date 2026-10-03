using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>Runtime systems for the B-52J Stratofortress mod (B-52J Stratofortress_x.y.z.nobp).</summary>
    [BepInPlugin("com.ironman1213.b52systems", "B-52J Systems", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        public const string JsonKey = "B52J";

        private void Awake()
        {
            Log = Logger;
            new Harmony("com.ironman1213.b52systems").PatchAll();
            Log.LogInfo("B-52J Systems loaded");
        }

        internal static bool IsB52(Aircraft a) => a != null && a.definition != null && a.definition.jsonKey == JsonKey;
    }

    /// <summary>
    /// In complex physics every part becomes its own rigidbody joined to its parent. The B-52's parts overlap
    /// their non-adjacent neighbours (wing roots inside the fuselage, outboard pods over the inner wing), and
    /// PhysX would push them apart until the joints break. Parts of one aircraft never need to collide with
    /// each other, so all collider pairs inside the aircraft are ignored.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.SetComplexPhysics))]
    internal static class IgnoreSelfCollision
    {
        private static void Postfix(Aircraft __instance)
        {
            if (!Plugin.IsB52(__instance)) return;
            var cols = __instance.GetComponentsInChildren<Collider>(true);
            int pairs = 0;
            for (int i = 0; i < cols.Length; i++)
                for (int j = i + 1; j < cols.Length; j++)
                {
                    Physics.IgnoreCollision(cols[i], cols[j], true);
                    pairs++;
                }
            Plugin.Log.LogInfo($"B-52J {__instance.name}: ignoring {pairs} self-collision pairs ({cols.Length} colliders)");
        }
    }
}
