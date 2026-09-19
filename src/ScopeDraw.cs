using UnityEngine;

namespace RDA
{
    internal static class ScopeDraw
    {
        // Green phosphor + cyan DL accent + deep MFD panel.
        internal static readonly Color Phosphor = new Color(0f, 1f, 0.4f, 1f); // #00FF66 CRT green
        internal static readonly Color PhosphorDim = new Color(0f, 0.45f, 0.18f, 0.95f);
        internal static readonly Color PhosphorMuted = new Color(0.2f, 0.75f, 0.35f, 0.8f);
        /// <summary>Deep panel #020B10.</summary>
        internal static readonly Color PanelBg = new Color(0.02f, 0.02f, 0.02f, 0.97f); // near-black CRT
        internal static readonly Color PanelBezel = new Color(0.02f, 0.08f, 0.1f, 0.98f);
        internal static readonly Color PanelBorder = new Color(0.18f, 0.72f, 0.42f, 0.98f);
        internal static readonly Color PanelBorderOuter = new Color(0.08f, 0.35f, 0.22f, 0.9f);
        /// <summary>DL cyan #3DFFD0.</summary>
        internal static readonly Color CyanAccent = new Color(0.239f, 1f, 0.816f, 1f);
        internal static readonly Color AmberLock = new Color(1f, 0.7f, 0.15f, 1f);
        internal static readonly Color LedGreen = new Color(0.25f, 1f, 0.45f, 1f);
        internal static readonly Color LedAmber = new Color(1f, 0.72f, 0.12f, 1f);
        internal static readonly Color GlowDark = new Color(0.05f, 0.22f, 0.12f, 0.55f);

        private static Texture2D? _pixel;
        private static GUIStyle? _header;
        private static GUIStyle? _tiny;
        private static GUIStyle? _muted;
        private static GUIStyle? _chipLabel;
        private static GUIStyle? _chipLabelOn;
        private static GUIStyle? _window;
        private static GUIStyle? _strip;
        private static GUIStyle? _lockBadge;
        private static GUIStyle? _footerBrand;
        private static GUIStyle? _titleStyle;
        private static Font? _hudFont;
        private static Font? _brandFont;

        internal static Texture2D Pixel
        {
            get
            {
                if (_pixel == null)
                {
                    _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point
                    };
                    _pixel.SetPixel(0, 0, Color.white);
                    _pixel.Apply();
                }

                return _pixel;
            }
        }

        internal static Rect Snap(Rect r)
        {
            return new Rect(
                Mathf.Round(r.x),
                Mathf.Round(r.y),
                Mathf.Round(r.width),
                Mathf.Round(r.height));
        }

        internal static Vector2 Snap(Vector2 v)
        {
            return new Vector2(Mathf.Round(v.x), Mathf.Round(v.y));
        }

        internal static GUIStyle HeaderStyle => Ensure(ref _header, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontStyle = FontStyle.Bold,
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                fixedHeight = 0f,
                padding = new RectOffset(2, 2, 1, 1),
                normal = { textColor = Phosphor }
            };
            return s;
        });

        internal static GUIStyle TinyStyle => Ensure(ref _tiny, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(1, 1, 0, 0),
                normal = { textColor = new Color(0.65f, 0.98f, 0.72f, 0.95f) }
            };
            return s;
        });

        internal static GUIStyle MutedStyle => Ensure(ref _muted, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                clipping = TextClipping.Clip,
                normal = { textColor = PhosphorMuted }
            };
            return s;
        });

        internal static GUIStyle ChipLabelStyle => Ensure(ref _chipLabel, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.55f, 0.95f, 0.7f, 1f) }
            };
            return s;
        });

        internal static GUIStyle ChipLabelOnStyle => Ensure(ref _chipLabelOn, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.04f, 0.1f, 0.06f, 1f) }
            };
            return s;
        });

        internal static GUIStyle StripStyle => Ensure(ref _strip, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                wordWrap = false,
                padding = new RectOffset(4, 4, 1, 1),
                normal = { textColor = Phosphor }
            };
            return s;
        });

        internal static GUIStyle LockBadgeStyle => Ensure(ref _lockBadge, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.08f, 0.04f, 0.01f, 1f) }
            };
            return s;
        });

        internal static GUIStyle FooterBrandStyle => Ensure(ref _footerBrand, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip,
                normal = { textColor = new Color(0.55f, 0.85f, 0.62f, 0.8f) }
            };
            return s;
        });

        internal static GUIStyle TitleStyle => Ensure(ref _titleStyle, () =>
        {
            GUIStyle s = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                normal = { textColor = Phosphor }
            };
            return s;
        });

        /// <summary>Transparent frameless window chrome — content drawn manually.</summary>
        internal static GUIStyle WindowStyle
        {
            get
            {
                if (_window == null)
                {
                    _window = new GUIStyle(GUI.skin.window)
                    {
                        fontSize = 1,
                        border = new RectOffset(0, 0, 0, 0),
                        padding = new RectOffset(0, 0, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0),
                        overflow = new RectOffset(0, 0, 0, 0),
                        contentOffset = Vector2.zero
                    };
                    Texture2D empty = Pixel;
                    _window.normal.background = empty;
                    _window.onNormal.background = empty;
                    _window.active.background = empty;
                    _window.onActive.background = empty;
                    _window.focused.background = empty;
                    _window.onFocused.background = empty;
                    _window.hover.background = empty;
                    _window.onHover.background = empty;
                    _window.normal.textColor = Color.clear;
                    _window.onNormal.textColor = Color.clear;
                }

                return _window;
            }
        }

        internal static void ApplyFonts(Font? hudFont, Font? brandFont)
        {
            _hudFont = hudFont;
            _brandFont = brandFont;
            ApplyOne(_header, hudFont);
            ApplyOne(_tiny, hudFont);
            ApplyOne(_muted, hudFont);
            ApplyOne(_chipLabel, hudFont);
            ApplyOne(_chipLabelOn, hudFont);
            ApplyOne(_strip, hudFont);
            ApplyOne(_lockBadge, hudFont);
            ApplyOne(_titleStyle, hudFont);
            ApplyOne(_footerBrand, brandFont ?? hudFont);
        }

        private static void ApplyOne(GUIStyle? style, Font? font)
        {
            if (style != null && font != null)
            {
                style.font = font;
            }
        }

        private static GUIStyle Ensure(ref GUIStyle? slot, System.Func<GUIStyle> factory)
        {
            if (slot == null)
            {
                slot = factory();
                Font? font = slot == _footerBrand ? (_brandFont ?? _hudFont) : _hudFont;
                if (font != null)
                {
                    slot.font = font;
                }
            }

            return slot;
        }

        internal static void Fill(Rect rect, Color color)
        {
            Rect r = Snap(rect);
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Pixel);
            GUI.color = prev;
        }

        internal static void Border(Rect rect, Color color, float width = 1.5f)
        {
            Rect r = Snap(rect);
            float w = Mathf.Max(1.5f, width);
            Fill(new Rect(r.x, r.y, r.width, w), color);
            Fill(new Rect(r.x, r.yMax - w, r.width, w), color);
            Fill(new Rect(r.x, r.y, w, r.height), color);
            Fill(new Rect(r.xMax - w, r.y, w, r.height), color);
        }

        /// <summary>Dark bezel + double border + corner brackets for MFD chrome.</summary>
        internal static void HudPanel(Rect rect, bool accentTop = true)
        {
            // CRT: flat near-black fill + thin phosphor border. No bezel / brackets / glow chrome.
            Rect r = Snap(rect);
            Fill(r, PanelBg);
            Border(r, PhosphorDim, 1f);
        }

        internal static void CornerBrackets(Rect rect, Color color, float arm = 14f, float width = 2f)
        {
            Rect r = Snap(rect);
            float a = arm;
            float w = width;
            // NW
            Fill(new Rect(r.x, r.y, a, w), color);
            Fill(new Rect(r.x, r.y, w, a), color);
            // NE
            Fill(new Rect(r.xMax - a, r.y, a, w), color);
            Fill(new Rect(r.xMax - w, r.y, w, a), color);
            // SW
            Fill(new Rect(r.x, r.yMax - w, a, w), color);
            Fill(new Rect(r.x, r.yMax - a, w, a), color);
            // SE
            Fill(new Rect(r.xMax - a, r.yMax - w, a, w), color);
            Fill(new Rect(r.xMax - w, r.yMax - a, w, a), color);
        }

        /// <summary>Optional CRT scanlines (PPI). Gated by Config.DrawScanlines; cheap every-2px / every-other-frame.</summary>
        internal static void Scanlines(Rect rect, float alpha = 0.025f, float spacing = 6f)
        {
            if (!Config.DrawScanlines.Value)
            {
                return;
            }

            // Skip alternate frames under PerfMode (still readable flicker).
            if (Config.PerfMode.Value && (Time.frameCount & 1) != 0)
            {
                return;
            }

            Rect r = Snap(rect);
            float step = Config.PerfMode.Value ? Mathf.Max(spacing, 8f) : Mathf.Max(2f, spacing);
            Color c = new Color(0.4f, 1f, 0.7f, Config.PerfMode.Value ? alpha * 0.65f : alpha);
            float yOff = (Time.unscaledTime * 8f) % step;
            for (float y = r.y + yOff; y < r.yMax; y += step)
            {
                Fill(new Rect(r.x, Mathf.Round(y), r.width, 1f), c);
            }
        }

        /// <summary>Custom chip / mode hitbox — truncated bar, not GUI.skin.button.</summary>
        internal static bool ChipButton(Rect rect, string label, bool active)
        {
            Rect r = Snap(rect);
            Color fill = active
                ? new Color(0.35f, 0.95f, 0.55f, 0.95f)
                : new Color(0.03f, 0.1f, 0.08f, 0.85f);
            Color outline = active ? Phosphor : PanelBorderOuter;

            // Truncated rectangle (cut corners)
            Fill(new Rect(r.x + 3f, r.y, r.width - 6f, r.height), fill);
            Fill(new Rect(r.x, r.y + 3f, r.width, r.height - 6f), fill);
            Border(r, outline, active ? 1.75f : 1.5f);

            if (active)
            {
                // Small triangle tick under active chip
                float mid = r.x + r.width * 0.5f;
                float ty = r.yMax - 1f;
                Line(new Vector2(mid - 4f, ty), new Vector2(mid, ty - 4f), Phosphor, 1.5f);
                Line(new Vector2(mid + 4f, ty), new Vector2(mid, ty - 4f), Phosphor, 1.5f);
            }

            GUI.Label(r, label, active ? ChipLabelOnStyle : ChipLabelStyle);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        internal static void StatusLed(Vector2 center, Color color, float radius = 4f)
        {
            Vector2 c = Snap(center);
            Fill(new Rect(c.x - radius - 1f, c.y - radius - 1f, (radius + 1f) * 2f, (radius + 1f) * 2f), GlowDark);
            Fill(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), color);
        }

        internal static void Line(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 sa = Snap(a);
            Vector2 sb = Snap(b);
            Vector2 d = sb - sa;
            float len = d.magnitude;
            if (len < 0.5f)
            {
                return;
            }

            float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            Color prev = GUI.color;
            Matrix4x4 matrix = GUI.matrix;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(angle, sa);
            GUI.DrawTexture(new Rect(sa.x, sa.y - width * 0.5f, len, width), Pixel);
            GUI.matrix = matrix;
            GUI.color = prev;
        }

        /// <summary>Soft outer glow (darker thicker) then sharp bright stroke.</summary>
        internal static void GlowLine(Vector2 a, Vector2 b, Color color, float width = 1.75f)
        {
            Color glow = new Color(color.r * 0.25f, color.g * 0.35f, color.b * 0.25f, color.a * 0.45f);
            Line(a, b, glow, width + 2.5f);
            Line(a, b, color, width);
        }

        internal static void Circle(Vector2 center, float radius, Color color, float width)
        {
            const int segments = 64;
            Vector2 c = Snap(center);
            Vector2 prev = c + new Vector2(0f, -radius);
            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments * Mathf.PI * 2f;
                Vector2 next = c + new Vector2(Mathf.Sin(t), -Mathf.Cos(t)) * radius;
                Line(prev, next, color, width);
                prev = next;
            }
        }

        internal static void GlowCircle(Vector2 center, float radius, Color color, float width = 1.75f)
        {
            Color glow = new Color(color.r * 0.25f, color.g * 0.35f, color.b * 0.25f, color.a * 0.4f);
            Circle(center, radius, glow, width + 2.5f);
            Circle(center, radius, color, width);
        }

        internal static void Wedge(Vector2 center, float radius, float startDeg, float endDeg, Color color)
        {
            const int segments = 28;
            Vector2 c = Snap(center);
            Vector2 prev = c;
            for (int i = 0; i <= segments; i++)
            {
                float t = Mathf.Lerp(startDeg, endDeg, i / (float)segments) * Mathf.Deg2Rad;
                Vector2 next = c + new Vector2(Mathf.Sin(t), -Mathf.Cos(t)) * radius;
                Line(prev, next, color, 1.6f);
                prev = next;
            }

            Vector2 a = c + new Vector2(Mathf.Sin(startDeg * Mathf.Deg2Rad), -Mathf.Cos(startDeg * Mathf.Deg2Rad)) * radius;
            Vector2 b = c + new Vector2(Mathf.Sin(endDeg * Mathf.Deg2Rad), -Mathf.Cos(endDeg * Mathf.Deg2Rad)) * radius;
            Line(c, a, color, 1.6f);
            Line(c, b, color, 1.6f);
        }

        internal static void Diamond(Vector2 center, float size, Color color)
        {
            Vector2 c = Snap(center);
            Vector2 n = c + new Vector2(0f, -size);
            Vector2 e = c + new Vector2(size, 0f);
            Vector2 s = c + new Vector2(0f, size);
            Vector2 w = c + new Vector2(-size, 0f);
            GlowLine(n, e, color, 1.75f);
            GlowLine(e, s, color, 1.75f);
            GlowLine(s, w, color, 1.75f);
            GlowLine(w, n, color, 1.75f);
        }

        internal static void Chevron(Vector2 center, float size, Color color)
        {
            Vector2 c = Snap(center);
            Vector2 tip = c + new Vector2(0f, -size);
            Vector2 left = c + new Vector2(-size * 0.75f, size * 0.65f);
            Vector2 right = c + new Vector2(size * 0.75f, size * 0.65f);
            Vector2 baseMid = c + new Vector2(0f, size * 0.25f);
            GlowLine(left, tip, color, 2.2f);
            GlowLine(right, tip, color, 2.2f);
            Line(left, baseMid, color, 1.75f);
            Line(right, baseMid, color, 1.75f);
        }

        
        /// <summary>Missile / threat chevron tip (triangle pointing up).</summary>
        internal static void Triangle(Vector2 center, float size, Color color)
        {
            Vector2 c = Snap(center);
            Vector2 tip = c + new Vector2(0f, -size);
            Vector2 left = c + new Vector2(-size * 0.7f, size * 0.55f);
            Vector2 right = c + new Vector2(size * 0.7f, size * 0.55f);
            GlowLine(left, tip, color, 2f);
            GlowLine(right, tip, color, 2f);
            Line(left, right, color, 1.75f);
        }

internal static void Dot(Vector2 center, float size, Color color)
        {
            Vector2 c = Snap(center);
            Fill(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), color);
        }

        /// <summary>Hollow square used for datalink (DL) symbology.</summary>
        internal static void HollowBox(Vector2 center, float half, Color color, float width = 1.75f)
        {
            Vector2 c = Snap(center);
            Vector2 ne = c + new Vector2(half, -half);
            Vector2 se = c + new Vector2(half, half);
            Vector2 sw = c + new Vector2(-half, half);
            Vector2 nw = c + new Vector2(-half, -half);
            Line(nw, ne, color, width);
            Line(ne, se, color, width);
            Line(se, sw, color, width);
            Line(sw, nw, color, width);
        }

        /// <summary>Dashed hollow square for coasting / stale datalink tracks.</summary>
        internal static void DashedBox(Vector2 center, float half, Color color, float width = 1.5f)
        {
            Vector2 c = Snap(center);
            Vector2 nw = c + new Vector2(-half, -half);
            Vector2 ne = c + new Vector2(half, -half);
            Vector2 se = c + new Vector2(half, half);
            Vector2 sw = c + new Vector2(-half, half);
            DashedSegment(nw, ne, color, width);
            DashedSegment(ne, se, color, width);
            DashedSegment(se, sw, color, width);
            DashedSegment(sw, nw, color, width);
        }

        private static void DashedSegment(Vector2 a, Vector2 b, Color color, float width)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f)
            {
                return;
            }

            Vector2 dir = d / len;
            const float dash = 3.5f;
            const float gap = 2.5f;
            float t = 0f;
            while (t < len)
            {
                float t1 = Mathf.Min(len, t + dash);
                Line(a + dir * t, a + dir * t1, color, width);
                t = t1 + gap;
            }
        }


        /// <summary>Dashed range circle (ACM A/G CRT).</summary>
        internal static void DashedCircle(Vector2 center, float radius, Color color, float width = 1.25f, int dashes = 48)
        {
            Vector2 c = Snap(center);
            float step = 360f / Mathf.Max(8, dashes);
            for (int i = 0; i < dashes; i += 2)
            {
                float a0 = (i * step) * Mathf.Deg2Rad;
                float a1 = ((i + 1) * step) * Mathf.Deg2Rad;
                Vector2 p0 = c + new Vector2(Mathf.Sin(a0), -Mathf.Cos(a0)) * radius;
                Vector2 p1 = c + new Vector2(Mathf.Sin(a1), -Mathf.Cos(a1)) * radius;
                Line(p0, p1, color, width);
            }
        }

        /// <summary>Vertical scan bar sweeping L↔R inside dashed circle (ACM A/G). Clipped to circle.
        /// Uses linear az/halfFov mapping (same as contact plot) so the beam glides continuously.</summary>
        internal static void VerticalScanBar(Vector2 center, float radius, float azimuthDeg, Color color, float width = 2f)
        {
            VerticalScanBar(center, radius, azimuthDeg, halfFovDeg: 90f, color, width);
        }

        internal static void VerticalScanBar(Vector2 center, float radius, float azimuthDeg, float halfFovDeg, Color color, float width = 2f)
        {
            float half = Mathf.Max(5f, halfFovDeg);
            float xn = Mathf.Clamp(azimuthDeg / half, -1f, 1f);
            float x = center.x + xn * radius * 0.92f;
            float dx = x - center.x;
            float inside = radius * radius - dx * dx;
            if (inside <= 0.5f)
            {
                return; // would be outside circle
            }

            float maxDy = Mathf.Sqrt(inside) * 0.98f;
            Vector2 a = Snap(new Vector2(x, center.y - maxDy));
            Vector2 b = Snap(new Vector2(x, center.y + maxDy));
            Line(a, b, color, width);
        }

        /// <summary>Center stack of parallel vertical lines (single-target STT page).</summary>
        internal static void ParallelVerticalStack(Vector2 center, float halfH, int count, float spacing, Color color, float width = 1f)
        {
            int n = Mathf.Max(1, count);
            float total = (n - 1) * spacing;
            float x0 = center.x - total * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float x = x0 + i * spacing;
                Line(new Vector2(x, center.y - halfH), new Vector2(x, center.y + halfH), color, width);
            }
        }

        /// <summary>Corner-bracket lock pip with center dot (single STT).</summary>
        internal static void LockPipBrackets(Vector2 center, float half, Color color, float width = 1.5f)
        {
            Vector2 c = Snap(center);
            float h = half;
            float arm = h * 0.55f;
            // TL
            Line(new Vector2(c.x - h, c.y - h), new Vector2(c.x - h + arm, c.y - h), color, width);
            Line(new Vector2(c.x - h, c.y - h), new Vector2(c.x - h, c.y - h + arm), color, width);
            // TR
            Line(new Vector2(c.x + h, c.y - h), new Vector2(c.x + h - arm, c.y - h), color, width);
            Line(new Vector2(c.x + h, c.y - h), new Vector2(c.x + h, c.y - h + arm), color, width);
            // BL
            Line(new Vector2(c.x - h, c.y + h), new Vector2(c.x - h + arm, c.y + h), color, width);
            Line(new Vector2(c.x - h, c.y + h), new Vector2(c.x - h, c.y + h - arm), color, width);
            // BR
            Line(new Vector2(c.x + h, c.y + h), new Vector2(c.x + h - arm, c.y + h), color, width);
            Line(new Vector2(c.x + h, c.y + h), new Vector2(c.x + h, c.y + h - arm), color, width);
            Dot(c, 3f, color);
        }

        /// <summary>Strip / data-line label — always clipped inside fixed rect (no overflow past MFD edges).</summary>
        internal static void StripLabel(Rect rect, string text, GUIStyle style)
        {
            ClippedLabel(rect, text, style);
        }

        /// <summary>Clipped label helper — BeginGroup so text never escapes the strip.</summary>
        internal static void ClippedLabel(Rect rect, string text, GUIStyle style)
        {
            Rect r = Snap(rect);
            GUI.BeginGroup(r);
            GUI.Label(new Rect(0f, 0f, r.width, r.height), text, style);
            GUI.EndGroup();
        }
    }
}
