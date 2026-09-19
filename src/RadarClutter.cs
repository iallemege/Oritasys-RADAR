using System.Collections.Generic;
using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Cheap IMGUI radar clutter (ground speckles, scan noise, radial grain).
    /// Draw-only — never creates RadarContact / lockable tracks.
    /// </summary>
    internal static class RadarClutter
    {
        private const int GroundDots = 96;
        private const int ScanBlips = 18;
        private const int GrainRays = 48;

        /// <summary>Classic PPI polar clutter inside the scope circle.</summary>
        internal static void DrawPpi(
            Vector2 center,
            float radius,
            ModeState modes,
            float halfFovDeg,
            float coneCenterDeg,
            float sweepDeg)
        {
            if (!ShouldDraw(modes))
            {
                return;
            }

            float intensity = Mathf.Clamp01(Config.ClutterIntensity.Value);
            if (Config.PerfMode.Value)
            {
                intensity *= 0.55f;
            }

            float elev = modes.AntennaElevationDeg;
            float lookDown = Mathf.Clamp01((-elev) / 40f); // 0 look-level → 1 strong look-down
            float groundMul = GroundMul(modes.Waveform) * (0.35f + 0.65f * lookDown);
            float weatherMul = WeatherMul(modes.Waveform);

            // Sea/land radial grain (subtle) — skip under PerfMode (expensive line fan)
            if (!Config.PerfMode.Value)
            {
                DrawRadialGrain(center, radius, intensity * 0.35f * (0.5f + 0.5f * groundMul));
            }

            // Ground clutter ring — denser at mid/near range when look-down / PULSE
            float countScale = Config.PerfMode.Value ? 0.35f : 1f;
            int groundN = Mathf.RoundToInt(GroundDots * intensity * (0.4f + 0.6f * groundMul) * countScale);
            uint seed = Hash((uint)(Time.frameCount / 2), 0xC1A77Eu) ^ (uint)(modes.ScanAzimuthDeg * 10f);
            for (int i = 0; i < groundN; i++)
            {
                float u = Frac(HashFloat(seed, (uint)i));
                float v = Frac(HashFloat(seed, (uint)(i + 101)));
                // Bias toward near/mid range under look-down
                float rangeU = Mathf.Pow(u, 1f / (1f + lookDown * 1.2f));
                float ang = (v * 360f) - 180f;
                // Soft preference for lower half / look-down wedge noise
                float a = ang * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (radius * rangeU);
                if ((pos - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }

                float alpha = intensity * groundMul * (0.08f + 0.18f * (1f - rangeU)) *
                              (0.6f + 0.4f * Frac(HashFloat(seed, (uint)(i + 7))));
                ScopeDraw.Dot(pos, 1.2f + 1.4f * (1f - rangeU), new Color(0.2f, 0.85f, 0.4f, alpha));
            }

            // Main-lobe / scan-wedge transient blips
            float half = Mathf.Max(5f, halfFovDeg);
            int blipN = Mathf.RoundToInt(ScanBlips * intensity * (0.5f + 0.5f * weatherMul) * countScale);
            uint seed2 = Hash((uint)(Time.frameCount / 3), 0x5CAFu) ^ (uint)(sweepDeg * 17f);
            for (int i = 0; i < blipN; i++)
            {
                float u = Frac(HashFloat(seed2, (uint)i));
                float v = Frac(HashFloat(seed2, (uint)(i + 33)));
                float az = coneCenterDeg + (v * 2f - 1f) * half;
                // Decay with angular distance from sweep
                float dSweep = Mathf.Abs(Mathf.DeltaAngle(az, sweepDeg));
                float decay = Mathf.Clamp01(1f - dSweep / Mathf.Max(8f, half * 0.55f));
                if (decay < 0.05f)
                {
                    continue;
                }

                float rangeU = 0.15f + 0.8f * u;
                float a = az * Mathf.Deg2Rad;
                Vector2 pos = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * (radius * rangeU);
                float alpha = intensity * weatherMul * decay * (0.12f + 0.2f * Frac(HashFloat(seed2, (uint)(i + 9))));
                ScopeDraw.Dot(pos, 1.5f, new Color(0.35f, 1f, 0.55f, alpha));
            }
        }

        private struct AcmSpeckle
        {
            internal float Xn;
            internal float Yn;
            internal float Born;
            internal float Size;
            internal float BaseAlpha;
        }

        private static readonly List<AcmSpeckle> AcmTrail = new List<AcmSpeckle>(256);
        private static float _lastAcmBarXn = float.NaN;

        /// <summary>
        /// ACM B-scope scroll: spawn phosphor speckles at the L↔R scan bar; they fade after
        /// <paramref name="persistenceSec"/> — unswept areas stay empty/dark (not static 360 PPI).
        /// </summary>
        internal static void DrawAcm(
            Vector2 center,
            float radius,
            ModeState modes,
            float halfFovDeg,
            float scanAzimuthDeg,
            float persistenceSec = 0.8f)
        {
            if (!ShouldDraw(modes))
            {
                AcmTrail.Clear();
                return;
            }

            float intensity = Mathf.Clamp01(Config.ClutterIntensity.Value);
            if (Config.PerfMode.Value)
            {
                intensity *= 0.5f;
            }

            float groundMul = GroundMul(modes.Waveform) * 1.15f;
            float weatherMul = WeatherMul(modes.Waveform);
            float half = Mathf.Max(5f, halfFovDeg);
            float barXn = Mathf.Clamp(scanAzimuthDeg / half, -1f, 1f);
            float now = Time.unscaledTime;
            float ttl = Mathf.Clamp(persistenceSec, 0.2f, 2.5f);

            // Spawn a vertical column of speckles at the current bar (reel / scroll feel).
            bool moved = float.IsNaN(_lastAcmBarXn) || Mathf.Abs(barXn - _lastAcmBarXn) > 0.002f;
            _lastAcmBarXn = barXn;
            if (moved || AcmTrail.Count < 8)
            {
                int spawn = Mathf.RoundToInt((Config.PerfMode.Value ? 4f : 10f) * intensity * (0.5f + 0.5f * groundMul));
                uint seed = Hash((uint)(Time.frameCount), 0xAC3u) ^ (uint)(scanAzimuthDeg * 97f);
                for (int i = 0; i < spawn; i++)
                {
                    float u = Frac(HashFloat(seed, (uint)i));
                    float jitter = (Frac(HashFloat(seed, (uint)(i + 9))) - 0.5f) * 0.06f;
                    float xn = Mathf.Clamp(barXn + jitter, -1f, 1f);
                    float yn = Mathf.Pow(u, 0.85f); // 0 near → 1 far (up)
                    AcmTrail.Add(new AcmSpeckle
                    {
                        Xn = xn,
                        Yn = yn,
                        Born = now,
                        Size = 1.1f + 1.3f * (1f - yn),
                        BaseAlpha = intensity * groundMul * (0.12f + 0.28f * (1f - yn)) *
                                    (0.55f + 0.45f * Frac(HashFloat(seed, (uint)(i + 21))))
                    });
                }

                // Brighter weather blips on the bar itself
                int blipN = Mathf.RoundToInt(4f * intensity * weatherMul);
                uint seed2 = Hash((uint)(Time.frameCount), 0xBAADu);
                for (int i = 0; i < blipN; i++)
                {
                    float u = Frac(HashFloat(seed2, (uint)i));
                    AcmTrail.Add(new AcmSpeckle
                    {
                        Xn = barXn + (Frac(HashFloat(seed2, (uint)(i + 3))) - 0.5f) * 0.04f,
                        Yn = u,
                        Born = now,
                        Size = 1.6f,
                        BaseAlpha = intensity * weatherMul * (0.2f + 0.3f * Frac(HashFloat(seed2, (uint)(i + 11))))
                    });
                }
            }

            // Cap trail length
            while (AcmTrail.Count > (Config.PerfMode.Value ? 120 : 320))
            {
                AcmTrail.RemoveAt(0);
            }

            // Draw + cull expired (scanned area disappears after persistence)
            for (int i = AcmTrail.Count - 1; i >= 0; i--)
            {
                AcmSpeckle s = AcmTrail[i];
                float age = now - s.Born;
                if (age > ttl)
                {
                    AcmTrail.RemoveAt(i);
                    continue;
                }

                float life = Mathf.Clamp01(1f - age / ttl);
                // Slight extra fade once the bar has moved past this az
                float past = Mathf.Abs(s.Xn - barXn);
                if (past > 0.08f)
                {
                    life *= Mathf.Clamp01(1f - (past - 0.08f) * 0.35f);
                }

                if (life < 0.03f)
                {
                    continue;
                }

                Vector2 pos = center + new Vector2(s.Xn * radius * 0.92f, -s.Yn * radius * 0.92f);
                if ((pos - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }

                float alpha = s.BaseAlpha * life;
                ScopeDraw.Dot(pos, s.Size, new Color(0.25f, 0.95f, 0.45f, alpha));
            }
        }

        /// <summary>Overload without persistence (uses config / default 0.8s).</summary>
        internal static void DrawAcm(
            Vector2 center,
            float radius,
            ModeState modes,
            float halfFovDeg,
            float scanAzimuthDeg)
        {
            float persist = Config.AcmPersistenceSec != null ? Config.AcmPersistenceSec.Value : 0.8f;
            DrawAcm(center, radius, modes, halfFovDeg, scanAzimuthDeg, persist);
        }

        private static bool ShouldDraw(ModeState modes)
        {
            if (!Config.ShowRadarClutter.Value || modes == null)
            {
                return false;
            }

            if (modes.Mode == RadarMode.Standby)
            {
                return false;
            }

            float intensity = Config.ClutterIntensity.Value;
            return intensity > 0.01f;
        }

        private static float GroundMul(RadarWaveform wf) => wf switch
        {
            RadarWaveform.Pulse => 1f,
            RadarWaveform.Pd => 0.22f,   // PD suppresses ground clutter
            RadarWaveform.Cw => 0.55f,
            _ => 0.7f
        };

        private static float WeatherMul(RadarWaveform wf) => wf switch
        {
            RadarWaveform.Pulse => 0.7f,
            RadarWaveform.Pd => 0.85f,   // PD keeps some weather speckles
            RadarWaveform.Cw => 0.5f,
            _ => 0.6f
        };

        private static void DrawRadialGrain(Vector2 center, float radius, float strength)
        {
            if (strength < 0.02f)
            {
                return;
            }

            uint seed = Hash((uint)(Time.frameCount / 8), 0xA11Fu);
            int n = Mathf.RoundToInt(GrainRays * Mathf.Clamp01(strength * 2f));
            for (int i = 0; i < n; i++)
            {
                float ang = (i / (float)n) * 360f + Frac(HashFloat(seed, (uint)i)) * 4f;
                float a = ang * Mathf.Deg2Rad;
                float r0 = radius * (0.2f + 0.55f * Frac(HashFloat(seed, (uint)(i + 19))));
                float r1 = r0 + radius * (0.08f + 0.12f * Frac(HashFloat(seed, (uint)(i + 41))));
                Vector2 p0 = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * r0;
                Vector2 p1 = center + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * Mathf.Min(radius, r1);
                float alpha = strength * (0.04f + 0.06f * Frac(HashFloat(seed, (uint)(i + 7))));
                ScopeDraw.Line(p0, p1, new Color(0.15f, 0.55f, 0.3f, alpha), 1f);
            }
        }

        private static uint Hash(uint a, uint b)
        {
            uint x = a * 747796405u + b * 2891336453u;
            x = (x ^ (x >> 16)) * 0x45d9f3bu;
            x = (x ^ (x >> 16)) * 0x45d9f3bu;
            return x ^ (x >> 16);
        }

        private static float HashFloat(uint seed, uint i)
        {
            uint h = Hash(seed, i);
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
