using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52's own values in its aircraft definition and parameters (docs/DONOR_REPLACEMENT.md, phase 4), for the
    /// fields MakeDefinition doesn't already set.
    /// </summary>
    public static partial class B52Builder
    {
        // A number, whichever way the game declared the field (int or float).
        static void SetN(Object o, string prop, float v) =>
            Set(o, prop, p => { if (p.propertyType == UnityEditor.SerializedPropertyType.Integer) p.intValue = Mathf.RoundToInt(v); else p.floatValue = v; });

        static void SpecDefinition(Object def, Object par)
        {
            // What the B-52 is to the game's AI: an aircraft, a radar emitter (its AESA and ECM), a strategic asset; a
            // threat to ground and sea targets and to radars (JASSM, MALD-J), not to aircraft or missiles (no air-to-air
            // weapons).
            Set(def, "typeIdentity", p =>
            {
                p.FindPropertyRelative("surface").floatValue = 0f; p.FindPropertyRelative("air").floatValue = 1f;
                p.FindPropertyRelative("missile").floatValue = 0f; p.FindPropertyRelative("radar").floatValue = 0.6f;
                p.FindPropertyRelative("strategic").floatValue = 1f;
            });
            Set(def, "roleIdentity", p =>
            {
                p.FindPropertyRelative("antiSurface").floatValue = 1f; p.FindPropertyRelative("antiAir").floatValue = 0f;
                p.FindPropertyRelative("antiMissile").floatValue = 0f; p.FindPropertyRelative("antiRadar").floatValue = 0.4f;
            });
            Set(def, "bogeyName", p => p.stringValue = "");      // unidentified contacts show as the game's default
            // Seen from further than a 31 m jet: the B-52 spans 56 m (the visual range scales with size, ~1.8x the donor's).
            SetN(def, "visibleRange", 9000f);
            SetN(def, "iconRange", 100000f);
            SetN(def, "iconSize", 1.3f);
            SetN(def, "mapIconSize", 1.6f);
            Set(def, "mapOrient", p => p.boolValue = true);      // the map icon turns with the heading
            Set(def, "IsObstacle", p => p.boolValue = true);     // parked, it is in the way
            SetN(def, "captureCapacity", 0f); SetN(def, "captureStrength", 0f); SetN(def, "captureDefense", 0f);   // aircraft don't capture
            Set(def, "manpower", p => p.intValue = 5);           // five crew
            SetN(def, "armorTier", 1f);                           // an aircraft: no armour against guns or fragments
            // How many hits the AI expects to need (attacks = tolerance / kill probability): a 200 t bomber that keeps
            // flying with an engine pod or a wingtip gone takes about twice what a 34 t jet does.
            SetN(def, "damageTolerance", 3f);
            Set(def, "CanSlingLoad", p => p.boolValue = false);
            Set(def, "disabled", p => p.boolValue = false);
            Set(def, "isEventContent", p => p.boolValue = false);
            Set(def, "dontAutomaticallyAddToEncyclopedia", p => p.boolValue = false);
            SetN(def, "minEditorHeight", 0f);
            SetN(def, "maxEditorHeight", 15000f);                 // service ceiling 50,000 ft
            B52DonorAudit.Chose(def, "friendlyIcon"); B52DonorAudit.Chose(def, "hostileIcon");   // the game's aircraft markers

            // Parameters. Buffet (AoAEffects): a heavy, low-g aircraft starts to shake later in airspeed and nearer the
            // stall (17 deg wing angle, the wing set at 6 deg), and less. AI references at the B-52's speeds; the low-level
            // floor at its 400 ft penetration height.
            SetN(par, "AoAEffects.OnsetSpeed", 40f);
            SetN(par, "AoAEffects.FullVolumeSpeed", 180f);
            SetN(par, "AoAEffects.OnsetAlpha", 9f);
            SetN(par, "AoAEffects.FullVolumeAlpha", 18f);
            SetN(par, "AoAEffects.ShakeFactor", 0.05f);
            B52DonorAudit.Chose(par, "AoAEffects.AudioClip");    // the game's buffet sound
            SetN(par, "PIDReferenceAirspeed", 230f);
            // The AI's pattern speed (AIPilotLandingState flies the pattern at cornerSpeed) and its best-turn speed. 165 was
            // a fighter's: AI B-52s flew the pattern at 600 km/h and reached the runway at 400 km/h. The stock AWACS
            // uses 120; the B-52 flies its pattern at about 240 kt.
            SetN(par, "cornerSpeed", 125f);
            SetN(par, "minimumRadarAlt", 120f);
            Set(par, "verticalLanding", p => p.boolValue = false);
            SetN(par, "levelBias", 0f);                           // not read by the game
            SetN(par, "hoverTiltFactor", 0f);
            foreach (var pid in new[] { "collectivePID", "hoverPID", "tiltPID" }) Set(par, pid, p => p.vector3Value = Vector3.zero);   // not a VTOL
            foreach (var f in new[] { "takeoffMusic", "liveries", "StandardLoadouts", "airfoils", "loadouts" }) B52DonorAudit.Chose(par, f);   // the game's HUD and music; set in MakeDefinition
        }
    }
}
