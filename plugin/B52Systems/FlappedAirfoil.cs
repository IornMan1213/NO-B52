using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The inner wing (wing1, wing2) flies on the flapped section (airfoil 1, B52_flapped) while the flap behind it is
    /// more than half down, and on the plain wing section otherwise. A part's airfoil is fixed in the game, and the flaps
    /// camber these sections by turning their lift frames 8 deg: on the plain curve that moved their stall 8 deg lower
    /// too, so with flaps down the B-52 had almost no lift left to flare with (FP9). The two curves are the same up to
    /// 17 deg, where the switch happens (half flap, about 117 m/s, sections near 10 deg), so lift doesn't jump; clean,
    /// the wing stalls as before. The flap panels use the flapped section all the time (B52Spec.Parts).
    /// </summary>
    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.UpdateJobFields))]
    internal static class FlappedAirfoil
    {
        private const int FlappedIndex = 1;

        private static readonly AccessTools.FieldRef<AeroPart, PtrAllocation<AeroPartFields>> Fields =
            AccessTools.FieldRefAccess<AeroPart, PtrAllocation<AeroPartFields>>("JobFields");
        private static readonly AccessTools.FieldRef<HighLiftDevice, float> Position =
            AccessTools.FieldRefAccess<HighLiftDevice, float>("position");

        // inner-wing part -> the flap behind it (null: not an inner wing, or no flap found)
        private static readonly Dictionary<AeroPart, HighLiftDevice> flapOf = new Dictionary<AeroPart, HighLiftDevice>();

        private static void Postfix(AeroPart __instance)
        {
            if (!(__instance.parentUnit is Aircraft a) || !Plugin.IsB52(a)) return;
            if (!flapOf.TryGetValue(__instance, out var hld))
            {
                hld = null;
                string n = __instance.name;
                string flap = n.StartsWith("wing1_") ? "flap1_" + n.Substring(6) : n.StartsWith("wing2_") ? "flap2_" + n.Substring(6) : null;
                if (flap != null)
                    foreach (var h in a.GetComponentsInChildren<HighLiftDevice>(true))
                        if (h.name == flap) { hld = h; break; }
                flapOf[__instance] = hld;
            }
            if (hld == null) return;
            ref var jf = ref Fields(__instance);
            if (!jf.IsCreated) return;
            var par = (a.definition as AircraftDefinition)?.aircraftParameters;
            if (par == null || par.airfoils == null || par.airfoils.Length <= FlappedIndex) return;
            if (Position(hld) > 0.5f) jf.Ref().airfoilID = par.GetAirfoilID(FlappedIndex);
        }

        internal static void Forget(AeroPart part) => flapOf.Remove(part);
    }

    [HarmonyPatch(typeof(AeroPart), "OnDestroy")]
    internal static class FlappedAirfoilForget
    {
        private static void Postfix(AeroPart __instance) => FlappedAirfoil.Forget(__instance);
    }
}
