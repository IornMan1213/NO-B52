using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Exterior lights the donor bomber doesn't give the B-52 (it keeps the stock red/green navigation lights):
    ///  - landing / taxi lights under the nose, ahead of the forward trucks: on while the gear is down;
    ///  - red anti-collision beacons on top of the fuselage and under the belly, flashing once a second;
    ///  - white strobes at the wingtips and the fin tip, a double flash every 1.5 s.
    /// Each flasher is a camera-facing glow plus a short-range point light, so it reads at night without lighting up
    /// the ground. Positions are in the aircraft frame (metres), measured from the model.
    /// </summary>
    public class ExteriorLights : MonoBehaviour
    {
        private class Flasher
        {
            public Transform glow; public Light light; public float period, phase, intensity; public bool doubleFlash;
        }

        private Aircraft aircraft;
        private Light[] landing;
        private Flasher[] flashers;
        private Material glowMat;
        private Texture2D glowTex;
        private Mesh quad;

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            if (!aircraft || !aircraft.networked) { Destroy(this); return; }   // hangar preview
            BuildAssets();
            var warm = new Color(1f, 0.95f, 0.85f);
            landing = new[]
            {
                Spot("B52_LandingLight_L", new Vector3(-0.8f, -2.55f, 16.6f), -4f, warm),
                Spot("B52_LandingLight_R", new Vector3(0.8f, -2.55f, 16.6f), -4f, warm),
                Spot("B52_TaxiLight", new Vector3(0f, -2.6f, 15.8f), -9f, warm, range: 160f, angle: 50f),
            };
            var red = new Color(1f, 0.12f, 0.08f); var white = new Color(1f, 1f, 1f);
            flashers = new[]
            {
                Flash("B52_BeaconTop", new Vector3(0f, 1.47f, 9f), red, 1.0f, 0.0f, 0.8f, false),
                Flash("B52_BeaconBottom", new Vector3(0f, -2.72f, -6f), red, 1.0f, 0.5f, 0.8f, false),
                Flash("B52_StrobeL", new Vector3(-28.35f, 1.15f, -7.1f), white, 1.5f, 0.0f, 0.6f, true),
                Flash("B52_StrobeR", new Vector3(28.35f, 1.15f, -7.1f), white, 1.5f, 0.0f, 0.6f, true),
                Flash("B52_StrobeTail", new Vector3(0f, 8.7f, -16.6f), white, 1.5f, 0.1f, 0.5f, true),
            };
        }

        private void BuildAssets()
        {
            // soft round glow: alpha falls off from the centre
            const int n = 64;
            glowTex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "B52_LightGlow" };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - d); a *= a;
                    glowTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            glowTex.Apply(false, true);
            var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            glowMat = sh ? new Material(sh) { mainTexture = glowTex, name = "B52_LightGlow", renderQueue = 3100 } : null;
            quad = new Mesh
            {
                name = "B52_LightGlow",
                vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) },
                uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) },
                triangles = new[] { 0, 2, 1, 0, 3, 2 }
            };
            quad.RecalculateBounds();
        }

        private Light Spot(string name, Vector3 pos, float pitchDown, Color c, float range = 550f, float angle = 24f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(-pitchDown, 0f, 0f);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot; l.range = range; l.spotAngle = angle; l.intensity = 4f; l.color = c;
            l.shadows = LightShadows.None; l.enabled = false;
            return l;
        }

        private Flasher Flash(string name, Vector3 pos, Color c, float period, float phase, float size, bool doubleFlash)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = pos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point; l.range = 14f; l.intensity = 0f; l.color = c; l.shadows = LightShadows.None;
            var g = new GameObject(name + "_glow");
            g.transform.SetParent(go.transform, false);
            g.transform.localScale = Vector3.one * size;
            g.AddComponent<MeshFilter>().sharedMesh = quad;
            var mr = g.AddComponent<MeshRenderer>();
            mr.sharedMaterial = glowMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            var block = new MaterialPropertyBlock(); block.SetColor("_Color", c); mr.SetPropertyBlock(block);
            g.SetActive(false);
            return new Flasher { glow = g.transform, light = l, period = period, phase = phase, intensity = 2.5f, doubleFlash = doubleFlash };
        }

        private void LateUpdate()
        {
            if (!aircraft || landing == null) return;
            var gear = aircraft.gearState;
            bool gearDown = gear == LandingGear.GearState.LockedExtended || gear == LandingGear.GearState.Extending;
            bool alive = !aircraft.disabled;
            foreach (var l in landing) if (l) l.enabled = gearDown && alive;

            var cam = Camera.main;
            float t = Time.time;
            foreach (var f in flashers)
            {
                if (!f.glow) continue;
                float u = Mathf.Repeat(t / f.period + f.phase, 1f);
                bool on = alive && (u < 0.08f || (f.doubleFlash && u > 0.16f && u < 0.24f));
                f.glow.gameObject.SetActive(on);
                f.light.intensity = on ? f.intensity : 0f;
                if (on && cam) f.glow.rotation = Quaternion.LookRotation(f.glow.position - cam.transform.position, cam.transform.up);
            }
        }

        private void OnDestroy()
        {
            if (glowMat) Destroy(glowMat);
            if (glowTex) Destroy(glowTex);
            if (quad) Destroy(quad);
        }
    }
}
