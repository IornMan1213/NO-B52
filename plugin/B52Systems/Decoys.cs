using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// ADM-160C MALD-J: while it flies, jams every emitting enemy radar within 30 km, the way the game's jamming pod
    /// does (Unit.Jam every 0.2 s, server side). Strength 0.8 at the decoy, falling linearly to 0 at 30 km (the stock
    /// pod: ~1, to 0 at 80 km). The plain ADM-160B MALD needs no code: its definition gives it the B-52J's radar size,
    /// so enemy radars see a bomber (B52Tools.B52Weapons.MakeDecoy).
    /// </summary>
    public class MaldJammer : MonoBehaviour
    {
        public const string JsonKey = "B52_ADM160C";
        private const float Range = 30000f, Strength = 0.8f, Interval = 0.2f;
        private Missile missile;
        private float next;
        private static readonly List<Unit> nearby = new List<Unit>();

        private void Start() => missile = GetComponent<Missile>();

        private void FixedUpdate()
        {
            if (!missile || missile.disabled || Time.time < next) return;
            if (!NetworkManagerNuclearOption.i || !NetworkManagerNuclearOption.i.Server.Active) return;
            next = Time.time + Interval;
            nearby.Clear();
            BattlefieldGrid.GetUnitsInRangeNonAlloc(missile.GlobalPosition(), Range, nearby);
            foreach (var u in nearby)
            {
                if (!u || u == missile || u.radar == null || u.disabled) continue;
                if (u.NetworkHQ == null || u.NetworkHQ == missile.NetworkHQ || !u.HasRadarEmission()) continue;
                float d = Vector3.Distance(u.transform.position, missile.transform.position);
                if (d > Range) continue;
                u.Jam(new Unit.JamEventArgs { jammingUnit = missile, jamAmount = Strength * (1f - d / Range) });
            }
        }
    }

    [HarmonyPatch(typeof(Missile), nameof(Missile.Awake))]
    internal static class AttachMaldJammer
    {
        private static void Postfix(Missile __instance)
        {
            if (__instance.definition != null && __instance.definition.jsonKey == MaldJammer.JsonKey && !__instance.GetComponent<MaldJammer>())
                __instance.gameObject.AddComponent<MaldJammer>();
        }
    }
}
