using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Stabiliser trim with the flaps. The B-52 trims its whole tailplane; with the flaps down the crew trims it nose-up
    /// against the flaps' nose-down moment. Here each moving stabiliser (hstab_L/R, the ControlSurface turning its lift
    /// frame) rests StabTrim degrees nose-up when the flaps are fully down, in step with them, and the pitch input moves
    /// it from there as before, so the whole elevator range is left for the pilot. Without it, in the AI landing tests
    /// full up-elevator held at most 6.4 deg angle of attack on approach (the aero moment logged by Plugin.LogAiApproach
    /// balanced there): at 72-80 m/s that isn't enough lift, and the B-52 sank at 8-10 m/s with the stick held back (FP9).
    /// </summary>
    [HarmonyPatch(typeof(ControlSurface), nameof(ControlSurface.UpdateJobFields))]
    internal static class FlapTrim
    {
        internal const float StabTrim = 4f;   // deg nose-up at full flap: about +1.6 MN.m at 75 m/s

        private static readonly AccessTools.FieldRef<ControlSurface, PtrAllocation<ControlSurfaceFields>> Fields =
            AccessTools.FieldRefAccess<ControlSurface, PtrAllocation<ControlSurfaceFields>>("JobFields");
        private static readonly AccessTools.FieldRef<ControlSurface, UnitPart> Attached =
            AccessTools.FieldRefAccess<ControlSurface, UnitPart>("attachedSurface");
        private static readonly AccessTools.FieldRef<HighLiftDevice, float> Position =
            AccessTools.FieldRefAccess<HighLiftDevice, float>("position");

        private class Stab { public Quaternion rest; public HighLiftDevice flap; }
        private static readonly Dictionary<ControlSurface, Stab> stabs = new Dictionary<ControlSurface, Stab>();

        private static void Postfix(ControlSurface __instance)
        {
            if (!stabs.TryGetValue(__instance, out var s))
            {
                s = null;
                var part = Attached(__instance);
                if (part != null && part.name.StartsWith("hstab_") && part.parentUnit is Aircraft a && Plugin.IsB52(a))
                {
                    ref var f0 = ref Fields(__instance);
                    if (!f0.IsCreated) return;                           // not set up yet: look again next time
                    HighLiftDevice flap = null;
                    foreach (var h in a.GetComponentsInChildren<HighLiftDevice>(true))
                        if (h.name.StartsWith("flap1_")) { flap = h; break; }
                    if (flap != null) s = new Stab { rest = f0.Ref().restingRotation, flap = flap };
                }
                stabs[__instance] = s;
            }
            if (s == null) return;
            ref var f = ref Fields(__instance);
            if (!f.IsCreated) return;
            float pos = s.flap != null ? Position(s.flap) : 0f;              // a flap shot away: no trim
            f.Ref().restingRotation = s.rest * Quaternion.AngleAxis(StabTrim * pos, Vector3.right);
        }

        internal static void Forget(ControlSurface cs) => stabs.Remove(cs);
    }

    [HarmonyPatch(typeof(ControlSurface), "OnDestroy")]
    internal static class FlapTrimForget
    {
        private static void Prefix(ControlSurface __instance) => FlapTrim.Forget(__instance);
    }
}
