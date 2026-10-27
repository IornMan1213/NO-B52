using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>Runtime systems for the B-52J Stratofortress mod (B-52J Stratofortress_x.y.z.nobp).</summary>
    [BepInPlugin("com.ironman1213.b52systems", "B-52J Systems", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RigidAirframe;
        internal static ConfigEntry<bool> TelemetryEnabled;
        internal static ConfigEntry<bool> VerboseAeroCfg;
        internal static ConfigEntry<KeyboardShortcut> CrabLeft, CrabRight, CrabCentre, HelpKey;
        internal static bool VerboseAero => VerboseAeroCfg != null && VerboseAeroCfg.Value;
        public const string JsonKey = "B52J";

        private void Awake()
        {
            Log = Logger;
            RigidAirframe = Config.Bind("Physics", "RigidAirframe", true,
                "Keep the B-52 as one rigid body (the game's simple physics). Parts still break off when damaged. " +
                "Turn off to use per-part jointed physics, which lets the long wings and fuselage flex visibly.");
            TelemetryEnabled = Config.Bind("Telemetry", "Enabled", false,
                "Write a CSV flight log of your own B-52J flights to BepInEx/B52_telemetry (speed, altitude, attitude, " +
                "events), for tuning and bug reports. Only the newest 20 logs are kept.");
            VerboseAeroCfg = Config.Bind("Debug", "LogAeroCentres", true,
                "Log each part's aerodynamic centre (aircraft frame) when a B-52J first flies.");
            CrabLeft = Config.Bind("Gear", "CrabLeft", new KeyboardShortcut(KeyCode.LeftBracket),
                "Crosswind crab: turn the main trucks 5 deg further left (max 20). For wind from the left, set crab left and fly the nose into the wind. " +
                "Any key works here; pick another if your keyboard layout has no [ ] or \\ keys. Ignored while typing in chat or paused.");
            CrabRight = Config.Bind("Gear", "CrabRight", new KeyboardShortcut(KeyCode.RightBracket),
                "Crosswind crab: turn the main trucks 5 deg further right (max 20).");
            CrabCentre = Config.Bind("Gear", "CrabCentre", new KeyboardShortcut(KeyCode.Backslash),
                "Crosswind crab: trucks straight ahead.");
            HelpKey = Config.Bind("Interface", "HelpCard", new KeyboardShortcut(KeyCode.F1, KeyCode.LeftShift),
                "Shows or hides the B-52J help card (keys and speeds to fly by) while you fly one. It also shows by itself " +
                "for a few seconds the first time you fly the B-52J in a session.");
            gameObject.AddComponent<HudReadout>();
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
        // That method is also where the game gives the player's aircraft continuous collision detection, so that one
        // step is repeated here (the game calls this often while the aircraft is near the camera, hence the check).
        private static bool Prefix(Aircraft __instance, out Collider[] __state)
        {
            __state = null;
            if (!Plugin.IsB52(__instance)) return true;
            if (Plugin.RigidAirframe.Value)
            {
                var rb = __instance.rb;
                if (rb && !rb.isKinematic && GameManager.IsLocalAircraft(__instance)
                    && rb.collisionDetectionMode != CollisionDetectionMode.Continuous)
                    rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                return false;
            }
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
