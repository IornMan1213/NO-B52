using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52H/J rolls with spoilers only (no ailerons), and spoilers only ever rise. The game's ControlSurface
    /// swings both ways, so the aero surface (spoilers_X_visible) stays hidden and drives the lift, while the seven
    /// visible panels a side (spoilerPanels_X1..7, each a flat plate hinged on its own front edge, as on the real
    /// aircraft) copy only the upward part of its deflection. When the airbrake is open (throttle at idle) both sides
    /// rise together, as the B-52 uses its spoilers as speedbrakes.
    /// </summary>
    public class SpoilerDriver : MonoBehaviour
    {
        private readonly Transform[] aero = new Transform[2];
        private readonly List<Transform>[] panels = { new List<Transform>(), new List<Transform>() };
        private readonly List<Quaternion>[] rest = { new List<Quaternion>(), new List<Quaternion>() };
        private readonly List<Renderer> renderers = new List<Renderer>();
        private Airbrake airbrake;
        private static readonly AccessTools.FieldRef<Airbrake, float> OpenAmount = AccessTools.FieldRefAccess<Airbrake, float>("openAmount");
        private const float SpeedbrakeAngle = 60f;

        private void Start()
        {
            string[] sides = { "L", "R" };
            for (int i = 0; i < 2; i++)
            {
                aero[i] = FindDeep(transform, "spoilers_" + sides[i] + "_visible");
                var part = FindDeep(transform, "spoilers_" + sides[i]);
                if (!part) continue;
                foreach (Transform c in part)
                {
                    if (!c.name.StartsWith("spoilerPanels_")) continue;
                    panels[i].Add(c); rest[i].Add(c.localRotation);
                    var r = c.GetComponent<Renderer>(); if (r) renderers.Add(r);
                }
            }
            airbrake = GetComponent<Airbrake>();
        }

        private void LateUpdate()
        {
            for (int i = 0; i < 2; i++)
            {
                if (!aero[i] || panels[i].Count == 0) continue;
                float a = aero[i].localEulerAngles.x;
                if (a > 180f) a -= 360f;
                float up = Mathf.Max(0f, a);              // + about the hinge raises the trailing edge
                if (airbrake) up = Mathf.Max(up, OpenAmount(airbrake) * SpeedbrakeAngle);   // speedbrake: both sides up
                var turn = Quaternion.AngleAxis(up, Vector3.right);
                for (int p = 0; p < panels[i].Count; p++)
                    if (panels[i][p]) panels[i][p].localRotation = rest[i][p] * turn;   // about the panel's own front edge
            }
            foreach (var r in renderers) if (r && !r.enabled) r.enabled = true;   // the panels are the wing's skin: always shown
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
            return null;
        }
    }

}
