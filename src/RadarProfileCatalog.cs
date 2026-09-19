using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Builtin + capacity-derived + optional JSON override radar profiles.
    /// Resolve once per ownship instance change (see ContactProvider).
    /// </summary>
    internal static class RadarProfileCatalog
    {
        private const string OverrideFileName = "OritasyRadar.profiles.json";

        private static readonly Dictionary<string, AircraftRadarProfile> Builtins =
            new Dictionary<string, AircraftRadarProfile>(StringComparer.OrdinalIgnoreCase);

        private static readonly List<(string[] Keys, AircraftRadarProfile Profile)> Overrides =
            new List<(string[], AircraftRadarProfile)>();

        private static bool _builtinsSeeded;
        private static bool _overridesAttempted;

        internal static void EnsureReady()
        {
            SeedBuiltins();
            TryLoadOverridesOnce();
        }

        /// <summary>
        /// Resolve profile for an aircraft instance. Prefer JSON override → builtin key match →
        /// capacity/hybrid fallback (when enabled) → default.
        /// </summary>
        internal static AircraftRadarProfile Resolve(object? aircraft)
        {
            EnsureReady();

            string key = GameReflect.ResolveAircraftKey(aircraft);
            string label = string.IsNullOrEmpty(key) ? GameReflect.LabelOf(aircraft) : key;

            if (TryMatchOverride(key, label, out AircraftRadarProfile over))
            {
                // JSON may set radarRank; live rankRequired still preferred when present.
                int rk = ResolveRadarRank(aircraft, over.RadarRank, preferExplicit: true);
                return over.RadarRank == rk ? over : over.WithRank(rk);
            }

            if (TryMatchBuiltin(key, label, out AircraftRadarProfile builtin))
            {
                int rk = ResolveRadarRank(aircraft, builtin.RadarRank, preferExplicit: true);
                return builtin.RadarRank == rk ? builtin : builtin.WithRank(rk);
            }

            if (!Config.UseCapacityFallback.Value)
            {
                AircraftRadarProfile d = WithLabel(AircraftRadarProfile.Default, label);
                int rk = ResolveRadarRank(aircraft, d.RadarRank, preferExplicit: false);
                return d.WithRank(rk);
            }

            float maxCharge = 0f;
            float maxPower = 0f;
            bool hasSupply = GameReflect.TryGetPowerSupply(aircraft, out maxCharge, out maxPower);

            float? nativeRangeM = null;
            float? nativeConeDeg = null;
            if (Config.PreferNativeRadarStats.Value &&
                GameReflect.TryGetNativeRadarStats(aircraft, out float cone, out float rangeM))
            {
                if (cone > 0.1f)
                {
                    nativeConeDeg = cone;
                }

                if (rangeM > 1f)
                {
                    nativeRangeM = rangeM;
                }
            }

            if (!hasSupply && !nativeRangeM.HasValue && !nativeConeDeg.HasValue)
            {
                AircraftRadarProfile d = WithLabel(AircraftRadarProfile.Default, string.IsNullOrEmpty(label) ? "Unknown" : label);
                return d.WithRank(ResolveRadarRank(aircraft, d.RadarRank, preferExplicit: false));
            }

            AircraftRadarProfile derived = FromCapacity(maxCharge, maxPower, nativeRangeM, nativeConeDeg);
            string id = string.IsNullOrEmpty(key) ? derived.Id : NormalizeKey(key);
            string display = string.IsNullOrEmpty(label) ? derived.DisplayLabel : TrimLabel(label);
            int rank = ResolveRadarRank(aircraft, derived.RadarRank, preferExplicit: false);
            return new AircraftRadarProfile(
                id,
                display,
                derived.MaxRangeKm,
                derived.HalfFovDeg,
                derived.TwsHalfFov,
                derived.AcmHalfFov,
                derived.AcmMaxRangeKm,
                derived.MaxTracks,
                derived.ScanRateDegPerSec,
                derived.Source,
                rank);
        }

        /// <summary>
        /// Rank priority: JSON/builtin explicit (when <paramref name="preferExplicit"/>) →
        /// AircraftDefinition.aircraftParameters.rankRequired → capacity heuristic / fallback.
        /// Never uses player PlayerRank.
        /// </summary>
        private static int ResolveRadarRank(object? aircraft, int fallbackRank, bool preferExplicit)
        {
            if (preferExplicit && fallbackRank >= 1)
            {
                // Still allow live rankRequired to raise AESA if game says so and builtin was low?
                // Spec: builtin may set explicitly — trust explicit, but overlay rankRequired when present.
            }

            if (GameReflect.TryGetAircraftRankRequired(aircraft, out int req) && req >= 1)
            {
                return req;
            }

            return fallbackRank >= 1 ? fallbackRank : 2;
        }

        /// <summary>
        /// Map electrical capacity (prefer <paramref name="maxCharge"/> / 电容量; fallback <paramref name="maxPower"/>)
        /// to a radar envelope. Optionally blend with native Radar.radarCone (full cone deg) and
        /// RadarParameters.maxRange.
        /// Assumption: serialized <c>radarCone</c> is the **full** cone angle in degrees; half-FOV = cone/2.
        /// If the value is already small (&lt; 45°), treat it as half-FOV.
        /// </summary>
        internal static AircraftRadarProfile FromCapacity(
            float maxCharge,
            float maxPower,
            float? nativeRangeM,
            float? nativeConeDeg)
        {
            float capacity = maxCharge > 0.01f ? maxCharge : maxPower;
            if (capacity < 0f)
            {
                capacity = 0f;
            }

            // Soft log map: ~100 → low tier, ~10000 → high. Linear InverseLerp as secondary clamp.
            float tLog = 0f;
            if (capacity > 1f)
            {
                tLog = Mathf.Clamp01((Mathf.Log10(capacity) - 2f) / 2f); // 100..10000 → 0..1
            }

            float tLin = Mathf.Clamp01(Mathf.InverseLerp(80f, 9000f, capacity));
            float t = capacity <= 0.01f ? 0.35f : Mathf.Clamp01(tLog * 0.65f + tLin * 0.35f);

            float formulaRangeKm = Mathf.Lerp(25f, 220f, t);
            float halfFov = Mathf.Lerp(70f, 35f, t);
            // ACM A/G: wide mapping FOV + short surface range (≠ air envelope).
            float acmRange = Mathf.Clamp(Mathf.Min(25f, formulaRangeKm * 0.25f), 20f, 30f);
            int tracks = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(4f, 32f, t)), 4, 32);
            float acmHalf = Mathf.Lerp(60f, 50f, t);
            float scan = Mathf.Lerp(40f, 95f, t);

            RadarProfileSource source = RadarProfileSource.Capacity;

            if (nativeRangeM.HasValue && nativeRangeM.Value > 1f)
            {
                float nativeKm = nativeRangeM.Value > 500f
                    ? nativeRangeM.Value / 1000f
                    : nativeRangeM.Value;
                nativeKm = Mathf.Clamp(nativeKm, 5f, 400f);
                formulaRangeKm = (formulaRangeKm + nativeKm) * 0.5f;
                source = RadarProfileSource.Hybrid;
            }

            if (nativeConeDeg.HasValue && nativeConeDeg.Value > 0.1f)
            {
                // Treat radarCone as full cone degrees; half = cone/2.
                // If already small (< 45), assume it was authored as half-FOV.
                float cone = nativeConeDeg.Value;
                halfFov = cone >= 45f ? cone * 0.5f : cone;
                halfFov = Mathf.Clamp(halfFov, 8f, 90f);
                source = RadarProfileSource.Hybrid;
            }

            formulaRangeKm = Mathf.Clamp(formulaRangeKm, 25f, 220f);
            float twsHalf = halfFov * 0.65f;

            // Capacity heuristic: higher maxCharge → higher radar rank (mid default 2).
            // t 0→1 maps roughly rank 1..5. Rank ≥ 3 = AESA-like.
            int rank = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(1f, 5f, t)), 1, 5);
            if (capacity <= 0.01f)
            {
                rank = 2;
            }

            return new AircraftRadarProfile(
                "capacity",
                "CAP " + capacity.ToString("0", CultureInfo.InvariantCulture),
                formulaRangeKm,
                halfFov,
                twsHalf,
                acmHalf,
                Mathf.Clamp(acmRange, 20f, 30f),
                tracks,
                scan,
                source,
                rank);
        }

        private static void SeedBuiltins()
        {
            if (_builtinsSeeded)
            {
                return;
            }

            _builtinsSeeded = true;

            // Interceptor / air-superiority: high range, narrower ACM. Rank≥3 = AESA-like.
            AddBuiltin("revoker", "Revoker", 200f, 38f, 50f, 30f, 32, 90f, 5);
            AddBuiltin("medusa", "Medusa", 175f, 40f, 52f, 28f, 28, 85f, 4);
            AddBuiltin("compass", "Compass", 180f, 42f, 52f, 28f, 28, 80f, 4);
            AddBuiltin("smallfighter", "SmallFighter", 160f, 42f, 55f, 26f, 24, 75f, 3);
            AddBuiltin("fighter", "Fighter", 150f, 45f, 55f, 25f, 24, 70f, 3);

            // Strike / attack: medium envelope.
            AddBuiltin("darkreach", "Darkreach", 140f, 50f, 55f, 25f, 24, 60f, 3);
            AddBuiltin("chicane", "Chicane", 120f, 55f, 58f, 24f, 20, 55f, 2);
            AddBuiltin("tundra", "Tundra", 110f, 55f, 58f, 22f, 18, 50f, 2);

            // Light / trainer / VTOL: short range, wide FOV.
            AddBuiltin("cricket", "Cricket", 50f, 65f, 60f, 20f, 8, 45f, 1);
            AddBuiltin("quadvtol", "QuadVTOL", 40f, 70f, 60f, 20f, 6, 40f, 1);

            // Helo: short + wide.
            AddBuiltin("helotransport", "HeloTransport", 35f, 70f, 60f, 20f, 6, 35f, 1);
            AddBuiltin("helo", "Helo", 35f, 70f, 60f, 20f, 6, 35f, 1);
        }

        private static void AddBuiltin(
            string id,
            string label,
            float maxRangeKm,
            float halfFov,
            float acmHalf,
            float acmRange,
            int tracks,
            float scan,
            int radarRank = 2)
        {
            AircraftRadarProfile profile = new AircraftRadarProfile(
                id,
                label,
                maxRangeKm,
                halfFov,
                halfFov * 0.65f,
                acmHalf,
                acmRange,
                tracks,
                scan,
                RadarProfileSource.Builtin,
                radarRank);
            Builtins[NormalizeKey(id)] = profile;
            Builtins[NormalizeKey(label)] = profile;
        }

        private static bool TryMatchBuiltin(string key, string label, out AircraftRadarProfile profile)
        {
            if (TryLookupSoft(Builtins, key, out profile) || TryLookupSoft(Builtins, label, out profile))
            {
                return true;
            }

            // Soft Contains against builtin keys (unknown / mod naming variants).
            string hay = NormalizeKey(string.IsNullOrEmpty(key) ? label : key);
            if (hay.Length == 0)
            {
                profile = default;
                return false;
            }

            foreach (KeyValuePair<string, AircraftRadarProfile> pair in Builtins)
            {
                if (hay.IndexOf(pair.Key, StringComparison.Ordinal) >= 0 ||
                    pair.Key.IndexOf(hay, StringComparison.Ordinal) >= 0)
                {
                    profile = pair.Value;
                    return true;
                }
            }

            profile = default;
            return false;
        }

        private static bool TryMatchOverride(string key, string label, out AircraftRadarProfile profile)
        {
            string a = NormalizeKey(key);
            string b = NormalizeKey(label);
            for (int i = 0; i < Overrides.Count; i++)
            {
                string[] keys = Overrides[i].Keys;
                for (int k = 0; k < keys.Length; k++)
                {
                    string nk = keys[k];
                    if (nk.Length == 0)
                    {
                        continue;
                    }

                    if ((!string.IsNullOrEmpty(a) && (a == nk || a.IndexOf(nk, StringComparison.Ordinal) >= 0 ||
                                                       nk.IndexOf(a, StringComparison.Ordinal) >= 0)) ||
                        (!string.IsNullOrEmpty(b) && (b == nk || b.IndexOf(nk, StringComparison.Ordinal) >= 0 ||
                                                       nk.IndexOf(b, StringComparison.Ordinal) >= 0)))
                    {
                        profile = Overrides[i].Profile;
                        return true;
                    }
                }
            }

            profile = default;
            return false;
        }

        private static bool TryLookupSoft(
            Dictionary<string, AircraftRadarProfile> map,
            string raw,
            out AircraftRadarProfile profile)
        {
            string n = NormalizeKey(raw);
            if (n.Length == 0)
            {
                profile = default;
                return false;
            }

            if (map.TryGetValue(n, out profile))
            {
                return true;
            }

            foreach (KeyValuePair<string, AircraftRadarProfile> pair in map)
            {
                if (n.IndexOf(pair.Key, StringComparison.Ordinal) >= 0 ||
                    pair.Key.IndexOf(n, StringComparison.Ordinal) >= 0)
                {
                    profile = pair.Value;
                    return true;
                }
            }

            profile = default;
            return false;
        }

        private static void TryLoadOverridesOnce()
        {
            if (_overridesAttempted)
            {
                return;
            }

            _overridesAttempted = true;
            try
            {
                string dir = ResolveConfigDir();
                if (string.IsNullOrEmpty(dir))
                {
                    return;
                }

                string path = Path.Combine(dir, OverrideFileName);
                if (!File.Exists(path))
                {
                    Log.Debug("Radar profile override missing (OK): " + path);
                    return;
                }

                string json = File.ReadAllText(path);
                ParseOverrideArray(json);
                Log.Info("Loaded " + Overrides.Count + " radar profile override(s) from " + path);
            }
            catch (Exception ex)
            {
                Log.Warn("Radar profile override soft-fail: " + ex.Message);
            }
        }

        private static string ResolveConfigDir()
        {
            try
            {
                // BepInEx.Paths.ConfigPath when available.
                Type? paths = Type.GetType("BepInEx.Paths, BepInEx") ??
                              Type.GetType("BepInEx.Paths, BepInEx.Core");
                object? configPath = paths?.GetProperty("ConfigPath",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)?.GetValue(null);
                if (configPath is string s && !string.IsNullOrEmpty(s))
                {
                    return s;
                }
            }
            catch
            {
                // ignore
            }

            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
                string candidate = Path.Combine(baseDir, "BepInEx", "config");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                // Walk up from plugin folder.
                string walk = baseDir;
                for (int i = 0; i < 5 && !string.IsNullOrEmpty(walk); i++)
                {
                    string cfg = Path.Combine(walk, "BepInEx", "config");
                    if (Directory.Exists(cfg))
                    {
                        return cfg;
                    }

                    walk = Path.GetDirectoryName(walk) ?? string.Empty;
                }
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        /// <summary>
        /// Minimal JSON array parser for override entries.
        /// Expected: [ { "keys":["a","b"], "maxRangeKm":120, "halfFovDeg":50, ... }, ... ]
        /// Soft-skips malformed objects.
        /// </summary>
        private static void ParseOverrideArray(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            int i = 0;
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '[')
            {
                Log.Warn("Radar profile override: expected JSON array");
                return;
            }

            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ']')
                {
                    break;
                }

                if (i < json.Length && json[i] == ',')
                {
                    i++;
                    continue;
                }

                if (i >= json.Length || json[i] != '{')
                {
                    break;
                }

                if (!TryParseObject(json, ref i, out Dictionary<string, string> fields, out List<string> keys))
                {
                    break;
                }

                if (keys.Count == 0)
                {
                    continue;
                }

                float maxRange = ReadFloat(fields, "maxRangeKm", 120f);
                float halfFov = ReadFloat(fields, "halfFovDeg", 55f);
                float tws = ReadFloat(fields, "twsHalfFov", halfFov * 0.65f);
                float acmHalf = ReadFloat(fields, "acmHalfFov", 12f);
                float acmRange = ReadFloat(fields, "acmMaxRangeKm", 15f);
                int tracks = Mathf.Clamp(Mathf.RoundToInt(ReadFloat(fields, "maxTracks", 16f)), 1, 64);
                float scan = ReadFloat(fields, "scanRateDegPerSec", 60f);
                int radarRank = Mathf.Clamp(Mathf.RoundToInt(ReadFloat(fields, "radarRank", 2f)), 1, 20);
                string label = fields.TryGetValue("displayLabel", out string? dl) && !string.IsNullOrEmpty(dl)
                    ? dl
                    : keys[0];
                string id = NormalizeKey(keys[0]);

                string[] normKeys = new string[keys.Count];
                for (int k = 0; k < keys.Count; k++)
                {
                    normKeys[k] = NormalizeKey(keys[k]);
                }

                AircraftRadarProfile profile = new AircraftRadarProfile(
                    id,
                    label,
                    maxRange,
                    halfFov,
                    tws,
                    acmHalf,
                    acmRange,
                    tracks,
                    scan,
                    RadarProfileSource.Override,
                    radarRank);
                Overrides.Add((normKeys, profile));
            }
        }

        private static bool TryParseObject(
            string json,
            ref int i,
            out Dictionary<string, string> fields,
            out List<string> keys)
        {
            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            keys = new List<string>();
            if (json[i] != '{')
            {
                return false;
            }

            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == '}')
                {
                    i++;
                    return true;
                }

                if (i < json.Length && json[i] == ',')
                {
                    i++;
                    continue;
                }

                if (!TryReadString(json, ref i, out string prop))
                {
                    return false;
                }

                SkipWs(json, ref i);
                if (i >= json.Length || json[i] != ':')
                {
                    return false;
                }

                i++;
                SkipWs(json, ref i);

                if (string.Equals(prop, "keys", StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryReadStringArray(json, ref i, keys))
                    {
                        return false;
                    }
                }
                else if (i < json.Length && json[i] == '"')
                {
                    if (!TryReadString(json, ref i, out string val))
                    {
                        return false;
                    }

                    fields[prop] = val;
                }
                else
                {
                    if (!TryReadNumberToken(json, ref i, out string num))
                    {
                        return false;
                    }

                    fields[prop] = num;
                }
            }

            return false;
        }

        private static bool TryReadStringArray(string json, ref int i, List<string> into)
        {
            SkipWs(json, ref i);
            if (i >= json.Length || json[i] != '[')
            {
                return false;
            }

            i++;
            while (i < json.Length)
            {
                SkipWs(json, ref i);
                if (i < json.Length && json[i] == ']')
                {
                    i++;
                    return true;
                }

                if (i < json.Length && json[i] == ',')
                {
                    i++;
                    continue;
                }

                if (!TryReadString(json, ref i, out string item))
                {
                    return false;
                }

                if (!string.IsNullOrEmpty(item))
                {
                    into.Add(item);
                }
            }

            return false;
        }

        private static bool TryReadString(string json, ref int i, out string value)
        {
            SkipWs(json, ref i);
            value = string.Empty;
            if (i >= json.Length || json[i] != '"')
            {
                return false;
            }

            i++;
            StringBuilder sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i++];
                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }

                if (c == '\\' && i < json.Length)
                {
                    char n = json[i++];
                    sb.Append(n switch
                    {
                        'n' => '\n',
                        't' => '\t',
                        'r' => '\r',
                        '"' => '"',
                        '\\' => '\\',
                        _ => n
                    });
                }
                else
                {
                    sb.Append(c);
                }
            }

            return false;
        }

        private static bool TryReadNumberToken(string json, ref int i, out string token)
        {
            SkipWs(json, ref i);
            int start = i;
            if (i < json.Length && (json[i] == '-' || json[i] == '+'))
            {
                i++;
            }

            while (i < json.Length && (char.IsDigit(json[i]) || json[i] == '.' || json[i] == 'e' || json[i] == 'E'))
            {
                i++;
            }

            // bool true/false / null
            if (i == start)
            {
                if (MatchLiteral(json, ref i, "true") || MatchLiteral(json, ref i, "false") ||
                    MatchLiteral(json, ref i, "null"))
                {
                    token = "0";
                    return true;
                }

                token = string.Empty;
                return false;
            }

            token = json.Substring(start, i - start);
            return true;
        }

        private static bool MatchLiteral(string json, ref int i, string lit)
        {
            if (i + lit.Length <= json.Length &&
                string.Compare(json, i, lit, 0, lit.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                i += lit.Length;
                return true;
            }

            return false;
        }

        private static void SkipWs(string json, ref int i)
        {
            while (i < json.Length && char.IsWhiteSpace(json[i]))
            {
                i++;
            }
        }

        private static float ReadFloat(Dictionary<string, string> fields, string key, float fallback)
        {
            if (!fields.TryGetValue(key, out string? raw) || string.IsNullOrEmpty(raw))
            {
                return fallback;
            }

            if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
            {
                return v;
            }

            return fallback;
        }

        internal static string NormalizeKey(string? raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            StringBuilder sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (char.IsWhiteSpace(c) || c == '_' || c == '-' || c == '.')
                {
                    continue;
                }

                sb.Append(char.ToLowerInvariant(c));
            }

            return sb.ToString();
        }

        private static string TrimLabel(string label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return "?";
            }

            // Strip common Unity clone suffixes.
            string t = label;
            int paren = t.IndexOf('(');
            if (paren > 0)
            {
                t = t.Substring(0, paren).Trim();
            }

            return t.Length > 24 ? t.Substring(0, 24) : t;
        }

        private static AircraftRadarProfile WithLabel(AircraftRadarProfile src, string label)
        {
            string display = string.IsNullOrEmpty(label) ? src.DisplayLabel : TrimLabel(label);
            return new AircraftRadarProfile(
                src.Id,
                display,
                src.MaxRangeKm,
                src.HalfFovDeg,
                src.TwsHalfFov,
                src.AcmHalfFov,
                src.AcmMaxRangeKm,
                src.MaxTracks,
                src.ScanRateDegPerSec,
                src.Source,
                src.RadarRank);
        }
    }
}
