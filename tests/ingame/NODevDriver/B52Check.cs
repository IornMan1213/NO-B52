using System;
using System.Linq;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using UnityEngine;

namespace NODevDriverB52
{
    /// <summary>"b52check": is the B-52J mod loaded and set up the way a player gets it? One "[TEST] PASS/FAIL" line each.</summary>
    static class B52Check
    {
        internal static void Run(ManualLogSource log, Action<bool, string> test)
        {
            test(Chainloader.PluginInfos.ContainsKey("com.ironman1213.b52systems"), "B52Systems.dll loaded");
            if (Chainloader.PluginInfos.TryGetValue("com.ironman1213.b52systems", out var info))
            {
                var cfg = info.Instance.Config;
                test(cfg.TryGetEntry<bool>("Telemetry", "Enabled", out var tel) && !(bool)tel.DefaultValue, "telemetry is off by default");
                test(info.Instance.GetComponent("HudReadout") != null, "HUD readout is attached");
            }
            var defs = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
            var b52 = defs.FirstOrDefault(d => d.jsonKey == "B52J");
            test(b52 != null, "B52J aircraft definition registered");
            if (b52 != null) log.LogInfo($"  B52J: {b52.unitName}, span {b52.width} m, description: {b52.description}");
            var keys = Resources.FindObjectsOfTypeAll<ScriptableObject>().Select(o => o.name).ToList();
            foreach (var k in new[] { "B52_ADM160B", "B52_ADM160C" })
                test(keys.Contains(k), $"decoy {k} registered");
            int racks = keys.Count(n => n.StartsWith("B52_") && (n.Contains("_bayx") || n.Contains("_hsabx")));
            test(racks >= 60, $"B-52 weapon racks registered ({racks})");
            test(keys.Contains("B52_AGM86C_hsabx4") && !keys.Contains("B52_AGM86C_hsabx6"), "CALCM pylon rack is x4 (payload limit)");
            var heavy = Chainloader.PluginInfos.ContainsKey("com.ironman1213.heavyhangar");
            log.LogInfo($"  Heavy Hangar installed: {heavy}");
        }
    }
}
