using HarmonyLib;
using NuclearOption.SavedMission;

namespace B52Systems
{
    /// <summary>
    /// The tail turret (B-52H M61A1, the "Tail Turret" station) is optional: the B-52J has no gun. The game's AI fills
    /// every station that has an option (WeaponManager.SelectAIAircraftWeapons never leaves one empty), so without this
    /// every AI B-52J would fly with the gun. A postfix: it only empties the tail station of AI B-52 loadouts, unless
    /// the AiTailGun setting allows it, and leaves the game's choice (and any other mod's patch) as it is otherwise.
    /// </summary>
    [HarmonyPatch(typeof(WeaponManager), nameof(WeaponManager.SelectAIAircraftWeapons))]
    internal static class TailGunAiLoadout
    {
        internal const string MountKey = "B52_M61A1_tail";      // B52Weapons.TailTurretKey

        private static void Postfix(WeaponManager __instance, Loadout __result)
        {
            if (__result == null || Plugin.AiTailGun.Value) return;
            var ac = __instance.GetComponentInParent<Aircraft>();
            if (!Plugin.IsB52(ac)) return;
            var sets = __instance.hardpointSets;
            for (int i = 0; i < sets.Length && i < __result.weapons.Count; i++)
                if (__result.weapons[i] != null && __result.weapons[i].jsonKey == MountKey)
                    __result.weapons[i] = null;
        }
    }

    /// <summary>
    /// The game scores a target for a weapon as (target vs the weapon's effectiveness) x (target vs the carrying
    /// aircraft's role). The B-52J's role has no anti-air share (it is a bomber, and other AIs judge it by that), so every
    /// aircraft scored 0 for the tail gun and the turret never took one. For the tail gun's station only, the score is
    /// the gun's own effectiveness against air and missile targets: a defensive gun, as the B-52H's ASG-21 system was,
    /// that doesn't strafe ground targets behind the bomber. Other stations and other aircraft keep the game's score.
    /// </summary>
    [HarmonyPatch(typeof(WeaponStation), nameof(WeaponStation.CalcOpportunityThreat))]
    internal static class TailGunTargets
    {
        private static void Postfix(WeaponStation __instance, UnitDefinition definition, ref OpportunityThreat __result)
        {
            var info = __instance.WeaponInfo;
            if (info == null || definition == null || info.name != "info_" + TailGunAiLoadout.MountKey) return;
            var t = definition.typeIdentity; var e = info.effectiveness;
            __result = new OpportunityThreat(t.air * e.antiAir + t.missile * e.antiMissile, __result.threat);
        }
    }
}
