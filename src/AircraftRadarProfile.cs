namespace RDA
{
    /// <summary>How an <see cref="AircraftRadarProfile"/> was produced.</summary>
    internal enum RadarProfileSource
    {
        Builtin,
        Capacity,
        Hybrid,
        Override
    }

    /// <summary>Per-aircraft radar capability used by mode limits and the GUI data strip.</summary>
    internal readonly struct AircraftRadarProfile
    {
        /// <summary>
        /// Radar class rank (1–5+). Rank ≥ <see cref="Config.AesaMinRank"/> (default 3)
        /// is treated as AESA-like: PD aspect/head-on gate is largely waived.
        /// Sourced from builtin table, JSON override, AircraftDefinition.aircraftParameters.rankRequired,
        /// or capacity heuristic (higher maxCharge → higher rank). Default mid = 2.
        /// </summary>
        internal AircraftRadarProfile(
            string id,
            string displayLabel,
            float maxRangeKm,
            float halfFovDeg,
            float twsHalfFov,
            float acmHalfFov,
            float acmMaxRangeKm,
            int maxTracks,
            float scanRateDegPerSec,
            RadarProfileSource source,
            int radarRank = 2)
        {
            Id = id ?? string.Empty;
            DisplayLabel = displayLabel ?? id ?? "?";
            MaxRangeKm = maxRangeKm;
            HalfFovDeg = halfFovDeg;
            TwsHalfFov = twsHalfFov;
            AcmHalfFov = acmHalfFov;
            AcmMaxRangeKm = acmMaxRangeKm;
            MaxTracks = maxTracks;
            ScanRateDegPerSec = scanRateDegPerSec;
            Source = source;
            RadarRank = radarRank < 1 ? 1 : radarRank;
        }

        internal string Id { get; }
        internal string DisplayLabel { get; }
        internal float MaxRangeKm { get; }
        /// <summary>SRC/RWS half-FOV (degrees).</summary>
        internal float HalfFovDeg { get; }
        internal float TwsHalfFov { get; }
        internal float AcmHalfFov { get; }
        internal float AcmMaxRangeKm { get; }
        internal int MaxTracks { get; }
        internal float ScanRateDegPerSec { get; }
        internal RadarProfileSource Source { get; }

        /// <summary>
        /// Radar class 1–5+. Rank ≥ 3 (AESA-like) waives PD head-on aspect culling.
        /// Proxy: Nuclear Option airframe <c>rankRequired</c>, not player PlayerRank.
        /// </summary>
        internal int RadarRank { get; }

        /// <summary>True when RadarRank meets AESA minimum (default ≥ 3).</summary>
        internal bool IsAesaLike => RadarRank >= (Config.AesaMinRank?.Value ?? 3);

        /// <summary>
        /// Generic mid-tier fallback. ACM A/G envelope is intentionally separate from air SRC/TWS
        /// (wide half-FOV ~55°, short surface range ~25 km — not a copy of air MaxRangeKm).
        /// RadarRank default mid = 2 (non-AESA).
        /// </summary>
        internal static AircraftRadarProfile Default { get; } = new AircraftRadarProfile(
            "default",
            "Generic",
            120f,
            55f,
            55f * 0.65f,
            55f,   // AcmHalfFov — wide A/G mapping (~50–60°), not air search FOV copy
            25f,   // AcmMaxRangeKm — A/G default ~20–30 km (≠ air 80/160 scales)
            16,
            60f,
            RadarProfileSource.Capacity,
            2);

        /// <summary>Short tag for the data strip (CAP / HYB / OVR / BLT).</summary>
        internal string SourceTag => Source switch
        {
            RadarProfileSource.Builtin => "BLT",
            RadarProfileSource.Capacity => "CAP",
            RadarProfileSource.Hybrid => "HYB",
            RadarProfileSource.Override => "OVR",
            _ => "?"
        };

        /// <summary>Data-strip rank tag, e.g. <c>RK2</c> or <c>RK3 AESA</c>.</summary>
        internal string RankTag
        {
            get
            {
                string rk = "RK" + RadarRank;
                return IsAesaLike ? rk + " AESA" : rk;
            }
        }

        internal AircraftRadarProfile WithRank(int rank) =>
            new AircraftRadarProfile(
                Id, DisplayLabel, MaxRangeKm, HalfFovDeg, TwsHalfFov, AcmHalfFov, AcmMaxRangeKm,
                MaxTracks, ScanRateDegPerSec, Source, rank);
    }
}
