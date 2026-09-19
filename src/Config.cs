using BepInEx.Configuration;
using UnityEngine;

namespace RDA
{
    internal static class Config
    {
        internal static ConfigEntry<KeyboardShortcut> ToggleHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> CycleModeHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> NorthUpHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> RangeUpHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> RangeDownHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> ToggleLockHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> ElevUpHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> ElevDownHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> ElevAutoHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> ScanLeftHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> ScanRightHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> ScanCenterHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> CycleWaveformHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> CycleAcmDesignateHotkey = null!;
        internal static ConfigEntry<KeyboardShortcut> SettingsHotkey = null!;
        internal static ConfigEntry<bool> ShowWindow = null!;
        internal static ConfigEntry<string> HudGateMode = null!;
        internal static ConfigEntry<bool> ForceShowHud = null!;
        internal static ConfigEntry<int> GuiDepth = null!;
        internal static ConfigEntry<bool> NorthUp = null!;
        internal static ConfigEntry<bool> ShowRwr = null!;
        internal static ConfigEntry<bool> ShowGunFunnel = null!;
        internal static ConfigEntry<bool> ShowWorldTrkLabel = null!;
        internal static ConfigEntry<bool> ShowWeaponEta = null!;
        internal static ConfigEntry<bool> ShowAllContactLabels = null!;
        internal static ConfigEntry<int> MaxContactLabels = null!;
        internal static ConfigEntry<bool> DrawScanlines = null!;
        internal static ConfigEntry<bool> PerfMode = null!;
        internal static ConfigEntry<bool> VerboseLogging = null!;
        internal static ConfigEntry<bool> FallbackSceneScan = null!;
        internal static ConfigEntry<bool> ElevAuto = null!;
        internal static ConfigEntry<bool> DisableVanillaRadar = null!;
        internal static ConfigEntry<bool> UseCapacityFallback = null!;
        internal static ConfigEntry<bool> PreferNativeRadarStats = null!;
        internal static ConfigEntry<float> RangeKm = null!;
        internal static ConfigEntry<float> WindowX = null!;
        internal static ConfigEntry<float> WindowY = null!;
        internal static ConfigEntry<float> WindowWidth = null!;
        internal static ConfigEntry<float> WindowHeight = null!;
        internal static ConfigEntry<float> WindowOpacity = null!;
        internal static ConfigEntry<float> AcmAltitudeBandM = null!;
        internal static ConfigEntry<float> AcmLockDwellSec = null!;
        internal static ConfigEntry<float> AcmPersistenceSec = null!;
        internal static ConfigEntry<float> TwsLockDwellSec = null!;
        internal static ConfigEntry<bool> TwsAutoLock = null!;
        internal static ConfigEntry<float> StandbyRcsFactor = null!;
        internal static ConfigEntry<float> AcmBreakSec = null!;
        internal static ConfigEntry<float> ElevStepDeg = null!;
        internal static ConfigEntry<float> ScanSlewStepDeg = null!;
        internal static ConfigEntry<float> ContactUpdateHz = null!;
        internal static ConfigEntry<string> FriendColorHex = null!;
        internal static ConfigEntry<string> FoeColorHex = null!;
        internal static ConfigEntry<string> UnknownColorHex = null!;
        internal static ConfigEntry<string> NeutralColorHex = null!;
        internal static ConfigEntry<string> LockColorHex = null!;

        // Datalink (vanilla FactionHQ trackingDatabase)
        internal static ConfigEntry<bool> UseVanillaDatalink = null!;
        internal static ConfigEntry<bool> ContributeToDatalink = null!;
        internal static ConfigEntry<bool> DatalinkInStandby = null!;
        internal static ConfigEntry<bool> ShowDatalinkOnlyOutsideCone = null!;
        internal static ConfigEntry<bool> ShowOutOfRangeHighValueOnly = null!;
        internal static ConfigEntry<bool> ShowRadarClutter = null!;
        internal static ConfigEntry<float> ClutterIntensity = null!;
        internal static ConfigEntry<float> PdHeadOnAspectDeg = null!;
        internal static ConfigEntry<float> PdMinCloseRangeKm = null!;
        internal static ConfigEntry<int> AesaMinRank = null!;
        internal static ConfigEntry<float> DatalinkFreshSec = null!;
        internal static ConfigEntry<float> DatalinkCoastSec = null!;
        internal static ConfigEntry<float> DatalinkMaxAgeSec = null!;
        internal static ConfigEntry<float> DatalinkPositionNoiseMeters = null!;
        internal static ConfigEntry<float> DatalinkRefreshHz = null!;
        internal static ConfigEntry<int> DatalinkMaxMarkers = null!;
        internal static ConfigEntry<bool> DatalinkPreferHighValue = null!;
        internal static ConfigEntry<float> FriendlyDisplayRangeKm = null!;
        internal static ConfigEntry<float> AirDisplayRangeKm = null!;

        // Missile radar re-lock (ARH/ARM SlowChecks null-lock suicide)
        internal static ConfigEntry<bool> MissileRadarRelock = null!;
        internal static ConfigEntry<bool> BlockLockLostSuicide = null!;

        internal static Color FriendColor => ParseHex(FriendColorHex.Value, new Color(0.25f, 0.95f, 0.45f, 1f));
        internal static Color FoeColor => ParseHex(FoeColorHex.Value, new Color(0.95f, 0.22f, 0.22f, 1f));
        internal static Color UnknownColor => ParseHex(UnknownColorHex.Value, new Color(0.95f, 0.85f, 0.2f, 1f));
        internal static Color NeutralColor => ParseHex(NeutralColorHex.Value, new Color(0.7f, 0.75f, 0.8f, 1f));
        internal static Color LockColor => ParseHex(LockColorHex.Value, new Color(1f, 0.55f, 0.1f, 1f));

        internal static void BindAll(ConfigFile file)
        {
            ToggleHotkey = file.Bind("Hotkeys", "Toggle", new KeyboardShortcut(KeyCode.F6), "Show or hide Oritasy's RADAR overlay.");
            CycleModeHotkey = file.Bind("Hotkeys", "CycleMode", new KeyboardShortcut(KeyCode.F7), "Cycle STBY → SRC/RWS → TWS → ACM → TRK.");
            NorthUpHotkey = file.Bind("Hotkeys", "NorthUp", new KeyboardShortcut(KeyCode.N), "Toggle heading-up / north-up.");
            RangeUpHotkey = file.Bind("Hotkeys", "RangeUp", new KeyboardShortcut(KeyCode.RightBracket), "Increase PPI range.");
            RangeDownHotkey = file.Bind("Hotkeys", "RangeDown", new KeyboardShortcut(KeyCode.LeftBracket), "Decrease PPI range.");
            ToggleLockHotkey = file.Bind("Hotkeys", "ToggleLock", new KeyboardShortcut(KeyCode.None), "Deprecated: RDA does not invent locks. Use vanilla target select/paint/clear. Digit 6 logs a help cue only.");
            ElevUpHotkey = file.Bind("Hotkeys", "ElevUp", new KeyboardShortcut(KeyCode.None), "Optional remap for antenna elev up (digit 7 always works).");
            ElevDownHotkey = file.Bind("Hotkeys", "ElevDown", new KeyboardShortcut(KeyCode.None), "Optional remap for antenna elev down (digit 8 always works).");
            ElevAutoHotkey = file.Bind("Hotkeys", "ElevAuto", new KeyboardShortcut(KeyCode.None), "Optional remap for elev auto toggle (digit 9 always works).");
            ScanLeftHotkey = file.Bind("Hotkeys", "ScanLeft", new KeyboardShortcut(KeyCode.Semicolon), "Slew scan center left (SRC/TWS). Semicolon (;).");
            ScanRightHotkey = file.Bind("Hotkeys", "ScanRight", new KeyboardShortcut(KeyCode.Quote), "Slew scan center right (SRC/TWS). Apostrophe (').");
            ScanCenterHotkey = file.Bind("Hotkeys", "ScanCenter", new KeyboardShortcut(KeyCode.None), "Reset scan center to nose / clear ACM manual override. (Default none — `/` opens Settings.)");
            CycleWaveformHotkey = file.Bind("Hotkeys", "CycleWaveform", new KeyboardShortcut(KeyCode.F8), "Cycle radar waveform PULSE → PD → CW.");
            CycleAcmDesignateHotkey = file.Bind("Hotkeys", "CycleAcmDesignate", new KeyboardShortcut(KeyCode.R), "R: cycle vanilla WeaponManager.targetList (primary=[0]) so missiles guide; ACM may AddTargetList designate when list empty. Syncs LK/TRK.");
            SettingsHotkey = file.Bind("Hotkeys", "Settings", new KeyboardShortcut(KeyCode.Slash), "Open/close Oritasy-style RADAR settings (window position + opacity). Default `/`.");

            ShowWindow = file.Bind("Display", "ShowWindow", true, "Start with the overlay visible.");
            HudGateMode = file.Bind("Display", "HudGateMode", "AircraftPresent",
                new ConfigDescription(
                    "When to draw the MFD: Seated (full HasEjected/seat gate) | AircraftPresent (show when local aircraft resolves; hide only if HasEjected==true or destroyed) | AlwaysWhenToggled (draw whenever ShowWindow — hangar/menu debug / Oritasy conflict bypass).",
                    new AcceptableValueList<string>("Seated", "AircraftPresent", "AlwaysWhenToggled")));
            ForceShowHud = file.Bind("Display", "ForceShowHud", false,
                "Emergency bypass: when true, draw whenever ShowWindow (ignore seat/aircraft gate). Use to verify OritasyHud is not covering RDA.");
            GuiDepth = file.Bind("Display", "GuiDepth", -1000,
                new ConfigDescription(
                    "IMGUI GUI.depth for RDA OnGUI (lower = drawn later / on top). Default -1000 draws above most IMGUI mods including BIAMfd / YukikazeHud; raise toward 0 if RDA covers BIA MFD.",
                    new AcceptableValueRange<int>(-10000, 10000)));
            NorthUp = file.Bind("Display", "NorthUp", false, "If false, the PPI is heading-up.");
            ShowRwr = file.Bind("Display", "ShowRwr", true, "Draw the RWR panel beside the PPI.");
            ShowGunFunnel = file.Bind("Display", "ShowGunFunnel", true, "Draw Su-27-style LCOS gun funnel (tapering diamonds/chevrons + gravity drop) when gun + solution exists.");
            ShowWorldTrkLabel = file.Bind("Display", "ShowWorldTrkLabel", true, "Draw red TRK label above the locked target in the 3D world (screen overlay, not MFD).");
            ShowWeaponEta = file.Bind("Display", "ShowWeaponEta", true, "Show locked-target weapon ETA / HIT|MARG|NO line on the STT lock strip.");
            ShowAllContactLabels = file.Bind("Display", "ShowAllContactLabels", false,
                "If true, label every PPI contact. Default false: locked / air-missile / top-N closest own-radar only (DL ground symbols without text).");
            MaxContactLabels = file.Bind("Display", "MaxContactLabels", 12,
                new ConfigDescription("Max PPI text labels when ShowAllContactLabels is false (PerfMode caps further).", new AcceptableValueRange<int>(0, 64)));
            DrawScanlines = file.Bind("Display", "DrawScanlines", false,
                "Draw CRT scanline fill over the PPI. Default false (large FPS cost when dense).");
            PerfMode = file.Bind("Display", "PerfMode", true,
                "Lighter draw: thinner clutter, fewer contact labels, skip non-essential scanlines/grain. Core radar unchanged.");
            RangeKm = file.Bind("Display", "RangeKm", 40f, new ConfigDescription("PPI range in kilometres.", new AcceptableValueRange<float>(2f, 400f)));
            WindowX = file.Bind("Display", "WindowX", 24f, "Overlay window X.");
            WindowY = file.Bind("Display", "WindowY", 24f, "Overlay window Y.");
            WindowWidth = file.Bind("Display", "WindowWidth", 680f, "Overlay window width (default raised in 1.0.3 for strip text; saved larger sizes kept).");
            WindowHeight = file.Bind("Display", "WindowHeight", 520f, "Overlay window height (default raised in 1.0.3 for taller data strip; saved larger sizes kept).");
            WindowOpacity = file.Bind("Display", "WindowOpacity", 0.92f,
                new ConfigDescription("MFD window opacity / transparency (0.05 = nearly clear, 1.0 = opaque). Applied to panel fills.", new AcceptableValueRange<float>(0.05f, 1f)));

            AcmAltitudeBandM = file.Bind("Radar", "AcmAltitudeBandM", 1200f, new ConfigDescription("ACM A/G same-height gate (meters). Keep surface contacts near ground (alt ≤ band) or within ±band of ownship altitude. High air-altitude unknowns are dropped.", new AcceptableValueRange<float>(200f, 5000f)));
            AcmLockDwellSec = file.Bind("Radar", "AcmLockDwellSec", 0.2f, new ConfigDescription("Legacy alias; ACM is A/G designate (no air auto-lock). Prefer TwsLockDwellSec for air STT.", new AcceptableValueRange<float>(0.05f, 5f)));
            AcmPersistenceSec = file.Bind("Radar", "AcmPersistenceSec", 0.8f, new ConfigDescription("ACM phosphor trail TTL (seconds). Swept speckles/contacts fade after the scan bar passes.", new AcceptableValueRange<float>(0.2f, 2.5f)));
            TwsLockDwellSec = file.Bind("Radar", "TwsLockDwellSec", 0.25f, new ConfigDescription("Seconds an air contact must stay nearest the TWS scan center before auto-lock (air auto-lock).", new AcceptableValueRange<float>(0.05f, 5f)));
            TwsAutoLock = file.Bind("Radar", "TwsAutoLock", true,
                "TWS (对空): auto-lock nearest + highest-threat air unit via vanilla WeaponManager.targetList / AddTargetList (WSO-style). Default true.");
            StandbyRcsFactor = file.Bind("Radar", "StandbyRcsFactor", 0.8f, new ConfigDescription("Local-player RCS scale while radar is STBY (0.8 = −20%). AI unaffected.", new AcceptableValueRange<float>(0.1f, 1f)));
            AcmBreakSec = file.Bind("Radar", "AcmBreakSec", 0.8f, new ConfigDescription("Seconds out of gate before ACM A/G or TWS lock breaks.", new AcceptableValueRange<float>(0.1f, 5f)));
            ElevStepDeg = file.Bind("Radar", "ElevStepDeg", 5f, new ConfigDescription("Manual antenna elevation step (degrees).", new AcceptableValueRange<float>(1f, 30f)));
            ElevAuto = file.Bind("Radar", "ElevAuto", true, "Slave antenna elevation toward locked target (or 0 in ACM search).");
            ScanSlewStepDeg = file.Bind("Radar", "ScanSlewStepDeg", 5f, new ConfigDescription("Manual scan-center slew step (degrees).", new AcceptableValueRange<float>(1f, 30f)));
            ContactUpdateHz = file.Bind("Radar", "ContactUpdateHz", 15f, new ConfigDescription("Throttle for ContactProvider heavy work (Hz). GUI draws from cached snapshot.", new AcceptableValueRange<float>(5f, 60f)));
            DisableVanillaRadar = file.Bind("Radar", "DisableVanillaRadar", false, "When true, Harmony Prefix skips TacScreen.ScanRadar / Radar.TargetSearch for the local player aircraft only (AI unaffected). Default false for BIA coexistence (BIARadar / shared tracks). User may enable. Fail-soft.");
            UseCapacityFallback = file.Bind("Radar", "UseCapacityFallback", true,
                "When no builtin/override profile matches, derive envelope from PowerSupply.maxCharge / maxPower.");
            PreferNativeRadarStats = file.Bind("Radar", "PreferNativeRadarStats", true,
                "When deriving from capacity, blend 50/50 with native Radar.radarCone + RadarParameters.maxRange if reflection finds them (Hybrid source).");

            FriendColorHex = file.Bind("Colors", "Friend", "#40F273", "Friendly blip color (hex).");
            FoeColorHex = file.Bind("Colors", "Foe", "#F23838", "Hostile blip color (hex).");
            UnknownColorHex = file.Bind("Colors", "Unknown", "#F2D933", "Unknown IFF color (hex).");
            NeutralColorHex = file.Bind("Colors", "Neutral", "#B3BFCC", "Neutral blip color (hex).");
            LockColorHex = file.Bind("Colors", "Lock", "#FF8C1A", "TRK / locked highlight (hex).");

            UseVanillaDatalink = file.Bind("Datalink", "UseVanillaDatalink", true,
                "Ingest FactionHQ.trackingDatabase into the PPI as Source=datalink contacts.");
            ContributeToDatalink = file.Bind("Datalink", "ContributeToDatalink", true,
                "When RDA detects/locks foes, call Rpc/CmdUpdateTrackingInfo on local HQ (rate-limited). Helps when DisableVanillaRadar suppresses shared radar.");
            DatalinkFreshSec = file.Bind("Datalink", "DatalinkFreshSec", 4f,
                new ConfigDescription("Age under which a HQ track is Fresh (vanilla LAST_SPOTTED_EXTRA_TIME ≈ 4s).", new AcceptableValueRange<float>(0.5f, 30f)));
            DatalinkCoastSec = file.Bind("Datalink", "DatalinkCoastSec", 30f,
                new ConfigDescription("Show coasting after Fresh until this age.", new AcceptableValueRange<float>(1f, 300f)));
            DatalinkMaxAgeSec = file.Bind("Datalink", "DatalinkMaxAgeSec", 60f,
                new ConfigDescription("Drop HQ tracks older than this.", new AcceptableValueRange<float>(5f, 600f)));
            DatalinkPositionNoiseMeters = file.Bind("Datalink", "DatalinkPositionNoiseMeters", 50f,
                new ConfigDescription("Position noise (meters) that grows with age while coasting/stale.", new AcceptableValueRange<float>(0f, 500f)));
            ShowDatalinkOnlyOutsideCone = file.Bind("Datalink", "ShowDatalinkOnlyOutsideCone", false,
                "If true, datalink tracks are only kept when outside the own-radar cone (and not already in the own-radar set).");
            ShowOutOfRangeHighValueOnly = file.Bind("Datalink", "ShowOutOfRangeHighValueOnly", true,
                "Outside current mode cone/range: keep only high-value (strategic / Building/Airbase/RadarStation/Factory/large) or threats (RWR / missile). Drops DL spam (QuadVT/storage).");
            DatalinkInStandby = file.Bind("Datalink", "DatalinkInStandby", true,
                "When UseVanillaDatalink, still show datalink tracks in STBY (AWACS-feed style).");
            DatalinkRefreshHz = file.Bind("Datalink", "DatalinkRefreshHz", 4f,
                new ConfigDescription("Throttle HQ trackingDatabase ingest (Hz). Lower = less CPU when DL is dense.", new AcceptableValueRange<float>(1f, 30f)));
            DatalinkMaxMarkers = file.Bind("Datalink", "DatalinkMaxMarkers", 24,
                new ConfigDescription("Hard cap on ingested AND drawn datalink-only markers after priority cull (range 4–128; PerfMode may cap further to 18).", new AcceptableValueRange<int>(4, 128)));
            DatalinkPreferHighValue = file.Bind("Datalink", "DatalinkPreferHighValue", true,
                "When culling dense DL: keep threats / missiles / HV / foes first; drop distant spam.");

            FriendlyDisplayRangeKm = file.Bind("Radar", "FriendlyDisplayRangeKm", 10f,
                new ConfigDescription("Friendly contacts: only show / designate if within this range (km). Applies PPI/ACM/TWS/datalink.", new AcceptableValueRange<float>(1f, 200f)));
            AirDisplayRangeKm = file.Bind("Radar", "AirDisplayRangeKm", 20f,
                new ConfigDescription("Air contacts (空军): only show / designate if within this range (km). Applies PPI/ACM/TWS/datalink.", new AcceptableValueRange<float>(1f, 400f)));

            ShowRadarClutter = file.Bind("Display", "ShowRadarClutter", true,
                "Draw realistic PPI/ACM ground clutter, scan noise, and radial grain (visual only — never lockable contacts).");
            ClutterIntensity = file.Bind("Display", "ClutterIntensity", 0.45f,
                new ConfigDescription("Radar clutter intensity 0–1 (PULSE denser look-down; PD cleaner air).", new AcceptableValueRange<float>(0f, 1f)));

            PdHeadOnAspectDeg = file.Bind("Radar", "PdHeadOnAspectDeg", 45f,
                new ConfigDescription("PD waveform: half-cone (deg) around head-on (AspectDeg≈0 / strong closing) that always detects. Beam/tail outside this cone are culled unless very close.", new AcceptableValueRange<float>(10f, 90f)));
            PdMinCloseRangeKm = file.Bind("Radar", "PdMinCloseRangeKm", 6f,
                new ConfigDescription("PD off-aspect bleed-through range (km). Beam/tail may flicker-detect inside this range on non-AESA radars.", new AcceptableValueRange<float>(1f, 30f)));
            AesaMinRank = file.Bind("Radar", "AesaMinRank", 3,
                new ConfigDescription("RadarRank ≥ this value is AESA-like: PD aspect/head-on gate waived. Proxy: AircraftDefinition.aircraftParameters.rankRequired (not player PlayerRank).", new AcceptableValueRange<int>(1, 10)));

            MissileRadarRelock = file.Bind("Missile", "MissileRadarRelock", true,
                "When an ARH/ARM missile seeker clears lock mid-flight, re-lock onto local WeaponManager.targetList[0] (or HQ-tracked prior target) if the missile is local-player owned.");
            BlockLockLostSuicide = file.Bind("Missile", "BlockLockLostSuicide", true,
                "Suppress only the SlowChecks null-target self-destruct while ownship radar still has a usable lock. Does not disable impact/proximity/timer/sea/ground/miss-kinematic detonation.");

            VerboseLogging = file.Bind("Debug", "VerboseLogging", false, "Log discovered game members and reflection probes.");
            FallbackSceneScan = file.Bind("Debug", "FallbackSceneScan", true, "If private radar track lists are empty, geometrically scan nearby Units inside the cone. Display-only.");
        }

        internal static bool IsForceOrAlwaysGate()
        {
            return ForceShowHud.Value || IsAlwaysWhenToggledGate();
        }

        internal static bool IsAlwaysWhenToggledGate()
        {
            return string.Equals(HudGateMode.Value, "AlwaysWhenToggled", System.StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsAircraftPresentGate()
        {
            return string.Equals(HudGateMode.Value, "AircraftPresent", System.StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsSeatedGate()
        {
            return !IsAircraftPresentGate() && !IsAlwaysWhenToggledGate();
        }

        internal static Color ParseHex(string? hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex))
            {
                return fallback;
            }

            string t = hex.Trim();
            if (t.StartsWith("#"))
            {
                t = t.Substring(1);
            }

            if (t.Length != 6 && t.Length != 8)
            {
                return fallback;
            }

            try
            {
                byte r = System.Convert.ToByte(t.Substring(0, 2), 16);
                byte g = System.Convert.ToByte(t.Substring(2, 2), 16);
                byte b = System.Convert.ToByte(t.Substring(4, 2), 16);
                byte a = t.Length == 8 ? System.Convert.ToByte(t.Substring(6, 2), 16) : (byte)255;
                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }
            catch
            {
                return fallback;
            }
        }
    }
}
