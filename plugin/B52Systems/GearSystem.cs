using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// B-52 landing gear behaviour the game's LandingGear doesn't have.
    ///  - Well doors: LandingGear opens a gear's doors before extending and leaves them open while the gear is down.
    ///    The B-52's main-gear well doors close again once the gear is locked down (the struts pass the door line
    ///    outboard of them), and open for retraction while the trucks start to fold; LandingGear closes them after.
    ///  - Crosswind crab: on the real aircraft all four main trucks can be turned up to 20 deg either side of the
    ///    fuselage line (crab-angle knob on the centre pedestal), so it lands and takes off pointing into the wind
    ///    with the wheels running straight down the runway. Here: keys (default [ and ] in 5 deg steps, \ to centre)
    ///    set the angle, and the trucks slew to it at 5 deg/s while the gear is locked down. The forward trucks still
    ///    steer for taxiing on top of the crab.
    /// </summary>
    public class GearSystem : MonoBehaviour
    {
        private const float DoorCloseTime = 1.2f, DoorOpenTime = 0.6f, CrabMax = 20f, CrabStep = 5f, CrabSlew = 5f;
        private static readonly AccessTools.FieldRef<LandingGear, List<LandingGear.GearDoor>> DoorsRef =
            AccessTools.FieldRefAccess<LandingGear, List<LandingGear.GearDoor>>("gearDoors");
        private static readonly AccessTools.FieldRef<LandingGear, Transform> SwivelRef =
            AccessTools.FieldRefAccess<LandingGear, Transform>("strutRotationTransform");
        private static readonly AccessTools.FieldRef<LandingGear, float> StrutRotRef =
            AccessTools.FieldRefAccess<LandingGear, float>("strutRotation");

        internal static float CrabSelected;                     // set by the local pilot, deg, + = trucks to the right
        private static float messageUntil;

        private Aircraft aircraft;
        private readonly List<LandingGear.GearDoor> doors = new List<LandingGear.GearDoor>();
        private readonly List<Transform> swivels = new List<Transform>();
        private LandingGear.GearState lastState = LandingGear.GearState.Uninitialized;
        private float doorTimer = -1f, doorFrom, doorTo, crab;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            if (!aircraft) { Destroy(this); return; }
            foreach (var lg in GetComponentsInChildren<LandingGear>(true))
            {
                if (StrutRotRef(lg) == 0f) continue;            // outriggers: no well doors, no crab
                doors.AddRange(DoorsRef(lg));
                var sw = SwivelRef(lg);
                if (sw) swivels.Add(sw);
            }
        }

        private void Update()
        {
            if (!aircraft) return;
            var state = aircraft.gearState;
            if (state != lastState)
            {
                if (state == LandingGear.GearState.LockedExtended)
                    StartDoors(1f, 0f, lastState == LandingGear.GearState.Uninitialized ? 0.01f : DoorCloseTime);
                else if (state == LandingGear.GearState.Retracting)
                    StartDoors(0f, 1f, DoorOpenTime);
                else doorTimer = -1f;                           // Extending / LockedRetracted: LandingGear drives the doors
                lastState = state;
            }
            if (doorTimer >= 0f)
            {
                doorTimer += Time.deltaTime;
                float t = Mathf.Clamp01(doorTimer / doorDuration);
                float open = Mathf.Lerp(doorFrom, doorTo, Mathf.SmoothStep(0f, 1f, t));
                foreach (var d in doors) d.Animate(open);
                if (t >= 1f) doorTimer = -1f;
            }

            if (GameManager.IsLocalAircraft(aircraft)) HandleCrabKeys();
            float target = GameManager.IsLocalAircraft(aircraft) ? CrabSelected : 0f;
            if (state == LandingGear.GearState.LockedExtended)
            {
                crab = Mathf.MoveTowards(crab, target, CrabSlew * Time.deltaTime);
                foreach (var sw in swivels) if (sw) sw.localEulerAngles = new Vector3(0f, crab, 0f);
            }
            else crab = 0f;                                     // LandingGear owns the swivel while folding
        }

        private float doorDuration = 1f;

        private void StartDoors(float from, float to, float duration)
        {
            doorFrom = from; doorTo = to; doorDuration = duration; doorTimer = 0f;
        }

        private static void HandleCrabKeys()
        {
            float before = CrabSelected;
            if (Plugin.CrabLeft.Value.IsDown()) CrabSelected = Mathf.Max(CrabSelected - CrabStep, -CrabMax);
            if (Plugin.CrabRight.Value.IsDown()) CrabSelected = Mathf.Min(CrabSelected + CrabStep, CrabMax);
            if (Plugin.CrabCentre.Value.IsDown()) CrabSelected = 0f;
            if (CrabSelected != before) messageUntil = Time.time + 3f;
        }

        private void OnGUI()
        {
            if (!GameManager.IsLocalAircraft(aircraft) || (Time.time > messageUntil && CrabSelected == 0f)) return;
            string text = CrabSelected == 0f ? "GEAR CRAB  0" :
                $"GEAR CRAB  {Mathf.Abs(CrabSelected):F0}° {(CrabSelected > 0 ? "R" : "L")}" + (Mathf.Abs(crab - CrabSelected) > 0.1f ? "  (slewing)" : "");
            var style = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperCenter };
            style.normal.textColor = new Color(0.3f, 1f, 0.4f);
            GUI.Label(new Rect(Screen.width / 2f - 150f, 60f, 300f, 30f), text, style);
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachGearSystem
    {
        private static void Postfix(Aircraft __instance)
        {
            if (Plugin.IsB52(__instance) && !__instance.GetComponent<GearSystem>())
                __instance.gameObject.AddComponent<GearSystem>();
        }
    }
}
