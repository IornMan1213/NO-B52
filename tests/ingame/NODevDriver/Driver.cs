using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace NODevDriverB52
{
    /// <summary>Dev-only (never shipped): the B-52 copy of the MiG-29 pack's test driver (own GUID and command file, so both can be
    /// installed). Drives the game's UI from BepInEx/nodev_b52_cmd.txt, one command per line, for unattended tests.
    /// Commands: buttons | click &lt;label&gt; | clickat &lt;x&gt; &lt;y&gt; (screen px, top-left origin) | wait &lt;s&gt; | shot &lt;file&gt; | b52check | say | quit
    /// and the measuring commands below. tests/ingame/run.sh feeds it a scenario and collects the "[TEST]" lines.</summary>
    // While a test flies the player's jet, it holds the stick and throttle: the game's player pilot (PilotPlayerState) writes the
    // real stick or mouse into the control inputs every physics step, and its own throttle (simulatedThrottle) every frame.
    [HarmonyLib.HarmonyPatch]
    static class StickHold
    {
        internal static Vector3? Held;   // the game's pitch, roll, yaw inputs (-1..1; pitch + is nose DOWN), or null to give the stick back
        internal static float? Throttle;   // 0..1; the player's throttle is left there when the hold ends

        static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (var name in new[] { "PlayerControls", "PlayerAxisControls" })
            {
                var m = HarmonyLib.AccessTools.Method(typeof(PilotPlayerState), name);
                if (m != null) yield return m;
            }
        }

        static readonly System.Reflection.FieldInfo inputsField = HarmonyLib.AccessTools.Field(typeof(PilotBaseState), "controlInputs");
        static readonly System.Reflection.FieldInfo throttleField = HarmonyLib.AccessTools.Field(typeof(PilotPlayerState), "simulatedThrottle");

        static void Postfix(PilotPlayerState __instance)
        {
            var inp = inputsField?.GetValue(__instance) as ControlInputs;
            if (Held is Vector3 s)
            {
                __instance.pitchInput = s.x; __instance.rollInput = s.y; __instance.yawInput = s.z;
                if (inp != null) { inp.pitch = s.x; inp.roll = s.y; inp.yaw = s.z; }
            }
            if (Throttle is float t) { throttleField?.SetValue(__instance, t); if (inp != null) inp.throttle = t; }
        }
    }

    [BepInPlugin("iornman.nodevdriver.b52", "NO Dev Driver B-52", "0.0.1")]
    public class Driver : BaseUnityPlugin
    {
        string CmdFile => Path.Combine(Paths.BepInExRootPath, "nodev_b52_cmd.txt");   // its own file: the MiG pack's driver reads nodev_cmd.txt
        readonly Queue<string> queue = new Queue<string>();
        float next;

        void Awake()
        {
            new HarmonyLib.Harmony("nodev.driver.b52").PatchAll(typeof(StickHold));
            var go = new GameObject("NODevDriverB52"); DontDestroyOnLoad(go); go.hideFlags = HideFlags.HideAndDontSave;
            go.AddComponent<Runner>().owner = this;
        }

        class Runner : MonoBehaviour
        {
            internal Driver owner;
            void Update() { owner.HoldLook(); B52Check.Tick(owner.Test); owner.Tick(); }
        }

        // "look": the pilot's head held at a pan/tilt (degrees) every frame, so a following "shot" shows the real cockpit
        // view (the game's cockpit overlay camera and exposure) in that direction
        Vector2? lookHeld;
        void HoldLook()
        {
            if (lookHeld == null) return;
            var csm = FindObjectOfType<CameraStateManager>();
            if (csm == null) return;
            csm.panView = lookHeld.Value.x; csm.tiltView = lookHeld.Value.y;
            var st = csm.cockpitState;
            if (st == null) return;
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(CameraCockpitState).GetField("panView", bf)?.SetValue(st, lookHeld.Value.x);
            typeof(CameraCockpitState).GetField("tiltView", bf)?.SetValue(st, lookHeld.Value.y);
        }

        void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 0.5f;
            if (queue.Count == 0 && File.Exists(CmdFile))
            {
                foreach (var l in File.ReadAllLines(CmdFile)) if (l.Trim().Length > 0) queue.Enqueue(l.Trim());
                File.Delete(CmdFile);
            }
            if (queue.Count == 0) return;
            var cmd = queue.Dequeue();
            try { Run(cmd); } catch (System.Exception e) { Logger.LogError($"{cmd}: {e}"); }
        }

        static string Label(Component b)
        {
            var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text.Trim();
            var t = b.GetComponentInChildren<Text>(true);
            return t != null ? t.text.Trim() : b.name;
        }

        System.Collections.IEnumerator Fps(float secs)
        {
            int frames = 0; float t0 = Time.unscaledTime, worst = 0f;
            while (Time.unscaledTime - t0 < secs) { yield return null; frames++; worst = Mathf.Max(worst, Time.unscaledDeltaTime); }
            float dt = (Time.unscaledTime - t0) / frames;
            Logger.LogInfo($"  fps {1f / dt:F1} ({dt * 1000f:F2} ms/frame, worst {worst * 1000f:F1} ms) over {frames} frames");
        }

        System.Collections.IEnumerator GearLog(float secs)
        {
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var gears = FindObjectsOfType<MonoBehaviour>().Where(m => m.GetType().Name == "LandingGear").ToList();
            var peak = gears.ToDictionary(g => g, g => 0f); var peakHinge = gears.ToDictionary(g => g, g => 0f);
            var was = gears.ToDictionary(g => g, g => g.enabled);
            float t0 = Time.time;
            while (Time.time - t0 < secs)
            {
                yield return new WaitForFixedUpdate();
                foreach (var g in gears)
                {
                    if (g == null) continue;
                    var ty = g.GetType();
                    float c = (float)ty.GetField("compressionDistance", bf).GetValue(g);
                    var hinge = ty.GetField("gearHinge", bf).GetValue(g) as Component;
                    float hx = hinge != null ? hinge.transform.localEulerAngles.x : 0f;
                    if (c > peak[g]) peak[g] = c;
                    if (Mathf.Abs(Mathf.DeltaAngle(0f, hx)) > Mathf.Abs(Mathf.DeltaAngle(0f, peakHinge[g]))) peakHinge[g] = hx;
                    if (was[g] && !g.enabled) { Logger.LogInfo($"  BROKE {g.name} at t={Time.time - t0:F2}: comp {c:F3} hingeX {hx:F2}"); was[g] = false; }
                }
            }
            foreach (var g in gears) if (g != null) Logger.LogInfo($"  {g.name}: peak comp {peak[g]:F3}, peak hingeX {peakHinge[g]:F2}, enabled {g.enabled}");
        }

        void Run(string cmd)
        {
            var parts = cmd.Split(new[] { ' ' }, 2);
            string verb = parts[0].ToLowerInvariant(), arg = parts.Length > 1 ? parts[1] : "";
            Logger.LogInfo($"> {cmd}");
            switch (verb)
            {
                case "buttons":
                    foreach (var b in FindObjectsOfType<Button>().Where(b => b.isActiveAndEnabled && b.interactable))
                    {
                        var p = RectTransformUtility.WorldToScreenPoint(null, b.transform.position);
                        Logger.LogInfo($"  button '{Label(b)}' ({b.name}) at {p.x:F0},{Screen.height - p.y:F0}");
                    }
                    break;
                case "click":
                    var hit = FindObjectsOfType<Button>().Where(b => b.isActiveAndEnabled && b.interactable)
                        .FirstOrDefault(b => Label(b).ToLowerInvariant().Contains(arg.ToLowerInvariant()) || b.name.ToLowerInvariant().Contains(arg.ToLowerInvariant()));
                    if (hit == null) { Logger.LogWarning($"  no button '{arg}'"); break; }
                    Logger.LogInfo($"  clicking '{Label(hit)}' ({hit.name})");
                    hit.onClick.Invoke();
                    break;
                case "clickat":
                    var xy = arg.Split(' ');
                    var ped = new PointerEventData(EventSystem.current) { position = new Vector2(float.Parse(xy[0]), Screen.height - float.Parse(xy[1])), button = PointerEventData.InputButton.Left };
                    var results = new List<RaycastResult>();
                    EventSystem.current.RaycastAll(ped, results);
                    if (results.Count == 0) { Logger.LogWarning("  nothing there"); break; }
                    var go = results[0].gameObject;
                    ped.pointerPressRaycast = results[0]; ped.pointerCurrentRaycast = results[0];
                    var down = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerUpHandler);
                    var clicked = ExecuteEvents.ExecuteHierarchy(go, ped, ExecuteEvents.pointerClickHandler);
                    Logger.LogInfo($"  hit {go.name} (down {(down != null ? down.name : "-")}, click {(clicked != null ? clicked.name : "-")})");
                    break;
                case "find":   // active UI objects whose name contains arg, with screen positions and any label
                    foreach (var rt in FindObjectsOfType<RectTransform>().Where(r => r.gameObject.activeInHierarchy && r.name.ToLowerInvariant().Contains(arg.ToLowerInvariant())))
                    {
                        var p = RectTransformUtility.WorldToScreenPoint(null, rt.position);
                        var tmp = rt.GetComponentInChildren<TMPro.TMP_Text>();
                        Logger.LogInfo($"  {rt.name} at {p.x:F0},{Screen.height - p.y:F0} '{(tmp != null ? tmp.text : "")}'");
                    }
                    break;
                case "inspect":   // fields of every component on active objects named arg
                    foreach (var go2 in FindObjectsOfType<Transform>().Where(t => t.gameObject.activeInHierarchy && t.name.ToLowerInvariant().Contains(arg.ToLowerInvariant())).Select(t => t.gameObject))
                        foreach (var comp in go2.GetComponents<Component>())
                        {
                            var ty = comp.GetType();
                            if (ty.Namespace != null && ty.Namespace.StartsWith("UnityEngine")) continue;
                            var vals = ty.GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                                .Select(fi => { object v = null; try { v = fi.GetValue(comp); } catch { } return $"{fi.Name}={(v is Object uo ? (uo != null ? uo.name : "null") : v)}"; });
                            Logger.LogInfo($"  {go2.name}/{ty.Name}: {string.Join(", ", vals)}");
                        }
                    break;
                case "base":   // click the map icon of the airbase whose name contains arg
                    foreach (var icon in FindObjectsOfType<MonoBehaviour>().Where(m => m.GetType().Name == "AirbaseMapIcon"))
                    {
                        var fi = icon.GetType().GetField("<airbase>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var ab = fi?.GetValue(icon) as Object;
                        Logger.LogInfo($"  icon {(ab != null ? ab.name : "?")}");
                        if (ab == null || !ab.name.ToLowerInvariant().Contains(arg.ToLowerInvariant())) continue;
                        var e = new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left };
                        var r = ExecuteEvents.ExecuteHierarchy(icon.gameObject, e, ExecuteEvents.pointerClickHandler);
                        Logger.LogInfo($"  clicked {ab.name} ({(r != null ? r.name : "no handler")})");
                        break;
                    }
                    break;
                case "configmgr":   // open (true) or close BepInEx Configuration Manager's window
                    foreach (var pl in BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Where(i => i.Metadata.GUID == "com.bepis.bepinex.configurationmanager").Select(i => (object)i.Instance))
                    {
                        var prop = pl.GetType().GetProperty("DisplayingWindow");
                        prop?.SetValue(pl, arg.Trim() != "false", null);
                        Logger.LogInfo($"  ConfigurationManager.DisplayingWindow = {prop?.GetValue(pl, null)}");
                    }
                    break;
                case "cmsearch":   // type into Configuration Manager's search box
                    foreach (var pl in BepInEx.Bootstrap.Chainloader.PluginInfos.Values.Where(i => i.Metadata.GUID == "com.bepis.bepinex.configurationmanager").Select(i => (object)i.Instance))
                    {
                        var prop = pl.GetType().GetProperty("SearchString", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                        if (prop != null) prop.SetValue(pl, arg, null);
                        Logger.LogInfo($"  search = {(prop != null ? prop.GetValue(pl, null) : "no SearchString property")}");
                    }
                    break;
                case "mouse":   // where Unity thinks the cursor is
                    Logger.LogInfo($"  mouse {Input.mousePosition}");
                    break;
                case "renderers":   // renderers whose name or parent's name contains arg: state and bounds
                    foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>().Where(r => r.gameObject.scene.IsValid() &&
                             (r.name.ToLowerInvariant().Contains(arg.ToLowerInvariant()) || (r.transform.parent != null && r.transform.parent.name.ToLowerInvariant().Contains(arg.ToLowerInvariant())))))
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        Logger.LogInfo($"  {r.transform.parent?.parent?.name}/{r.transform.parent?.name}/{r.name} active={r.gameObject.activeInHierarchy} enabled={r.enabled} mesh={(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")} c={r.bounds.center:F2} s={r.bounds.size:F2} layer={r.gameObject.layer}");
                    }
                    break;
                case "view":   // view <x y z> <lx ly lz> <fov> <file>: camera at a point in the player's aircraft frame, looking at another (negative fov: orthographic half-height)
                {
                    var a = arg.Split(' ');
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    var fr = ac.transform;
                    var from = fr.TransformPoint(new Vector3(F(0), F(1), F(2))); var to = fr.TransformPoint(new Vector3(F(3), F(4), F(5)));
                    var go3 = new GameObject("devcam"); var cam = go3.AddComponent<Camera>();
                    cam.transform.position = from; cam.transform.LookAt(to, fr.up);
                    cam.fieldOfView = Mathf.Abs(F(6)); cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f;
                    if (F(6) < 0f) { cam.orthographic = true; cam.orthographicSize = -F(6); cam.farClipPlane = 1.5f * Vector3.Distance(from, to); }
                    var main = Camera.main; if (main != null) cam.cullingMask = main.cullingMask | (1 << 3) | (1 << 14);   // + the cockpit interior layers
                    var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath, a[7]), tex.EncodeToPNG());
                    Destroy(tex); rt.Release(); Destroy(rt); Destroy(go3);
                    Logger.LogInfo($"  view -> {a[7]}");
                    break;
                }
                case "pview":   // pview <x y z> <lx ly lz> <fov> <file>: like view, but through the game's own camera (its post-processing and exposure), i.e. what the player sees
                {
                    var a = arg.Split(' ');
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    var main = Camera.main;
                    if (ac == null || main == null) { Logger.LogWarning("  no aircraft or main camera"); break; }
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    var fr = ac.transform;
                    var from = fr.TransformPoint(new Vector3(F(0), F(1), F(2))); var to = fr.TransformPoint(new Vector3(F(3), F(4), F(5)));
                    var pos0 = main.transform.position; var rot0 = main.transform.rotation; var fov0 = main.fieldOfView; var tt0 = main.targetTexture;
                    main.transform.position = from; main.transform.LookAt(to, fr.up); main.fieldOfView = F(6);
                    var rt = new RenderTexture(1600, 1000, 24); main.targetTexture = rt; main.Render();
                    RenderTexture.active = rt; var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply(); RenderTexture.active = null;
                    main.targetTexture = tt0; main.transform.SetPositionAndRotation(pos0, rot0); main.fieldOfView = fov0;
                    File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath, a[7]), tex.EncodeToPNG());
                    Destroy(tex); rt.Release(); Destroy(rt);
                    Logger.LogInfo($"  pview -> {a[7]}");
                    break;
                }
                case "look":   // look <pan deg> <tilt deg> | look off: hold the pilot's head there (for "shot"); look off lets the mouse have it back
                {
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    lookHeld = arg == "off" ? (Vector2?)null : new Vector2(F(0), F(1));
                    break;
                }
                case "campos":   // campos: every enabled camera's position, forward, fov, near plane and culling layers, in the player's aircraft frame
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    foreach (var c in Camera.allCameras)
                    {
                        var lp = ac.transform.InverseTransformPoint(c.transform.position); var lf = ac.transform.InverseTransformDirection(c.transform.forward);
                        var layers = string.Join(",", Enumerable.Range(0, 32).Where(i => (c.cullingMask & (1 << i)) != 0));
                        Logger.LogInfo($"  {c.name}: pos {lp:F3} fwd {lf:F2} fov {c.fieldOfView:F1} near {c.nearClipPlane:F3} depth {c.depth} layers [{layers}]");
                    }
                    break;
                }
                case "pos":   // pos <substring>: aircraft-frame positions of the player's aircraft's transforms whose name contains it
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var all = FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac).SelectMany(p => p.GetComponentsInChildren<Transform>(true)).Concat(ac.GetComponentsInChildren<Transform>(true)).Distinct();
                    foreach (var t in all.Where(x => x.name.ToLowerInvariant().Contains(arg.ToLowerInvariant())))
                        Logger.LogInfo($"  {t.name}: {ac.transform.InverseTransformPoint(t.position):F3} active={t.gameObject.activeInHierarchy}");
                    break;
                }
                case "camat":   // camat <transform|@ac> <dx> <dy> <dz> <fov> <file>: render from offset (aircraft frame) looking at the transform (@ac: the player's jet)
                {
                    var a = arg.Split(' ');
                    var cHud = SceneSingleton<CombatHUD>.i;
                    var own = cHud != null && cHud.aircraft != null ? cHud.aircraft.transform : null;
                    // a part name means the player's own aircraft first (other aircraft share names like cockpit, wingtip_L)
                    var t = a[0] == "@ac" ? own
                          : (own != null ? own.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == a[0]) : null)
                            ?? Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy && x.name == a[0]);
                    if (t == null) { Logger.LogWarning("  no transform " + a[0]); break; }
                    var frame = t.root;
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    // In cockpit view the game hides the aircraft's exterior renderers: show them for the shot.
                    var shown = new List<Renderer>();
                    var acx = cHud != null ? cHud.aircraft : null;
                    if (acx != null && typeof(Aircraft).GetField("exteriorRenderers", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(acx) is Renderer[] ext)
                        foreach (var r in ext) if (r && !r.enabled) { r.enabled = true; shown.Add(r); }
                    var go3 = new GameObject("devcam"); var cam = go3.AddComponent<Camera>();
                    cam.transform.position = t.position + frame.TransformDirection(new Vector3(F(1), F(2), F(3)));
                    cam.transform.LookAt(t.position, frame.up);
                    cam.fieldOfView = Mathf.Abs(F(4)); cam.nearClipPlane = 0.05f; cam.farClipPlane = 500f;
                    if (F(4) < 0f) { cam.orthographic = true; cam.orthographicSize = -F(4); cam.farClipPlane = 1.2f * new Vector3(F(1), F(2), F(3)).magnitude; }   // negative fov: orthographic, half-height in m, sees only to just past the target
                    var main = Camera.main; if (main != null) cam.cullingMask = main.cullingMask;
                    var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath, a[5]), tex.EncodeToPNG());
                    Destroy(tex); rt.Release(); Destroy(rt); Destroy(go3);
                    foreach (var r in shown) if (r) r.enabled = false;
                    Logger.LogInfo($"  camat {t.name} at {t.position:F3} (root {frame.name}) -> {a[5]}");
                    break;
                }
                case "dropdowns":   // list TMP dropdowns and their options
                {
                    var dds = FindObjectsOfType<TMPro.TMP_Dropdown>().Where(d => d.isActiveAndEnabled).OrderBy(d => d.transform.position.x).ToList();
                    for (int i = 0; i < dds.Count; i++)
                        Logger.LogInfo($"  dropdown {i} {dds[i].name} value={dds[i].value}: {string.Join(" | ", dds[i].options.Select((o, k) => k + "=" + o.text))}");
                    break;
                }
                case "pick":   // pick <dropdown index> <option index>
                {
                    var a = arg.Split(' ');
                    var dds = FindObjectsOfType<TMPro.TMP_Dropdown>().Where(d => d.isActiveAndEnabled).OrderBy(d => d.transform.position.x).ToList();
                    var dd = dds[int.Parse(a[0])]; dd.value = int.Parse(a[1]);
                    Logger.LogInfo($"  {dd.name} = {dd.options[dd.value].text}");
                    break;
                }
                case "mounts":   // what hangs under each hardpoint: renderer bounds relative to the hardpoint, in its frame
                    foreach (var hp in Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid() && t.gameObject.activeInHierarchy && t.name.StartsWith("hardpoint_")))
                        foreach (var r in hp.GetComponentsInChildren<Renderer>().Where(r => r.enabled))
                        {
                            var mf = r.GetComponent<MeshFilter>();
                            if (mf == null || mf.sharedMesh == null) continue;
                            var b = mf.sharedMesh.bounds; float top = float.MinValue;
                            foreach (var c in new[] { new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1) })
                                top = Mathf.Max(top, hp.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents, c))).y);
                            Logger.LogInfo($"  {hp.name} / {r.transform.parent?.name}/{r.name}: top {top:F3} m from the hardpoint");
                        }
                    break;
                case "fps":   // average frame time over the next N seconds (default 3)
                    StartCoroutine(Fps(arg.Length > 0 ? float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) : 3f));
                    break;
                case "config":   // config <plugin guid>|<section>|<key>|<value>
                {
                    var a = arg.Split('|');
                    var info = BepInEx.Bootstrap.Chainloader.PluginInfos[a[0]];
                    var entry = info.Instance.Config[a[1], a[2]];
                    entry.BoxedValue = TomlTypeConverter.ConvertToValue(a[3], entry.SettingType);
                    Logger.LogInfo($"  {a[0]} {a[1]}/{a[2]} = {entry.BoxedValue}");
                    break;
                }
                case "gear":   // each LandingGear: compression, max, hinge local euler, enabled
                {
                    var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    foreach (var lg in FindObjectsOfType<MonoBehaviour>().Where(m => m.GetType().Name == "LandingGear"))
                    {
                        var ty = lg.GetType();
                        object G(string n) { var fi = ty.GetField(n, bf); return fi != null ? fi.GetValue(lg) : "?"; }
                        var hinge = G("gearHinge") as Component;
                        Logger.LogInfo($"  {lg.name} enabled={lg.enabled} comp={G("compressionDistance")} max={G("maxCompression")} fold={G("foldAmount")} travel={G("suspensionTravel")} force={G("compressionForce")} damp={G("dampingForce")} hinge={(hinge != null ? hinge.name + " " + hinge.transform.localEulerAngles.ToString("F2") : "null")}");
                    }
                    break;
                }
                case "drop":   // drop <height m> <sink m/s> [forward m/s]: lift the player's jet and drop it, logging the gear for 4 s
                {
                    var a = arg.Split(' ');
                    float h = float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), vz = float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture);
                    float fwd = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var rbs = FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac).Select(p => p.rb).Where(r => r != null).Distinct().ToList();
                    foreach (var r in rbs) { r.isKinematic = false; r.position += Vector3.up * h; r.velocity = Vector3.down * vz + ac.transform.forward * fwd; r.angularVelocity = Vector3.zero; }
                    Logger.LogInfo($"  dropped {rbs.Count} bodies {h} m at {vz} m/s");
                    StartCoroutine(GearLog(4f));
                    break;
                }
                case "hold":   // hold <height m>: lift the player's jet and freeze it (kinematic) there; "hold 0" releases it
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    float h = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var r in FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac).Select(p => p.rb).Where(r => r != null).Distinct())
                    {
                        if (h > 0f) { r.isKinematic = true; r.position += Vector3.up * h; r.transform.position += Vector3.up * h; }
                        else { r.isKinematic = false; r.velocity = Vector3.zero; }
                    }
                    Logger.LogInfo($"  hold {h}");
                    break;
                }
                case "move":   // move <dy m> [forward m]: shift the (held) player's jet up or down (and along its heading), staying kinematic
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var mv = arg.Split(' ');
                    float dy = float.Parse(mv[0], System.Globalization.CultureInfo.InvariantCulture);
                    float dz = mv.Length > 1 ? float.Parse(mv[1], System.Globalization.CultureInfo.InvariantCulture) : 0f;
                    var fw = Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up).normalized;
                    var d = Vector3.up * dy + fw * dz;
                    foreach (var r in FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac).Select(p => p.rb).Where(r => r != null).Distinct())
                    { r.position += d; r.transform.position += d; }
                    Logger.LogInfo($"  moved {dy}, radar alt {ac.radarAlt:F1}");
                    break;
                }
                case "dmgtex":   // dmgtex <basecolorDmg png|-> [normalDmg png|-]: replace the damage textures on the player's jet's skin materials (runtime only)
                case "skinhp":   // skinhp <v>: set _HitPoints on the player's jet's skin materials (look only, no damage)
                case "skinprop":   // skinprop <name> <float>: any float on those materials
                case "skintex":    // skintex <texture property> <png>: any texture on those materials (normal maps load linear)
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    Texture2D Load(string p, bool lin)
                    {
                        if (p == "-" ) return null;
                        var t = new Texture2D(2, 2, TextureFormat.RGBA32, true, lin);
                        ImageConversion.LoadImage(t, System.IO.File.ReadAllBytes(p));
                        return t;
                    }
                    Texture2D b = null, n = null;
                    if (verb == "dmgtex") { b = Load(a[0], false); if (a.Length > 1) n = Load(a[1], true); }
                    if (verb == "skintex") b = Load(a[1], a[0].Contains("Normal"));
                    int count = 0;
                    foreach (var r in FindObjectsOfType<Renderer>())   // complex physics re-parents the parts
                    {
                        var m = r.sharedMaterial;
                        if (m == null || !m.HasProperty("_HitPoints") || !r.name.StartsWith("MiG29_skin")) continue;
                        m = r.material; count++;
                        if (verb == "dmgtex") { if (b != null) m.SetTexture("_BasecolorDmg", b); if (n != null) m.SetTexture("_NormalDmg", n); }
                        else if (verb == "skintex") m.SetTexture(a[0], b);
                        else if (verb == "skinhp") m.SetFloat("_HitPoints", float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture));
                        else m.SetFloat(a[0], float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture));
                    }
                    Logger.LogInfo($"  {verb} {arg}: {count} skin renderers");
                    break;
                }
                case "hurt":      // hurt <part substring|all> <amount>: ApplyDamage on the player's jet's parts (shading/effects, no detaching)
                case "partvis":   // partvis <part substring> on|off: show/hide every renderer of the matching parts (to see the seams they leave)
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    var sel = FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac && (a[0] == "all" || p.name == a[0] || (a[0].EndsWith("*") && p.name.StartsWith(a[0].TrimEnd('*'))))).ToList();
                    foreach (var p in sel)
                    {
                        if (verb == "hurt") p.ApplyDamage(float.Parse(a[1], System.Globalization.CultureInfo.InvariantCulture), 0f, 0f, 0f);
                        else foreach (var r in p.GetComponentsInChildren<Renderer>(true).Where(r => r.GetComponentInParent<UnitPart>() == p)) r.enabled = a[1] == "on";
                    }
                    Logger.LogInfo($"  {verb} {arg}: {sel.Count} parts ({string.Join(",", sel.Select(p => p.name))})");
                    break;
                }
                case "fire":   // fire <from x y z> <to x y z> [damage]: a ray in the aircraft frame; the first collider it hits goes to the MiG plugin's Holes.Hit (as a round would), and its part takes the damage
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    var from = ac.transform.TransformPoint(new Vector3(F(0), F(1), F(2))); var to = ac.transform.TransformPoint(new Vector3(F(3), F(4), F(5)));
                    var dir = (to - from).normalized;
                    if (!Physics.Raycast(from, dir, out var fhit, (to - from).magnitude + 5f)) { Logger.LogWarning("  fire: no hit"); break; }
                    var holes = System.AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("MiG29Instruments.Holes")).FirstOrDefault(x => x != null);
                    var m = holes?.GetMethod("Hit", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    m?.Invoke(null, new object[] { fhit.collider, fhit.point, dir, 0.045f });
                    var part = fhit.collider.GetComponentInParent<UnitPart>();
                    if (part != null && a.Length > 6) part.ApplyDamage(F(6), 0f, 0f, 0f);
                    Logger.LogInfo($"  fire: hit {fhit.collider.name} part {part?.name} at {ac.transform.InverseTransformPoint(fhit.point):F2} holes {(m != null)}");
                    break;
                }
                case "rvis":   // rvis <name prefix> on|off: show/hide the player's jet's renderers whose name starts with the prefix
                {
                    var a = arg.Split(' ');
                    int nr = 0;
                    foreach (var r in FindObjectsOfType<Renderer>(true)) if (r.name.StartsWith(a[0]) && r.GetComponentInParent<UnitPart>() != null) { r.enabled = a[1] == "on"; nr++; }
                    Logger.LogInfo($"  rvis {arg}: {nr} renderers");
                    break;
                }
                case "rnear":   // rnear <x y z> <radius>: the player's jet's renderers whose bounds come within radius of that point (aircraft frame)
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    var w = ac.transform.TransformPoint(new Vector3(F(0), F(1), F(2)));
                    foreach (var r in FindObjectsOfType<Renderer>().Where(r => r.enabled && r.GetComponentInParent<UnitPart>()?.parentUnit == ac && r.bounds.SqrDistance(w) < F(3) * F(3)))
                        Logger.LogInfo($"    {r.name} (part {r.GetComponentInParent<UnitPart>().name}) mat {r.sharedMaterial?.name} size {r.bounds.size:F2}");
                    break;
                }
                case "rabove":   // rabove <y>: the player's jet's enabled renderers reaching above that height (aircraft frame), with their extents
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    float y0 = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    foreach (var r in FindObjectsOfType<Renderer>().Where(r => r.enabled && (r.GetComponentInParent<UnitPart>()?.parentUnit == ac || r.GetComponentInParent<Unit>() == ac)))
                    {
                        var b = r.bounds; var lo = ac.transform.InverseTransformPoint(b.min); var hi = ac.transform.InverseTransformPoint(b.max);
                        var top = Mathf.Max(lo.y, hi.y);
                        if (top > y0) Logger.LogInfo($"    {r.name} parent {r.transform.parent?.name} top {top:F2} centre {ac.transform.InverseTransformPoint(b.center):F2} size {b.size:F2} mat {r.sharedMaterial?.name}");
                    }
                    break;
                }
                case "wview":   // wview <object name> <dx dy dz> <fov> <file>: camera at the object's position + a world offset, looking at it (works on wrecks)
                {
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    var target = FindObjectsOfType<Transform>().FirstOrDefault(x => x.name == a[0] && x.gameObject.activeInHierarchy);
                    if (target == null) { Logger.LogWarning($"  no object {a[0]}"); break; }
                    var go3 = new GameObject("devcam"); var cam = go3.AddComponent<Camera>();
                    cam.transform.position = target.position + new Vector3(F(1), F(2), F(3)); cam.transform.LookAt(target.position, Vector3.up);
                    cam.fieldOfView = F(4); cam.nearClipPlane = 0.05f; cam.farClipPlane = 2000f;
                    var main = Camera.main; if (main != null) cam.cullingMask = main.cullingMask;
                    var rt = new RenderTexture(1600, 1000, 24); cam.targetTexture = rt; cam.Render();
                    RenderTexture.active = rt; var tex = new Texture2D(1600, 1000, TextureFormat.RGB24, false);
                    tex.ReadPixels(new Rect(0, 0, 1600, 1000), 0, 0); tex.Apply(); RenderTexture.active = null;
                    File.WriteAllBytes(Path.Combine(Paths.BepInExRootPath, a[5]), tex.EncodeToPNG());
                    Destroy(tex); rt.Release(); Destroy(rt); Destroy(go3);
                    Logger.LogInfo($"  wview {a[0]} at {target.position} -> {a[5]}");
                    break;
                }
                case "shaderprops":   // shaderprops: the skin shader's properties
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var r = ac.GetComponentsInChildren<Renderer>(true).First(x => x.name.StartsWith("MiG29_skin"));
                    var s = r.sharedMaterial.shader;
                    Logger.LogInfo($"  shader {s.name} keywords {string.Join(",", r.sharedMaterial.shaderKeywords)}");
                    for (int i = 0; i < s.GetPropertyCount(); i++) Logger.LogInfo($"  prop {s.GetPropertyName(i)} {s.GetPropertyType(i)} {s.GetPropertyDescription(i)}");
                    break;
                }
                case "cfgset":   // cfgset <plugin GUID>|<section>|<key>|<value>: sets a plugin's config entry live (SettingChanged fires)
                {
                    var a = arg.Split('|');
                    if (a.Length < 4) { Logger.LogWarning("  cfgset guid|section|key|value"); break; }
                    if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(a[0], out var pi)) { Logger.LogWarning("  no plugin " + a[0]); break; }
                    var cfg = pi.Instance.Config;
                    var def = new BepInEx.Configuration.ConfigDefinition(a[1], a[2]);
                    if (!cfg.TryGetEntry<string>(def, out var e)) { Logger.LogWarning($"  no string entry {a[1]}/{a[2]}"); break; }
                    try { e.Value = a[3]; Logger.LogInfo($"  {a[1]}/{a[2]} = {e.Value}"); } catch (System.Exception ex) { Logger.LogWarning("  " + ex.Message); }
                    break;
                }
                case "texlimit":   // texlimit <n>: QualitySettings.globalTextureMipmapLimit (0 full, 1 half, 2 quarter: the game's Texture Quality)
                    QualitySettings.globalTextureMipmapLimit = int.Parse(arg);
                    Logger.LogInfo($"  globalTextureMipmapLimit {QualitySettings.globalTextureMipmapLimit}");
                    break;
                case "lodbias":   // lodbias <v>: QualitySettings.lodBias (small values force the distance levels up close)
                    QualitySettings.lodBias = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    Logger.LogInfo($"  lodBias {QualitySettings.lodBias}");
                    break;
                case "parts":   // parts: every part of the player's jet: joints intact / total, hit points, own rigidbody or merged
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    int broken = 0, total = 0;
                    foreach (var p in FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac).OrderBy(p => p.name))
                    {
                        var joints = p.GetType().GetField("joints", bf)?.GetValue(p) as System.Array;
                        int n = joints?.Length ?? 0, ok = 0;
                        if (joints != null) foreach (var j in joints) { var jj = j.GetType().GetField("joint", bf)?.GetValue(j) as Joint; if (jj != null) ok++; }
                        total += n; broken += n - ok;
                        var hp = p.GetType().GetField("hitPoints", bf)?.GetValue(p);
                        var mp = p.transform.Find("MiG29_massprops"); Logger.LogInfo($"  {p.name}: joints {ok}/{n} hp {hp} {(p.rb != null && p.rb != ac.rb ? "own rb" : "merged")} pos {ac.transform.InverseTransformPoint(p.transform.position):F2} com {(p.rb != null ? p.rb.centerOfMass.ToString("F3") : "-")} tuned {(mp != null ? mp.localPosition.ToString("F3") : "-")}");
                    }
                    Logger.LogInfo($"  parts summary: {broken} of {total} joints broken, alt {ac.radarAlt:F1} speed {ac.speed:F1}");
                    // A rigid airframe (the B-52's default) has no joints at all: every part shares the aircraft's rigidbody.
                    // Then "intact" means no part has come off.
                    var acParts = FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac).ToList();
                    if (acParts.All(p => p.rb == null || p.rb == ac.rb))
                    {
                        int off = acParts.Count(p => p.IsDetached());
                        Test(off == 0, $"parts: rigid airframe, {off} of {acParts.Count} parts detached");
                    }
                    else Test(broken == 0, $"parts: {broken} of {total} joints broken");
                    break;
                }
                case "gtest":   // gtest <pitch deg>: player's jet to 1,500 m up at 220 m/s, then nose up by that much; logs g and how far each part moves off the root
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    hud.StartCoroutine(GTest(ac, float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)));
                    break;
                }
                case "pickac":   // pickac <definition name>: clicks the aircraft selection button for that aircraft (exact name, e.g. MiG29SMT)
                {
                    var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    bool done = false;
                    foreach (var b in FindObjectsOfType<MonoBehaviour>().Where(m => m.GetType().Name == "AircraftSelectionButton"))
                    {
                        var def = b.GetType().GetField("definition", bf)?.GetValue(b) as UnityEngine.Object;
                        if (def == null || def.name != arg) continue;
                        var btn = b.GetType().GetField("button", bf)?.GetValue(b) as UnityEngine.UI.Button;
                        btn?.onClick.Invoke();
                        Logger.LogInfo($"  picked {def.name}"); done = true; break;
                    }
                    if (!done) Logger.LogWarning("  no aircraft button for " + arg + "; have: " + string.Join(", ", FindObjectsOfType<MonoBehaviour>().Where(m => m.GetType().Name == "AircraftSelectionButton").Select(m => (m.GetType().GetField("definition", bf)?.GetValue(m) as UnityEngine.Object)?.name)));
                    break;
                }
                case "mkgauge":   // mkgauge <object name>: a small cube under the player's cockpit with that name, then NO Gauges' AttachAll
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var parent = ac.cockpit != null ? ((Component)ac.cockpit).transform : ac.transform;
                    var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Destroy(cube.GetComponent<Collider>());
                    cube.name = arg; cube.transform.SetParent(parent, false); cube.transform.localScale = Vector3.one * 0.05f;
                    var gs = System.Type.GetType("NOGauges.GaugeSystem, NOGauges");
                    var n = gs?.GetMethod("AttachAll")?.Invoke(null, new object[] { ac });
                    Logger.LogInfo($"  made {arg}; AttachAll -> {n}");
                    break;
                }
                case "gauges":   // every NO Gauges element on the player's aircraft: spec, current value, pose
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var gt = System.Type.GetType("NOGauges.Gauge, NOGauges");
                    if (gt == null) { Logger.LogWarning("  NO Gauges not loaded"); break; }
                    foreach (var g in FindObjectsOfType(gt).Cast<Component>())
                    {
                        var mv = (Transform)gt.GetProperty("Moving").GetValue(g);
                        var r = g.GetComponent<Renderer>();
                        Logger.LogInfo($"  {g.name}: {gt.GetProperty("Spec").GetValue(g)} current {gt.GetProperty("Current").GetValue(g)} euler {mv.localEulerAngles:F1} pos {mv.localPosition:F3} renderer {(r != null ? r.enabled.ToString() : "-")}");
                    }
                    break;
                }
                case "state":   // speed, radar alt, throttle, gear of the player's jet
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var inp = ac.GetInputs();
                    Logger.LogInfo($"  speed {ac.speed:F1} m/s, radar alt {ac.radarAlt:F2}, throttle {(inp != null ? inp.throttle : -1f):F2}, gear {ac.gearState}, chute {ChuteOut()}");
                    break;
                }
                case "setgear":   // setgear true|false
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    ac.SetGear(bool.Parse(arg));
                    Logger.LogInfo($"  gear {arg}, state {ac.gearState}");
                    break;
                }
                case "under":   // under <hardpoint>: every enabled renderer of the aircraft whose bounds cover the hardpoint's x/z, with its bottom height relative to the hardpoint (hardpoint frame)
                {
                    var hp = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy && x.name == arg);
                    if (hp == null) { Logger.LogWarning("  no " + arg); break; }
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    foreach (var r in FindObjectsOfType<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy && r.transform.IsChildOf(hp.root) && !r.transform.IsChildOf(hp)))
                    {
                        var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) continue;
                        var b = mf.sharedMesh.bounds; float minY = float.MaxValue, maxY = float.MinValue, minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                        foreach (var c in new[] { new Vector3(-1, -1, -1), new Vector3(1, -1, -1), new Vector3(-1, 1, -1), new Vector3(1, 1, -1), new Vector3(-1, -1, 1), new Vector3(1, -1, 1), new Vector3(-1, 1, 1), new Vector3(1, 1, 1) })
                        {
                            var p = hp.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.Scale(b.extents, c)));
                            minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); minX = Mathf.Min(minX, p.x); maxX = Mathf.Max(maxX, p.x); minZ = Mathf.Min(minZ, p.z); maxZ = Mathf.Max(maxZ, p.z);
                        }
                        if (minX > 0.15f || maxX < -0.15f || minZ > 0.5f || maxZ < -0.5f || minY > 0.6f) continue;
                        Logger.LogInfo($"  {arg} / {r.transform.parent?.name}/{r.name}: y {minY:F3}..{maxY:F3} x {minX:F2}..{maxX:F2} z {minZ:F2}..{maxZ:F2}");
                    }
                    break;
                }
                case "hps":   // every hardpoint: forward direction and euler in the aircraft's frame
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    Transform rootT = ac != null ? ac.transform : null;
                    foreach (var t in Resources.FindObjectsOfTypeAll<Transform>().Where(x => x.gameObject.scene.IsValid() && x.name.StartsWith("hardpoint_")).OrderBy(x => x.name))
                    {
                        var r = rootT != null ? rootT : t.root;
                        var rel = Quaternion.Inverse(r.rotation) * t.rotation;
                        Logger.LogInfo($"  {t.name}: fwd {r.InverseTransformDirection(t.forward):F3} up {r.InverseTransformDirection(t.up):F3} euler {rel.eulerAngles:F2} pos {r.InverseTransformPoint(t.position):F3}");
                    }
                    break;
                }
                case "stores":   // each store's mesh under a hardpoint: pitch of its forward axis relative to the aircraft (deg, + = nose up)
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    var root = ac != null ? ac.transform : null;
                    foreach (var hp in Resources.FindObjectsOfTypeAll<Transform>().Where(x => x.gameObject.scene.IsValid() && x.gameObject.activeInHierarchy && x.name.StartsWith("hardpoint_pylon")).OrderBy(x => x.name))
                        foreach (var r in hp.GetComponentsInChildren<MeshRenderer>().Where(r => r.enabled))
                        {
                            var f = (root != null ? root : hp.root).InverseTransformDirection(r.transform.forward);
                            Logger.LogInfo($"  {hp.name}/{r.transform.parent?.name}/{r.name}: pitch {Mathf.Atan2(f.y, f.z) * Mathf.Rad2Deg:F2} yaw {Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg:F2}");
                        }
                    break;
                }
                case "fuel":   // fuel <0..1>: set every tank of the player's aircraft to that fraction through FuelTank.Refuel (it sets, not adds; part masses follow), then log the mass
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    float frac = arg == "full" ? 1f : float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    int tanks = 0;
                    foreach (var tank in ac.GetFuelTanks()) { tanks++; tank.Refuel(frac); }
                    Logger.LogInfo($"  fuel: {tanks} tanks set to {frac:F2}, fuel level {ac.GetFuelLevel():F2}, rigidbody mass {(ac.rb != null ? ac.rb.mass : 0f):F0} kg (updates next physics step)");
                    break;
                }
                case "mass":   // mass: the player's aircraft's rigidbody mass and fuel level
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    Logger.LogInfo($"  mass {(ac.rb != null ? ac.rb.mass : 0f):F0} kg, fuel level {ac.GetFuelLevel():F2}");
                    break;
                }
                case "bayfit":   // bayfit [margin m]: every B-52 bay rack, loaded (inactive) on the player's B-52's bay hardpoint and measured against the bay (the bombBay mesh's box): one PASS/FAIL line per rack
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    float margin = arg.Length > 0 ? float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) : 0.05f;
                    var all = ac.GetComponentsInChildren<Transform>(true);
                    var mount = all.FirstOrDefault(t => t.name == "bayMount"); var bay = all.FirstOrDefault(t => t.name == "bombBay");
                    if (mount == null) { Logger.LogWarning("  no bayMount"); break; }
                    Bounds Box(Transform root, Transform frame)   // mesh bounds of everything under root, in frame's space
                    {
                        var b = new Bounds(); bool any = false;
                        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                        {
                            if (mf.sharedMesh == null) continue; var mb = mf.sharedMesh.bounds;
                            for (int c = 0; c < 8; c++)
                            {
                                var p = frame.InverseTransformPoint(mf.transform.TransformPoint(mb.center + Vector3.Scale(mb.extents, new Vector3((c & 1) == 0 ? -1 : 1, (c & 2) == 0 ? -1 : 1, (c & 4) == 0 ? -1 : 1))));
                                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                            }
                        }
                        return b;
                    }
                    var bayBox = bay != null && bay.GetComponentInChildren<MeshFilter>(true) != null ? Box(bay, mount) : new Bounds(Vector3.zero, new Vector3(1.7f, 1.8f, 10.3f));
                    Logger.LogInfo($"  bay box in the hardpoint's frame: min {bayBox.min:F2} max {bayBox.max:F2}{(bay == null ? " (fallback: no bombBay mesh)" : "")}");
                    var racks = Resources.FindObjectsOfTypeAll<WeaponMount>().Where(m => m.name.StartsWith("B52_") && m.name.Contains("_bayx") && m.prefab != null).OrderBy(m => m.name).ToList();
                    foreach (var m in racks)
                    {
                        var holder = new GameObject("bayfit"); holder.SetActive(false);
                        holder.transform.SetParent(mount, false);
                        var rack = Instantiate(m.prefab, holder.transform, false);
                        var rb = Box(rack.transform, mount);
                        Destroy(holder);
                        float over = Mathf.Max(Mathf.Max(bayBox.min.x - rb.min.x, rb.max.x - bayBox.max.x), Mathf.Max(bayBox.min.z - rb.min.z, rb.max.z - bayBox.max.z));
                        float roof = rb.max.y - bayBox.max.y, below = bayBox.min.y - rb.min.y;
                        Test(Mathf.Max(over, roof, below) <= margin,
                             $"bay fit {m.name}: stores min {rb.min:F2} max {rb.max:F2}; past the bay sides/ends {Mathf.Max(over, 0f):F2} m, roof {Mathf.Max(roof, 0f):F2} m, door line {Mathf.Max(below, 0f):F2} m");
                    }
                    break;
                }
                case "hud":   // hud: the B-52's HUD readout state (help card shown yet, card open, flash message, status lines); passes if the help card has shown itself once this session
                {
                    var t = HudType(); if (t == null) { Logger.LogWarning("  no HudReadout"); break; }
                    var bf = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
                    bool shown = (bool)t.GetField("cardShownOnce", bf).GetValue(null), open = (bool)t.GetField("cardOpen", bf).GetValue(null);
                    var status = t.GetField("status", bf).GetValue(null) as System.Collections.IDictionary;
                    Logger.LogInfo($"  hud: flash '{t.GetField("flash", bf).GetValue(null)}', status [{string.Join("; ", status.Values.Cast<object>())}], card open {open}");
                    Test(shown, "help card showed itself on the first flight of the session");
                    break;
                }
                case "helpcard":   // helpcard on|off: open or close the B-52's key card as Shift+F1 would (for a screenshot)
                {
                    var t = HudType(); if (t == null) { Logger.LogWarning("  no HudReadout"); break; }
                    t.GetField("cardOpen", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, arg != "off");
                    break;
                }
                case "hudwatch":   // hudwatch <seconds> <text>: passes if a B-52 HUD message containing <text> appears within that time (runs alongside the next commands)
                {
                    var a = arg.Split(new[] { ' ' }, 2);
                    StartCoroutine(HudWatch(float.Parse(a[0], System.Globalization.CultureInfo.InvariantCulture), a.Length > 1 ? a[1] : ""));
                    break;
                }
                case "spawnai":   // spawnai <aircraft jsonKey>: the heavy hangar nearest the player (else the nearest hangar) spawns an AI-flown aircraft of that type, as a mission would; "trackai" follows it
                {
                    var hud = SceneSingleton<CombatHUD>.i; var me = hud != null ? hud.aircraft : null;
                    var def = Resources.FindObjectsOfTypeAll<AircraftDefinition>().FirstOrDefault(d => d.jsonKey == arg);
                    if (def == null) { Logger.LogWarning("  no aircraft definition " + arg); break; }
                    var from = me != null ? me.transform.position : Vector3.zero;
                    var hangars = FindObjectsOfType<Hangar>().Where(h => h.attachedUnit != null && h.attachedUnit.definition != null).ToList();
                    var hangar = hangars.Where(h => h.attachedUnit.definition.jsonKey == "hangar_heavy").OrderBy(h => (h.transform.position - from).sqrMagnitude).FirstOrDefault()
                                 ?? hangars.OrderBy(h => (h.transform.position - from).sqrMagnitude).FirstOrDefault();
                    if (hangar == null) { Logger.LogWarning("  no hangar"); break; }
                    var res = hangar.TrySpawnAircraft(null, def, default(LiveryKey), null, 0.6f);
                    aiDef = def; aiSince = Time.time;
                    Logger.LogInfo($"  spawnai {def.unitName} from {hangar.attachedUnit.definition.unitName} at {hangar.parentAirbase?.name}: allowed {res.Allowed}, delayed {res.DelayedSpawn}");
                    if (!res.Allowed) StartCoroutine(SpawnAiDirect(hangar, def));   // the hangar refused player-less spawns: do what it does ourselves
                    break;
                }
                case "trackai":   // trackai <seconds>: follow the AI aircraft "spawnai" made (waits up to 60 s for it to roll out of the hangar)
                {
                    StartCoroutine(TrackAi(float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)));
                    break;
                }
                case "track":   // track <seconds>: log the player's aircraft every 2 s (speed, height, pilot state, distance moved); at the end: did it fly, and how much it bobbed in level flight
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    StartCoroutine(Track(ac, float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)));
                    break;
                }
                case "hld":   // hld: every HighLiftDevice (flaps/slats) on the player's aircraft: deployed position, speed and alpha settings, with the aircraft's speed
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                    object F(object o, string n) => o.GetType().GetField(n, bf)?.GetValue(o);
                    foreach (var h in ac.GetComponentsInChildren<HighLiftDevice>(true))
                        Logger.LogInfo($"  hld {h.name}: position {F(h, "position"):0.00}, speed deployed/retracted {F(h, "speedDeployed")}/{F(h, "speedRetracted")} m/s, alphaMin {F(h, "alphaMin")}, alphaFactor {F(h, "alphaFactor")}, area {F(h, "partAreaRetracted")} -> {F(h, "partAreaDeployed")}; aircraft {ac.speed:F1} m/s");
                    break;
                }
                case "cpinv":   // cpinv [radius m, default 4]: every renderer, canvas and text near the pilot's eye, in the aircraft frame: path, layer, on/off, centre, size, and whether its centre is inside the hull
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    float radius = arg.Length > 0 ? float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture) : 4f;
                    var cam = Camera.main; var eye = cam != null ? cam.transform.position : ac.transform.TransformPoint(new Vector3(-0.55f, 0.8f, 24.6f));
                    string Path(Transform t) { var n = t.name; for (int i = 0; i < 3 && t.parent != null; i++) { t = t.parent; n = t.name + "/" + n; } return n; }
                    var hull = ac.GetComponentsInChildren<Collider>(true).Where(c => c.GetComponentInParent<UnitPart>() != null && c.GetComponentInParent<UnitPart>().name == "cockpit" && !c.isTrigger).ToList();
                    bool Inside(Vector3 w)   // a ray from the cabin axis out through the point must meet the cockpit hull beyond it
                    {
                        var l = ac.transform.InverseTransformPoint(w); var axis = ac.transform.TransformPoint(new Vector3(0f, 0.35f, l.z));
                        var d = w - axis; if (d.magnitude < 0.05f) return true;
                        foreach (var c in hull) if (c.Raycast(new Ray(w, d.normalized), out _, 8f)) return true;
                        return false;
                    }
                    var items = new System.Collections.Generic.List<(float dist, string line)>();
                    foreach (var r in Resources.FindObjectsOfTypeAll<Renderer>().Where(r => r.gameObject.scene.IsValid()))
                    {
                        var c = r.bounds.center; float dist = Vector3.Distance(c, eye);
                        if (dist > radius || r.GetComponentInParent<Aircraft>() != ac && r.transform.root != ac.transform.root) continue;
                        var lc = ac.transform.InverseTransformPoint(c);
                        items.Add((dist, $"R {Path(r.transform)} [{r.GetType().Name}] layer {r.gameObject.layer} {(r.gameObject.activeInHierarchy && r.enabled ? "on" : "off")} at {lc:F2} size {r.bounds.size:F2} {(Inside(c) ? "" : "OUTSIDE HULL")}"));
                    }
                    foreach (var cv in Resources.FindObjectsOfTypeAll<Canvas>().Where(x => x.gameObject.scene.IsValid() && x.renderMode == RenderMode.WorldSpace))
                    {
                        var c = cv.transform.position; float dist = Vector3.Distance(c, eye);
                        if (dist > radius) continue;
                        var lc = ac.transform.InverseTransformPoint(c);
                        items.Add((dist, $"C {Path(cv.transform)} layer {cv.gameObject.layer} {(cv.gameObject.activeInHierarchy && cv.enabled ? "on" : "off")} at {lc:F2} scale {cv.transform.lossyScale.x:F4} {(Inside(c) ? "" : "OUTSIDE HULL")}"));
                    }
                    Logger.LogInfo($"  cpinv: eye at {ac.transform.InverseTransformPoint(eye):F2}, {items.Count} items within {radius} m, hull colliders {hull.Count}");
                    foreach (var it in items.OrderBy(x => x.dist)) Logger.LogInfo("  " + it.line);
                    break;
                }
                case "crewvis":   // crewvis on|off: show (or hide again) the crew's bodies and seats on the player's aircraft, which the game hides in cockpit view, for a screenshot
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null || ac.pilots == null) { Logger.LogWarning("  no aircraft"); break; }
                    int n = 0;
                    foreach (var pl in ac.pilots.Where(x => x != null))
                        foreach (var r in pl.GetComponentsInChildren<Renderer>(true)) { r.enabled = arg != "off"; n++; }
                    Logger.LogInfo($"  crew renderers {(arg != "off" ? "shown" : "hidden")}: {n}");
                    break;
                }
                case "expectac":   // expectac <jsonKey>: check the player is flying that aircraft (if the spawn clicks missed, the rest of the scenario would shoot another one)
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    var key = ac != null && ac.definition != null ? ac.definition.jsonKey : "none";
                    Test(key == arg, $"player aircraft is {arg} (got {key})");
                    break;
                }
                case "cpfill":   // cpfill <k>: give the cockpit interior's paint an emission of k x its own colour (the builder's fill light), to compare levels in screenshots
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    var ci = ac != null ? ac.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "cockpit_int") : null;
                    if (ci == null) { Logger.LogWarning("  no cockpit_int"); break; }
                    float k = float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    var done = new HashSet<Material>();
                    foreach (var r in ci.GetComponentsInChildren<Renderer>(true))
                        foreach (var m in r.sharedMaterials)
                        {
                            if (m == null || !m.name.StartsWith("B52_CP_") || m.name.StartsWith("B52_CP_MFD") || !done.Add(m)) continue;
                            m.EnableKeyword("_EMISSION");
                            if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
                            m.SetColor("_EmissionColor", (m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white) * k);
                        }
                    Logger.LogInfo($"  cockpit fill {k:F2} on {done.Count} materials: {string.Join(", ", done.Select(m => m.name))}");
                    break;
                }
                case "b52live":   // b52live: checks on the player's own B-52 (B52Check.Live: crab reset, engine sound, altimeter) and its fans turning
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    B52Check.Live(ac, Logger, Test);
                    StartCoroutine(FansTurn(ac));
                    break;
                }
                case "crabshow":   // crabshow <deg>: set the crosswind crab on the player's B-52 as the keys would (for a HUD screenshot); crabshow 0 clears it
                {
                    var gear = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.ironman1213.b52systems", out var pi) ? pi.Instance.GetType().Assembly.GetType("B52Systems.GearSystem") : null;
                    gear?.GetField("CrabSelected", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                        ?.SetValue(null, float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture));
                    Logger.LogInfo($"  crab {arg} deg");
                    break;
                }
                case "eject":   // eject: the player's aircraft starts its ejection sequence; passes if the crew's seats leave it within 5 s
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    StartCoroutine(Eject(ac));
                    break;
                }
                case "b52check":   // B-52 mod checks (B52Check.cs): "[TEST] PASS/FAIL" lines
                    B52Check.Run(Logger, Test);
                    break;
                case "stick":   // stick <pitch> <roll> <yaw> | stick off: hold the game's inputs there (-1..1, pitch + = nose down) until "stick off"
                {
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    StickHold.Held = arg == "off" ? (Vector3?)null : new Vector3(F(0), F(1), F(2));
                    break;
                }
                case "skinshadow":   // skinshadow cast|receive|both|none: which shadows the player's jet's skin casts and receives
                {
                    int count = 0;
                    foreach (var r in FindObjectsOfType<Renderer>())
                    {
                        if (!r.name.StartsWith("MiG29_skin")) continue;
                        r.shadowCastingMode = arg == "cast" || arg == "both" ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                        r.receiveShadows = arg == "receive" || arg == "both"; count++;
                    }
                    Logger.LogInfo($"  skinshadow {arg}: {count} skin renderers");
                    break;
                }
                case "sun":   // where the sun is: elevation and azimuth relative to the player's jet's nose
                {
                    var sun = FindObjectsOfType<Light>().Where(l => l.type == LightType.Directional).OrderByDescending(l => l.intensity).FirstOrDefault();
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (sun == null) { Logger.LogWarning("  no directional light"); break; }
                    var toSun = -sun.transform.forward;
                    float el = Mathf.Asin(toSun.y) * Mathf.Rad2Deg;
                    float az = ac != null ? Vector3.SignedAngle(Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up), Vector3.ProjectOnPlane(toSun, Vector3.up), Vector3.up) : float.NaN;
                    Logger.LogInfo($"  sun {sun.name}: elevation {el:F1} deg, {az:F0} deg right of the nose, intensity {sun.intensity:F2}, shadows {sun.shadows}");
                    break;
                }
                case "release":   // give the stick and throttle back to the player (run.sh ends every run with it)
                    ++flightGen; StickHold.Held = null; StickHold.Throttle = null;
                    break;
                case "hitray":   // hitray <ox oy oz> <dx dy dz>: the first mesh triangle of the player's jet a ray meets (aircraft frame)
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    var o = ac.transform.TransformPoint(new Vector3(F(0), F(1), F(2)));
                    var d = ac.transform.TransformDirection(new Vector3(F(3), F(4), F(5))).normalized;
                    string best = "nothing"; float bestT = float.MaxValue;
                    foreach (var mf in ac.GetComponentsInChildren<MeshFilter>(true))
                    {
                        var mr = mf.GetComponent<MeshRenderer>();
                        if (mf.sharedMesh == null || mr == null || !mr.enabled || !mf.sharedMesh.isReadable) continue;
                        var lo = mf.transform.InverseTransformPoint(o); var ld = mf.transform.InverseTransformDirection(d);
                        var v = mf.sharedMesh.vertices; var tri = mf.sharedMesh.triangles;
                        for (int i = 0; i < tri.Length; i += 3)
                        {
                            Vector3 p0 = v[tri[i]], e1 = v[tri[i + 1]] - p0, e2 = v[tri[i + 2]] - p0, h = Vector3.Cross(ld, e2);
                            float det = Vector3.Dot(e1, h); if (Mathf.Abs(det) < 1e-9f) continue;
                            float inv = 1f / det; var sv = lo - p0; float u = Vector3.Dot(sv, h) * inv; if (u < 0 || u > 1) continue;
                            var q = Vector3.Cross(sv, e1); float w = Vector3.Dot(ld, q) * inv; if (w < 0 || u + w > 1) continue;
                            float tt = Vector3.Dot(e2, q) * inv;
                            if (tt > 0.01f && tt < bestT)
                            {
                                bestT = tt;
                                var hp = ac.transform.InverseTransformPoint(mf.transform.TransformPoint(lo + ld * tt));
                                string path = mf.name; for (var pt = mf.transform.parent; pt != null && pt != ac.transform; pt = pt.parent) path = pt.name + "/" + path;
                                best = $"{path} (mesh {mf.sharedMesh.name}, tri {i / 3}, mat {mr.sharedMaterial?.name}) at {hp:F2}, {tt:F2} m";
                            }
                        }
                    }
                    Logger.LogInfo($"  hitray: {best}");
                    break;
                }
                case "salvo":   // salvo <station> <trigger pulls> [interval s]: select that weapon station and pull the trigger; checks something left
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null || ac.weaponManager == null) { Logger.LogWarning("  no aircraft or weapon manager"); break; }
                    var a = arg.Split(' ');
                    int st = int.Parse(a[0]), pulls = int.Parse(a[1]);
                    float every = a.Length > 2 ? float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture) : 0.5f;
                    StartCoroutine(Salvo(ac, st, pulls, every));
                    break;
                }
                case "expect":   // expect chute|nochute: whether a brake chute (MiG-29's or B-52's) is out now
                    Test(ChuteOut() == (arg == "chute"), $"brake chute {(arg == "chute" ? "deployed" : "stowed")}");
                    break;
                case "say":   // say <text>: a marker line for tests/ingame/run.sh
                    Logger.LogInfo($"[TEST] {arg}");
                    break;
                case "quit":   // quit the game (end of an unattended test run)
                    Logger.LogInfo("[TEST] quitting");
                    Application.Quit();
                    break;
                case "wait":
                    next = Time.unscaledTime + float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "runway":   // runway: put the player's jet on the nearest takeoff runway's start, facing down it, at rest
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    Airbase.Runway best = null; float bd = float.MaxValue;
                    foreach (var ab in FindObjectsOfType<Airbase>())
                        if (ab.runways != null) foreach (var rw in ab.runways)
                            if (rw != null && rw.Takeoff && rw.Start != null && rw.End != null) { float d = (rw.Start.position - ac.transform.position).sqrMagnitude; if (d < bd) { bd = d; best = rw; } }
                    if (best == null) { Logger.LogWarning("  no runway"); break; }
                    var dir = best.End.position - best.Start.position; float len = dir.magnitude; dir.y = 0f; dir.Normalize();
                    var at = best.Start.position + dir * 60f;
                    var rot = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up).normalized, Vector3.up));
                    var pivot = ac.transform.position;
                    // keep the jet's height over the ground (its root sits well above the wheels)
                    float Ground(Vector3 from, float range)   // distance down to the first collider that is not part of this jet
                    {
                        var hits = Physics.RaycastAll(from, Vector3.down, range, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
                        foreach (var h in hits) { var up = h.collider.GetComponentInParent<UnitPart>(); if (up == null || up.parentUnit != ac) return h.distance; }
                        return -1f;
                    }
                    // in the air (after a takeoff), reuse the height this jet had standing on the ground
                    float g0 = Ground(pivot + Vector3.up * 0.5f, 50f), h0 = g0 > 0f && g0 < 6f ? g0 - 0.5f : (runwayAc == ac && runwayH0 > 0f ? runwayH0 : 2f);
                    if (g0 > 0f && g0 < 6f) { runwayAc = ac; runwayH0 = h0; }
                    float g1 = Ground(at + Vector3.up * 30f, 100f); if (g1 > 0f) at = at + Vector3.up * (30f - g1);
                    at += Vector3.up * (h0 + 0.05f);
                    foreach (var rb in FindObjectsOfType<UnitPart>().Where(q => q.parentUnit == ac).Select(q => q.rb).Where(r => r != null).Distinct())
                    {
                        var np = at + rot * (rb.position - pivot);
                        rb.isKinematic = false; rb.velocity = Vector3.zero; rb.angularVelocity = Vector3.zero;
                        rb.position = np; rb.rotation = rot * rb.rotation; rb.transform.SetPositionAndRotation(np, rb.rotation);
                    }
                    // the pilot and aircraft compute g from the change in velocity: stopping a flying jet must not read as a crash
                    var vbf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                    var pvp = typeof(Pilot).GetField("velocityPrev", vbf);
                    foreach (var pl in ac.pilots) if (pl != null) pvp?.SetValue(pl, Vector3.zero);
                    typeof(Aircraft).GetField("velocityPrev", vbf)?.SetValue(ac, Vector3.zero);
                    StartCoroutine(Idle(++flightGen));   // stop any flight step and leave the jet at idle
                    Logger.LogInfo($"  runway: {len:F0} m long, at start + 60 m, heading {Quaternion.LookRotation(dir).eulerAngles.y:F0}");
                    break;
                }
                case "takeoff":   // takeoff <throttle 0..1> <rotate km/h> [pull 0..1, nose up; default 0.6]: full run down the runway, rotate, 10 s climb-out, logs the takeoff numbers
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    StartCoroutine(Takeoff(ac, F(0), F(1), a.Length > 2 ? F(2) : 0.6f));
                    break;
                }
                case "taxiout":   // taxiout <throttle 0..1> <metres> [timeout s, default 120]: roll straight ahead (out of a hangar door) with the stick centred until that far from the start, then idle and brakes; passes if the ground under the aircraft stayed paved (hangar floor, apron, taxiway) all the way and nothing broke
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    float F(int i) => float.Parse(a[i], System.Globalization.CultureInfo.InvariantCulture);
                    StartCoroutine(TaxiOut(ac, F(0), F(1), a.Length > 2 ? F(2) : 120f));
                    break;
                }
                case "rollout":   // rollout <km/h>: on the runway, roll at that speed, then idle and full brakes; logs the stopping distance
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    StartCoroutine(Rollout(ac, float.Parse(arg, System.Globalization.CultureInfo.InvariantCulture)));
                    break;
                }
                case "spawnac":   // spawnac <jsonKey> <count> <distance m>: AI aircraft of that type in a ring ahead of the player, same height, flying
                {
                    var hud = SceneSingleton<CombatHUD>.i; var ac = hud != null ? hud.aircraft : null;
                    if (ac == null) { Logger.LogWarning("  no aircraft"); break; }
                    var a = arg.Split(' ');
                    var def = Resources.FindObjectsOfTypeAll<AircraftDefinition>().FirstOrDefault(d => d.jsonKey == a[0]);
                    if (def == null) { Logger.LogWarning("  no aircraft definition " + a[0] + "; have: " + string.Join(", ", Resources.FindObjectsOfTypeAll<AircraftDefinition>().Select(d => d.jsonKey))); break; }
                    int n = int.Parse(a[1]); float dist = float.Parse(a[2], System.Globalization.CultureInfo.InvariantCulture);
                    var prm = def.aircraftParameters;
                    var loadout = prm.StandardLoadouts != null && prm.StandardLoadouts.Length > 0 ? prm.StandardLoadouts[0].loadout : prm.loadouts[1];
                    var fwd = Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up).normalized;
                    var sp = NetworkSceneSingleton<Spawner>.i;
                    for (int i = 0; i < n; i++)
                    {
                        float ang = (i - (n - 1) / 2f) * 12f;
                        var pos = ac.transform.position + Quaternion.Euler(0f, ang, 0f) * fwd * dist + Vector3.up * (40f + 15f * i);
                        sp.SpawnAircraft(null, def.unitPrefab, loadout, 0.6f, default(LiveryKey), pos.ToGlobalPosition(), Quaternion.LookRotation(fwd), fwd * 200f, null, ac.NetworkHQ, $"perf_{a[0]}_{i}", 0.5f, 0.5f);
                    }
                    Logger.LogInfo($"  spawned {n} x {a[0]} at {dist} m");
                    break;
                }
                case "shot":
                    ScreenCapture.CaptureScreenshot(Path.Combine(Paths.BepInExRootPath, arg));
                    break;
            }
        }

        void Test(bool ok, string what)   // a check line for tests/ingame/run.sh
        {
            if (ok) Logger.LogInfo($"[TEST] PASS {what}"); else Logger.LogWarning($"[TEST] FAIL {what}");
        }

        // A deployed chute: the MiG-29's, or the B-52's (B52Systems.DragChute names its canopy B52_DragChute; a released
        // one keeps the name while it settles, so it must have no ChuteDrop to count).
        static bool ChuteOut() =>
            GameObject.Find("MiG29_brake_chute") != null
            || FindObjectsOfType<MeshRenderer>().Any(r => r.name == "B52_DragChute" && r.GetComponents<MonoBehaviour>().All(m => m.GetType().Name != "ChuteDrop"));

        System.Collections.IEnumerator Salvo(Aircraft ac, int station, int pulls, float every)
        {
            if (station >= ac.weaponStations.Count) { Test(false, $"salvo: no station {station} ({ac.weaponStations.Count} stations)"); yield break; }
            ac.weaponManager.SetActiveStation((byte)station);
            var ws = ac.weaponStations[station];
            string name = ws.WeaponInfo != null ? ws.WeaponInfo.weaponName : "?";
            int before = ws.Ammo;
            Logger.LogInfo($"  salvo: station {station} {name}, {before} rounds, {pulls} pulls every {every:F1} s");
            for (int i = 0; i < pulls && ac != null && !ac.disabled; i++)
            {
                ac.weaponManager.Fire();
                yield return new WaitForSeconds(every);
            }
            yield return new WaitForSeconds(1f);
            int after = ac != null ? ws.Ammo : before;
            Test(after < before, $"salvo: {name} released {before - after} of {before}");
        }

        static Aircraft runwayAc; static float runwayH0;   // the "runway" command's last on-ground root height

        static float Pitch(Aircraft ac) { var f = ac.transform.forward; return Mathf.Asin(Mathf.Clamp(f.y, -1f, 1f)) * Mathf.Rad2Deg; }

        static System.Type HudType() =>
            BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("com.ironman1213.b52systems", out var pi) ? pi.Instance.GetType().Assembly.GetType("B52Systems.HudReadout") : null;

        System.Collections.IEnumerator HudWatch(float seconds, string text)
        {
            var f = HudType()?.GetField("flash", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            float t0 = Time.time; string seen = null, last = null;
            while (f != null && Time.time - t0 < seconds)
            {
                var now = f.GetValue(null) as string;
                if (now != last) { last = now; if (now != null) Logger.LogInfo($"  hud message: '{now}' at {Time.time - t0:F1} s"); }
                if (now != null && now.Contains(text)) { seen = now; break; }
                yield return null;
            }
            Test(seen != null, $"HUD message '{text}' " + (seen != null ? $"shown after {Time.time - t0:F1} s" : $"not shown in {seconds:F0} s"));
        }

        // Open the hangar's doors with its own routine, wait for them, then spawn the AI aircraft at its spawn point through
        // the spawner (as Hangar.SpawnAircraft does).
        System.Collections.IEnumerator SpawnAiDirect(Hangar hangar, AircraftDefinition def)
        {
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(Hangar).GetMethod("OpenDoors", bf)?.Invoke(hangar, null);
            Logger.LogInfo("  spawnai: opening the hangar doors");
            yield return new WaitForSeconds(22f);
            var sp = typeof(Hangar).GetField("spawnTransform", bf)?.GetValue(hangar) as Transform;
            if (sp == null) { Logger.LogWarning("  no spawn point"); yield break; }
            var pos = sp.position + Vector3.up * def.spawnOffset.y + sp.forward * def.spawnOffset.z;
            var ai = NetworkSceneSingleton<Spawner>.i.SpawnAircraft(null, def.unitPrefab, null, 0.6f, default(LiveryKey), pos.ToGlobalPosition(),
                        sp.rotation * Quaternion.Euler(def.restRotation), Vector3.zero, hangar, hangar.attachedUnit.NetworkHQ, null, 1f, 0.5f);
            if (ai != null) ai.Networkloadout = ai.weaponManager.SelectAIAircraftWeapons(hangar.parentAirbase);
            Logger.LogInfo($"  spawnai: spawned at the hangar's spawn point with the doors open: {(ai != null ? ai.name : "failed")}");
        }

        static AircraftDefinition aiDef; static float aiSince;
        System.Collections.IEnumerator TrackAi(float seconds)
        {
            var hud = SceneSingleton<CombatHUD>.i; var me = hud != null ? hud.aircraft : null;
            Aircraft ai = null; float t0 = Time.time;
            while (ai == null && Time.time - t0 < 60f)
            {
                ai = FindObjectsOfType<Aircraft>().Where(a => a != me && a.definition == aiDef && !a.disabled).OrderByDescending(a => a.GetInstanceID()).FirstOrDefault();
                if (ai == null) yield return new WaitForSeconds(1f);
            }
            if (ai == null) { Test(false, "AI aircraft: none spawned within 60 s"); yield break; }
            Logger.LogInfo($"  trackai: found {ai.definition.unitName} after {Time.time - t0:F0} s");
            yield return Track(ai, seconds);
        }

        System.Collections.IEnumerator Track(Aircraft ac, float seconds)
        {
            var start = ac.GlobalPosition(); float t0 = Time.time, maxAgl = 0f, maxDist = 0f;
            var vs = new System.Collections.Generic.List<float>();
            while (Time.time - t0 < seconds && ac != null && !ac.disabled)
            {
                var pilot = ac.pilots != null ? ac.pilots.FirstOrDefault(x => x != null) : null;
                float dist = Vector3.ProjectOnPlane(ac.GlobalPosition() - start, Vector3.up).magnitude, vy = ac.rb != null ? ac.rb.velocity.y : 0f;
                maxAgl = Mathf.Max(maxAgl, ac.radarAlt); maxDist = Mathf.Max(maxDist, dist);
                if (ac.radarAlt > 300f) vs.Add(vy);
                Logger.LogInfo($"  track t {Time.time - t0:F0} s: {pilot?.GetCurrentState()}, {ac.speed * 3.6f:F0} km/h, radar alt {ac.radarAlt:F0} m, climb {vy:F1} m/s, pitch {Pitch(ac):F1}, {dist:F0} m from start, gear {ac.gearState}");
                yield return new WaitForSeconds(2f);
            }
            float mean = vs.Count > 0 ? vs.Average() : 0f, sd = vs.Count > 1 ? Mathf.Sqrt(vs.Sum(v => (v - mean) * (v - mean)) / (vs.Count - 1)) : 0f;
            Logger.LogInfo($"  track summary: max radar alt {maxAgl:F0} m, moved {maxDist:F0} m, vertical speed above 300 m: mean {mean:F1} m/s, spread {sd:F1} m/s over {vs.Count} samples");
            Test(ac != null && !ac.disabled && maxAgl > 150f, $"AI flight: took off and climbed to {maxAgl:F0} m (moved {maxDist:F0} m)");
        }

        System.Collections.IEnumerator FansTurn(Aircraft ac)
        {
            var fans = ac.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("fan_pod")).ToList();
            var before = fans.Select(t => t.localRotation).ToList();
            yield return new WaitForSeconds(1f);
            int turned = fans.Where((t, i) => t != null && Quaternion.Angle(before[i], t.localRotation) > 5f).Count();
            Test(fans.Count > 0 && turned == fans.Count, $"engine fans: {turned} of {fans.Count} turned in a second");
        }

        System.Collections.IEnumerator Eject(Aircraft ac)
        {
            int seats0 = FindObjectsOfType<EjectionSeat>().Length;
            ac.StartEjectionSequence();
            float t0 = Time.time; int seats = seats0;
            while (Time.time - t0 < 5f) { yield return new WaitForSeconds(0.25f); seats = FindObjectsOfType<EjectionSeat>().Length; if (seats > seats0) break; }
            Test(seats > seats0, $"eject: {seats - seats0} ejection seat(s) left the aircraft in {Time.time - t0:F1} s");
        }

        System.Collections.IEnumerator TaxiOut(Aircraft ac, float throttle, float metres, float timeout)
        {
            int gen = ++flightGen;
            var inp = ac.GetInputs(); var start = ac.GlobalPosition(); float t0 = Time.time, nextTrace = 0f, dist = 0f;
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var parts = FindObjectsOfType<UnitPart>().Where(q => q.parentUnit == ac).ToList();
            int Intact() => parts.Count(q => q != null && q.GetType().GetField("joints", bf)?.GetValue(q) is System.Array js
                                            && js.Cast<object>().All(j => j.GetType().GetField("joint", bf)?.GetValue(j) as Joint != null));
            int intact0 = Intact(); bool reached = false;
            string firstUnpaved = null, lastSurface = "?"; float nextProbe = 0f;
            bool Under(out string what)   // the first surface below the aircraft that isn't the aircraft itself
            {
                what = "nothing";
                foreach (var h in Physics.RaycastAll(ac.transform.position + Vector3.up * 3f, Vector3.down, 40f, ~(1 << 2), QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
                {
                    if (h.collider.GetComponentInParent<Aircraft>() == ac) continue;
                    var unit = h.collider.GetComponentInParent<Unit>();
                    what = unit != null ? $"{h.collider.name} ({unit.definition?.unitName})" : h.collider.name;
                    return h.collider.sharedMaterial != GameAssets.i.terrainMaterial;
                }
                return false;
            }
            while (Time.time - t0 < timeout)
            {
                yield return null;
                if (ac == null || ac.disabled) { StickHold.Held = null; StickHold.Throttle = null; Test(false, $"taxiout: the aircraft was destroyed at {Time.time - t0:F1} s"); yield break; }
                if (inp == null) { inp = ac.GetInputs(); continue; }
                StickHold.Held = Vector3.zero; StickHold.Throttle = throttle; inp.brake = 0f;
                dist = Vector3.ProjectOnPlane(ac.GlobalPosition() - start, Vector3.up).magnitude;
                if (Time.time >= nextTrace) { nextTrace = Time.time + 2f; Logger.LogInfo($"  taxi t {Time.time - t0:F0} s: {ac.speed * 3.6f:F0} km/h, {dist:F0} m, radar alt {ac.radarAlt:F2}, parts intact {Intact()}/{intact0}"); }
                if (Time.time >= nextProbe)
                {
                    nextProbe = Time.time + 0.25f;
                    bool paved = Under(out var what);
                    if (what != lastSurface) { Logger.LogInfo($"  taxi {dist:F0} m: now on {what} ({(paved ? "paved" : "terrain")})"); lastSurface = what; }
                    if (!paved && firstUnpaved == null) firstUnpaved = $"{what} at {dist:F0} m";
                }
                if (dist >= metres) { reached = true; break; }
            }
            StickHold.Throttle = 0f; if (inp != null) inp.brake = 1f;
            yield return new WaitForSeconds(6f);
            if (inp != null) inp.brake = 0f;
            if (gen == flightGen) { StickHold.Held = null; StickHold.Throttle = null; }
            Test(reached && firstUnpaved == null && ac != null && Intact() == intact0,
                 $"taxiout: {dist:F0} of {metres:F0} m, " + (firstUnpaved == null ? "paved all the way" : $"left the paving: {firstUnpaved}") + $", parts intact {(ac != null ? Intact() : 0)}/{intact0}");
        }

        System.Collections.IEnumerator Takeoff(Aircraft ac, float throttle, float rotateKmh, float pitchIn)
        {
            int gen = ++flightGen;
            var inp = ac.GetInputs(); var start = ac.GlobalPosition(); float t0 = Time.time, p0 = Pitch(ac);
            bool rotating = false, noseUp = false, liftedOff = false; float alt0 = ac.radarAlt, nextTrace = 0f;
            var track = Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up).normalized;
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            var parts = FindObjectsOfType<UnitPart>().Where(q => q.parentUnit == ac).ToList();
            int Intact() => parts.Count(q => q != null && q.GetType().GetField("joints", bf)?.GetValue(q) is System.Array js
                                            && js.Cast<object>().All(j => j.GetType().GetField("joint", bf)?.GetValue(j) as Joint != null));
            int intact0 = Intact();
            void Trace(string why)   // one line of the run: along/across the runway axis, heading off the axis, attitude, height, inputs, parts
            {
                var d = ac.GlobalPosition() - start; var f = Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up).normalized;
                Logger.LogInfo($"  {why} t {Time.time - t0:F1} s: {ac.speed * 3.6f:F0} km/h, along {Vector3.Dot(d, track):F0} m, across {Vector3.Cross(track, d).y:F1} m, "
                             + $"heading off {Vector3.SignedAngle(track, f, Vector3.up):F1}, pitch {Pitch(ac) - p0:F1}, roll {Vector3.SignedAngle(Vector3.ProjectOnPlane(ac.transform.up, ac.transform.forward), Vector3.ProjectOnPlane(Vector3.up, ac.transform.forward), ac.transform.forward):F1}, "
                             + $"radar alt {ac.radarAlt:F2}, pitch in {(inp != null ? inp.pitch : 0f):F2}, parts intact {Intact()}/{intact0}, gear {ac.gearState}");
            }
            Logger.LogInfo($"  takeoff: throttle {throttle}, rotate at {rotateKmh} km/h; nose attitude at rest {p0:F2} deg, radar alt {alt0:F2}");
            while (Time.time - t0 < 90f)
            {
                yield return null;
                if (inp == null) inp = ac.GetInputs();
                if (ac == null || ac.disabled) { StickHold.Held = null; StickHold.Throttle = null; Test(false, $"takeoff: the aircraft was destroyed at {Time.time - t0:F1} s"); yield break; }
                if (inp == null) continue;
                if (Time.time >= nextTrace) { nextTrace = Time.time + 1f; Trace("roll"); }
                float kmh = ac.speed * 3.6f, dist = Vector3.ProjectOnPlane(ac.GlobalPosition() - start, Vector3.up).magnitude;
                StickHold.Throttle = throttle; inp.brake = 0f;
                if (!rotating && kmh >= rotateKmh) { rotating = true; Logger.LogInfo($"  rotate: {kmh:F0} km/h at {dist:F0} m, {Time.time - t0:F1} s"); }
                // rotate to 10 deg, easing off over the last 4 (the game's pitch input is + nose down)
                StickHold.Held = new Vector3(rotating ? -pitchIn * Mathf.Clamp01((10f - (Pitch(ac) - p0)) / 4f) : 0f, 0f, 0f);
                if (rotating && !noseUp && Pitch(ac) - p0 > 2f) { noseUp = true; Logger.LogInfo($"  nose up 2 deg: {kmh:F0} km/h at {dist:F0} m"); }
                if (ac.radarAlt - alt0 > 1.5f)
                {
                    liftedOff = true; Trace("lift-off");
                    Test(Pitch(ac) - p0 > 0f && Intact() == intact0, $"takeoff: lift-off at {kmh:F0} km/h after {dist:F0} m, {Time.time - t0:F1} s, nose up {Pitch(ac) - p0:F1} deg");
                    break;
                }
            }
            if (!liftedOff) { StickHold.Held = null; StickHold.Throttle = null; Test(false, $"takeoff: no lift-off in {Time.time - t0:F0} s"); yield break; }
            // climb out: hold 10 deg nose up, gear up at 50 m, for 10 s, then level the inputs and leave the jet flying
            float t1 = Time.time; bool gearUp = false;
            while (Time.time - t1 < 10f && ac != null && !ac.disabled && inp != null)
            {
                yield return null;
                StickHold.Held = new Vector3(-Mathf.Clamp((10f - Pitch(ac)) * 0.12f, -0.6f, 0.6f), 0f, 0f);
                if (!gearUp && ac.radarAlt > 30f) { gearUp = true; ac.SetGear(false); }
                if (Time.time >= nextTrace) { nextTrace = Time.time + 1f; Trace("climb"); }
            }
            if (ac == null || ac.disabled) { StickHold.Held = null; StickHold.Throttle = null; Test(false, "climb-out: the aircraft was destroyed"); }
            else
            {
                Trace("done"); Test(ac.radarAlt > 60f && Intact() == intact0, $"climb-out: {ac.radarAlt:F0} m up at {ac.speed * 3.6f:F0} km/h after 10 s, gear {ac.gearState}");
                StartCoroutine(Cruise(ac, gen));
            }
        }

        // Stick back to the player, throttle to idle (held for a moment so the player's own throttle is left there too).
        System.Collections.IEnumerator Idle(int gen)
        {
            StickHold.Held = null; StickHold.Throttle = 0f;
            yield return new WaitForSeconds(0.5f);
            if (gen == flightGen) StickHold.Throttle = null;
        }

        // Between test steps the jet flies on: wings as they are, the nose 5 deg up, 85 % throttle, until the next flight step starts.
        int flightGen;
        System.Collections.IEnumerator Cruise(Aircraft ac, int gen)
        {
            StickHold.Throttle = 0.85f;
            while (gen == flightGen && ac != null && !ac.disabled)
            {
                StickHold.Held = new Vector3(-Mathf.Clamp((5f - Pitch(ac)) * 0.1f, -0.5f, 0.5f), 0f, 0f);
                yield return null;
            }
            if (gen == flightGen) { StickHold.Held = null; StickHold.Throttle = null; }
        }

        System.Collections.IEnumerator Rollout(Aircraft ac, float kmh)
        {
            var bodies = FindObjectsOfType<UnitPart>().Where(q => q.parentUnit == ac).Select(q => q.rb).Where(r => r != null).Distinct().ToList();
            var fwd = Vector3.ProjectOnPlane(ac.transform.forward, Vector3.up).normalized;
            foreach (var rb in bodies) rb.velocity = fwd * kmh / 3.6f;
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            typeof(Aircraft).GetField("velocityPrev", bf)?.SetValue(ac, fwd * kmh / 3.6f);
            var pvp = typeof(Pilot).GetField("velocityPrev", bf);
            foreach (var pl in ac.pilots) if (pl != null) pvp?.SetValue(pl, fwd * kmh / 3.6f);
            ++flightGen;
            var inp = ac.GetInputs(); var start = ac.GlobalPosition(); float t0 = Time.time, minPitch = 0f, p0 = Pitch(ac);
            while (Time.time - t0 < 60f)
            {
                yield return null;
                if (inp == null) inp = ac.GetInputs();
                StickHold.Held = Vector3.zero; StickHold.Throttle = 0f;
                if (inp != null) inp.brake = 1f;
                minPitch = Mathf.Min(minPitch, Pitch(ac) - p0);
                if (ac.speed < 1f && Time.time - t0 > 1f) break;
            }
            float dist = Vector3.ProjectOnPlane(ac.GlobalPosition() - start, Vector3.up).magnitude;
            Logger.LogInfo($"  rollout from {kmh} km/h: stopped in {dist:F0} m, {Time.time - t0:F1} s; nose dipped {-minPitch:F2} deg under braking");
            Test(ac != null && !ac.disabled && ac.speed < 1f, $"rollout from {kmh} km/h: stopped in {dist:F0} m, {Time.time - t0:F1} s");
            if (inp != null) inp.brake = 0f;
            StickHold.Held = null; StickHold.Throttle = null;
        }
System.Collections.IEnumerator GTest(Aircraft ac, float pitch)
        {
            int gen = ++flightGen; StickHold.Held = Vector3.zero;
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            var fwd = ac.transform.forward; fwd.y = 0f; fwd.Normalize();
            List<Rigidbody> Bodies() => FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac).Select(p => p.rb).Where(r => r != null).Distinct().ToList();
            foreach (var rb in Bodies()) { rb.isKinematic = false; rb.transform.position += Vector3.up * 1500f; rb.position = rb.transform.position; rb.velocity = fwd * 220f; rb.angularVelocity = Vector3.zero; }
            var vp = typeof(Pilot).GetField("velocityPrev", bf);
            foreach (var pl in ac.pilots) if (pl != null) vp?.SetValue(pl, fwd * 220f);
            typeof(Aircraft).GetField("velocityPrev", bf)?.SetValue(ac, fwd * 220f);
            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();   // settle at 1 g
            var root = ac.rb;
            var parts = FindObjectsOfType<UnitPart>().Where(p => p.parentUnit == ac && p.rb != null && p.rb != root).ToList();
            var rest = parts.ToDictionary(p => p, p => root.transform.InverseTransformPoint(p.transform.position));
            // nose up about the root: rigid, so the parts keep their relative poses
            var q = Quaternion.AngleAxis(-pitch, ac.transform.right);
            var pivot = root.position;
            foreach (var rb in Bodies()) { rb.position = pivot + q * (rb.position - pivot); rb.rotation = q * rb.rotation; rb.transform.SetPositionAndRotation(rb.position, rb.rotation); }
            float maxG = 0f; var worst = parts.ToDictionary(p => p, p => 0f);
            for (int i = 0; i < 100; i++)
            {
                yield return new WaitForFixedUpdate();
                maxG = Mathf.Max(maxG, ac.gForce);
                foreach (var p in parts) if (p != null && p.rb != null) worst[p] = Mathf.Max(worst[p], (root.transform.InverseTransformPoint(p.transform.position) - rest[p]).magnitude);
            }
            Logger.LogInfo($"  gtest {pitch} deg: max g {maxG:F1}, iterations {root.solverIterations}/{root.solverVelocityIterations}");
            Test(maxG > 2f && maxG < 12f, $"g-test {pitch} deg pull: max {maxG:F1} g (2 to 12 expected)");
            if (ac != null && !ac.disabled) StartCoroutine(Cruise(ac, gen));
            foreach (var kv in worst.OrderByDescending(kv => kv.Value).Take(10)) Logger.LogInfo($"    {kv.Key.name}: {kv.Value * 100f:F1} cm");
        }

    }
}
