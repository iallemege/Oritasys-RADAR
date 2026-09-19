using UnityEngine;

namespace RDA
{
    internal enum IffRelation
    {
        Unknown,
        Friend,
        Foe,
        Neutral
    }

    internal enum ContactKind
    {
        Unknown,
        Air,
        Ground,
        Naval,
        Missile
    }

    internal enum PositionQuality
    {
        Fresh,
        Coast,
        Stale
    }

    internal sealed class RadarContact
    {
        internal int Id;
        internal string Label = "?";
        internal Vector3 WorldPosition;
        internal float RangeMeters;
        internal float AzimuthDeg;
        internal float ElevationDeg;
        internal float AbsoluteBearingDeg;
        internal float HeadingDeg;
        internal float AltitudeMeters;
        internal float SpeedMps;
        internal float ClosingSpeedMps;
        internal float AspectDeg;
        internal IffRelation Iff;
        internal ContactKind Kind;
        internal bool Tracked;
        internal bool Locked;
        internal bool InCone;
        internal string Source = "unknown";
        internal object? Raw;

        /// <summary>True when this contact came from FactionHQ.trackingDatabase.</summary>
        internal bool FromDatalink;

        /// <summary>Seconds since last HQ spot (datalink tracks).</summary>
        internal float TrackAgeSec;

        /// <summary>Fresh / Coast / Stale quality for datalink positions.</summary>
        internal PositionQuality PositionQuality = PositionQuality.Fresh;

        /// <summary>
        /// Soft-hidden by PD aspect gate (beam/tail). Prefer dim on PPI rather than drop when
        /// already locked via vanilla targetList.
        /// </summary>
        internal bool PdAspectDimmed;
    }
}
