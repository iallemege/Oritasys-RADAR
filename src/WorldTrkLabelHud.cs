using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Screen-space red "TRK" label above the vanilla locked target.
    /// Uses targetList <b>last</b> entry (display lock). Cleared immediately when list empty —
    /// no sticky TRK after unlock.
    /// </summary>
    internal static class WorldTrkLabelHud
    {
        private const float DefaultHeight = 3f;

        private static GUIStyle? _style;
        private static object? _cachedUnit;
        private static Vector3 _cachedWorld;

        internal static void Draw(ModeState modes, ContactProvider contacts)
        {
            if (!Config.ShowWorldTrkLabel.Value)
            {
                return;
            }

            if (contacts == null || !contacts.ShouldDrawHud)
            {
                ClearCache();
                return;
            }

            object? player = contacts.Player;
            object? unit = null;
            // Prefer last targetList entry for the single world TRK label.
            bool haveLive = WeaponReflect.TryGetDisplayLockTargetUnit(player, out unit) && unit != null;

            // Unity destroyed wrapper
            if (haveLive && unit is Object uo && uo == null)
            {
                haveLive = false;
                unit = null;
            }

            if (!haveLive)
            {
                // Immediate clear when vanilla lock gone — no HoldSec sticky TRK.
                ClearCache();
                return;
            }

            // Never draw on ownship / clutter camera helpers
            if (ReferenceEquals(unit, player) || GameReflect.IsClutterUnit(unit))
            {
                ClearCache();
                return;
            }

            if (!TryResolveLabelWorld(unit, out Vector3 world))
            {
                ClearCache();
                return;
            }

            _cachedUnit = unit;
            _cachedWorld = world;
            DrawAt(world);

            _ = modes;
        }

        private static void DrawAt(Vector3 world)
        {
            Camera? cam = WeaponReflect.FindMainCamera();
            if (cam == null)
            {
                return;
            }

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0f)
            {
                return;
            }

            Vector2 gui = new Vector2(sp.x, Screen.height - sp.y);
            EnsureStyle();
            const float w = 48f;
            const float h = 18f;
            Rect r = new Rect(gui.x - w * 0.5f, gui.y - h, w, h);
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.15f, 0.1f, 1f);
            GUI.Label(r, "TRK", _style);
            GUI.color = prev;
        }

        private static bool TryResolveLabelWorld(object? raw, out Vector3 world)
        {
            world = Vector3.zero;
            if (raw == null)
            {
                return false;
            }

            if (raw is Object uo && uo == null)
            {
                return false;
            }

            try
            {
                if (raw is Component c && c != null)
                {
                    Renderer? rend = null;
                    try
                    {
                        rend = c.GetComponentInChildren<Renderer>();
                    }
                    catch
                    {
                        // ignored
                    }

                    if (rend != null)
                    {
                        Bounds b = rend.bounds;
                        world = b.center + Vector3.up * (b.extents.y + 0.35f);
                        return true;
                    }

                    world = c.transform.position + Vector3.up * DefaultHeight;
                    return true;
                }

                world = GameReflect.WorldPosition(raw) + Vector3.up * DefaultHeight;
                return world.sqrMagnitude > 0.01f || true;
            }
            catch
            {
                return false;
            }
        }

        internal static void ClearCache()
        {
            _cachedUnit = null;
            _cachedWorld = Vector3.zero;
        }

        private static void EnsureStyle()
        {
            if (_style != null)
            {
                return;
            }

            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.15f, 0.1f, 1f) }
            };
            if (OritasyUi.HudFont != null)
            {
                _style.font = OritasyUi.HudFont;
            }
        }
    }
}
