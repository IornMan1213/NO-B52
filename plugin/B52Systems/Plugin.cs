using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>Runtime systems for the B-52J Stratofortress mod (B-52J Stratofortress_x.y.z.nobp).</summary>
    [BepInPlugin("com.ironman1213.b52systems", "B-52J Systems", "0.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RigidAirframe;
        internal static ConfigEntry<bool> TelemetryEnabled;
        internal static ConfigEntry<bool> VerboseAeroCfg;
        internal static bool VerboseAero => VerboseAeroCfg != null && VerboseAeroCfg.Value;
        public const string JsonKey = "B52J";

        private void Awake()
        {
            Log = Logger;
            RigidAirframe = Config.Bind("Physics", "RigidAirframe", true,
                "Keep the B-52 as one rigid body (the game's simple physics). Parts still break off when damaged. " +
                "Turn off to use per-part jointed physics, which lets the long wings and fuselage flex visibly.");
            TelemetryEnabled = Config.Bind("Telemetry", "Enabled", true,
                "Write a CSV flight log for each B-52J to BepInEx/B52_telemetry (speed, altitude, attitude, events).");
            VerboseAeroCfg = Config.Bind("Debug", "LogAeroCentres", true,
                "Log each part's aerodynamic centre (aircraft frame) when a B-52J first flies.");
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
        // SetComplexPhysics un-parents every part into its own rigidbody, so the colliders are gathered before it runs.
        // With RigidAirframe on, the B-52 never enters complex physics: returning false skips the original method.
        private static bool Prefix(Aircraft __instance, out Collider[] __state)
        {
            __state = null;
            if (!Plugin.IsB52(__instance)) return true;
            if (Plugin.RigidAirframe.Value) return false;
            __state = __instance.GetComponentsInChildren<Collider>(true);
            return true;
        }

        private static void Postfix(Aircraft __instance, Collider[] __state)
        {
            if (__state == null) return;
            int pairs = 0;
            for (int i = 0; i < __state.Length; i++)
                for (int j = i + 1; j < __state.Length; j++)
                {
                    if (!__state[i] || !__state[j]) continue;
                    Physics.IgnoreCollision(__state[i], __state[j], true);
                    pairs++;
                }
            Plugin.Log.LogInfo($"B-52J {__instance.name}: ignoring {pairs} self-collision pairs ({__state.Length} colliders)");

            // Stiffen the airframe. PhysX fixed joints go soft when the bodies' masses differ a lot (a 65 t wing on
            // a lighter fuselage section), so each joint solves its two bodies as equal masses, with more iterations.
            var bodies = new System.Collections.Generic.HashSet<Rigidbody>();
            foreach (var c in __state) if (c && c.attachedRigidbody) bodies.Add(c.attachedRigidbody);
            int joints = 0;
            foreach (var rb in bodies)
            {
                rb.solverIterations = 30;
                rb.solverVelocityIterations = 12;
                foreach (var j in rb.GetComponents<FixedJoint>())
                {
                    if (!j.connectedBody) continue;
                    j.massScale = 1f;
                    j.connectedMassScale = j.connectedBody.mass / Mathf.Max(1f, rb.mass);   // scales inverse mass
                    joints++;
                }
            }
            Plugin.Log.LogInfo($"B-52J {__instance.name}: stiffened {joints} joints on {bodies.Count} bodies");
        }
    }
}
