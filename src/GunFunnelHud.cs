using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Su-27 / Flanker-style LCOS gun funnel (IMGUI OnGUI):
    /// converging ladder of dashed diamonds/chevrons from gun bore toward lead pip,
    /// plus a gravity-drop cue along the lead line. Color by HitVerdict.
    /// </summary>
    internal static class GunFunnelHud
    {
        private const int FunnelSteps = 10;
        private const float BoresightSize = 7f;
        private const float LeadSize = 9f;

        private static GUIStyle? _labelStyle;

        internal static void Draw(ContactProvider contacts)
        {
            if (!Config.ShowGunFunnel.Value || contacts == null || !contacts.ShouldDrawHud)
            {
                return;
            }

            HitSolution sol = contacts.LastHitSolution;
            if (sol == null || !sol.Valid || sol.Kind != WeaponKind.Gun)
            {
                return;
            }

            if (!sol.CanHit && sol.Verdict == HitVerdict.Unknown)
            {
                return;
            }

            if (float.IsNaN(sol.EtaSec) || float.IsInfinity(sol.EtaSec))
            {
                return;
            }

            Camera? cam = WeaponReflect.FindMainCamera();
            if (cam == null)
            {
                return;
            }

            Vector3 boreDir = sol.BoresightDir.sqrMagnitude > 0.01f
                ? sol.BoresightDir.normalized
                : Vector3.forward;
            float leadDist = sol.LeadWorld.sqrMagnitude > 0.01f
                ? Vector3.Distance(sol.MuzzleWorld, sol.LeadWorld)
                : Mathf.Max(50f, sol.RangeMeters * 0.15f);
            Vector3 boreWorld = sol.MuzzleWorld + boreDir * Mathf.Max(20f, leadDist);

            Vector3 boreScreen3 = cam.WorldToScreenPoint(boreWorld);
            Vector3 leadScreen3 = cam.WorldToScreenPoint(sol.LeadWorld);
            if (boreScreen3.z <= 0f || leadScreen3.z <= 0f)
            {
                return;
            }

            Vector2 bore = ToGui(boreScreen3);
            Vector2 lead = ToGui(leadScreen3);

            Color color = ColorFor(sol.Verdict);
            Color dim = new Color(color.r, color.g, color.b, color.a * 0.55f);

            DrawSu27Funnel(bore, lead, color, dim, sol);
            DrawCross(bore, BoresightSize, color);
            ScopeDraw.Circle(bore, BoresightSize * 0.45f, dim, 1f);
            ScopeDraw.Diamond(lead, LeadSize * 0.55f, color);
            ScopeDraw.Dot(lead, 3f, color);

            EnsureLabel();
            string tag = sol.VerdictTag + "  " + sol.EtaSec.ToString("0.0") + "s";
            Color prev = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(lead.x + 10f, lead.y - 8f, 90f, 16f), tag, _labelStyle);
            GUI.color = prev;
        }

        /// <summary>
        /// Classic Flanker LCOS: ladder of decreasing diamonds/chevrons along bore→lead,
        /// dashed guide rails, and a gravity-drop arc below the geometric lead line.
        /// </summary>
        private static void DrawSu27Funnel(Vector2 bore, Vector2 lead, Color color, Color dim, HitSolution sol)
        {
            Vector2 delta = lead - bore;
            float len = delta.magnitude;
            if (len < 4f)
            {
                ScopeDraw.Diamond(lead, 12f, color);
                return;
            }

            Vector2 dir = delta / len;
            Vector2 perp = new Vector2(-dir.y, dir.x);

            float startHalf = Mathf.Clamp(len * 0.28f, 18f, 56f);
            float endHalf = Mathf.Clamp(len * 0.035f, 2.5f, 8f);

            // Gravity drop cue: secondary dashed curve sagging below the lead line.
            float dropPx = Mathf.Clamp(len * 0.08f + Mathf.Max(0f, sol.EtaSec) * 6f, 6f, 36f);
            DrawGravityDrop(bore, lead, dir, perp, dropPx, dim);

            // Ladder of tapering diamonds / chevrons (Su-27 pipper funnel).
            for (int i = 0; i < FunnelSteps; i++)
            {
                float u = (i + 1) / (float)(FunnelSteps + 1);
                Vector2 center = Vector2.Lerp(bore, lead, u);
                float half = Mathf.Lerp(startHalf, endHalf, u * u); // accelerate taper toward lead
                Color c = (i % 2 == 0) ? color : dim;

                if (i < FunnelSteps / 2)
                {
                    // Outer half: dashed diamond rungs
                    DrawDashedDiamond(center, half, c, 1.15f);
                }
                else
                {
                    // Inner half: chevron / V markers pointing to lead
                    DrawChevron(center, dir, perp, half, c, 1.2f);
                }
            }

            // Two converging dashed rails (classic funnel sides).
            Vector2 rail0a = bore + perp * startHalf;
            Vector2 rail0b = lead + perp * endHalf;
            Vector2 rail1a = bore - perp * startHalf;
            Vector2 rail1b = lead - perp * endHalf;
            DrawDashedLine(rail0a, rail0b, dim, 1.1f, 14);
            DrawDashedLine(rail1a, rail1b, dim, 1.1f, 14);

            // Center dashed lead line (bore → lead).
            DrawDashedLine(bore, lead, new Color(color.r, color.g, color.b, color.a * 0.4f), 1f, 16);
        }

        private static void DrawGravityDrop(Vector2 bore, Vector2 lead, Vector2 dir, Vector2 perp, float dropPx, Color dim)
        {
            // Quadratic sag in screen space (down = +Y in IMGUI). Prefer "down" on screen.
            Vector2 down = Vector2.up; // IMGUI Y grows downward
            // Bias sag toward world-down if perp aligns poorly.
            Vector2 sagDir = down;
            const int segs = 8;
            Vector2 prev = bore;
            for (int i = 1; i <= segs; i++)
            {
                float u = i / (float)segs;
                Vector2 onLine = Vector2.Lerp(bore, lead, u);
                // Parabola peaking mid-way: 4u(1-u)
                float sag = 4f * u * (1f - u) * dropPx;
                Vector2 p = onLine + sagDir * sag;
                if ((i & 1) == 1)
                {
                    ScopeDraw.Line(prev, p, dim, 1f);
                }

                prev = p;
            }
        }

        private static void DrawDashedDiamond(Vector2 center, float half, Color color, float width)
        {
            Vector2 n = center + new Vector2(0f, -half);
            Vector2 e = center + new Vector2(half, 0f);
            Vector2 s = center + new Vector2(0f, half);
            Vector2 w = center + new Vector2(-half, 0f);
            DrawDashedLine(n, e, color, width, 4);
            DrawDashedLine(e, s, color, width, 4);
            DrawDashedLine(s, w, color, width, 4);
            DrawDashedLine(w, n, color, width, 4);
        }

        private static void DrawChevron(Vector2 center, Vector2 dir, Vector2 perp, float half, Color color, float width)
        {
            // V opening toward bore, tip toward lead.
            Vector2 tip = center + dir * (half * 0.35f);
            Vector2 left = center - dir * (half * 0.25f) + perp * half;
            Vector2 right = center - dir * (half * 0.25f) - perp * half;
            ScopeDraw.Line(left, tip, color, width);
            ScopeDraw.Line(right, tip, color, width);
        }

        private static void DrawDashedLine(Vector2 a, Vector2 b, Color color, float width, int dashes)
        {
            dashes = Mathf.Max(2, dashes);
            for (int i = 0; i < dashes; i++)
            {
                if ((i & 1) == 1)
                {
                    continue;
                }

                float u0 = i / (float)dashes;
                float u1 = (i + 1) / (float)dashes;
                ScopeDraw.Line(Vector2.Lerp(a, b, u0), Vector2.Lerp(a, b, u1), color, width);
            }
        }

        private static void DrawCross(Vector2 center, float size, Color color)
        {
            ScopeDraw.Line(center + new Vector2(-size, 0f), center + new Vector2(size, 0f), color, 1.4f);
            ScopeDraw.Line(center + new Vector2(0f, -size), center + new Vector2(0f, size), color, 1.4f);
        }

        private static Vector2 ToGui(Vector3 screen)
        {
            return new Vector2(screen.x, Screen.height - screen.y);
        }

        private static Color ColorFor(HitVerdict verdict) => verdict switch
        {
            HitVerdict.Hit => new Color(0.25f, 0.95f, 0.4f, 0.95f),
            HitVerdict.Marginal => new Color(0.95f, 0.85f, 0.2f, 0.95f),
            HitVerdict.No => new Color(0.85f, 0.25f, 0.25f, 0.85f),
            _ => new Color(0.6f, 0.65f, 0.7f, 0.8f)
        };

        private static void EnsureLabel()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
        }
    }
}
