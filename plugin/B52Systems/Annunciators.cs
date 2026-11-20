using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// Working caution and warning lights: the row of 16 lamps under the glareshield (painted by
    /// tools/make_cockpit_trim.py as annunciators.png, all lit) is redrawn at runtime, each lamp dark until its condition
    /// is met, on the local player's B-52. Other B-52s (seen through their windows) show the row dark.
    /// Lamps, left to right, in four groups of four (colours as painted: red warnings at the ends, amber cautions, green
    /// advisories):
    ///   FIRE (a tank burning) · ENG (an engine out) · HYD L · GEAR DOWN
    ///   HYD R · FUEL LOW (under 15%) · CHUTE · SPD BRK
    ///   GEAR (in transit, or up while slow and low) · BAY (doors open) · LEAK (a tank leaking) · CRAB (trucks turned)
    ///   ALL ENG (all eight running) · LOW ALT (gear up, under 150 m, descending) · DAMAGE (a part under half strength) · PART LOST
    /// The emission follows CockpitLighting (it dims these with the displays): the material copy keeps the shared one's
    /// name plus " (Instance)", which CockpitLighting keys on.
    /// </summary>
    public class Annunciators : MonoBehaviour
    {
        private const int W = 1024, H = 64, LampW = 52, Gap = 8, GroupGap = 24, X0 = 14;
        private static readonly char[] Colour = "raagaagaagaagaar".ToCharArray();
        private static readonly Dictionary<char, Color> Lit = new Dictionary<char, Color>
        {
            ['a'] = new Color(0.62f, 0.40f, 0.08f), ['g'] = new Color(0.14f, 0.50f, 0.20f), ['r'] = new Color(0.62f, 0.12f, 0.08f)
        };
        private static readonly AccessTools.FieldRef<FuelTank, bool> TankFire = AccessTools.FieldRefAccess<FuelTank, bool>("onFire");
        private static readonly AccessTools.FieldRef<FuelTank, bool> TankLeak = AccessTools.FieldRefAccess<FuelTank, bool>("isLeaking");
        private static readonly AccessTools.FieldRef<Turbofan, bool> Operable = AccessTools.FieldRefAccess<Turbofan, bool>("operable");
        private static readonly AccessTools.FieldRef<Airbrake, float> Brake = AccessTools.FieldRefAccess<Airbrake, float>("openAmount");
        private static readonly AccessTools.FieldRef<BayDoor, float> DoorOpen = AccessTools.FieldRefAccess<BayDoor, float>("openAmount");
        private static readonly AccessTools.FieldRef<DragChute, bool> ChuteOut = AccessTools.FieldRefAccess<DragChute, bool>("deployed");

        private Aircraft aircraft;
        private Texture2D tex;
        private Color32[] px;
        private readonly bool[] on = new bool[16];
        private bool drawn;
        private float next;
        private Turbofan[] engines;
        private Airbrake airbrake;
        private BayDoor[] doors;
        private DragChute chute;
        private Hydraulics hyd;
        private UnitPart[] parts;
        private readonly Dictionary<UnitPart, float> fullHp = new Dictionary<UnitPart, float>();

        private void Start()
        {
            aircraft = GetComponent<Aircraft>();
            Material copy = null;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials; bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (!mats[i] || !mats[i].name.Contains("CP_MFD_Annunciators")) continue;
                    if (!copy)
                    {
                        copy = new Material(mats[i]) { name = mats[i].name.Replace(" (Instance)", "") + " (Instance)" };
                        tex = new Texture2D(W, H, TextureFormat.RGB24, true) { name = "B52_annunciators_live", anisoLevel = 4 };
                        px = new Color32[W * H];
                        copy.SetTexture("_BaseMap", tex);
                        if (copy.HasProperty("_EmissionMap")) copy.SetTexture("_EmissionMap", tex);
                    }
                    mats[i] = copy; changed = true;
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (!copy) { enabled = false; return; }
            engines = GetComponentsInChildren<Turbofan>(true);
            airbrake = GetComponent<Airbrake>() ?? GetComponentInChildren<Airbrake>(true);
            doors = GetComponentsInChildren<BayDoor>(true);
            chute = GetComponent<DragChute>();
            hyd = GetComponent<Hydraulics>();
            parts = GetComponentsInChildren<UnitPart>(true);
            foreach (var p in parts) fullHp[p] = Mathf.Max(p.hitPoints, 1f);
            Draw();
        }

        private void OnDestroy() { if (tex) Destroy(tex); }

        private bool Mine()
        {
            var hud = SceneSingleton<CombatHUD>.i;
            return hud != null && hud.aircraft == aircraft;
        }

        private void Update()
        {
            if (!aircraft || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.4f;
            bool mine = Mine();
            var s = new bool[16];
            if (mine && !aircraft.disabled)
            {
                var tanks = aircraft.GetFuelTanks();
                s[0] = tanks.Any(t => t && TankFire(t));
                s[1] = engines.Any(e => e && !Operable(e));
                s[2] = hyd && hyd.SideLocked(0);
                s[3] = aircraft.gearState == LandingGear.GearState.LockedExtended;
                s[4] = hyd && hyd.SideLocked(1);
                s[5] = aircraft.GetFuelLevel() < 0.15f;
                s[6] = chute && ChuteOut(chute);
                s[7] = airbrake && Brake(airbrake) > 0.1f;
                bool transit = aircraft.gearState == LandingGear.GearState.Extending || aircraft.gearState == LandingGear.GearState.Retracting;
                bool upSlowLow = aircraft.gearState == LandingGear.GearState.LockedRetracted && aircraft.radarAlt < 300f && aircraft.speed < 90f;
                s[8] = transit || upSlowLow;
                s[9] = doors.Any(d => d && DoorOpen(d) > 0.05f);
                s[10] = tanks.Any(t => t && TankLeak(t));
                s[11] = Mathf.Abs(GearSystem.CrabSelected) > 0.1f;
                s[12] = engines.Length > 0 && engines.All(e => e && Operable(e));
                s[13] = aircraft.gearState == LandingGear.GearState.LockedRetracted && aircraft.radarAlt < 150f && aircraft.rb && aircraft.rb.velocity.y < -2f;
                s[14] = parts.Any(p => p && !p.IsDetached() && fullHp.TryGetValue(p, out var hp) && p.hitPoints < 0.5f * hp);
                s[15] = aircraft.partDamageTracker != null && aircraft.partDamageTracker.GetDetachedRatio() > 0f;
            }
            bool change = !drawn;
            for (int i = 0; i < 16; i++) if (s[i] != on[i]) { on[i] = s[i]; change = true; }
            if (change) Draw();
        }

        private void Draw()
        {
            drawn = true;
            var bg = new Color32(13, 13, 13, 255);
            for (int i = 0; i < px.Length; i++) px[i] = bg;
            int x = X0;
            for (int k = 0; k < 16; k++)
            {
                if (k > 0 && k % 4 == 0) x += GroupGap;
                var c = Lit[Colour[k]];
                Color face = on[k] ? c : c * 0.10f, lens = on[k] ? c * 0.55f : c * 0.06f;
                Color legend = on[k] ? new Color(Mathf.Min(c.r * 1.6f + 0.15f, 1f), Mathf.Min(c.g * 1.6f + 0.15f, 1f), Mathf.Min(c.b * 1.6f + 0.15f, 1f)) : c * 0.18f;
                Fill(x, 10, x + LampW, H - 10, lens);
                Fill(x + 2, 12, x + LampW - 2, H - 12, face);
                Fill(x + 6, 24, x + 6 + (int)((LampW - 12) * 0.7f), 28, legend);
                Fill(x + 6, 34, x + 6 + (int)((LampW - 12) * 0.5f), 38, legend);
                x += LampW + Gap;
            }
            tex.SetPixels32(px);
            tex.Apply(true);
        }

        // rows counted from the top, as in the painted strip (textures run bottom-up)
        private void Fill(int x0, int y0, int x1, int y1, Color c)
        {
            var c32 = (Color32)c;
            for (int y = y0; y < y1; y++)
            {
                int row = (H - 1 - y) * W;
                for (int x = x0; x < x1 && x < W; x++) px[row + x] = c32;
            }
        }
    }
}
