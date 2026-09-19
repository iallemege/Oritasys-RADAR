using System.Collections.Generic;
using UnityEngine;

namespace RDA
{
    internal enum RadarMode
    {
        Standby,
        SrcRws,
        Tws,
        Acm,
        Trk
    }

    internal enum RadarWaveform
    {
        Pulse,
        Pd,
        Cw
    }

    internal readonly struct ModeLimits
    {
        internal ModeLimits(float halfFovDeg, float maxRangeKm, int maxTracks, bool emitsSearch, bool hardLock)
        {
            HalfFovDeg = halfFovDeg;
            MaxRangeKm = maxRangeKm;
            MaxTracks = maxTracks;
            EmitsSearch = emitsSearch;
            HardLock = hardLock;
        }

        internal float HalfFovDeg { get; }
        internal float MaxRangeKm { get; }
        internal int MaxTracks { get; }
        internal bool EmitsSearch { get; }
        internal bool HardLock { get; }
    }

    internal sealed class ModeState
    {
        internal static readonly float[] RangeStepsKm = { 5f, 10f, 20f, 40f, 80f, 160f, 240f };

        private const float ElevSlewDegPerSec = 45f;
        private const float ElevClampDeg = 60f;
        private const float ScanCenterClampDeg = 90f;

        internal RadarMode Mode { get; private set; } = RadarMode.SrcRws;
        internal RadarWaveform Waveform { get; private set; } = RadarWaveform.Pulse;
        internal float ScanAzimuthDeg { get; private set; }

        /// <summary>+1 / −1 ping-pong direction for continuous sweep (no wrap teleport).</summary>
        private float _scanDir = 1f;

        /// <summary>Manual scan-center offset from nose (degrees). SRC/TWS wedge is centered on nose+offset.</summary>
        internal float ScanCenterAzimuthDeg { get; private set; }

        /// <summary>When true, ACM also uses ScanCenterAzimuthDeg instead of pure nose-boresight.</summary>
        internal bool ManualScanOverride { get; private set; }

        internal int? LockedContactId { get; set; }

        /// <summary>All vanilla WeaponManager.targetList contact IDs (multi-lock). Display/primary last = LockedContactId.</summary>
        private readonly List<int> _lockedContactIds = new List<int>(8);
        internal IReadOnlyList<int> LockedContactIds => _lockedContactIds;

        internal bool Locked { get; private set; }
        internal float AntennaElevationDeg { get; private set; }
        internal bool ElevAuto { get; set; } = true;
        internal int? AcmCandidateId { get; set; }
        internal float AcmDwellElapsed { get; set; }
        internal float AcmOutGateElapsed { get; set; }
        internal float? LockedElevationDeg { get; set; }
        internal float LockElapsedSec { get; private set; }

        /// <summary>Per-aircraft envelope; refreshed when ownship instance changes.</summary>
        internal AircraftRadarProfile ActiveProfile { get; set; } = AircraftRadarProfile.Default;

        /// <summary>When false, Tick freezes scan animation (engine off). Fail-soft default true.</summary>
        internal bool EnginePowered { get; set; } = true;

        /// <summary>Mode remembered when vanilla lock soft-switches display to TRK; restored on unlock.</summary>
        internal RadarMode? ModeBeforeVanillaLock { get; set; }

        /// <summary>True when Mode was soft-set to TRK because vanilla targetList became nonempty.</summary>
        internal bool SoftTrkFromVanilla { get; set; }

        internal ModeLimits Limits => LimitsFor(Mode, ActiveProfile);

        /// <summary>Mode label kept as ACM even when STT-locked; status strip uses StatusLabel.</summary>
        internal string StatusLabel
        {
            get
            {
                if (Locked)
                {
                    return Mode switch
                    {
                        RadarMode.Acm => "ACM LOCK",
                        RadarMode.Tws => "TWS LOCK",
                        RadarMode.Trk => "TRK LOCK",
                        _ => "LOCK"
                    };
                }

                if (Mode == RadarMode.Acm)
                {
                    return "ACM";
                }

                if (Mode == RadarMode.Tws && AcmCandidateId != null)
                {
                    return "TWS ACQ";
                }

                return Label(Mode);
            }
        }

        /// <summary>LOCK text only when Locked==true — never from mode name alone.</summary>
        internal string LockBadge => Locked ? "LOCK" : "—";

        internal string WaveformLabel => Waveform switch
        {
            RadarWaveform.Pulse => "PULSE",
            RadarWaveform.Pd => "PD",
            RadarWaveform.Cw => "CW",
            _ => "PULSE"
        };

        internal static string Label(RadarMode mode) => mode switch
        {
            RadarMode.Standby => "STBY",
            RadarMode.SrcRws => "SRC/RWS",
            RadarMode.Tws => "TWS",
            RadarMode.Acm => "ACM",
            RadarMode.Trk => "TRK",
            _ => throw new System.ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        internal static ModeLimits LimitsFor(RadarMode mode) => LimitsFor(mode, AircraftRadarProfile.Default);

        /// <summary>
        /// Mode envelopes scale relative to the active aircraft profile
        /// (e.g. TWS half-FOV ≈ 0.65× SRC; STBY zero).
        /// </summary>
        internal static ModeLimits LimitsFor(RadarMode mode, AircraftRadarProfile profile) => mode switch
        {
            RadarMode.Standby => new ModeLimits(0f, 0f, 0, false, false),
            RadarMode.SrcRws => new ModeLimits(
                profile.HalfFovDeg,
                profile.MaxRangeKm,
                UnityEngine.Mathf.Max(1, profile.MaxTracks),
                true,
                false),
            // TWS = 对空自动锁定 (air auto-lock), multi-track search
            RadarMode.Tws => new ModeLimits(
                profile.TwsHalfFov > 0.1f ? profile.TwsHalfFov : profile.HalfFovDeg * 0.65f,
                UnityEngine.Mathf.Max(5f, profile.MaxRangeKm * 0.5f),
                UnityEngine.Mathf.Max(1, (profile.MaxTracks * 12 + 16) / 32),
                true,
                true),
            // ACM = 对地 A/G — DISTINCT from air SRC/TWS: wide half-FOV (~50–60°), short surface range.
            // Never copy air MaxRangeKm / HalfFovDeg as ACM defaults.
            RadarMode.Acm => new ModeLimits(
                profile.AcmHalfFov > 0.1f
                    ? profile.AcmHalfFov
                    : 55f,
                profile.AcmMaxRangeKm > 0.1f
                    ? profile.AcmMaxRangeKm
                    : UnityEngine.Mathf.Clamp(UnityEngine.Mathf.Min(25f, profile.MaxRangeKm * 0.25f), 20f, 30f),
                UnityEngine.Mathf.Max(4, profile.MaxTracks / 2),
                true,
                false),
            // TRK STT: narrow pencil beam — do NOT derive half-FOV from wide ACM A/G mapping.
            RadarMode.Trk => new ModeLimits(
                6f,
                UnityEngine.Mathf.Max(5f, profile.MaxRangeKm * 0.5f),
                1,
                true,
                true),
            _ => throw new System.ArgumentOutOfRangeException(nameof(mode), mode, null)
        };

        /// <summary>Effective wedge center relative to nose. ACM A/G honors scan slew; TRK stays boresight.</summary>
        internal float EffectiveScanCenterAzimuthDeg()
        {
            if (Mode == RadarMode.Trk)
            {
                return 0f;
            }

            return ScanCenterAzimuthDeg;
        }

        internal void SetMode(RadarMode mode)
        {
            if (Mode == mode)
            {
                return;
            }

            RadarMode previous = Mode;
            bool keepLock = Locked && (
                mode == RadarMode.Trk ||
                mode == RadarMode.Acm ||
                mode == RadarMode.Tws ||
                (previous == RadarMode.Trk && (mode == RadarMode.Tws || mode == RadarMode.Acm)));

            Mode = mode;
            if (!keepLock)
            {
                ClearLock();
            }

            // Reset A/G / TWS acquire timers on mode change (lock itself may be preserved).
            AcmDwellElapsed = 0f;
            AcmOutGateElapsed = 0f;
            if (!Locked)
            {
                AcmCandidateId = null;
            }

            if (mode == RadarMode.Acm)
            {
                Waveform = RadarWaveform.Pulse;
            }

            ApplySoftRangeClamp();
            Log.Info("Radar mode " + Label(mode) + (Locked ? " (lock held)" : ""));
        }

        internal void Cycle(int delta)
        {
            int count = 5;
            int next = ((int)Mode + delta) % count;
            if (next < 0)
            {
                next += count;
            }

            SetMode((RadarMode)next);
        }

        internal void StepRange(int delta)
        {
            float current = Config.RangeKm.Value;
            int index = 0;
            float best = float.MaxValue;
            for (int i = 0; i < RangeStepsKm.Length; i++)
            {
                float d = Mathf.Abs(RangeStepsKm[i] - current);
                if (d < best)
                {
                    best = d;
                    index = i;
                }
            }

            index = Mathf.Clamp(index + delta, 0, RangeStepsKm.Length - 1);
            Config.RangeKm.Value = Mathf.Min(RangeStepsKm[index], EffectiveMaxRangeKm());
        }

        internal void SetRangeKm(float km)
        {
            Config.RangeKm.Value = Mathf.Clamp(km, 2f, EffectiveMaxRangeKm());
        }

        internal float EffectiveMaxRangeKm()
        {
            ModeLimits limits = Limits;
            if (Mode == RadarMode.Standby)
            {
                return Mathf.Max(5f, Config.RangeKm.Value);
            }

            return limits.MaxRangeKm <= 0f ? Config.RangeKm.Value : limits.MaxRangeKm;
        }

        internal float DisplayRangeKm()
        {
            return Mathf.Clamp(Config.RangeKm.Value, 2f, EffectiveMaxRangeKm());
        }

        internal void ApplySoftRangeClamp()
        {
            float max = EffectiveMaxRangeKm();
            if (Config.RangeKm.Value > max)
            {
                Config.RangeKm.Value = max;
            }
        }

        internal void SlewScan(int direction)
        {
            float step = Mathf.Max(1f, Config.ScanSlewStepDeg.Value) * direction;
            ScanCenterAzimuthDeg = Mathf.Clamp(ScanCenterAzimuthDeg + step, -ScanCenterClampDeg, ScanCenterClampDeg);
            if (Mode == RadarMode.Acm)
            {
                ManualScanOverride = true;
            }
        }

        internal void ResetScanCenter()
        {
            ScanCenterAzimuthDeg = 0f;
            ManualScanOverride = false;
        }

        /// <summary>
        /// Legacy no-op. Real lock comes from vanilla WeaponManager.targetList / CombatHUD paint.
        /// Key 6 must not invent LOCK without a vanilla target.
        /// </summary>
        internal void ToggleLock()
        {
            Log.Info("ToggleLock disabled — use game target select / paint / clear (vanilla targetList)");
        }

        internal void ClearLock()
        {
            Locked = false;
            LockedContactId = null;
            _lockedContactIds.Clear();
            LockedElevationDeg = null;
            LockElapsedSec = 0f;
            AcmDwellElapsed = 0f;
            AcmOutGateElapsed = 0f;
        }

        /// <summary>True when id is any vanilla multi-lock entry.</summary>
        internal bool IsLockedContact(int id)
        {
            if (LockedContactId == id)
            {
                return true;
            }

            for (int i = 0; i < _lockedContactIds.Count; i++)
            {
                if (_lockedContactIds[i] == id)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Replace multi-lock ID set from SyncVanilla. <paramref name="displayId"/> is last/display lock.
        /// </summary>
        internal void SetVanillaLockIds(IReadOnlyList<int> ids, int displayId, bool softSwitchToTrk)
        {
            _lockedContactIds.Clear();
            if (ids != null)
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    int id = ids[i];
                    if (!_lockedContactIds.Contains(id))
                    {
                        _lockedContactIds.Add(id);
                    }
                }
            }

            if (!_lockedContactIds.Contains(displayId))
            {
                _lockedContactIds.Add(displayId);
            }

            ApplyVanillaLock(displayId, softSwitchToTrk);
        }

        /// <param name="switchToTrk">True for player manual lock → auto TRK; false for TWS auto-lock.</param>
        internal void EngageLock(int contactId, bool switchToTrk = false)
        {
            if (!Locked || LockedContactId != contactId)
            {
                LockElapsedSec = 0f;
            }

            Locked = true;
            LockedContactId = contactId;
            _lockedContactIds.Clear();
            _lockedContactIds.Add(contactId);
            AcmCandidateId = contactId;
            AcmDwellElapsed = 0f;
            AcmOutGateElapsed = 0f;

            if (switchToTrk && Mode != RadarMode.Trk)
            {
                // SetMode preserves lock when entering TRK and logs once.
                SetMode(RadarMode.Trk);
            }
            else
            {
                Log.Info("Lock engaged id=" + contactId + " mode=" + Label(Mode));
            }
        }

        /// <summary>
        /// Mirror vanilla WeaponManager targetList[0] into RDA lock state.
        /// Soft-sets Mode=TRK for display when a new vanilla lock appears (keeps prior mode memory).
        /// </summary>
        internal void ApplyVanillaLock(int contactId, bool softSwitchToTrk)
        {
            if (!Locked || LockedContactId != contactId)
            {
                LockElapsedSec = 0f;
            }

            Locked = true;
            LockedContactId = contactId;
            if (_lockedContactIds.Count == 0 || (_lockedContactIds.Count == 1 && _lockedContactIds[0] != contactId))
            {
                _lockedContactIds.Clear();
                _lockedContactIds.Add(contactId);
            }
            else if (!_lockedContactIds.Contains(contactId))
            {
                _lockedContactIds.Add(contactId);
            }

            AcmCandidateId = contactId;
            AcmDwellElapsed = 0f;
            AcmOutGateElapsed = 0f;

            if (softSwitchToTrk && Mode != RadarMode.Trk)
            {
                if (!SoftTrkFromVanilla)
                {
                    ModeBeforeVanillaLock = Mode;
                    SoftTrkFromVanilla = true;
                }

                RadarMode previous = Mode;
                Mode = RadarMode.Trk;
                AcmDwellElapsed = 0f;
                AcmOutGateElapsed = 0f;
                ApplySoftRangeClamp();
                Log.Info("Vanilla lock id=" + contactId + " → TRK display (was " + Label(previous) + ")");
            }
        }

        /// <summary>Clear RDA lock mirroring empty vanilla targetList; optionally restore pre-lock mode.</summary>
        internal void ClearVanillaLock(bool restoreMode)
        {
            bool wasSoft = SoftTrkFromVanilla;
            RadarMode? restore = ModeBeforeVanillaLock;
            ClearLock();
            SoftTrkFromVanilla = false;
            ModeBeforeVanillaLock = null;
            if (restoreMode && wasSoft && restore.HasValue && Mode == RadarMode.Trk && restore.Value != RadarMode.Trk)
            {
                // SetMode would ClearLock again (already clear) — just assign + clamp.
                Mode = restore.Value;
                AcmDwellElapsed = 0f;
                AcmOutGateElapsed = 0f;
                AcmCandidateId = null;
                ApplySoftRangeClamp();
                Log.Info("Vanilla unlock → restore mode " + Label(Mode));
            }
            else
            {
                Log.Info("Vanilla unlock");
            }
        }

        internal void CycleWaveform()
        {
            Waveform = Waveform switch
            {
                RadarWaveform.Pulse => RadarWaveform.Pd,
                RadarWaveform.Pd => RadarWaveform.Cw,
                _ => RadarWaveform.Pulse
            };
            Log.Info("Radar waveform " + WaveformLabel);
        }

        /// <summary>Cycle ACM surface designate (and lock if held) among candidate ids. Wrap.</summary>
        internal void CycleAcmDesignate(System.Collections.Generic.IReadOnlyList<int> surfaceIds)
        {
            if (surfaceIds == null || surfaceIds.Count == 0)
            {
                return;
            }

            int current = AcmCandidateId ?? LockedContactId ?? -1;
            int idx = 0;
            for (int i = 0; i < surfaceIds.Count; i++)
            {
                if (surfaceIds[i] == current)
                {
                    idx = i;
                    break;
                }
            }

            int next = surfaceIds[(idx + 1) % surfaceIds.Count];
            AcmCandidateId = next;
            // Highlight only — actual lock stays with vanilla targetList / paint key.
            Log.Info("ACM designate highlight → id=" + next);
        }

        internal void ElevUp()
        {
            ElevAuto = false;
            AntennaElevationDeg = Mathf.Clamp(AntennaElevationDeg + Config.ElevStepDeg.Value, -ElevClampDeg, ElevClampDeg);
        }

        internal void ElevDown()
        {
            ElevAuto = false;
            AntennaElevationDeg = Mathf.Clamp(AntennaElevationDeg - Config.ElevStepDeg.Value, -ElevClampDeg, ElevClampDeg);
        }

        internal void ToggleElevAuto()
        {
            ElevAuto = !ElevAuto;
        }

        /// <summary>
        /// Advance scan + elev. When ElevAuto, slews toward lock / candidate / best air / ACM look-down.
        /// Manual ElevUp/Down clears ElevAuto and is sticky until digit 9 / ElevAutoHotkey re-enables.
        /// </summary>
        internal void Tick(float dt, IReadOnlyList<RadarContact>? contacts = null, float ownshipHatMeters = 0f)
        {
            if (Locked)
            {
                LockElapsedSec += dt;
            }

            if (Mode == RadarMode.Standby)
            {
                if (ElevAuto)
                {
                    AntennaElevationDeg = Mathf.MoveTowards(AntennaElevationDeg, 0f, ElevSlewDegPerSec * dt);
                }

                AntennaElevationDeg = Mathf.Clamp(AntennaElevationDeg, -ElevClampDeg, ElevClampDeg);
                return;
            }

            if (!EnginePowered)
            {
                // Freeze sweep; still allow elev auto toward lock if desired — hold antenna.
                AntennaElevationDeg = Mathf.Clamp(AntennaElevationDeg, -ElevClampDeg, ElevClampDeg);
                return;
            }

            float baseScan = ActiveProfile.ScanRateDegPerSec > 1f ? ActiveProfile.ScanRateDegPerSec : 60f;
            // TWS air search: brisk; ACM A/G mapping: moderate; TRK: slow
            float sweep = Mode == RadarMode.SrcRws
                ? baseScan
                : Mode == RadarMode.Tws
                    ? baseScan * 1.15f
                    : Mode == RadarMode.Acm
                        ? baseScan * 0.7f
                        : baseScan * (20f / 90f);
            // Continuous ping-pong (no Mathf.Repeat wrap — that teleports +half → −half and looks like flashing).
            float half = Mathf.Max(0.1f, Limits.HalfFovDeg);
            if (dt > 0f && !float.IsNaN(dt))
            {
                if (_scanDir == 0f)
                {
                    _scanDir = 1f;
                }

                ScanAzimuthDeg += _scanDir * sweep * dt;
                if (ScanAzimuthDeg >= half)
                {
                    ScanAzimuthDeg = half;
                    _scanDir = -1f;
                }
                else if (ScanAzimuthDeg <= -half)
                {
                    ScanAzimuthDeg = -half;
                    _scanDir = 1f;
                }
            }
            else
            {
                ScanAzimuthDeg = Mathf.Clamp(ScanAzimuthDeg, -half, half);
            }

            // Manual elev has priority: ElevUp/Down set ElevAuto=false and we do not fight the antenna.
            if (ElevAuto)
            {
                float targetElev = ResolveAutoElevTarget(contacts, ownshipHatMeters);
                AntennaElevationDeg = Mathf.MoveTowards(AntennaElevationDeg, targetElev, ElevSlewDegPerSec * dt);
            }

            AntennaElevationDeg = Mathf.Clamp(AntennaElevationDeg, -ElevClampDeg, ElevClampDeg);
        }

        /// <summary>
        /// Auto elev priority: locked elev → AcmCandidate/best air elev → ACM look-down (−6…−12° by HAT) → 0° air search.
        /// </summary>
        private float ResolveAutoElevTarget(IReadOnlyList<RadarContact>? contacts, float ownshipHatMeters)
        {
            if (Locked && LockedElevationDeg.HasValue)
            {
                return LockedElevationDeg.Value;
            }

            // AcmCandidate elev (ACM designate, or SRC/TWS best-track highlight).
            if (AcmCandidateId is int candId && contacts != null)
            {
                for (int i = 0; i < contacts.Count; i++)
                {
                    RadarContact c = contacts[i];
                    if (c.Id == candId)
                    {
                        return c.ElevationDeg;
                    }
                }
            }

            // TWS/SRC/TRK air search: best air contact elev (foe/unknown preferred, then nearest).
            if (Mode == RadarMode.SrcRws || Mode == RadarMode.Tws || Mode == RadarMode.Trk)
            {
                if (TryBestAirElev(contacts, out float airElev))
                {
                    return airElev;
                }

                return 0f;
            }

            if (Mode == RadarMode.Acm)
            {
                return AcmLookDownElev(ownshipHatMeters);
            }

            return 0f;
        }

        private static bool TryBestAirElev(IReadOnlyList<RadarContact>? contacts, out float elev)
        {
            elev = 0f;
            if (contacts == null || contacts.Count == 0)
            {
                return false;
            }

            RadarContact? best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < contacts.Count; i++)
            {
                RadarContact c = contacts[i];
                // Air-search elev: skip surface; allow Air / Unknown / Missile.
                if (c.Kind == ContactKind.Ground || c.Kind == ContactKind.Naval)
                {
                    continue;
                }

                float threat = c.Iff == IffRelation.Foe ? 0f : c.Iff == IffRelation.Unknown ? 1f : 3f;
                float score = threat * 1e6f + c.RangeMeters;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = c;
                }
            }

            if (best == null)
            {
                return false;
            }

            elev = best.ElevationDeg;
            return true;
        }

        /// <summary>ACM A/G look-down ≈ −6° (low HAT) … −12° (high HAT).</summary>
        private static float AcmLookDownElev(float hatMeters)
        {
            float t = Mathf.Clamp01(Mathf.Max(0f, hatMeters) / 4000f);
            return Mathf.Lerp(-6f, -12f, t);
        }
    }
}
