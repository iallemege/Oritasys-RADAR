using System;
using System.Collections.Generic;
using UnityEngine;

namespace RDA
{
    internal sealed class ContactProvider
    {
        private static readonly string[] PlayerFlags =
        {
            "isPlayer", "IsPlayer", "isLocalPlayer", "IsLocalPlayer", "localPlayer", "playerControlled",
            "isLocal", "controlledByPlayer"
        };

        private static readonly string[] AircraftMembers =
        {
            "aircraft", "Aircraft", "playerAircraft", "localAircraft", "currentAircraft"
        };

        private static readonly string[] RadarMembers =
        {
            "radar", "Radar", "targetDetector", "TargetDetector", "detector", "airRadar"
        };

        private static readonly string[] ConeMembers =
        {
            "cone", "radarCone", "coneAngle", "horizontalFov", "fov", "FOV", "azimuthLimit", "halfAngle"
        };

        private static readonly string[] RangeMembers =
        {
            "maxRange", "MaxRange", "range", "Range", "radarRange", "detectionRange"
        };

        private readonly ModeState _modes;
        private readonly RwrPanel? _rwr;
        private readonly List<RadarContact> _contacts = new List<RadarContact>(64);
        private readonly List<RadarContact> _scratch = new List<RadarContact>(64);
        private readonly object _extraGate = new object();
        private readonly List<RadarContact> _extra = new List<RadarContact>(16);

        private object? _player;
        private object? _radar;
        private object? _tacScreen;
        private string _status = "boot";
        private bool _loggedPlayerPath;
        private bool _loggedTrackSource;
        private float _nextRetry;
        private float _nextHeavy;
        private float _lastHeavyTime;
        private float _heavyDt;
        private readonly List<RadarContact> _display = new List<RadarContact>(64);
        private bool _terrainRayOk;
        private bool _terrainRayProbed;
        private int _cachedOwnshipId = int.MinValue;
        private AircraftRadarProfile _activeProfile = AircraftRadarProfile.Default;
        private HitSolution _lastHit = HitSolution.None;
        private WeaponSnapshot _cachedWeapon;
        private bool _weaponLogged;
        private readonly List<RadarContact> _datalinkScratch = new List<RadarContact>(64);
        private object? _networkHq;
        private int _datalinkCount;
        private float _nextDatalinkIngest;
        private bool _wasInAircraft;
        private object? _lastBoardedAircraft;
        private bool? _lastSeatGate;
        private string _lastSeatReason = "";
        private bool? _lastHasEjected;

        /// <summary>Cached raw unit for world TRK label between contact ticks.</summary>
        internal object? LockedUnitRaw { get; private set; }
        internal Vector3 LockedUnitWorldPos { get; private set; }
        internal float LockedUnitCacheTime { get; private set; }

        internal ContactProvider(ModeState modes, RwrPanel? rwr = null)
        {
            _modes = modes;
            _rwr = rwr;
        }

        /// <summary>Cached snapshot for GUI; updated at ContactUpdateHz.</summary>
        internal IReadOnlyList<RadarContact> Contacts => _display;
        internal object? Player => _player;
        internal object? Radar => _radar;
        internal object? TacScreen => _tacScreen;
        internal string Status => _status;
        internal Vector3 OwnshipPosition { get; private set; }
        internal float OwnshipHeadingDeg { get; private set; }
        internal float OwnshipAltitudeMeters { get; private set; }
        internal float OwnshipSpeedMps { get; private set; }
        internal float HardwareHalfFovDeg { get; private set; } = 60f;
        internal float HardwareRangeMeters { get; private set; } = 80000f;
        internal bool RadarPowered { get; private set; }
        internal bool InMission { get; private set; }

        /// <summary>True when local player is seated in a flyable aircraft (not eject/dead/spectate).</summary>
        internal bool InAircraft { get; private set; }

        /// <summary>Last HasEjected probe (null = unknown / no aircraft). For diagnostics.</summary>
        internal bool? LastHasEjected => _lastHasEjected;

        /// <summary>MFD / gun funnel / world TRK / RWR draw gate (respects Display.HudGateMode / ForceShowHud).</summary>
        internal bool ShouldDrawHud
        {
            get
            {
                if (Config.ForceShowHud.Value || Config.IsAlwaysWhenToggledGate())
                {
                    return true;
                }

                if (Config.IsAircraftPresentGate())
                {
                    // Aircraft resolved + mission tick running; hide only via ejected/destroyed leave path.
                    return InMission;
                }

                return InMission && InAircraft;
            }
        }

        /// <summary>True when engines appear running. Unknown reflection → true (fail-soft).</summary>
        internal bool EngineRunning { get; private set; } = true;
        /// <summary>Accumulated seconds of consecutive clear engine-off probes (debounce before freeze).</summary>
        private float _engineOffAccum;
        /// <summary>Estimated ground range along antenna boresight (meters). NaN if unavailable.</summary>
        internal float TerrainRangeMeters { get; private set; } = float.NaN;
        internal float TerrainElevDeg { get; private set; }
        internal bool TerrainFromRaycast { get; private set; }
        internal AircraftRadarProfile ActiveProfile => _activeProfile;
        /// <summary>Latest weapon ETA / lead solution (updated with contact throttle).</summary>
        internal HitSolution LastHitSolution => _lastHit;
        internal Vector3 OwnshipVelocity { get; private set; }
        /// <summary>Number of datalink-sourced contacts in the current snapshot.</summary>
        internal int DatalinkCount => _datalinkCount;

        internal void IngestDetected(object? raw)
        {
            if (raw == null || GameReflect.IsClutterUnit(raw))
            {
                return;
            }

            lock (_extraGate)
            {
                RadarContact contact = BuildContact(raw, "harmony");
                if (contact.RangeMeters > 1f)
                {
                    _extra.Add(contact);
                }
            }
        }

        internal void Tick()
        {
            GameReflect.EnsureDiscovered();

            float hz = Mathf.Clamp(Config.ContactUpdateHz.Value, 5f, 60f);
            float interval = 1f / hz;
            if (Time.unscaledTime < _nextHeavy)
            {
                return;
            }

            float now = Time.unscaledTime;
            _heavyDt = _lastHeavyTime > 0f ? Mathf.Max(interval * 0.5f, now - _lastHeavyTime) : interval;
            _lastHeavyTime = now;
            _nextHeavy = now + interval;

            if (!TryResolvePlayer())
            {
                HandleLeftAircraft("menu / no aircraft");
                return;
            }

            // HUD / mission gate — mode-dependent (compat with Oritasy soft seat probes).
            bool allowMission = EvaluateMissionGate(out string seatReason);
            if (_lastSeatGate != allowMission || _lastSeatReason != seatReason)
            {
                Log.Info("HUD gate [" + Config.HudGateMode.Value + "] → " + (allowMission ? "SHOW" : "HIDE") + " (" + seatReason + ")");
                _lastSeatGate = allowMission;
                _lastSeatReason = seatReason;
            }

            if (!allowMission)
            {
                HandleLeftAircraft(seatReason);
                return;
            }

            // Fresh board → wipe stale lock/TRK/RWR so re-enter is clean.
            // Intentionally do NOT force ShowWindow=false on board (hotkey preference preserved).
            if (!_wasInAircraft || !ReferenceEquals(_lastBoardedAircraft, _player))
            {
                ReinitRadarOnBoard();
            }

            _wasInAircraft = true;
            _lastBoardedAircraft = _player;
            InMission = true;
            InAircraft = true;
            RefreshProfileIfOwnshipChanged();
            OwnshipPosition = GameReflect.WorldPosition(_player);
            OwnshipHeadingDeg = GameReflect.HeadingDeg(_player);
            OwnshipAltitudeMeters = OwnshipPosition.y;
            OwnshipSpeedMps = GameReflect.SpeedMps(_player);
            bool? eng = GameReflect.TryIsEngineRunning(_player);
            // Fail-soft: null → ON. Debounce clear OFF for ≥0.75s to avoid idle-throttle flicker.
            bool probeOn = !eng.HasValue || eng.Value;
            if (probeOn)
            {
                _engineOffAccum = 0f;
                EngineRunning = true;
            }
            else
            {
                _engineOffAccum += _heavyDt;
                if (_engineOffAccum >= 0.75f)
                {
                    EngineRunning = false;
                }
                // else keep previous EngineRunning (typically true) during debounce
            }

            OwnshipVelocity = WeaponReflect.ReadVelocity(_player);

            if (!EngineRunning)
            {
                // Hold last contact frame; ownship already refreshed.
                _status = "ENG OFF";
                UpdateTerrainScan();
                SyncVanillaTargetLock();
                UpdateWeaponHitSolution();
                PublishSnapshot();
                return;
            }

            _contacts.Clear();
            ResolveRadar();
            ResolveTacScreen();
            ReadHardwareEnvelope();
            UpdateTerrainScan();

            _networkHq = DatalinkBridge.ResolveNetworkHq(_player);

            if (_modes.Mode == RadarMode.Standby)
            {
                _status = "STBY";
                if (RDA.Config.UseVanillaDatalink.Value && RDA.Config.DatalinkInStandby.Value)
                {
                    // STBY has no scan cone — DL feed still range-filtered + capped.
                    IngestAndMergeDatalink(markCone: false);
                    ApplyDisplayRangeFilters(_contacts);
                    _status = "STBY · DL " + _datalinkCount;
                }
                else
                {
                    _datalinkCount = 0;
                }

                SyncVanillaTargetLock();
                PollRwrOwnshipThreats();
                UpdateWeaponHitSolution();
                PublishSnapshot();
                return;
            }

            CollectTracks();
            DrainHarmonyExtras();
            ApplyDisplayRangeFilters(_contacts);
            ApplyModeFilters();
            IngestAndMergeDatalink(markCone: true);
            DatalinkBridge.ContributeTracks(_networkHq, _contacts);
            SyncVanillaTargetLock();
            ApplyPdAspectGate(hardDrop: false); // soft-dim locked off-aspect; never drop vanilla lock
            FilterOutOfScanHighValueOnly();
            ApplyDisplayRangeFilters(_contacts);
            PollRwrOwnshipThreats();
            _status = _contacts.Count + " contacts · DL " + _datalinkCount + " · " + (_loggedTrackSource ? "live" : "probe");
            UpdateWeaponHitSolution();
            PublishSnapshot();
        }

        private void IngestAndMergeDatalink(bool markCone)
        {
            _datalinkCount = 0;
            if (!RDA.Config.UseVanillaDatalink.Value)
            {
                return;
            }

            // Throttle dense HQ ingest independently of contact Hz.
            float dlHz = Mathf.Clamp(RDA.Config.DatalinkRefreshHz.Value, 1f, 30f);
            float dlInterval = 1f / dlHz;
            bool due = Time.unscaledTime >= _nextDatalinkIngest;
            if (!due)
            {
                // Still count existing DL contacts for status strip.
                int held = 0;
                for (int i = 0; i < _contacts.Count; i++)
                {
                    if (_contacts[i].FromDatalink)
                    {
                        held++;
                    }
                }

                _datalinkCount = held;
                return;
            }

            _nextDatalinkIngest = Time.unscaledTime + dlInterval;
            _datalinkScratch.Clear();

            DatalinkBridge.IngestTracks(
                _player,
                _networkHq,
                OwnshipPosition,
                OwnshipHeadingDeg,
                OwnshipSpeedMps,
                _datalinkScratch);

            // Drop DL rows that fail friend/air range filters before merge.
            ApplyDisplayRangeFilters(_datalinkScratch);

            if (markCone)
            {
                float halfFov = EffectiveHalfFovDeg();
                float maxRange = _modes.DisplayRangeKm() * 1000f;
                float antennaEl = _modes.AntennaElevationDeg;
                float elevGate = Mathf.Max(1f, halfFov);
                float scanCenter = _modes.EffectiveScanCenterAzimuthDeg();
                for (int i = 0; i < _datalinkScratch.Count; i++)
                {
                    RadarContact c = _datalinkScratch[i];
                    float azAbs = Mathf.Abs(Normalize180(c.AzimuthDeg - scanCenter));
                    float elOff = Mathf.Abs(c.ElevationDeg - antennaEl);
                    c.InCone = azAbs <= halfFov + 0.5f && elOff <= elevGate + 0.5f && c.RangeMeters <= maxRange;
                }
            }

            // Hard priority cull + cap before merge (fewer symbols/labels).
            CullDatalinkScratchHard();

            // Remove stale DL-only contacts that were not refreshed this ingest.
            HashSet<int> freshDl = new HashSet<int>();
            for (int i = 0; i < _datalinkScratch.Count; i++)
            {
                freshDl.Add(_datalinkScratch[i].Id);
            }

            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                if (_contacts[i].FromDatalink && !freshDl.Contains(_contacts[i].Id))
                {
                    // Keep if locked / RWR threat
                    bool keep = _contacts[i].Locked ||
                                (_modes.Locked && _modes.LockedContactId == _contacts[i].Id) ||
                                (_rwr != null && _rwr.ContainsId(_contacts[i].Id));
                    if (!keep)
                    {
                        _contacts.RemoveAt(i);
                    }
                }
            }

            DatalinkBridge.MergePreferOwnRadar(_contacts, _datalinkScratch);
            int dl = 0;
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].FromDatalink)
                {
                    _contacts[i].Tracked = true;
                    dl++;
                }
            }

            _datalinkCount = dl;
        }

        private void PublishSnapshot()
        {
            _display.Clear();
            for (int i = 0; i < _contacts.Count; i++)
            {
                _display.Add(_contacts[i]);
            }
        }

        private void UpdateTerrainScan()
        {
            TerrainElevDeg = _modes.AntennaElevationDeg;
            TerrainFromRaycast = false;
            TerrainRangeMeters = float.NaN;

            float elev = _modes.AntennaElevationDeg;
            float maxR = Mathf.Max(500f, _modes.DisplayRangeKm() * 1000f * 1.25f);
            float heading = OwnshipHeadingDeg + _modes.EffectiveScanCenterAzimuthDeg();
            Vector3 dir = Quaternion.Euler(-elev, heading, 0f) * Vector3.forward;

            if (TryPhysicsRaycast(OwnshipPosition + Vector3.up * 2f, dir, maxR, out float hitDist))
            {
                TerrainRangeMeters = hitDist;
                TerrainFromRaycast = true;
                return;
            }

            // Flat-earth geometric: ground range along boresight when looking down.
            float alt = Mathf.Max(0.5f, OwnshipAltitudeMeters);
            if (elev < -0.15f)
            {
                float elRad = elev * Mathf.Deg2Rad;
                float ground = alt / Mathf.Tan(-elRad);
                if (ground > 0f && ground < maxR * 4f)
                {
                    TerrainRangeMeters = ground;
                }
            }
            else if (Mathf.Abs(elev) <= 0.15f)
            {
                TerrainRangeMeters = float.PositiveInfinity;
            }
        }

        private bool TryPhysicsRaycast(Vector3 origin, Vector3 direction, float maxDistance, out float hitDistance)
        {
            hitDistance = 0f;
            if (_terrainRayProbed && !_terrainRayOk)
            {
                return false;
            }

            try
            {
                _terrainRayProbed = true;
                // May TypeLoadException if PhysicsModule is absent at runtime.
                if (PhysicsRaycastHelper.Try(origin, direction, maxDistance, out hitDistance))
                {
                    _terrainRayOk = true;
                    return true;
                }

                _terrainRayOk = true;
                return false;
            }
            catch (System.Exception ex)
            {
                _terrainRayOk = false;
                Log.Debug("Terrain Physics.Raycast soft-fail: " + ex.Message);
                return false;
            }
        }

        internal float EffectiveHalfFovDeg()
        {
            float mode = _modes.Limits.HalfFovDeg;
            if (mode <= 0f)
            {
                return 0f;
            }

            // Profile already blends native cone when PreferNativeRadarStats is on;
            // still soft-cap by live hardware envelope when readable.
            return HardwareHalfFovDeg > 0f ? Mathf.Min(mode, HardwareHalfFovDeg) : mode;
        }

        private void RefreshProfileIfOwnshipChanged()
        {
            int id = _player != null ? GameReflect.IdOf(_player) : int.MinValue;
            // Also treat reference change without stable id as a refresh.
            int refHash = _player?.GetHashCode() ?? 0;
            int cacheKey = id != 0 ? id : refHash;
            if (cacheKey == _cachedOwnshipId && _cachedOwnshipId != int.MinValue)
            {
                return;
            }

            _cachedOwnshipId = cacheKey;
            RadarProfileCatalog.EnsureReady();
            _activeProfile = RadarProfileCatalog.Resolve(_player);
            _modes.ActiveProfile = _activeProfile;
            _modes.ApplySoftRangeClamp();
            Log.Info(
                "Radar profile " + _activeProfile.DisplayLabel +
                " [" + _activeProfile.Source + "] rng=" + _activeProfile.MaxRangeKm.ToString("0") +
                "km fov=±" + _activeProfile.HalfFovDeg.ToString("0") +
                "° rank=" + _activeProfile.RadarRank +
                (_activeProfile.IsAesaLike ? " AESA" : ""));
        }

        private void ClearOwnshipProfileCache()
        {
            if (_cachedOwnshipId == int.MinValue)
            {
                return;
            }

            _cachedOwnshipId = int.MinValue;
            _activeProfile = AircraftRadarProfile.Default;
            _modes.ActiveProfile = _activeProfile;
        }

        /// <summary>
        /// Decide whether mission/radar tick should run and (for Seated/AircraftPresent) draw.
        /// AircraftPresent: local aircraft resolves + hide only if HasEjected==true or destroyed.
        /// AlwaysWhenToggled / ForceShowHud: still run mission when aircraft is present the same way.
        /// Seated: full HasEjected + soft seated probe (legacy).
        /// </summary>
        private bool EvaluateMissionGate(out string reason)
        {
            _lastHasEjected = null;
            if (_player == null)
            {
                reason = "menu / no aircraft";
                return false;
            }

            try
            {
                if (_player is UnityEngine.Object uo && uo == null)
                {
                    reason = "aircraft destroyed";
                    return false;
                }
            }
            catch
            {
                reason = "aircraft destroyed";
                return false;
            }

            bool? ejected = GameReflect.TryHasEjected(_player);
            _lastHasEjected = ejected;

            if (Config.IsSeatedGate())
            {
                return EvaluateInAircraft(out reason);
            }

            // AircraftPresent (default) and AlwaysWhenToggled mission path: hide only on eject/destroy.
            if (ejected == true)
            {
                reason = "HasEjected";
                return false;
            }

            if (GameReflect.TryIsUnitDestroyedOrDead(_player) == true)
            {
                reason = "destroyed / dead";
                return false;
            }

            reason = ejected == false ? "aircraft present (!HasEjected)" : "aircraft present (live)";
            return true;
        }

        private bool EvaluateInAircraft(out string reason)
        {
            if (_player == null)
            {
                reason = "menu / no aircraft";
                return false;
            }

            try
            {
                if (_player is UnityEngine.Object uo && uo == null)
                {
                    reason = "aircraft destroyed";
                    return false;
                }
            }
            catch
            {
                reason = "aircraft destroyed";
                return false;
            }

            bool? ejected = GameReflect.TryHasEjected(_player);
            if (ejected == true)
            {
                reason = "HasEjected";
                return false;
            }

            if (GameReflect.TryIsUnitDestroyedOrDead(_player) == true)
            {
                reason = "destroyed / dead";
                return false;
            }

            bool? seated = GameReflect.TryIsPlayerSeatedInAircraft(_player);
            if (seated == false)
            {
                reason = ejected == false ? "left aircraft" : "not seated";
                return false;
            }

            // Fail-soft: unknown → seated when live local aircraft and !HasEjected.
            reason = ejected == false ? "seated (!HasEjected)" : "seated (live aircraft)";
            return true;
        }

        private void HandleLeftAircraft(string reason)
        {
            bool was = _wasInAircraft || InAircraft;
            InMission = false;
            InAircraft = false;
            EngineRunning = true;
            _engineOffAccum = 0f;
            _radar = null;
            _tacScreen = null;
            _networkHq = null;
            _datalinkCount = 0;
            _status = reason;
            TerrainRangeMeters = float.NaN;
            LockedUnitRaw = null;
            _contacts.Clear();
            if (_modes.Locked || _modes.SoftTrkFromVanilla)
            {
                _modes.ClearVanillaLock(restoreMode: false);
            }

            _modes.ClearLock();
            _rwr?.Clear();
            WorldTrkLabelHud.ClearCache();
            if (was)
            {
                Log.Info("HUD hidden — " + reason);
            }

            _wasInAircraft = false;
            _lastBoardedAircraft = null;
            _lastHasEjected = null;
            ClearOwnshipProfileCache();
            // Keep _lastSeatGate so flip log still fires on re-board; do not touch ShowWindow.
            PublishSnapshot();
        }

        /// <summary>Clean radar state when boarding / re-entering aircraft (no stale lock/TRK).</summary>
        private void ReinitRadarOnBoard()
        {
            _modes.ClearVanillaLock(restoreMode: false);
            _modes.ClearLock();
            _modes.SoftTrkFromVanilla = false;
            _modes.ModeBeforeVanillaLock = null;
            _modes.AcmCandidateId = null;
            _modes.AcmDwellElapsed = 0f;
            _modes.AcmOutGateElapsed = 0f;
            LockedUnitRaw = null;
            _contacts.Clear();
            _datalinkScratch.Clear();
            _datalinkCount = 0;
            _nextDatalinkIngest = 0f;
            _rwr?.Clear();
            WorldTrkLabelHud.ClearCache();
            Log.Info("Radar re-init on aircraft board");
        }

        /// <summary>
        /// Friendly ≤ FriendlyDisplayRangeKm; Air ≤ AirDisplayRangeKm.
        /// Locked / RWR threats / missiles always kept. Applies to PPI/ACM/TWS/DL lists.
        /// </summary>
        private void ApplyDisplayRangeFilters(List<RadarContact> list)
        {
            if (list == null || list.Count == 0)
            {
                return;
            }

            float friendMax = Mathf.Max(100f, Config.FriendlyDisplayRangeKm.Value * 1000f);
            float airMax = Mathf.Max(100f, Config.AirDisplayRangeKm.Value * 1000f);

            for (int i = list.Count - 1; i >= 0; i--)
            {
                RadarContact c = list[i];
                bool keepLocked = c.Locked || (_modes.Locked && _modes.LockedContactId == c.Id);
                if (keepLocked)
                {
                    continue;
                }

                if (_rwr != null && _rwr.ContainsId(c.Id))
                {
                    continue;
                }

                if (c.Kind == ContactKind.Missile)
                {
                    continue;
                }

                if (c.Iff == IffRelation.Friend && c.RangeMeters > friendMax)
                {
                    list.RemoveAt(i);
                    continue;
                }

                if (c.Kind == ContactKind.Air && c.RangeMeters > airMax)
                {
                    list.RemoveAt(i);
                }
            }
        }

        /// <summary>Priority-cull + hard cap DL scratch before merge.</summary>
        private void CullDatalinkScratchHard()
        {
            if (_datalinkScratch.Count == 0)
            {
                return;
            }

            // Hard cap drawn/ingested DL contacts (BepInEx Datalink.DatalinkMaxMarkers, default 24, range 4–128).
            int cap = Mathf.Clamp(Config.DatalinkMaxMarkers.Value, 4, 128);
            if (Config.PerfMode.Value)
            {
                cap = Mathf.Min(cap, 18);
            }

            if (Config.DatalinkPreferHighValue.Value || _datalinkScratch.Count > cap)
            {
                _datalinkScratch.Sort((a, b) =>
                {
                    int pa = DlPriority(a);
                    int pb = DlPriority(b);
                    int cmp = pa.CompareTo(pb);
                    return cmp != 0 ? cmp : a.RangeMeters.CompareTo(b.RangeMeters);
                });
            }

            if (_datalinkScratch.Count > cap)
            {
                _datalinkScratch.RemoveRange(cap, _datalinkScratch.Count - cap);
            }
        }

        private int DlPriority(RadarContact c)
        {
            if (c.Kind == ContactKind.Missile) return 0;
            if (_rwr != null && _rwr.ContainsId(c.Id)) return 1;
            if (c.Iff == IffRelation.Foe && c.Kind == ContactKind.Air) return 2;
            if (GameReflect.IsHighValueUnit(c.Raw, c.Label, c.Kind)) return 3;
            if (c.Iff == IffRelation.Foe) return 4;
            if (c.Kind == ContactKind.Air) return 5;
            return 6;
        }

        /// <summary>
        /// Soft: poll FactionHQ.missileAttacks + contact seekers locking ownship into RWR
        /// (被锁定 / missile attack). Complements Harmony RadarWarning hooks.
        /// </summary>
        private void PollRwrOwnshipThreats()
        {
            if (_rwr == null || _player == null)
            {
                return;
            }

            try
            {
                foreach (object attacker in GameReflect.EnumerateMissileAttacksOn(_networkHq, _player))
                {
                    _rwr.IngestOwnshipThreat(attacker, RwrKind.Missile, flash: true);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("RWR missileAttacks soft-fail: " + ex.Message);
            }

            // Seeker / illuminate locks on ownship only (cut false positives from loose AI "target").
            int ownId = GameReflect.IdOf(_player);
            for (int i = 0; i < _contacts.Count; i++)
            {
                RadarContact c = _contacts[i];
                if (c.Raw == null || c.Iff == IffRelation.Friend)
                {
                    continue;
                }

                try
                {
                    if (GameReflect.ReadBool(c.Raw, false,
                            "lockedOnPlayer", "LockedOnPlayer", "isAttackingPlayer", "attackingPlayer",
                            "targetingPlayer", "TargetingPlayer", "hasLockOnPlayer", "illuminatingPlayer",
                            "isTarget", "IsTarget"))
                    {
                        RwrKind k = c.Kind == ContactKind.Missile ? RwrKind.Missile : RwrKind.Lock;
                        _rwr.IngestOwnshipThreat(c.Raw, k, flash: true);
                        continue;
                    }

                    // Prefer explicit lock/seeker members — not generic AI "target"/"Target".
                    object? tgt = GameReflect.FindMember(c.Raw.GetType(),
                        "lockedTarget", "LockedTarget", "currentTarget", "CurrentTarget",
                        "seekerTarget", "SeekerTarget", "trackedTarget", "TrackedTarget")?.Get(c.Raw);
                    if (tgt != null &&
                        (ReferenceEquals(tgt, _player) || GameReflect.IdOf(tgt) == ownId))
                    {
                        RwrKind k = c.Kind == ContactKind.Missile ? RwrKind.Missile : RwrKind.Lock;
                        _rwr.IngestOwnshipThreat(c.Raw, k, flash: true);
                        continue;
                    }

                    // Missile seekers: nested seeker.lockedTarget == ownship
                    if (c.Kind == ContactKind.Missile)
                    {
                        object? seeker = GameReflect.FindMember(c.Raw.GetType(),
                            "seeker", "Seeker", "missileSeeker", "MissileSeeker")?.Get(c.Raw);
                        object? sTgt = seeker == null ? null : GameReflect.FindMember(seeker.GetType(),
                            "lockedTarget", "LockedTarget", "currentTarget", "CurrentTarget",
                            "target", "Target")?.Get(seeker);
                        if (sTgt != null &&
                            (ReferenceEquals(sTgt, _player) || GameReflect.IdOf(sTgt) == ownId))
                        {
                            _rwr.IngestOwnshipThreat(c.Raw, RwrKind.Missile, flash: true);
                        }
                    }
                }
                catch
                {
                    // soft-fail
                }
            }
        }

        private bool TryResolvePlayer()
        {
            _player = TryLocalAircraft();
            if (_player != null)
            {
                return true;
            }

            if (Time.unscaledTime < _nextRetry)
            {
                return false;
            }

            _nextRetry = Time.unscaledTime + 1.5f;
            return false;
        }

        private object? TryLocalAircraft()
        {
            object? fromManager = InvokeStatic(GameReflect.GameManager, "GetLocalAircraft", "get_LocalAircraft", "LocalAircraft");
            if (fromManager != null)
            {
                NotePlayer("GameManager.GetLocalAircraft");
                return fromManager;
            }

            object? singleton = ReadSingleton(GameReflect.SceneSingleton);
            object? fromSingleton = GameReflect.FindMember(singleton?.GetType(), AircraftMembers)?.Get(singleton);
            if (fromSingleton != null)
            {
                NotePlayer("SceneSingleton.aircraft");
                return fromSingleton;
            }

            foreach (Type? hudType in new[] { GameReflect.CombatHud, GameReflect.FlightHud })
            {
                if (hudType == null)
                {
                    continue;
                }

                foreach (UnityEngine.Object instance in GameReflect.FindAll(hudType))
                {
                    object? aircraft = GameReflect.FindMember(instance.GetType(), AircraftMembers)?.Get(instance);
                    if (aircraft != null)
                    {
                        NotePlayer(hudType.Name + ".aircraft");
                        return aircraft;
                    }
                }
            }

            if (GameReflect.Aircraft == null)
            {
                return null;
            }

            foreach (UnityEngine.Object instance in GameReflect.FindAll(GameReflect.Aircraft))
            {
                if (instance == null)
                {
                    continue;
                }

                if (GameReflect.ReadBool(instance, false, PlayerFlags))
                {
                    NotePlayer("Aircraft player flag");
                    return instance;
                }
            }

            return null;
        }

        private void ResolveRadar()
        {
            Component? playerComp = GameReflect.AsComponent(_player);
            _radar = null;
            if (playerComp != null)
            {
                if (GameReflect.Radar != null)
                {
                    _radar = playerComp.GetComponentInChildren(GameReflect.Radar, true);
                }

                if (_radar == null && GameReflect.TargetDetector != null)
                {
                    _radar = playerComp.GetComponentInChildren(GameReflect.TargetDetector, true);
                }
            }

            if (_radar == null)
            {
                _radar = GameReflect.FindMember(_player?.GetType(), RadarMembers)?.Get(_player);
            }

            if (_radar != null && !_loggedPlayerPath)
            {
                Log.Debug("Radar instance " + _radar.GetType().FullName);
            }
        }

        private void ResolveTacScreen()
        {
            _tacScreen = null;
            if (GameReflect.TacScreen == null)
            {
                return;
            }

            Component? playerComp = GameReflect.AsComponent(_player);
            if (playerComp != null)
            {
                _tacScreen = playerComp.GetComponentInChildren(GameReflect.TacScreen, true);
            }

            if (_tacScreen == null)
            {
                foreach (UnityEngine.Object instance in GameReflect.FindAll(GameReflect.TacScreen))
                {
                    _tacScreen = instance;
                    break;
                }
            }
        }

        private void ReadHardwareEnvelope()
        {
            HardwareHalfFovDeg = 60f;
            HardwareRangeMeters = 80000f;
            RadarPowered = _modes.Mode != RadarMode.Standby;

            object? source = _radar ?? _tacScreen;
            if (source == null)
            {
                return;
            }

            object? param = GameReflect.FindMember(source.GetType(), "RadarParams", "radarParams", "params", "Params")?.Get(source);
            float cone = FirstFinite(
                GameReflect.ReadFloat(source, ConeMembers),
                GameReflect.ReadFloat(param, ConeMembers));
            if (!float.IsNaN(cone) && cone > 0f)
            {
                HardwareHalfFovDeg = cone > 180f ? cone * 0.5f : cone;
            }

            float range = FirstFinite(
                GameReflect.ReadFloat(source, RangeMembers),
                GameReflect.ReadFloat(param, RangeMembers));
            if (!float.IsNaN(range) && range > 1f)
            {
                HardwareRangeMeters = range > 1000f ? range : range * 1000f;
            }

            RadarPowered = GameReflect.ReadBool(source, true, "radarOn", "RadarOn", "enabled", "isOn", "powered");
        }

        private void CollectTracks()
        {
            _scratch.Clear();

            TryList(GameReflect.FindMember(_radar?.GetType(), "tracks", "Tracks", "trackedTargets", "targets", "contacts", "detections"));
            TryList(GameReflect.FindMember(_tacScreen?.GetType(), "tracks", "Tracks", "targets", "contacts"));
            if (_radar != null)
            {
                foreach (MemberHandle handle in GameReflect.FindEnumerableMembers(_radar.GetType()))
                {
                    TryList(handle);
                }
            }

            object? singleton = ReadSingleton(GameReflect.SceneSingleton) ?? ReadSingleton(GameReflect.CombatHud);
            object? list = GameReflect.FindMember(singleton?.GetType(), "GetTargetList", "targetList", "TargetList", "targets")?.Get(singleton);
            AddEnumerated(list, "GetTargetList");

            if (_scratch.Count == 0 && Config.FallbackSceneScan.Value)
            {
                FallbackSceneUnits();
            }

            if (!_loggedTrackSource && _scratch.Count > 0)
            {
                _loggedTrackSource = true;
                Log.Info("First contact batch: " + _scratch.Count);
            }

            foreach (RadarContact contact in _scratch)
            {
                _contacts.Add(contact);
            }
        }

        private void TryList(MemberHandle? handle)
        {
            if (handle == null)
            {
                return;
            }

            object? owner = OwnerFor(handle.Value);
            AddEnumerated(handle.Value.Get(owner), handle.Value.Member.DeclaringType?.Name + "." + handle.Value.Member.Name);
        }

        private object? OwnerFor(MemberHandle handle)
        {
            Type? declaring = handle.Member.DeclaringType;
            if (declaring == null)
            {
                return _radar;
            }

            if (_radar != null && declaring.IsInstanceOfType(_radar))
            {
                return _radar;
            }

            if (_tacScreen != null && declaring.IsInstanceOfType(_tacScreen))
            {
                return _tacScreen;
            }

            return _radar ?? _tacScreen ?? _player;
        }


        private bool IsLocalPlayerUnit(object? unit)
        {
            if (unit == null || _player == null)
            {
                return false;
            }

            if (ReferenceEquals(unit, _player))
            {
                return true;
            }

            int pid = GameReflect.IdOf(_player);
            int uid = GameReflect.IdOf(unit);
            if (pid != 0 && uid == pid)
            {
                return true;
            }

            try
            {
                Component? pc = GameReflect.AsComponent(_player);
                Component? uc = GameReflect.AsComponent(unit);
                if (pc != null && uc != null && pc.transform.root == uc.transform.root)
                {
                    return true;
                }
            }
            catch
            {
                // ignore
            }

            return false;
        }

        /// <summary>
        /// ACM must never designate/lock/highlight ownship, local pilot, or near-ownship ejecting crew.
        /// </summary>
        internal bool IsOwnshipOrCrewContact(RadarContact? c)
        {
            if (c == null)
            {
                return false;
            }

            if (c.Raw != null && IsLocalPlayerUnit(c.Raw))
            {
                return true;
            }

            if (_player != null)
            {
                int pid = GameReflect.IdOf(_player);
                if (pid != 0 && c.Id == pid)
                {
                    return true;
                }
            }

            // Near-zero range friend/self ghost on caret
            if (c.RangeMeters < 40f && c.Iff == IffRelation.Friend)
            {
                return true;
            }

            // Ejecting pilot / parachute / bail-out crew near ownship (separate unit)
            if (c.RangeMeters < 250f)
            {
                string lab = (c.Label ?? string.Empty) + " " + (c.Raw != null ? c.Raw.GetType().Name : string.Empty);
                if (GameReflectContainsCrew(lab))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool GameReflectContainsCrew(string blob)
        {
            return ContainsIgnoreCaseLocal(blob, "pilot") ||
                   ContainsIgnoreCaseLocal(blob, "parachute") ||
                   ContainsIgnoreCaseLocal(blob, "eject") ||
                   ContainsIgnoreCaseLocal(blob, "bail") ||
                   ContainsIgnoreCaseLocal(blob, "crewseat") ||
                   ContainsIgnoreCaseLocal(blob, "ejection");
        }

        private static bool ContainsIgnoreCaseLocal(string hay, string needle)
        {
            return hay.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// ACM A/G same-height gate: near ground (alt ≤ band MSL) or within ±band of ownship altitude.
        /// </summary>
        internal bool PassesAcmAltitudeGate(RadarContact c)
        {
            float band = Config.AcmAltitudeBandM != null ? Config.AcmAltitudeBandM.Value : 1200f;
            band = Mathf.Clamp(band, 200f, 5000f);
            float own = OwnshipAltitudeMeters;
            float alt = c.AltitudeMeters;
            // Ground / sea-skimming surface band (A/G mapping plane)
            if (alt <= band)
            {
                return true;
            }

            // Co-altitude with ownship (nap-of-earth / same-height search)
            if (Mathf.Abs(alt - own) <= band)
            {
                return true;
            }

            // Explicit ground/naval kinds still allowed if below ownship (look-down A/G)
            if ((c.Kind == ContactKind.Ground || c.Kind == ContactKind.Naval) && alt <= own + 50f)
            {
                return true;
            }

            return false;
        }

        private void AddEnumerated(object? raw, string source)
        {
            foreach (object item in GameReflect.Enumerate(raw))
            {
                object? unit = UnwrapUnit(item);
                if (unit == null || IsLocalPlayerUnit(unit) || GameReflect.IsClutterUnit(unit))
                {
                    continue;
                }

                _scratch.Add(BuildContact(unit, source));
            }
        }

        private void FallbackSceneUnits()
        {
            Type? probe = GameReflect.Unit ?? GameReflect.Aircraft;
            if (probe == null)
            {
                return;
            }

            float maxMeters = Mathf.Max(1000f, _modes.DisplayRangeKm() * 1000f);
            foreach (UnityEngine.Object instance in GameReflect.FindAll(probe))
            {
                if (instance == null || IsLocalPlayerUnit(instance) || GameReflect.IsClutterUnit(instance))
                {
                    continue;
                }

                if (instance is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                {
                    continue;
                }

                RadarContact contact = BuildContact(instance, "scene-fallback");
                if (contact.RangeMeters > 5f && contact.RangeMeters <= maxMeters * 1.15f)
                {
                    _scratch.Add(contact);
                }
            }
        }

        private void DrainHarmonyExtras()
        {
            lock (_extraGate)
            {
                foreach (RadarContact extra in _extra)
                {
                    bool exists = false;
                    for (int i = 0; i < _contacts.Count; i++)
                    {
                        if (_contacts[i].Id == extra.Id)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        _contacts.Add(extra);
                    }
                }

                _extra.Clear();
            }
        }

        private void ApplyModeFilters()
        {
            float halfFov = EffectiveHalfFovDeg();
            float maxRange = _modes.DisplayRangeKm() * 1000f;
            int cap = Mathf.Max(1, _modes.Limits.MaxTracks);
            float antennaEl = _modes.AntennaElevationDeg;
            float elevGate = Mathf.Max(1f, halfFov);
            float scanCenter = _modes.EffectiveScanCenterAzimuthDeg();

            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                RadarContact c = _contacts[i];
                float azAbs = Mathf.Abs(Normalize180(c.AzimuthDeg - scanCenter));
                float elOff = Mathf.Abs(c.ElevationDeg - antennaEl);
                bool azOk = azAbs <= halfFov + 0.5f;
                bool elOk = elOff <= elevGate + 0.5f;
                bool rangeOk = c.RangeMeters <= maxRange;
                c.InCone = azOk && elOk && rangeOk;

                if (_modes.Mode == RadarMode.SrcRws || _modes.Mode == RadarMode.Tws ||
                    _modes.Mode == RadarMode.Acm || _modes.Mode == RadarMode.Trk)
                {
                    bool keepLocked = _modes.Locked && _modes.LockedContactId == c.Id;
                    // When ShowOutOfRangeHighValueOnly: defer drop to FilterOutOfScanHighValueOnly
                    // (after DL merge) so HV/threats outside cone remain.
                    if (!c.InCone && !keepLocked && !Config.ShowOutOfRangeHighValueOnly.Value)
                    {
                        // Legacy: keep DL + ACM surface outside cone; drop other own-radar.
                        if (!c.FromDatalink)
                        {
                            bool keepAcmSurface = _modes.Mode == RadarMode.Acm &&
                                (c.Kind == ContactKind.Ground || c.Kind == ContactKind.Naval ||
                                 c.Kind == ContactKind.Unknown);
                            if (!keepAcmSurface)
                            {
                                _contacts.RemoveAt(i);
                            }
                        }
                    }
                }
            }

            // Kind filters before lock logic
            FilterContactsByMode();

            // PD aspect gate (non-AESA): head-on keep; beam/tail drop or close-range flicker.
            ApplyPdAspectGate(hardDrop: true);

            if (_modes.Mode == RadarMode.Acm)
            {
                ApplyAcmAgLogic(halfFov, elevGate);
            }
            else if (_modes.Mode == RadarMode.Tws)
            {
                ApplyTwsAirAutoLockLogic(halfFov, elevGate);
            }
            else if (_modes.Mode == RadarMode.Trk)
            {
                ApplyTrkLockLogic();
            }
            else
            {
                // SRC/RWS: maintain designate candidate for PPI highlight (vanilla paint does real lock).
                _modes.AcmDwellElapsed = 0f;
                _modes.AcmOutGateElapsed = 0f;
                if (!_modes.Locked)
                {
                    _modes.LockedElevationDeg = null;
                }

                _contacts.Sort((a, b) => a.RangeMeters.CompareTo(b.RangeMeters));
                if (_modes.Mode == RadarMode.SrcRws && _contacts.Count > 0)
                {
                    _modes.AcmCandidateId = _contacts[0].Id;
                }
                else if (!_modes.Locked)
                {
                    _modes.AcmCandidateId = null;
                }

                if (_contacts.Count > cap)
                {
                    _contacts.RemoveRange(cap, _contacts.Count - cap);
                }
            }

            foreach (RadarContact contact in _contacts)
            {
                contact.Tracked = true;
                if (_modes.Locked && _modes.LockedContactId == contact.Id)
                {
                    contact.Locked = true;
                    _modes.LockedElevationDeg = contact.ElevationDeg;
                }
            }
        }

        /// <summary>ACM = A/G surface; TWS = air/missile. SRC keeps mixed.</summary>
        private void FilterContactsByMode()
        {
            if (_modes.Mode != RadarMode.Acm && _modes.Mode != RadarMode.Tws)
            {
                return;
            }

            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                RadarContact c = _contacts[i];
                bool keepLocked = _modes.Locked && _modes.LockedContactId == c.Id;
                if (keepLocked)
                {
                    continue;
                }

                if (_modes.Mode == RadarMode.Acm)
                {
                    // Never keep ownship / ejecting crew as ACM A/G contacts
                    if (IsOwnshipOrCrewContact(c))
                    {
                        _contacts.RemoveAt(i);
                        continue;
                    }

                    // 对地: keep ground / naval / unknown (DL factories etc.); drop pure air
                    bool surface = c.Kind == ContactKind.Ground || c.Kind == ContactKind.Naval ||
                                   c.Kind == ContactKind.Unknown;
                    if (!surface)
                    {
                        _contacts.RemoveAt(i);
                        continue;
                    }

                    // Same-height / ground-band gate (AcmAltitudeBandM)
                    if (!PassesAcmAltitudeGate(c))
                    {
                        _contacts.RemoveAt(i);
                    }
                }
                else if (_modes.Mode == RadarMode.Tws)
                {
                    // 对空自动锁定: air + missile only
                    bool air = c.Kind == ContactKind.Air || c.Kind == ContactKind.Missile;
                    if (!air)
                    {
                        _contacts.RemoveAt(i);
                    }
                }
            }
        }

        /// <summary>
        /// ACM A/G (对地): soft-designate nearest surface track in scan — highlight only.
        /// Real lock comes from vanilla WeaponManager.targetList.
        /// </summary>
        private void ApplyAcmAgLogic(float halfFov, float elevGate)
        {
            float scanCenter = _modes.EffectiveScanCenterAzimuthDeg();
            float dt = _heavyDt;
            float breakNeed = Mathf.Max(0.1f, Config.AcmBreakSec.Value);
            int cap = Mathf.Max(1, _modes.Limits.MaxTracks);

            // Maintain ground lock if held (manual); break when out of gate / lost
            if (_modes.Locked && _modes.LockedContactId is int lockedId)
            {
                RadarContact? locked = FindById(lockedId);
                if (locked == null)
                {
                    _modes.AcmOutGateElapsed += dt;
                    // Lost contact momentarily — vanilla SyncVanillaTargetLock owns ClearLock.
                }
                else
                {
                    // ACM A/G: hold lock on surface even outside cone/range display.
                    _modes.AcmOutGateElapsed = 0f;
                    _modes.LockedElevationDeg = locked.ElevationDeg;
                    locked.Locked = true;
                }
            }

            // Drop ownship/crew from ACM candidate pool (hard skip)
            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                if (IsOwnshipOrCrewContact(_contacts[i]))
                {
                    _contacts.RemoveAt(i);
                }
            }

            // Soft designate: nearest in-cone surface by range (mapping cue), not angular dogfight
            _contacts.Sort((a, b) =>
            {
                int ic = (b.InCone ? 1 : 0).CompareTo(a.InCone ? 1 : 0);
                if (ic != 0)
                {
                    return ic;
                }

                // Prefer altitude-gate passers, then closer range
                int ag = (PassesAcmAltitudeGate(b) ? 1 : 0).CompareTo(PassesAcmAltitudeGate(a) ? 1 : 0);
                if (ag != 0)
                {
                    return ag;
                }

                return a.RangeMeters.CompareTo(b.RangeMeters);
            });

            if (_contacts.Count == 0)
            {
                _modes.AcmCandidateId = null;
                _modes.AcmDwellElapsed = 0f;
                return;
            }

            RadarContact? pick = null;
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (!IsOwnshipOrCrewContact(_contacts[i]))
                {
                    pick = _contacts[i];
                    break;
                }
            }

            if (pick == null)
            {
                _modes.AcmCandidateId = null;
                _modes.AcmDwellElapsed = 0f;
                return;
            }

            // Always maintain a designate candidate (nearest surface, in-cone preferred via sort).
            // Out-of-cone / out-of-range ground remain designatable for manual lock.
            // Never self.
            _modes.AcmCandidateId = pick.Id;

            _modes.AcmDwellElapsed = 0f; // no auto EngageLock in ACM A/G


            if (_contacts.Count > cap)
            {
                _contacts.RemoveRange(cap, _contacts.Count - cap);
            }
        }

        /// <summary>
        /// TWS air search: pick nearest angular error to scan center for ACQ highlight.
        /// Does not invent LOCK — SyncVanillaTargetLock mirrors WeaponManager.targetList.
        /// Multi-track retained up to TWS cap.
        /// </summary>
        private void ApplyTwsAirAutoLockLogic(float halfFov, float elevGate)
        {
            float scanCenter = _modes.EffectiveScanCenterAzimuthDeg();
            float dt = _heavyDt;
            float dwellNeed = Mathf.Max(0.05f, Config.TwsLockDwellSec.Value);
            float breakNeed = Mathf.Max(0.1f, Config.AcmBreakSec.Value);
            int cap = Mathf.Max(1, _modes.Limits.MaxTracks);

            if (_modes.Locked && _modes.LockedContactId is int lockedId)
            {
                RadarContact? locked = FindById(lockedId);
                if (locked == null)
                {
                    _modes.AcmOutGateElapsed += dt;
                    // Vanilla SyncVanillaTargetLock owns ClearLock when targetList empties.
                }
                else
                {
                    float azAbs = Mathf.Abs(Normalize180(locked.AzimuthDeg - scanCenter));
                    float elOff = Mathf.Abs(locked.ElevationDeg - _modes.AntennaElevationDeg);
                    bool inGate = azAbs <= halfFov + 1f && elOff <= elevGate + 1f;
                    if (inGate)
                    {
                        _modes.AcmOutGateElapsed = 0f;
                        _modes.LockedElevationDeg = locked.ElevationDeg;
                        locked.Locked = true;
                    }
                    else
                    {
                        _modes.AcmOutGateElapsed += dt;
                        // Out-of-gate: keep RDA lock while vanilla targetList still has entry.
                    }
                }

                // Keep multi-track TWS picture while locked
                _contacts.Sort((a, b) => a.RangeMeters.CompareTo(b.RangeMeters));
                if (_contacts.Count > cap)
                {
                    // Prefer keeping the locked contact
                    RadarContact? keep = FindById(lockedId);
                    _contacts.RemoveRange(cap, _contacts.Count - cap);
                    if (keep != null && FindById(keep.Id) == null)
                    {
                        _contacts.Insert(0, keep);
                        if (_contacts.Count > cap)
                        {
                            _contacts.RemoveAt(_contacts.Count - 1);
                        }
                    }
                }

                _modes.AcmCandidateId = _modes.LockedContactId;
                return;
            }

            // Auto-lock search: angular error to scan center (az + el), not range-first
            var inCone = new System.Collections.Generic.List<RadarContact>();
            foreach (RadarContact c in _contacts)
            {
                if (c.InCone)
                {
                    inCone.Add(c);
                }
            }

            inCone.Sort((a, b) =>
            {
                float sa = Mathf.Abs(Normalize180(a.AzimuthDeg - scanCenter)) + Mathf.Abs(a.ElevationDeg - _modes.AntennaElevationDeg);
                float sb = Mathf.Abs(Normalize180(b.AzimuthDeg - scanCenter)) + Mathf.Abs(b.ElevationDeg - _modes.AntennaElevationDeg);
                int cmp = sa.CompareTo(sb);
                return cmp != 0 ? cmp : a.RangeMeters.CompareTo(b.RangeMeters);
            });

            if (inCone.Count == 0)
            {
                _modes.AcmCandidateId = null;
                _modes.AcmDwellElapsed = 0f;
                _contacts.Sort((a, b) => a.RangeMeters.CompareTo(b.RangeMeters));
                if (_contacts.Count > cap)
                {
                    _contacts.RemoveRange(cap, _contacts.Count - cap);
                }

                return;
            }

            RadarContact pick = inCone[0];
            if (_modes.AcmCandidateId != pick.Id)
            {
                _modes.AcmCandidateId = pick.Id;
                _modes.AcmDwellElapsed = 0f;
            }
            else
            {
                _modes.AcmDwellElapsed += dt;
            }

            // Highlight / TWS ACQ only — do NOT invent LOCK into WeaponManager / ModeState.
            // LK LED + TRK page require vanilla targetList nonempty (SyncVanillaTargetLock).
            if (_modes.AcmDwellElapsed >= dwellNeed)
            {
                // Keep candidate; StatusLabel shows TWS ACQ while unlocked.
            }

            _contacts.Sort((a, b) => a.RangeMeters.CompareTo(b.RangeMeters));
            if (_contacts.Count > cap)
            {
                _contacts.RemoveRange(cap, _contacts.Count - cap);
            }
        }

        private void ApplyTrkLockLogic()
        {
            float dt = _heavyDt;

            if (_modes.LockedContactId is int id)
            {
                RadarContact? existing = FindById(id);
                if (existing != null)
                {
                    _modes.LockedElevationDeg = existing.ElevationDeg;
                    _modes.AcmOutGateElapsed = 0f;
                    _modes.AcmCandidateId = id;
                    _contacts.Clear();
                    existing.Locked = true;
                    existing.Tracked = true;
                    _contacts.Add(existing);
                    return;
                }

                _modes.AcmOutGateElapsed += dt;
                // Hold last; vanilla SyncVanillaTargetLock clears when targetList empty.
            }

            // TRK without vanilla lock: designate nearest for PPI cue only (NO TGT until vanilla paints).
            _contacts.Sort((a, b) => a.RangeMeters.CompareTo(b.RangeMeters));
            if (_contacts.Count == 0)
            {
                _modes.AcmCandidateId = null;
                return;
            }

            RadarContact pick = _contacts[0];
            _modes.AcmCandidateId = pick.Id;
            _contacts.Clear();
            pick.Tracked = true;
            if (_modes.Locked && _modes.LockedContactId == pick.Id)
            {
                pick.Locked = true;
            }

            _contacts.Add(pick);
        }

        
        /// <summary>
        /// R key (all modes): cycle vanilla WeaponManager.targetList so missiles guide on [0].
        /// ACM empty list may AddTargetList the current designate surface unit.
        /// Syncs RDA lock + ACM highlight to the new primary.
        /// </summary>
        internal void CycleVanillaTargetList()
        {
            object? acmDesignate = null;
            var surfaceUnits = new System.Collections.Generic.List<object>();
            CollectAcmSurfaceUnits(out acmDesignate, surfaceUnits);

            bool changed = WeaponReflect.CycleVanillaTargetList(_player, acmDesignate, surfaceUnits);

            // Always re-sync LK/TRK/world TRK from vanilla list after cycle attempt.
            SyncVanillaTargetLock();

            // Highlight sync to new primary (or ACM designate when list still empty).
            // Never ACM-highlight ownship / crew even if vanilla list somehow points at self.
            object? primary = WeaponReflect.TryGetPrimaryTarget(_player);
            if (primary != null && !IsLocalPlayerUnit(primary) && !GameReflect.IsClutterUnit(primary))
            {
                int pid = GameReflect.IdOf(primary);
                RadarContact? primaryContact = FindById(pid);
                if (primaryContact == null || !IsOwnshipOrCrewContact(primaryContact))
                {
                    _modes.AcmCandidateId = pid;
                }
                else
                {
                    _modes.AcmCandidateId = null;
                }
            }
            else if (_modes.Mode == RadarMode.Acm && surfaceUnits.Count > 0)
            {
                // Fallback highlight cycle among ACM surface when list empty / unchanged
                var ids = new System.Collections.Generic.List<int>(surfaceUnits.Count);
                for (int i = 0; i < surfaceUnits.Count; i++)
                {
                    if (IsLocalPlayerUnit(surfaceUnits[i]))
                    {
                        continue;
                    }

                    ids.Add(GameReflect.IdOf(surfaceUnits[i]));
                }

                if (ids.Count > 0)
                {
                    _modes.CycleAcmDesignate(ids);
                }
                else
                {
                    _modes.AcmCandidateId = null;
                }
            }
            else if (_modes.Mode == RadarMode.Acm)
            {
                _modes.AcmCandidateId = null;
            }

            if (changed)
            {
                Log.Info("R: vanilla targetList cycled — SyncVanillaTargetLock applied");
            }
        }

        /// <summary>Legacy ACM-only highlight cycle (kept for callers); prefer CycleVanillaTargetList.</summary>
        internal void CycleAcmSurfaceDesignate()
        {
            CycleVanillaTargetList();
        }

        private void CollectAcmSurfaceUnits(out object? designateRaw, System.Collections.Generic.List<object> surfaceUnits)
        {
            designateRaw = null;
            System.Collections.Generic.IReadOnlyList<RadarContact> src =
                _contacts.Count > 0 ? _contacts : _display;

            for (int i = 0; i < src.Count; i++)
            {
                RadarContact c = src[i];
                if (IsOwnshipOrCrewContact(c))
                {
                    continue;
                }

                bool surface = c.Kind == ContactKind.Ground || c.Kind == ContactKind.Naval ||
                               c.Kind == ContactKind.Unknown;
                if (!surface || c.Raw == null)
                {
                    continue;
                }

                if (c.Raw is UnityEngine.Object uo && uo == null)
                {
                    continue;
                }

                if (!PassesAcmAltitudeGate(c))
                {
                    continue;
                }

                surfaceUnits.Add(c.Raw);
                if (_modes.AcmCandidateId == c.Id)
                {
                    designateRaw = c.Raw;
                }
            }

            if (designateRaw == null && _modes.AcmCandidateId is int cid)
            {
                RadarContact? hit = FindById(cid);
                if (hit?.Raw != null && !IsOwnshipOrCrewContact(hit) && PassesAcmAltitudeGate(hit))
                {
                    designateRaw = hit.Raw;
                }
            }

            // If designate somehow still points at self, clear it
            if (designateRaw != null && IsLocalPlayerUnit(designateRaw))
            {
                designateRaw = null;
            }

            // Clear stale AcmCandidateId when it is ownship/crew
            if (_modes.AcmCandidateId is int aid)
            {
                RadarContact? cand = FindById(aid);
                if (cand != null && IsOwnshipOrCrewContact(cand))
                {
                    _modes.AcmCandidateId = null;
                    designateRaw = null;
                }
            }
        }


        /// <summary>
        /// PD waveform aspect gate for SRC/TWS/TRK air tracks on non-AESA radars (RadarRank &lt; AesaMinRank).
        /// BuildContact AspectDeg: abs(bearing-to-ownship − target heading). ≈0 = target nose toward ownship
        /// (头对头 / head-on); ≈90 beam; ≈180 tail-on.
        /// Head-on (or strong closing): keep. Beam/tail: drop, or close-range flicker bleed-through.
        /// Vanilla-locked contacts are soft-dimmed (PdAspectDimmed) rather than removed.
        /// PULSE/CW: no gate. AESA (rank ≥ AesaMinRank): skip culling (clutter draw still differs).
        /// </summary>
        private void ApplyPdAspectGate(bool hardDrop)
        {
            if (_modes.Waveform != RadarWaveform.Pd)
            {
                return;
            }

            if (_modes.Mode != RadarMode.SrcRws && _modes.Mode != RadarMode.Tws && _modes.Mode != RadarMode.Trk)
            {
                return;
            }

            int aesaMin = Config.AesaMinRank != null ? Config.AesaMinRank.Value : 3;
            if (_activeProfile.RadarRank >= aesaMin)
            {
                return;
            }

            float headCone = Config.PdHeadOnAspectDeg != null ? Config.PdHeadOnAspectDeg.Value : 45f;
            float minCloseKm = Config.PdMinCloseRangeKm != null ? Config.PdMinCloseRangeKm.Value : 6f;
            float minCloseM = Mathf.Max(500f, minCloseKm * 1000f);

            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                RadarContact c = _contacts[i];
                c.PdAspectDimmed = false;

                // Air-contact filter only (missiles included as air tracks).
                if (c.Kind != ContactKind.Air && c.Kind != ContactKind.Missile)
                {
                    continue;
                }

                // Datalink is not own-radar PD search — leave DL tracks alone.
                if (c.FromDatalink)
                {
                    continue;
                }

                bool headOn = IsPdHeadOn(c, headCone);
                if (headOn)
                {
                    continue;
                }

                bool locked = c.Locked || (_modes.Locked && _modes.LockedContactId == c.Id);
                float rangeM = c.RangeMeters;
                bool isTail = c.AspectDeg >= (180f - headCone);
                // Beam ≈ mid-aspect (everything not head-on / not tail).
                bool isBeam = !isTail;

                bool bleed = false;
                if (rangeM <= minCloseM)
                {
                    float t = Mathf.Clamp01(rangeM / minCloseM);
                    // Beam: low-prob flicker when close; tail: rarer still.
                    float p = isBeam
                        ? Mathf.Lerp(0.40f, 0.06f, t)
                        : Mathf.Lerp(0.18f, 0.02f, t);
                    bleed = PdFlickerKeep(c.Id, p);
                }

                if (bleed)
                {
                    // Flicker detect: show, slightly dim beam bleed.
                    c.PdAspectDimmed = isBeam;
                    continue;
                }

                if (locked)
                {
                    // Soft: dim/hide from PPI rather than delete vanilla lock.
                    c.PdAspectDimmed = true;
                    continue;
                }

                if (hardDrop)
                {
                    _contacts.RemoveAt(i);
                }
                else
                {
                    c.PdAspectDimmed = true;
                }
            }
        }

        /// <summary>
        /// Head-on (头对头): AspectDeg near 0 (target nose toward ownship) within cone,
        /// or strongly positive closing with aspect still in the forward hemisphere.
        /// </summary>
        private static bool IsPdHeadOn(RadarContact c, float headConeDeg)
        {
            if (c.AspectDeg <= headConeDeg)
            {
                return true;
            }

            // Closing speed strongly positive + not tail-aspect → treat as head-on closing geometry.
            if (c.ClosingSpeedMps > 80f && c.AspectDeg <= 90f)
            {
                return true;
            }

            return false;
        }

        /// <summary>Deterministic per-contact flicker (stable ~0.35s buckets).</summary>
        private static bool PdFlickerKeep(int contactId, float probability)
        {
            if (probability <= 0f)
            {
                return false;
            }

            if (probability >= 1f)
            {
                return true;
            }

            int bucket = Mathf.FloorToInt(Time.unscaledTime / 0.35f);
            unchecked
            {
                int h = contactId * 73856093 ^ bucket * 19349663;
                h = (h << 13) ^ h;
                float u = ((h & 0x7FFFFFFF) % 10000) / 10000f;
                return u < probability;
            }
        }

        /// <summary>
        /// Outside mode cone/range: drop contacts that are neither high-value nor threats.
        /// In-cone tracks always kept. Locked always kept.
        /// </summary>
        private void FilterOutOfScanHighValueOnly()
        {
            if (!Config.ShowOutOfRangeHighValueOnly.Value || _modes.Mode == RadarMode.Standby)
            {
                return;
            }

            for (int i = _contacts.Count - 1; i >= 0; i--)
            {
                RadarContact c = _contacts[i];
                if (c.InCone)
                {
                    continue;
                }

                bool keepLocked = c.Locked || (_modes.Locked && _modes.LockedContactId == c.Id);
                if (keepLocked)
                {
                    continue;
                }

                if (!IsHighValueOrThreat(c))
                {
                    _contacts.RemoveAt(i);
                }
            }

            // Refresh DL count after drops
            int dl = 0;
            for (int i = 0; i < _contacts.Count; i++)
            {
                if (_contacts[i].FromDatalink)
                {
                    dl++;
                }
            }

            _datalinkCount = dl;
        }

        private bool IsHighValueOrThreat(RadarContact c)
        {
            if (c.Kind == ContactKind.Missile)
            {
                return true;
            }

            if (_rwr != null && _rwr.ContainsId(c.Id))
            {
                return true;
            }

            if (GameReflect.IsHighValueUnit(c.Raw, c.Label, c.Kind))
            {
                return true;
            }

            // Soft: unit currently locking / attacking player (missile seeker / lockedOn flags).
            if (c.Raw != null)
            {
                try
                {
                    if (GameReflect.ReadBool(c.Raw, false,
                            "lockedOnPlayer", "LockedOnPlayer", "isAttackingPlayer", "attackingPlayer",
                            "targetingPlayer", "TargetingPlayer", "hasLockOnPlayer"))
                    {
                        return true;
                    }

                    object? tgt = GameReflect.FindMember(c.Raw.GetType(),
                        "target", "Target", "currentTarget", "lockedTarget")?.Get(c.Raw);
                    if (tgt != null && _player != null &&
                        (ReferenceEquals(tgt, _player) || GameReflect.IdOf(tgt) == GameReflect.IdOf(_player)))
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return false;
        }

        /// <summary>
        /// Nuclear Option truth: WeaponManager.GetTargetList() / targetList; primary = [0].
        /// Sets Locked + LockedContactId from vanilla; ClearLock when empty.
        /// Soft-switches display Mode to TRK when a lock appears.
        /// </summary>
        private void SyncVanillaTargetLock()
        {
            object? primary = null;
            try
            {
                // Display lock = last targetList entry (single PPI line + world TRK).
                // Missile guide primary remains [0] via CycleVanillaTargetList / TryGetPrimaryTarget.
                if (!WeaponReflect.TryGetDisplayLockTargetUnit(_player, out primary))
                {
                    primary = null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Vanilla targetList soft-fail: " + ex.Message);
            }

            // Unity destroyed objects can still be non-null wrappers — check Component.
            if (primary is UnityEngine.Object uo && uo == null)
            {
                primary = null;
            }

            if (primary != null && GameReflect.IsClutterUnit(primary))
            {
                primary = null;
            }

            // Never treat local player / ownship as a lock target (ACM or otherwise).
            if (primary != null && IsLocalPlayerUnit(primary))
            {
                primary = null;
            }

            if (primary == null)
            {
                LockedUnitRaw = null;
                if (_modes.Locked)
                {
                    _modes.ClearVanillaLock(restoreMode: true);
                }

                // Clear locked flags on contacts
                for (int i = 0; i < _contacts.Count; i++)
                {
                    _contacts[i].Locked = false;
                }

                return;
            }

            int id = GameReflect.IdOf(primary);
            RadarContact? existing = FindById(id);
            if (existing == null)
            {
                // Also search display snapshot / build fresh
                existing = BuildContact(primary, "vanilla-targetList");
                existing.Locked = true;
                existing.Tracked = true;
                _contacts.Add(existing);
            }
            else
            {
                existing.Locked = true;
                existing.Tracked = true;
                // Refresh kinematics from live unit
                RadarContact refreshed = BuildContact(primary, existing.Source ?? "vanilla-targetList");
                refreshed.Locked = true;
                refreshed.Tracked = true;
                refreshed.FromDatalink = existing.FromDatalink;
                refreshed.InCone = existing.InCone;
                refreshed.PositionQuality = existing.PositionQuality;
                int idx = _contacts.IndexOf(existing);
                if (idx >= 0)
                {
                    _contacts[idx] = refreshed;
                    existing = refreshed;
                }
            }

            LockedUnitRaw = primary;
            LockedUnitWorldPos = existing.WorldPosition;
            LockedUnitCacheTime = Time.unscaledTime;

            _modes.ApplyVanillaLock(id, softSwitchToTrk: true);
            _modes.LockedElevationDeg = existing.ElevationDeg;

            for (int i = 0; i < _contacts.Count; i++)
            {
                _contacts[i].Locked = _contacts[i].Id == id;
            }
        }

        private RadarContact? FindById(int id)
        {
            foreach (RadarContact contact in _contacts)
            {
                if (contact.Id == id)
                {
                    return contact;
                }
            }

            return null;
        }

        private RadarContact BuildContact(object raw, string source)
        {
            Vector3 world = GameReflect.WorldPosition(raw);
            Vector3 delta = world - OwnshipPosition;
            float range = delta.magnitude;
            float absBearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float az = Normalize180(absBearing - OwnshipHeadingDeg);
            float elev = range > 0.01f
                ? Mathf.Asin(Mathf.Clamp(delta.y / range, -1f, 1f)) * Mathf.Rad2Deg
                : 0f;
            float heading = GameReflect.HeadingDeg(raw);
            float speed = GameReflect.SpeedMps(raw);

            Vector3 los = range > 0.01f ? delta / range : Vector3.forward;
            Vector3 ownVel = Quaternion.Euler(0f, OwnshipHeadingDeg, 0f) * Vector3.forward * OwnshipSpeedMps;
            Vector3 tgtVel = Quaternion.Euler(0f, heading, 0f) * Vector3.forward * speed;
            float closing = Vector3.Dot(ownVel - tgtVel, los);

            float bearingToOwnFromTgt = Mathf.Atan2(-delta.x, -delta.z) * Mathf.Rad2Deg;
            float aspect = Mathf.Abs(Normalize180(bearingToOwnFromTgt - heading));

            return new RadarContact
            {
                Id = GameReflect.IdOf(raw),
                Label = GameReflect.LabelOf(raw),
                WorldPosition = world,
                RangeMeters = range,
                AzimuthDeg = az,
                ElevationDeg = elev,
                AbsoluteBearingDeg = absBearing,
                HeadingDeg = heading,
                AltitudeMeters = world.y,
                SpeedMps = speed,
                ClosingSpeedMps = closing,
                AspectDeg = aspect,
                Iff = GameReflect.CompareIff(_player, raw),
                Kind = GameReflect.KindOf(raw),
                Source = source,
                Raw = raw
            };
        }

        private object? UnwrapUnit(object item)
        {
            if (GameReflect.Unit != null && GameReflect.Unit.IsInstanceOfType(item))
            {
                return item;
            }

            if (GameReflect.Aircraft != null && GameReflect.Aircraft.IsInstanceOfType(item))
            {
                return item;
            }

            if (item is Component)
            {
                return item;
            }

            object? inner = GameReflect.FindMember(item.GetType(), "unit", "Unit", "target", "Target", "aircraft", "contact")?.Get(item);
            return inner ?? item;
        }

        private void NotePlayer(string path)
        {
            if (_loggedPlayerPath)
            {
                return;
            }

            _loggedPlayerPath = true;
            Log.Info("Ownship via " + path + " (" + _player?.GetType().FullName + ")");
        }

        private static object? ReadSingleton(Type? type)
        {
            if (type == null)
            {
                return null;
            }

            object? value = GameReflect.FindMember(type, "i", "I", "instance", "Instance", "singleton")?.Get(null);
            if (value != null)
            {
                return value;
            }

            foreach (UnityEngine.Object instance in GameReflect.FindAll(type))
            {
                return instance;
            }

            return null;
        }

        private static object? InvokeStatic(Type? type, params string[] names)
        {
            if (type == null)
            {
                return null;
            }

            foreach (string name in names)
            {
                try
                {
                    MemberHandle? member = GameReflect.FindMember(type, name);
                    object? value = member?.Get(null);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("static " + type.Name + "." + name + " " + ex.Message);
                }
            }

            return null;
        }

        private static float FirstFinite(params float[] values)
        {
            foreach (float value in values)
            {
                if (!float.IsNaN(value) && !float.IsInfinity(value) && value != 0f)
                {
                    return value;
                }
            }

            return float.NaN;
        }


        private void UpdateWeaponHitSolution()
        {
            if (!Config.ShowWeaponEta.Value && !Config.ShowGunFunnel.Value)
            {
                _lastHit = HitSolution.None;
                return;
            }

            try
            {
                WeaponReflect.EnsureDiscovered();
                WeaponSnapshot weapon = WeaponReflect.ResolveCurrent(_player);
                _cachedWeapon = weapon;
                if (!_weaponLogged && weapon.Valid)
                {
                    _weaponLogged = true;
                    Log.Info(
                        "Weapon " + weapon.ShortName + "/" + weapon.WeaponName +
                        " kind=" + weapon.Kind +
                        " ammo=" + weapon.Ammo +
                        " vmax=" + weapon.MaxSpeed.ToString("0") +
                        " Rmax=" + weapon.MaxRangeMeters.ToString("0"));
                }

                RadarContact? tgt = FindLockedOrPrimary();
                if (tgt == null || _player == null)
                {
                    _lastHit = HitSolution.None;
                    return;
                }

                Vector3 ownVel = OwnshipVelocity;
                if (ownVel.sqrMagnitude < 0.01f)
                {
                    ownVel = Quaternion.Euler(0f, OwnshipHeadingDeg, 0f) * Vector3.forward * OwnshipSpeedMps;
                }

                Vector3 tgtVel = WeaponReflect.ReadVelocity(tgt.Raw);
                if (tgtVel.sqrMagnitude < 0.01f)
                {
                    tgtVel = Quaternion.Euler(0f, tgt.HeadingDeg, 0f) * Vector3.forward * tgt.SpeedMps;
                }

                Vector3 forward = WeaponReflect.ReadForward(_player);
                _lastHit = HitSolver.Solve(
                    weapon,
                    OwnshipPosition,
                    ownVel,
                    forward,
                    tgt.WorldPosition,
                    tgtVel,
                    tgt.ClosingSpeedMps);
            }
            catch (Exception ex)
            {
                Log.Debug("Weapon hit solve soft-fail: " + ex.Message);
                _lastHit = HitSolution.None;
            }
        }

        private RadarContact? FindLockedOrPrimary()
        {
            RadarContact? primary = null;
            for (int i = 0; i < _contacts.Count; i++)
            {
                RadarContact c = _contacts[i];
                if (c.Locked || (_modes.Locked && _modes.LockedContactId == c.Id))
                {
                    return c;
                }

                if (primary == null)
                {
                    primary = c;
                }
            }

            // Only solve without lock when gun funnel wants a boresight option — still prefer locked.
            if (_modes.Locked)
            {
                return null;
            }

            // Optional boresight: use nearest in-cone air contact for gun funnel only.
            return primary;
        }

        internal static float Normalize180(float deg)
        {
            float n = deg;
            while (n > 180f)
            {
                n -= 360f;
            }

            while (n < -180f)
            {
                n += 360f;
            }

            return n;
        }
    }
}
