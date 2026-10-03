using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// The B-52H/J rolls with spoilers only (no ailerons), and spoilers only ever rise. The game's ControlSurface
    /// swings both ways, so the aero surface (spoilers_X_visible) stays hidden and drives the lift, while a separate
    /// visible panel set (spoilerPanels_X) copies only the upward part of its deflection.
    /// </summary>
    public class SpoilerDriver : MonoBehaviour
    {
        private Transform[] aero = new Transform[2];
        private Transform[] panels = new Transform[2];
        private Renderer[] panelRenderers = new Renderer[2];

        private void Start()
        {
            string[] sides = { "L", "R" };
            for (int i = 0; i < 2; i++)
            {
                aero[i] = FindDeep(transform, "spoilers_" + sides[i] + "_visible");
                panels[i] = FindDeep(transform, "spoilerPanels_" + sides[i]);
                if (panels[i]) panelRenderers[i] = panels[i].GetComponent<Renderer>();
            }
        }

        private void LateUpdate()
        {
            for (int i = 0; i < 2; i++)
            {
                if (!aero[i] || !panels[i]) continue;
                float a = aero[i].localEulerAngles.x;
                if (a > 180f) a -= 360f;
                float up = Mathf.Max(0f, a);              // + about the hinge raises the trailing edge
                panels[i].localRotation = Quaternion.AngleAxis(up, Vector3.right);
                if (panelRenderers[i]) panelRenderers[i].enabled = up > 1f;
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t) { var f = FindDeep(c, name); if (f) return f; }
            return null;
        }
    }

    [HarmonyPatch(typeof(Aircraft), nameof(Aircraft.Awake))]
    internal static class AttachSpoilerDriver
    {
        private static void Postfix(Aircraft __instance)
        {
            if (Plugin.IsB52(__instance) && !__instance.GetComponent<SpoilerDriver>())
                __instance.gameObject.AddComponent<SpoilerDriver>();
        }
    }
}
