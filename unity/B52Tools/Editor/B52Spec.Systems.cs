using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace B52Tools
{
    /// <summary>
    /// The B-52's own values for its gear, control surfaces, flaps, fuel tanks, bay doors, sensors, defensive systems,
    /// power, autopilot and crew (docs/DONOR_REPLACEMENT.md, phases 3 and 4), in place of the donor bomber's.
    /// </summary>
    public static partial class B52Builder
    {
        // Gear drag area (m2, drag coefficient folded in). The game sets the part the gear hangs on to extendedDrag while the
        // gear is down (it replaces that part's drag area, it doesn't add to it), so extendedDrag = the part's own drag +
        // its gear's. A B-52 main truck (dual 56x16 tyres, the strut and its open door) is about 1.4 m2; an outrigger 0.25.
        // Four trucks then add about 5.6 m2, roughly doubling the clean airframe's drag, as gear does on a heavy jet. (Every
        // leg kept the donor's 5 m2, which set the centre section, forward fuselage and both wingtips to 5 m2 each.)
        const float TruckDrag = 1.4f, OutriggerDrag = 0.25f;

        static void SpecSystems()
        {
            // ---------------------------------------------------------------- landing gear
            var gearType = T("LandingGear");
            var gears = ourRoot.GetComponentsInChildren(gearType, true);
            var perPart = new Dictionary<Object, float>();
            foreach (var lg in gears)
            {
                var part = SO(lg).FindProperty("attachedPart").objectReferenceValue;
                bool outrigger = lg.name.Contains("_O");
                if (part) perPart[part] = (perPart.TryGetValue(part, out var d) ? d : 0f) + (outrigger ? OutriggerDrag : TruckDrag);
            }
            foreach (var lg in gears)
            {
                bool outrigger = lg.name.Contains("_O");
                var part = SO(lg).FindProperty("attachedPart").objectReferenceValue as Component;
                float own = part && Phys.TryGetValue(part.name, out var ph) ? ph.drag : 0f;
                SetF(lg, "extendedDrag", own + (part && perPart.TryGetValue(part, out var g) ? g : 0f));
                // Dry concrete under heavily loaded aircraft tyres: about 0.75 at the limit (the game's brakes use all of it;
                // B52Systems.Hydraulics holds them to the B-52's anti-skid limit above taxi speed).
                SetF(lg, "frictionCoef", 0.75f);
                // Side-force build-up with slip angle: the game's tyre law, at its normal strength.
                SetF(lg, "response", 1f);
                // Tyre contact area (m2), for sinking into soft ground: the B-52's main tyres run at about 1.7 MPa
                // (250 psi), so ~60 t on a truck needs 0.35 m2; an outrigger ~0.08 m2. The game sinks a wheel by its load
                // over this area: a loaded B-52 bogs down on grass.
                SetF(lg, "contactArea", outrigger ? 0.08f : 0.35f);
                // Tyre and gear sounds: the game's, deeper for the big tyres and louder for the heavy legs.
                SetF(lg, "skidVolumeFloor", -0.2f);
                SetF(lg, "skidPitchMult", outrigger ? 0.8f : 0.65f);
                SetF(lg, "foldVolume", outrigger ? 0.35f : 0.55f);
                SetF(lg, "latchVolume", outrigger ? 0.4f : 0.6f);
                foreach (var f in new[] { "breakSound", "foldSound", "latchSound", "gearDoors" }) B52DonorAudit.Chose(lg, f);
                // The leg's wheels, moving parts and torque-link joints are the B-52's own objects, listed in BuildGear.
                foreach (var f in new[] { "wheels", "movingParts", "joints" }) B52DonorAudit.Chose(lg, f);
            }
            Note($"Gear: {gears.Length} legs, gear-down drag {string.Join(", ", perPart.Select(kv => $"{((Component)kv.Key).name} +{kv.Value:F2}"))} m2");

            // ---------------------------------------------------------------- control surfaces
            // The elevators move in pitch only, the rudder in yaw only, the stabiliser in pitch (trim); none acts as a
            // brake, a flap or a split surface (the B-52's speedbrakes are its spoilers, B52Systems.SpoilerDriver).
            foreach (var cs in ourRoot.GetComponentsInChildren(T("ControlSurface"), true))
            {
                var n = cs.name;
                bool rudder = n == "rudder", spoiler = n.StartsWith("spoilers");
                if (!rudder) SetF(cs, "yawRange", 0f);
                if (rudder) { SetF(cs, "pitchRange", 0f); SetF(cs, "rollRange", 0f); }
                SetF(cs, "brakeRange", 0f);
                Set(cs, "flap", p => p.boolValue = false);
                SetF(cs, "maxSplit", 0f); SetF(cs, "splitDrag", 0f); SetF(cs, "yawSplitFactor", 0f);
                SetRef(cs, "splitUpper", null); SetRef(cs, "splitLower", null); SetRef(cs, "splitSound", null);
                SetF(cs, "splitVolumeMultiplier", 0f); SetF(cs, "maxVolumeSpeed", 340f);
                SetF(cs, "splitPitchMin", 0.5f); SetF(cs, "splitPitchMax", 2f);
            }

            // ---------------------------------------------------------------- flaps
            // Fowler flaps, run by airspeed (HighLiftDevice speedDeployed/speedRetracted), not by angle of attack.
            foreach (var h in ourRoot.GetComponentsInChildren(T("HighLiftDevice"), true))
            {
                SetF(h, "alphaMin", 0f);
                SetF(h, "alphaFactor", 0f);
                B52DonorAudit.Chose(h, "movingParts");     // every entry set in WireControlSurfaces: the flap and wing camber, rotated, not moved
            }

            // ---------------------------------------------------------------- fuel tanks
            // Rupture and ignition (FuelTank): a hard impact ruptures a tank above a g drawn between the two limits, and a
            // hit ignites a leaking tank above a pierce or blast drawn between its limits, scaled by how fast it leaks.
            // Integral wing tanks (the skin is the tank wall) rupture in a crash more easily than the bladder tanks inside
            // the fuselage; JP-8 is hard to light (flash point 38 C), harder than the donor's numbers for a leak hit by
            // fragments, easy for an incendiary round.
            foreach (var ft in ourRoot.GetComponentsInChildren(T("FuelTank"), true))
            {
                bool wing = ft.name.StartsWith("tank_wing");
                SetF(ft, "ruptureGMin", wing ? 25f : 40f);   SetF(ft, "ruptureGMax", wing ? 150f : 250f);
                SetF(ft, "ignitionGMin", wing ? 40f : 60f);   SetF(ft, "ignitionGMax", wing ? 300f : 400f);
                SetF(ft, "ignitionPierceMin", 10f);           SetF(ft, "ignitionPierceMax", 120f);
                SetF(ft, "ignitionBlastMin", 6f);             SetF(ft, "ignitionBlastMax", 120f);
                foreach (var f in new[] { "leakEffect", "fireEffect", "fireball" }) B52DonorAudit.Chose(ft, f);   // the game's fuel fire
            }

            // ---------------------------------------------------------------- bay doors
            // The B-52's bay doors open in about 2.5 s and close in about 3 (hydraulic, long doors): BayDoor's speeds are
            // fractions of full travel per second. The game's bay door sounds.
            foreach (var bd in ourRoot.GetComponentsInChildren(T("BayDoor"), true))
            {
                SetF(bd, "openSpeed", 0.4f);
                SetF(bd, "closeSpeed", 0.33f);
                B52DonorAudit.Chose(bd, "openStartSound"); B52DonorAudit.Chose(bd, "closeStartSound");
            }

            // ---------------------------------------------------------------- sensors
            var nose = Find(ourRoot, "nose_sensors");
            var radar = nose ? nose.GetComponent(T("Radar")) : null;
            if (radar)
            {
                // AN/APQ-188 (the B-52J's AESA, from the APG-79 family): a fixed, electronically scanned array, about +-70
                // deg, long range against large targets, fast to update, hard to jam.
                SetF(radar, "RadarParameters.maxRange", 120000f);
                SetF(radar, "RadarParameters.maxSignal", 6f);
                SetF(radar, "RadarParameters.minSignal", 0.4f);
                SetF(radar, "RadarParameters.clutterFactor", 0.25f);
                SetF(radar, "RadarParameters.dopplerFactor", 0.15f);
                SetF(radar, "radarCone", 70f);
                SetF(radar, "jamTolerance", 0.7f);
                SetF(radar, "powerDraw", 5f);
                SetF(radar, "checkInterval", 2f);
                SetF(radar, "alertCheckInterval", 4f);
                // Not a visual sensor: no visual range or magnification; no moving scanner.
                SetF(radar, "visualRange", 0f); SetF(radar, "magnification", 0f); SetF(radar, "maxSpeed", 0f);
                Set(radar, "rotators", p => p.arraySize = 0); B52DonorAudit.Chose(radar, "rotators");   // a fixed array: nothing turns
                foreach (var f in new[] { "shared", "activated", "detectedTargets" }) B52DonorAudit.Chose(radar, f);   // runtime
            }
            var tcam = nose ? nose.GetComponent(T("TargetCam")) : null;
            if (tcam)
            {
                // The targeting pod's view when landing (the game's landing camera): a fairly wide field; not a VTOL.
                SetF(tcam, "landingCamFoV", 40f);
                Set(tcam, "vtolLandingCam", p => p.boolValue = false);
            }
            var loc = nose ? nose.GetComponent(T("RadarLocator")) : null;
            if (loc) B52DonorAudit.Chose(loc, "essentialParts");
            var det = ourRoot.GetComponent(T("TargetDetector"));
            if (det)
            {
                // What the crew see out of the windows: a big aircraft's crew with few windows and their eyes on their
                // stations, so a little shorter than a fighter's; the targeting pod's zoom for identification.
                SetF(det, "visualRange", 10000f);
                SetF(det, "magnification", 1.5f);
                SetF(det, "maxSpeed", 2000f);
                SetF(det, "checkInterval", 2f);
                SetF(det, "alertCheckInterval", 0f);
                Set(det, "rotators", p => p.arraySize = 0); B52DonorAudit.Chose(det, "rotators");
                foreach (var f in new[] { "shared", "activated", "detectedTargets" }) B52DonorAudit.Chose(det, f);
            }

            // ---------------------------------------------------------------- defensive systems
            var flares = ourRoot.GetComponent(T("FlareEjector"));
            if (flares)
            {
                // ALE-20 flare dispensers in the aft fuselage: 192 flares, fired in groups from the dispensers on both sides.
                Set(flares, "displayName", p => p.stringValue = "Flares (ALE-20)");
                Set(flares, "chargeable", p => p.boolValue = false);
                SetN(flares, "ammo", 192);
                SetF(flares, "ejectionVelocity", 30f);
                SetF(flares, "ejectionVelocityVariance", 0.4f);
                SetF(flares, "ejectionInterval", 0.15f);
                SetN(flares, "ejectionGrouping", 4);
                SetF(flares, "ejectionVolume", 1f);
                Set(flares, "flareDoors", p => p.arraySize = 0); B52DonorAudit.Chose(flares, "flareDoors");   // the dispensers fire through open ports: no doors
                foreach (var f in new[] { "displayImage", "flarePrefab", "ejectionSound", "ejectionPoints" }) B52DonorAudit.Chose(flares, f);   // game flare, icon and sound; points set in FixLeftovers
            }
            var jam = ourRoot.GetComponent(T("RadarJammer"));
            if (jam)
            {
                // The B-52H's ECM suite (ALQ-172 and successors): powerful, drawing on the aircraft's four generators.
                Set(jam, "displayName", p => p.stringValue = "ECM (ALQ-172)");
                Set(jam, "chargeable", p => p.boolValue = true);
                SetN(jam, "ammo", 0);
                SetF(jam, "powerUsage", 80f);
                SetF(jam, "dischargeVolume", 0.8f);
                SetF(jam, "volumeMultiplier", 1f);
                B52DonorAudit.Chose(jam, "displayImage"); B52DonorAudit.Chose(jam, "dischargeSound");
            }
            var power = ourRoot.GetComponent(T("PowerSupply"));
            if (power)
            {
                // Four engine-driven 120 kVA generators (one per pod here), about four times a fighter-bomber's electrical
                // power. PowerSupply charges each source's chargePerRPM x its RPM per second; RPM is % N1 (B52Spec.Engines),
                // so 0.12 gives 12 a second per generator at full power.
                var gens = new[] { "engine1", "engine3", "engine5", "engine7" }.Select(e => Find(ourRoot, e)).Where(t => t).Select(t => (Object)t.gameObject).ToList();
                SetRefArray(power, "powerSources", gens); B52DonorAudit.Chose(power, "powerSources");
                SetF(power, "chargePerRPM", 0.12f);
                SetF(power, "maxCharge", 800f);
                SetF(power, "maxPower", 200f);
                SetF(power, "pitchMin", 0.5f); SetF(power, "pitchMax", 1f); SetF(power, "volumeMultiplier", 0.6f);
                Set(power, "supplyAtCharge", p => p.animationCurveValue = Linear(0f, 0f, 0.6f, 1f, 1f, 1f));   // full supply above 60% charge
            }

            // ---------------------------------------------------------------- autopilot and fly-by-wire
            var cockpit = Find(ourRoot, "cockpit");
            var ap = cockpit ? cockpit.GetComponent(T("AutopilotPlane")) : null;
            if (ap)
            {
                // The AI's pilot: gains as the stock heavy aircraft's, never inverted, angle of attack held under the
                // wing's stall (17 deg; the wing is set at 6 deg on the fuselage).
                SetF(ap, "forwardFlightController.referenceAirspeed", 180f);   // as the stock heavies (gains scale with (180/v)^2)
                Set(ap, "forwardFlightController.Enabled", p => p.boolValue = true);
                // Pitch: the stock heavy aircraft's gains (Darkreach and the fast bomber use 0.07/0.02/0.01 around
                // 180-200 m/s), integral trimmed a little for the B-52's weight. At 0.05 around 230 m/s it had too
                // little authority at approach speed: on final it overshot a 1.4 deg correction into a 14 deg dive, and
                // with less integral still it dove into the ground joining the pattern (tests/ingame/b52_ai.txt).
                Set(ap, "forwardFlightController.pitchFlightPID.PID", p => p.vector3Value = new Vector3(0.07f, 0.015f, 0.012f));
                Set(ap, "forwardFlightController.yawFlightPID.PID", p => p.vector3Value = new Vector3(0.04f, 0.001f, 0.005f));
                Set(ap, "forwardFlightController.rollFlightPID.PID", p => p.vector3Value = new Vector3(0.025f, 0.001f, 0.015f));
                Set(ap, "hoverController", p => p.FindPropertyRelative("Enabled").boolValue = false);   // not a VTOL
                B52DonorAudit.Chose(ap, "hoverController");           // switched off: its gains are never used
                Set(ap, "preventInvertedFlight", p => p.boolValue = true);
                SetF(ap, "aoaLimiter.threshold", 6f);
                SetF(ap, "aoaLimiter.limit", 14f);
            }
            var cf = cockpit ? cockpit.GetComponent(T("ControlsFilter")) : null;
            if (cf)
            {
                // The fly-by-wire's g limiter (TuneFlyByWire sets its limits and gains). Corner speed: the B-52's manoeuvring
                // speed, about 135 m/s (260 kt); the same for the switch between its slow and fast gains. Below it the
                // game scales pitch authority by (speed / corner speed)^2: at 165 m/s, full stick at approach speed asked for
                // about 4 deg/s, and the AI (and a player) couldn't stop a sink on short final. Roll authority is cut
                // above 180 m/s, as the spoilers' rolling power is limited at speed. A big fin weathervanes into the wind.
                // Gentle input smoothing for a heavy aircraft. Active above 40 m/s (well before rotation).
                SetF(cf, "minSpeed", 40f);
                SetF(cf, "minAlt", 0f);
                Set(cf, "flightAssistName", p => p.stringValue = "G Limiter");
                Set(cf, "flyByWire.Enabled", p => p.boolValue = true);
                SetF(cf, "flyByWire.opacity", 1f);
                SetF(cf, "flyByWire.cornerSpeed", 135f);
                SetF(cf, "flyByWire.pidTransitionSpeed", 135f);
                SetF(cf, "flyByWire.maxRollSpeed", 180f);
                SetF(cf, "flyByWire.yawWeathervaning", 0.08f);
                Set(cf, "flyByWire.inputSmoothing", p => p.vector3Value = new Vector3(0.1f, 0.1f, 0.1f));
                Set(cf, "autoHover", p => { p.FindPropertyRelative("Enabled").boolValue = false; p.FindPropertyRelative("Active").boolValue = false; });   // not a VTOL
                Set(cf, "aimAssist", p => p.FindPropertyRelative("Enabled").boolValue = false);   // no gun to aim
                B52DonorAudit.Chose(cf, "autoHover"); B52DonorAudit.Chose(cf, "aimAssist");   // switched off: their settings are never used
                // Kept at the donor's on purpose: postStallManeuverSpeed is only stored (the game's fly-by-wire tuner
                // shows it; nothing flies by it), and iFactor is the pitch loop's small integral (0.01), which the
                // B-52's gains above were tuned and flown with.
                B52DonorAudit.Chose(cf, "flyByWire.postStallManeuverSpeed"); B52DonorAudit.Chose(cf, "flyByWire.iFactor");
                Set(cf, "flightAssistDefault", p => p.boolValue = true);
                Set(cf, "ReverseThrust", p => p.boolValue = false);   // the F130 installation has no thrust reversers (the B-52 uses its drag chute)
            }

            // ---------------------------------------------------------------- crew
            // The crew: the game's aircrew values (pierce armour 100, as on every stock aircraft's crew), so the B-52's crew
            // are no easier or harder to hurt than anyone else's; they sit deep in the nose structure, which the hits have
            // to get through first (B52Builder.Armour).
            foreach (var pl in ourRoot.GetComponentsInChildren(T("Pilot"), true))
            {
                Set(pl, "pilotType", p => p.enumValueIndex = 0);                      // a plane's crew
                Set(pl, "exitDirection", p => p.enumValueIndex = pl.transform.localPosition.x < 0 ? 0 : 1);   // out on their own side when bailing out on the ground
                SetF(pl, "armorProperties.pierceArmor", 100f);
                SetF(pl, "armorProperties.blastArmor", 0f);
                SetF(pl, "armorProperties.fireArmor", 0f);
                SetF(pl, "armorProperties.pierceTolerance", 0.5f);
                SetF(pl, "armorProperties.blastTolerance", 1f);
                SetF(pl, "armorProperties.fireTolerance", 1f);
                SetF(pl, "armorProperties.overpressureLimit", 10f);
                B52DonorAudit.Chose(pl, "relaxedStabilityController"); B52DonorAudit.Chose(pl, "autoTrimmer");   // the aircraft's own (wiring)
                // Seats: every station has the B-52's own seat in the cockpit model (tools/blender_cockpit.py: seat_L/R,
                // seat_aft_L/R, seat_low_L/R), so the game's generic seat mesh that came with the crew goes. Its collider
                // becomes a box the size of the B-52 seat bucket, where the crewman sits.
                var es = pl.transform.Find("EjectionSeat");
                if (es)
                {
                    var r = es.GetComponent<MeshRenderer>(); if (r) Object.DestroyImmediate(r);
                    var f = es.GetComponent<MeshFilter>(); if (f) Object.DestroyImmediate(f);
                    var mc = es.GetComponent<MeshCollider>(); if (mc) Object.DestroyImmediate(mc);
                    var box = es.gameObject.AddComponent<BoxCollider>();
                    box.size = new Vector3(0.5f, 0.9f, 0.5f); box.center = new Vector3(0f, 0.45f, 0f);
                    box.sharedMaterial = AssetDatabase.LoadAssetAtPath<PhysicMaterial>(DoNotShip + "/PhysicMaterial/Metal_PLACEHOLDER.physicMaterial");
                }
            }
            Note("Systems spec: gear, surfaces, flaps, tanks, bay doors, sensors, defensive systems, power, autopilot, fly-by-wire, crew");
        }
    }
}
