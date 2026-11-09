using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Turns the engine fans (fan_pod*_* objects, split out of the pods by tools/blender_fans.py) with the engines'
    /// spool-up. The visual speed is kept slow (about 1 to 4 turns a second): at real fan speeds the blades would just
    /// strobe at the frame rate, so this reads as "turning" without the wagon-wheel effect.
    /// </summary>
    public class EngineFans : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<Turbofan, float> Rpm = AccessTools.FieldRefAccess<Turbofan, float>("currentRPM");
        private static readonly AccessTools.FieldRef<Turbofan, float> MaxRpm = AccessTools.FieldRefAccess<Turbofan, float>("maxRPM");
        private const float IdleTurns = 0.4f, FullTurns = 4f;

        private Aircraft aircraft;
        private readonly List<Transform> fans = new List<Transform>();
        private Turbofan[] engines;
        private float angle;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (t.name.StartsWith("fan_pod")) fans.Add(t);
            engines = GetComponentsInChildren<Turbofan>(true);
            if (!aircraft || fans.Count == 0 || engines.Length == 0) Destroy(this);
        }

        private void LateUpdate()
        {
            float ratio = 0f; int n = 0;
            foreach (var e in engines)
            {
                if (!e) continue;
                float max = MaxRpm(e);
                if (max > 0f) { ratio += Mathf.Clamp01(Rpm(e) / max); n++; }
            }
            if (n > 0) ratio /= n;
            float turns = ratio <= 0.01f ? 0f : IdleTurns + (FullTurns - IdleTurns) * ratio * ratio;
            angle = Mathf.Repeat(angle + turns * 360f * Time.deltaTime, 360f);
            var rot = Quaternion.AngleAxis(angle, Vector3.forward);
            foreach (var f in fans) if (f) f.localRotation = rot;
        }
    }
}
