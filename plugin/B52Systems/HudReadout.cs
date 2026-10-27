using System.Collections.Generic;
using UnityEngine;

namespace B52Systems
{
    /// <summary>
    /// On-screen readouts for the local pilot's B-52 systems (GEAR CRAB, CHUTE). One line of short-lived messages
    /// that fade out, under it the standing states (crab angle set), and a help card with the keys and speeds. Drawn low in the centre of the screen, clear
    /// of the game's top-centre messages, in HUD green with a shadow, sized to the screen height.
    /// Lives on the plugin's own object, so it survives scene changes.
    /// </summary>
    public class HudReadout : MonoBehaviour
    {
        private const float Fade = 0.6f;
        private static string flash;
        private static float flashUntil;
        private static readonly SortedDictionary<string, string> status = new SortedDictionary<string, string>();

        private GUIStyle style, shadow, cardStyle;
        private int styledHeight;

        // Help card: the B-52's keys and speeds. Shown by itself the first time a B-52 is flown in a session, and
        // toggled with Plugin.HelpKey while flying one (GearSystem reports the local B-52 each frame).
        private static bool cardOpen, cardShownOnce;
        private static float cardUntil, localB52Until;
        private static readonly string[] Card =
        {
            "B-52J  STRATOFORTRESS",
            "Speedbrakes        throttle to idle",
            "Drag chute         on the runway at idle, hold the wheel brakes below 165 kt",
            "Crosswind crab     [  ]  turn the trucks 5° (max 20°),  \\  straightens them",
            "Taxi               rudder steers the forward trucks",
            "Takeoff            lifts off by itself at ~170 kt at full load; climb at 180 kt",
            "Approach           150-165 kt; flaps below 204 kt; touch down 135-145 kt",
        };

        /// <summary>Called every frame by the local pilot's B-52 (GearSystem).</summary>
        internal static void LocalB52Flying()
        {
            localB52Until = Time.unscaledTime + 0.5f;
            if (!cardShownOnce) { cardShownOnce = true; cardUntil = Time.unscaledTime + 10f; }
        }

        private void Update()
        {
            if (Time.unscaledTime > localB52Until) { cardOpen = false; return; }
            if (Plugin.HelpKey.Value.IsDown() && !NuclearOption.MissionEditorScripts.InputFieldChecker.InsideInputField)
            {
                cardOpen = !(cardOpen || Time.unscaledTime < cardUntil);
                cardUntil = 0f;
            }
        }

        /// <summary>Show a message for a few seconds (replaces the current one).</summary>
        internal static void Flash(string text, float seconds = 3f)
        {
            flash = text;
            flashUntil = Time.unscaledTime + seconds;
        }

        /// <summary>Set or clear (null) a standing state line, keyed so each system owns its own line.</summary>
        internal static void SetStatus(string key, string text)
        {
            if (string.IsNullOrEmpty(text)) status.Remove(key);
            else status[key] = text;
        }

        private void OnGUI()
        {
            if (Event.current.type != EventType.Repaint) return;
            bool showFlash = flash != null && Time.unscaledTime < flashUntil;
            bool showCard = Time.unscaledTime <= localB52Until && (cardOpen || Time.unscaledTime < cardUntil);
            if (!showFlash && status.Count == 0 && !showCard) return;
            if (GameplayUI.GameIsPaused) return;

            if (style == null || styledHeight != Screen.height)
            {
                styledHeight = Screen.height;
                int size = Mathf.Max(12, Mathf.RoundToInt(18f * Screen.height / 1080f));
                style = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
                shadow = new GUIStyle(style);
                cardStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.Max(11, Mathf.RoundToInt(15f * Screen.height / 1080f)), wordWrap = false };
            }
            if (showCard) DrawCard();

            float line = style.fontSize * 1.5f;
            float y = Screen.height * 0.78f;
            var hud = new Color(0.36f, 1f, 0.46f);
            if (showFlash)
            {
                float a = Mathf.Clamp01((flashUntil - Time.unscaledTime) / Fade);
                Draw(flash, y, hud, a);
                y += line;
            }
            foreach (var s in status.Values)
            {
                Draw(s, y, hud, 0.75f);
                y += line;
            }
        }

        private void DrawCard()
        {
            float line = cardStyle.fontSize * 1.45f, pad = cardStyle.fontSize;
            float w = cardStyle.fontSize * 40f, h = line * (Card.Length + 1) + pad * 2;
            var box = new Rect(Screen.width * 0.03f, Screen.height * 0.18f, w, h);
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = old;
            for (int i = 0; i < Card.Length; i++)
            {
                cardStyle.normal.textColor = i == 0 ? new Color(0.36f, 1f, 0.46f) : new Color(0.9f, 0.95f, 0.9f);
                cardStyle.fontStyle = i == 0 ? FontStyle.Bold : FontStyle.Normal;
                GUI.Label(new Rect(box.x + pad, box.y + pad + i * line, w - pad * 2, line), Card[i], cardStyle);
            }
            cardStyle.normal.textColor = new Color(0.7f, 0.75f, 0.7f); cardStyle.fontStyle = FontStyle.Italic;
            GUI.Label(new Rect(box.x + pad, box.y + pad + Card.Length * line, w - pad * 2, line),
                      $"{Plugin.HelpKey.Value} shows or hides this card", cardStyle);
        }

        private void Draw(string text, float y, Color color, float alpha)
        {
            var r = new Rect(0f, y, Screen.width, style.fontSize * 1.5f);
            float o = Mathf.Max(1f, style.fontSize / 12f);
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.6f * alpha);
            GUI.Label(new Rect(r.x + o, r.y + o, r.width, r.height), text, shadow);
            style.normal.textColor = new Color(color.r, color.g, color.b, alpha);
            GUI.Label(r, text, style);
        }
    }
}
