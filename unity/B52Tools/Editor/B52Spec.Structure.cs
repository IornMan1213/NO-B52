using System.Linq;
using UnityEngine;

namespace B52Tools
{
    /// <summary>
    /// The B-52's internal structure (tools/blender_structure.py): a "&lt;part&gt;_struct" child under each flight-model
    /// part holding its ribs, spars, frames, stringers and the inside of its skin. It ships switched off and stays off
    /// until the part, or one attached to it, breaks away; B52Systems.BreakStubs then switches on the structure on both
    /// sides of the break. Until then it costs nothing to draw.
    /// </summary>
    public static partial class B52Builder
    {
        static void SpecStructure()
        {
            int n = 0, tris = 0;
            foreach (var r in ourRoot.GetComponentsInChildren<MeshRenderer>(true).Where(r => r.name.EndsWith("_struct")))
            {
                r.enabled = false;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
                foreach (var c in r.GetComponents<Collider>()) Object.DestroyImmediate(c);
                var mf = r.GetComponent<MeshFilter>();
                if (mf && mf.sharedMesh) tris += (int)(mf.sharedMesh.triangles.LongLength / 3);
                B52DonorAudit.Chose(r, "*");
                n++;
            }
            Note(n > 0 ? $"Structure: {n} parts' internal structure ({tris} triangles), off until a break"
                       : "  ! no _struct objects (run tools/blender_structure.py): breaks show only the torn skin");
        }
    }
}
