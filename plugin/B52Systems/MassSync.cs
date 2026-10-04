using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// With RigidAirframe on, every part shares the aircraft's one rigidbody. The game never expects that for a
    /// locally flown aircraft: UnitPart.ModifyMass (fuel burn, stores loaded or released) sets rb.mass to that one
    /// part's own mass, so the 200 t bomber ended up weighing ~15 t. This keeps the rigidbody at the sum of every
    /// still-attached part (structure + fuel + stores) and the centre of mass at the designed CoM point.
    /// </summary>
    public class MassSync : MonoBehaviour
    {
        private Aircraft aircraft;
        private Rigidbody rb;
        private Transform com;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            rb = aircraft ? aircraft.rb : null;
            var root = GetComponent<UnitPart>();
            com = root ? root.CenterOfMass : null;
            if (!rb) { Destroy(this); return; }
            Sync();
            Plugin.Log.LogInfo($"B-52J {name}: rigid airframe mass {rb.mass:F0} kg");
        }

        private void FixedUpdate() => Sync();

        internal void Sync()
        {
            if (!rb) return;
            float m = 0f;
            var parts = aircraft.partLookup;
            for (int i = 0; i < parts.Count; i++)
            {
                var p = parts[i];
                if (p && p.rb == rb) m += p.mass;
            }
            if (m > 1f && Mathf.Abs(rb.mass - m) > 0.5f) rb.mass = m;
            if (com)
            {
                var local = rb.transform.InverseTransformPoint(com.position);
                if ((rb.centerOfMass - local).sqrMagnitude > 1e-4f) rb.centerOfMass = local;
            }
        }
    }

    /// <summary>
    /// Fuel burns every frame, and FixedUpdate order isn't guaranteed, so without this the physics step could still
    /// run with one part's mass (237 t of suspension springs launching a 15 t body: the B-52 bounced on its gear).
    /// Correcting right after each ModifyMass means the wrong value never reaches the solver.
    /// </summary>
    [HarmonyPatch(typeof(UnitPart), nameof(UnitPart.ModifyMass))]
    internal static class CorrectModifyMass
    {
        private static void Postfix(UnitPart __instance)
        {
            if (!(__instance.parentUnit is Aircraft a) || __instance.rb != a.rb) return;
            var sync = a.GetComponent<MassSync>();
            if (sync) sync.Sync();
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachMassSync
    {
        private static void Postfix(Aircraft __instance)
        {
            if (Plugin.IsB52(__instance) && Plugin.RigidAirframe.Value && !__instance.GetComponent<MassSync>())
                __instance.gameObject.AddComponent<MassSync>();
        }
    }
}
