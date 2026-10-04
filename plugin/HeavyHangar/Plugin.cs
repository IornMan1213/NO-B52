using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace HeavyHangar
{
    /// <summary>
    /// Runtime side of the Heavy Aircraft Hangar mod (Heavy Hangar_x.y.z.nobp holds the building itself).
    /// - Routes oversize aircraft (big span or tall fin) to heavy hangars instead of hangars they don't fit.
    /// - Lets a heavy hangar spawn everything its airbase's other hangars offer.
    /// - Optionally places one heavy hangar beside a long runway at each land airbase when a mission starts.
    /// Works for any aircraft; nothing here is B-52 specific.
    /// </summary>
    [BepInPlugin("com.ironman1213.heavyhangar", "Heavy Hangar", "0.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string JsonKey = "hangar_heavy";          // unity/B52Tools/Editor/HangarBuilder.cs

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> AutoPlace;
        internal static ConfigEntry<float> MinRunway;
        internal static ConfigEntry<float> OversizeSpan;
        internal static ConfigEntry<float> OversizeHeight;
        internal static ConfigEntry<bool> VerboseLog;

        private void Awake()
        {
            Log = Logger;
            AutoPlace = Config.Bind("Placement", "AutoPlace", true,
                "When a mission starts (host/server only), add a heavy hangar beside the longest runway of each land " +
                "airbase that doesn't already have one, if there is a flat, clear spot. Hangars placed in the mission " +
                "editor are always kept.");
            MinRunway = Config.Bind("Placement", "MinRunwayLength", 1800f,
                "Only airbases with a takeoff runway at least this long (metres) get a heavy hangar.");
            OversizeSpan = Config.Bind("Routing", "OversizeSpan", 44f,
                "Aircraft wider than this (metres) only spawn from heavy hangars when the airbase has one. " +
                "The largest stock hangar door is ~46 m wide.");
            OversizeHeight = Config.Bind("Routing", "OversizeHeight", 9f,
                "Aircraft taller than this (metres) only spawn from heavy hangars when the airbase has one. " +
                "Stock hangar doors are 8 m tall.");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Log every placement candidate that was rejected and why.");
            new Harmony("com.ironman1213.heavyhangar").PatchAll();
            Log.LogInfo("Heavy Hangar loaded");
        }

        internal static bool IsHeavy(Hangar h) =>
            h != null && h.attachedUnit != null && h.attachedUnit.definition != null && h.attachedUnit.definition.jsonKey == JsonKey;

        internal static bool IsOversize(AircraftDefinition d) =>
            d != null && (d.width > OversizeSpan.Value || d.height > OversizeHeight.Value);
    }
}
