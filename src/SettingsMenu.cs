using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Oritasy-style IMGUI settings: RADAR window position + opacity. Toggle with `/`.
    /// Independent of the PPI MFD; can open anytime (ForceShow / AlwaysWhenToggled not required).
    /// </summary>
    internal sealed class SettingsMenu
    {
        private const int WindowId = 0x00524442; // 'RDB'
        private Rect _window = new Rect(80f, 80f, 340f, 260f);
        private bool _open;
        private bool _stylesReady;
        private GUIStyle? _label;
        private GUIStyle? _header;
        private GUIStyle? _button;

        internal bool IsOpen => _open;

        internal void Toggle()
        {
            _open = !_open;
            if (_open)
            {
                // Place near current MFD if possible; otherwise a safe default.
                float x = Config.WindowX.Value + 40f;
                float y = Config.WindowY.Value + 40f;
                _window.x = Mathf.Clamp(x, 8f, Mathf.Max(8f, Screen.width - _window.width - 8f));
                _window.y = Mathf.Clamp(y, 8f, Mathf.Max(8f, Screen.height - _window.height - 8f));
            }
        }

        internal void SetOpen(bool open) => _open = open;

        internal void Draw(RadarGui? gui)
        {
            if (!_open)
            {
                return;
            }

            EnsureStyles();
            int prevDepth = GUI.depth;
            try
            {
                GUI.depth = Config.GuiDepth.Value - 1; // slightly above MFD
                _window = GUI.Window(WindowId, _window, id => DrawContents(id, gui), GUIContent.none, ScopeDraw.WindowStyle);
            }
            finally
            {
                GUI.depth = prevDepth;
            }
        }

        private void DrawContents(int id, RadarGui? gui)
        {
            Rect outer = ScopeDraw.Snap(new Rect(0f, 0f, _window.width, _window.height));
            DrawChrome(outer);

            float pad = 12f;
            float y = 14f;
            float w = outer.width - pad * 2f;

            ScopeDraw.ClippedLabel(new Rect(pad, y, w, 22f), "R.A.D.A.R  SETTINGS", _header!);
            y += 26f;
            ScopeDraw.Fill(new Rect(pad, y, w, 2f), ScopeDraw.CyanAccent * new Color(1f, 1f, 1f, 0.55f));
            y += 12f;

            // Position X
            float wx = Config.WindowX.Value;
            ScopeDraw.ClippedLabel(new Rect(pad, y, w, 18f), "Window X  " + wx.ToString("0"), _label!);
            y += 18f;
            float maxX = Mathf.Max(0f, Screen.width - 80f);
            wx = GUI.HorizontalSlider(new Rect(pad, y, w, 18f), wx, 0f, maxX);
            y += 24f;

            // Position Y
            float wy = Config.WindowY.Value;
            ScopeDraw.ClippedLabel(new Rect(pad, y, w, 18f), "Window Y  " + wy.ToString("0"), _label!);
            y += 18f;
            float maxY = Mathf.Max(0f, Screen.height - 80f);
            wy = GUI.HorizontalSlider(new Rect(pad, y, w, 18f), wy, 0f, maxY);
            y += 24f;

            // Opacity
            float op = Config.WindowOpacity.Value;
            ScopeDraw.ClippedLabel(
                new Rect(pad, y, w, 18f),
                "Panel opacity / translucency  " + op.ToString("0.00"),
                _label!);
            y += 18f;
            op = GUI.HorizontalSlider(new Rect(pad, y, w, 18f), op, 0.05f, 1f);
            y += 28f;

            // Apply live
            bool posChanged = !Mathf.Approximately(wx, Config.WindowX.Value)
                              || !Mathf.Approximately(wy, Config.WindowY.Value);
            if (posChanged)
            {
                Config.WindowX.Value = wx;
                Config.WindowY.Value = wy;
                gui?.ApplyWindowPosition(wx, wy);
            }

            if (!Mathf.Approximately(op, Config.WindowOpacity.Value))
            {
                Config.WindowOpacity.Value = Mathf.Clamp(op, 0.05f, 1f);
            }

            // Reset position
            if (GUI.Button(new Rect(pad, y, w * 0.48f, 28f), "Reset Pos", _button!))
            {
                Config.WindowX.Value = 24f;
                Config.WindowY.Value = 24f;
                gui?.ApplyWindowPosition(24f, 24f);
            }

            if (GUI.Button(new Rect(pad + w * 0.52f, y, w * 0.48f, 28f), "Close", _button!))
            {
                _open = false;
            }

            y += 34f;
            ScopeDraw.ClippedLabel(
                new Rect(pad, y, w, 18f),
                "Hotkey  /   ·  persists to BepInEx cfg",
                ScopeDraw.TinyStyle);

            GUI.DragWindow(new Rect(0f, 0f, _window.width, 28f));
        }

        private static void DrawChrome(Rect rect)
        {
            // Dark bezel #020B10, double border, cyan accent bar (Oritasy / ScopeDraw MFD chrome).
            // Dark GL panel (true translucency); avoid IMGUI white-pixel wash.
            float a = Mathf.Clamp(Config.WindowOpacity != null ? Config.WindowOpacity.Value : 0.92f, 0.05f, 1f);
            ScopeDraw.FillTranslucent(rect, new Color(0.02f, 0.02f, 0.02f, Mathf.Max(0.55f, a)));
            ScopeDraw.Border(rect, ScopeDraw.PanelBorderOuter, 2f);
            Rect inner = new Rect(rect.x + 3f, rect.y + 3f, rect.width - 6f, rect.height - 6f);
            ScopeDraw.Border(inner, ScopeDraw.PanelBorder, 1.5f);
            ScopeDraw.Fill(new Rect(rect.x + 6f, rect.y + 6f, rect.width - 12f, 3f), ScopeDraw.CyanAccent);
            ScopeDraw.CornerBrackets(rect, ScopeDraw.CyanAccent * new Color(1f, 1f, 1f, 0.7f), 12f, 2f);
        }

        private void EnsureStyles()
        {
            if (_stylesReady)
            {
                return;
            }

            _header = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = ScopeDraw.Phosphor }
            };
            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.65f, 0.98f, 0.72f, 0.95f) }
            };
            _button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = ScopeDraw.Phosphor },
                hover = { textColor = ScopeDraw.CyanAccent },
                active = { textColor = ScopeDraw.CyanAccent }
            };
            _stylesReady = true;
        }
    }
}
