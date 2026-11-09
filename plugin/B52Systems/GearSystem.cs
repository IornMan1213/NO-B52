using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// B-52 landing gear behaviour the game's LandingGear doesn't have. (The gear doors need nothing extra: like the
    /// real aircraft's, they open before the gear extends, hang open while it is down and shut after it retracts,
    /// which is LandingGear's own sequence.)
    ///  - Crosswind crab: on the real aircraft all four main trucks can be turned up to 20 deg either side of the
    ///    fuselage line (crab-angle knob on the centre pedestal), so it lands and takes off pointing into the wind
    ///    with the wheels running straight down the runway. Here: keys (default [ and ] in 5 deg steps, \ to centre)
    ///    set the angle, and the trucks slew to it at 5 deg/s while the gear is locked down. The forward trucks still
    ///    steer for taxiing on top of the crab.
    /// </summary>
    public class GearSystem : MonoBehaviour
    {
        private const float CrabMax = 20f, CrabStep = 5f, CrabSlew = 5f;
        private static readonly AccessTools.FieldRef<LandingGear, Transform> SwivelRef =
            AccessTools.FieldRefAccess<LandingGear, Transform>("strutRotationTransform");
        private static readonly AccessTools.FieldRef<LandingGear, float> StrutRotRef =
            AccessTools.FieldRefAccess<LandingGear, float>("strutRotation");

        internal static float CrabSelected;                     // set by the local pilot, deg, + = trucks to the right
        private static Aircraft crabOwner;                      // the aircraft CrabSelected was set in

        private Aircraft aircraft;
        private readonly List<Transform> swivels = new List<Transform>();
        private float crab;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            if (!aircraft) { Destroy(this); return; }
            foreach (var lg in GetComponentsInChildren<LandingGear>(true))
            {
                if (StrutRotRef(lg) == 0f) continue;            // outriggers don't crab
                var sw = SwivelRef(lg);
                if (sw) swivels.Add(sw);
            }
        }

        private void Update()
        {
            if (!aircraft) return;
            var state = aircraft.gearState;
            bool local = GameManager.IsLocalAircraft(aircraft);
            if (local)
            {
                // CrabSelected is static so it survives the game rebuilding components, but a new aircraft (respawn)
                // starts with its trucks straight.
                if (crabOwner != aircraft) { crabOwner = aircraft; CrabSelected = 0f; }
                HandleCrabKeys();
                HudReadout.LocalB52Flying();
            }
            else if (crabOwner == aircraft) HudReadout.SetStatus("crab", null);   // the pilot left this aircraft
            float target = local ? CrabSelected : 0f;
            if (state == LandingGear.GearState.LockedExtended)
            {
                crab = Mathf.MoveTowards(crab, target, CrabSlew * Time.deltaTime);
                foreach (var sw in swivels) if (sw) sw.localEulerAngles = new Vector3(0f, crab, 0f);
            }
            else crab = 0f;                                     // LandingGear owns the swivel while folding
            if (local) HudReadout.SetStatus("crab", CrabSelected == 0f ? null :
                CrabText() + (state == LandingGear.GearState.LockedExtended && Mathf.Abs(crab - CrabSelected) > 0.1f ? "  (slewing)" : ""));
        }

        private static string CrabText() => $"GEAR CRAB  {Mathf.Abs(CrabSelected):F0}° {(CrabSelected > 0 ? "R" : "L")}";

        private static void HandleCrabKeys()
        {
            if (NuclearOption.MissionEditorScripts.InputFieldChecker.InsideInputField || GameplayUI.GameIsPaused) return;   // chat, menus
            float before = CrabSelected;
            if (Plugin.CrabLeft.Value.IsDown()) CrabSelected = Mathf.Max(CrabSelected - CrabStep, -CrabMax);
            if (Plugin.CrabRight.Value.IsDown()) CrabSelected = Mathf.Min(CrabSelected + CrabStep, CrabMax);
            if (Plugin.CrabCentre.Value.IsDown()) CrabSelected = 0f;
            if (CrabSelected != before) HudReadout.Flash(CrabSelected == 0f ? "GEAR CRAB  CENTRED" : CrabText());
        }

        private void OnDestroy()
        {
            if (crabOwner == aircraft) HudReadout.SetStatus("crab", null);
        }
    }

}
