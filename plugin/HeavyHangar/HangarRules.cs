using System.Collections.Generic;
using System.Linq;
using HarmonyLib;

namespace HeavyHangar
{
    /// <summary>
    /// Which hangar spawns what. Airbase.TrySpawnAircraft asks each hangar in priority order and uses the first whose
    /// CanSpawnAircraft is true, and the aircraft menu lists the union of GetAvailableAircraft over the base's hangars.
    /// </summary>
    internal static class HangarRules
    {
        private static readonly AccessTools.FieldRef<Hangar, AircraftDefinition[]> ListedAircraft =
            AccessTools.FieldRefAccess<Hangar, AircraftDefinition[]>("availableAircraft");

        /// <summary>Everything any hangar at this airbase lists, plus the heavy hangar's own list. Uses the raw lists so a
        /// destroyed stock hangar doesn't take its aircraft types away from the heavy hangar.</summary>
        internal static AircraftDefinition[] HeavyList(Hangar heavy)
        {
            var set = new HashSet<AircraftDefinition>(ListedAircraft(heavy) ?? new AircraftDefinition[0]);
            var airbase = heavy.parentAirbase;
            if (airbase != null)
                foreach (var h in airbase.hangars)
                    if (h != null && h != heavy && !Plugin.IsHeavy(h) && ListedAircraft(h) != null)
                        set.UnionWith(ListedAircraft(h).Where(d => d != null));
            return set.ToArray();
        }

        internal static bool AirbaseHasHeavyHangar(Airbase airbase) =>
            airbase != null && airbase.hangars.Any(h => h != null && Plugin.IsHeavy(h) && !h.Disabled);
    }

    [HarmonyPatch(typeof(Hangar), nameof(Hangar.GetAvailableAircraft))]
    internal static class HeavyHangarList
    {
        private static void Postfix(Hangar __instance, ref AircraftDefinition[] __result)
        {
            if (!Plugin.IsHeavy(__instance) || __instance.attachedUnit.disabled) return;
            __result = HangarRules.HeavyList(__instance);
        }
    }

    [HarmonyPatch(typeof(Hangar), nameof(Hangar.CanSpawnAircraft))]
    internal static class RouteOversize
    {
        private static void Postfix(Hangar __instance, AircraftDefinition definition, ref bool __result)
        {
            if (Plugin.IsHeavy(__instance))
            {
                __result = __instance.Available && HangarRules.HeavyList(__instance).Contains(definition);
                return;
            }
            // A stock hangar the aircraft doesn't fit through: refuse it whenever this airbase has a working heavy
            // hangar. Without one, the old behaviour stays so the aircraft is never unavailable.
            if (__result && Plugin.IsOversize(definition) && HangarRules.AirbaseHasHeavyHangar(__instance.parentAirbase))
                __result = false;
        }
    }
}
