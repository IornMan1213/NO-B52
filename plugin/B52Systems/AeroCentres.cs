using System.Collections.Generic;
using HarmonyLib;
using NuclearOption.Jobs;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Gives every aero part of the rigid (simple-physics) B-52 its real lever arm and local airflow.
    ///
    /// The aero job applies each part's force with rb.AddForce, i.e. through the rigidbody's centre of mass, plus a
    /// torque from AeroPart.centerOfLift (a small serialized offset, here inherited from FastBomber parts). In
    /// complex physics every part has its own rigidbody, so that works. With all parts sharing one rigidbody the
    /// tail had no leverage, the elevator almost none, and the donor offsets pitched the nose up: the B-52 did
    /// backflips with full nose-down elevator. The job also used the CoM velocity for every part, so rotation never
    /// changed the tail's angle of attack (no pitch, roll or yaw damping).
    ///
    /// After UpdateJobFields writes the job inputs, this sets centerOfLift to the part's aerodynamic centre relative to
    /// the CoM (in the lift frame the job rotates it by) and velocity to the airflow at that point.
    /// Aerodynamic centre: quarter chord at mid-span for thin lifting surfaces, vertex centroid for bodies
    /// (same rule as tools/aero_centres.py, which tools/trim_check.py uses to check pitch trim).
    /// </summary>
    internal static class AeroCentres
    {
        private static readonly AccessTools.FieldRef<AeroPart, PtrAllocation<AeroPartFields>> Fields =
            AccessTools.FieldRefAccess<AeroPart, PtrAllocation<AeroPartFields>>("JobFields");
        private static readonly AccessTools.FieldRef<AeroPart, Transform> LiftNormal =
            AccessTools.FieldRefAccess<AeroPart, Transform>("liftNormal");

        // Aerodynamic centre in the part's own transform space (the part transform doesn't move; surfaces rotate a child).
        private static readonly Dictionary<AeroPart, Vector3?> localAC = new Dictionary<AeroPart, Vector3?>();

        internal static void Apply(AeroPart part)
        {
            var rb = part.rb;
            if (!rb || !(part.parentUnit is Aircraft a) || rb != a.rb) return;          // detached parts fly on their own
            if (!localAC.TryGetValue(part, out var ac))
            {
                ac = Compute(part);
                localAC[part] = ac;
                if (ac.HasValue && Plugin.VerboseAero)
                    Plugin.Log.LogInfo($"B-52J AC {part.name}: {a.transform.InverseTransformPoint(part.transform.TransformPoint(ac.Value)):F2}");
            }
            if (!ac.HasValue) return;
            ref var jf = ref Fields(part);
            if (!jf.IsCreated) return;
            ref var f = ref jf.Ref();
            var ln = LiftNormal(part);
            var world = part.transform.TransformPoint(ac.Value);
            var arm = world - rb.worldCenterOfMass;
            f.centerOfLift = Quaternion.Inverse(ln ? ln.rotation : part.transform.rotation) * arm;
            if (f.centerOfLift == Vector3.zero) f.centerOfLift = new Vector3(0, 0, 1e-4f);  // zero means "no torque" to the job
            f.velocity = rb.GetPointVelocity(world);
        }

        internal static void Forget(AeroPart part) => localAC.Remove(part);

        private static Vector3? Compute(AeroPart part)
        {
            var t = part.transform;
            var ln = LiftNormal(part) ?? t;
            var verts = new List<Vector3>();
            foreach (var mf in part.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh || mf.GetComponentInParent<UnitPart>() != part) continue;   // child parts own their meshes
                if (!mf.sharedMesh.isReadable) continue;
                foreach (var v in mf.sharedMesh.vertices) verts.Add(t.InverseTransformPoint(mf.transform.TransformPoint(v)));
            }
            if (verts.Count == 0) return null;

            Vector3 s = t.InverseTransformDirection(ln.right), c = t.InverseTransformDirection(ln.forward), n = t.InverseTransformDirection(ln.up);
            float sMin = float.MaxValue, sMax = float.MinValue, nMin = float.MaxValue, nMax = float.MinValue;
            var centroid = Vector3.zero;
            foreach (var v in verts)
            {
                float sv = Vector3.Dot(v, s), nv = Vector3.Dot(v, n);
                sMin = Mathf.Min(sMin, sv); sMax = Mathf.Max(sMax, sv); nMin = Mathf.Min(nMin, nv); nMax = Mathf.Max(nMax, nv);
                centroid += v;
            }
            centroid /= verts.Count;
            float span = sMax - sMin;
            if (part.GetWingArea() < 0.5f || nMax - nMin > 0.25f * span) return centroid;       // body, pod or nacelle

            float mid = (sMin + sMax) * 0.5f;
            foreach (var frac in new[] { 0.15f, 0.3f, 0.6f })
            {
                float le = float.MinValue, te = float.MaxValue, nSum = 0; int k = 0;
                foreach (var v in verts)
                {
                    if (Mathf.Abs(Vector3.Dot(v, s) - mid) > frac * span) continue;
                    float cv = Vector3.Dot(v, c);
                    le = Mathf.Max(le, cv); te = Mathf.Min(te, cv); nSum += Vector3.Dot(v, n); k++;
                }
                if (k < 4) continue;
                return s * mid + c * (le - 0.25f * (le - te)) + n * (nSum / k);
            }
            return centroid;
        }
    }

    [HarmonyPatch(typeof(AeroPart), nameof(AeroPart.UpdateJobFields))]
    internal static class AeroCentresPatch
    {
        private static void Postfix(AeroPart __instance)
        {
            if (!Plugin.RigidAirframe.Value || !(__instance.parentUnit is Aircraft a) || !Plugin.IsB52(a)) return;
            AeroCentres.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(AeroPart), "OnDestroy")]
    internal static class AeroCentresForget
    {
        private static void Postfix(AeroPart __instance) => AeroCentres.Forget(__instance);
    }
}
