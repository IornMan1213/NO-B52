using System.Collections.Generic;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Gives the B-52's particle effects back the game materials Blueprinter couldn't resolve. The wingtip vortex
    /// trails come from the donor bomber with the game's smokeRibbonScatter and arrive with no material, which Unity
    /// draws pink. Each takes the material of the same-named effect on the game's own aircraft, else the game material
    /// its placeholder named (Known); one with neither is switched off rather than left pink.
    /// </summary>
    internal static class EffectMaterials
    {
        // effect name prefix -> the game material the donor uses (from the placeholder in the B-52 prefab)
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string> { { "wingtipvortex", "smokeRibbonScatter" } };

        internal static void Fix(Aircraft aircraft)
        {
            Dictionary<string, ParticleSystemRenderer> stock = null;
            foreach (var r in aircraft.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r.sharedMaterial != null) continue;
                if (stock == null) stock = Stock();
                Material byName;
                if (stock.TryGetValue(r.name, out var src))
                {
                    r.sharedMaterial = src.sharedMaterial;
                    if (r.trailMaterial == null) r.trailMaterial = src.trailMaterial;
                }
                else if ((byName = Named(r.name)) != null) r.sharedMaterial = byName;
                else
                {
                    r.enabled = false;
                    Plugin.Log.LogWarning($"{r.name}: no material and no game effect of that name to take one from; switched off");
                }
            }
        }

        private static Material Named(string effect)
        {
            foreach (var k in Known)
                if (effect.StartsWith(k.Key))
                    foreach (var m in Resources.FindObjectsOfTypeAll<Material>())
                        if (m.name == k.Value) return m;
            return null;
        }

        // Every loaded effect that has a material, by object name (the game's aircraft prefabs are in memory).
        private static Dictionary<string, ParticleSystemRenderer> Stock()
        {
            var d = new Dictionary<string, ParticleSystemRenderer>();
            foreach (var r in Resources.FindObjectsOfTypeAll<ParticleSystemRenderer>())
                if (r.sharedMaterial != null && !d.ContainsKey(r.name)) d[r.name] = r;
            return d;
        }
    }
}
