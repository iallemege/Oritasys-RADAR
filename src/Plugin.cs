using System.Collections;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace RDA
{
    [BepInPlugin(Guid, DisplayName, Version)]
    [BepInDependency("bia.runtime")]
    [BepInDependency("com.iallemmege.oritasy", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.iallemmege.oritasyhud", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.iallemmege.RDA";
        public const string DisplayName = "Oritasy's RADAR";
        public const string BiaRuntimeGuid = "bia.runtime";
        // BepInEx parses this value as SemVer; keep the brand suffix in DisplayVersion.
        public const string Version = "0.0.6";
        public const string DisplayVersion = "0.0.6T";

        /// <summary>Full expansion for README / log only — never shown on the GUI chrome.</summary>
        public const string FullExpansion = "Realtime Aerial Detection And Ranging (R.A.D.A.R.)";

        internal static Plugin? Instance { get; private set; }

        private Harmony? _harmony;
        private RadarGui? _gui;
        private ContactProvider? _contacts;
        private ModeState? _modes;
        private RwrPanel? _rwr;
        private SettingsMenu? _settings;
        private float _nextWindowPersist;
        private float _nextHudDiag;
        private bool _stylesApplied;
        private bool _loggedOnGuiError;
        private bool _harmonyStarted;

        private void Awake()
        {
            Instance = this;
            Log.Source = Logger;
            RDA.Config.BindAll(base.Config);
            Log.Verbose = RDA.Config.VerboseLogging.Value;
            RDA.Config.VerboseLogging.SettingChanged += (_, _) => Log.Verbose = RDA.Config.VerboseLogging.Value;

            GameReflect.Discover();
            WeaponReflect.EnsureDiscovered();
            RadarProfileCatalog.EnsureReady();
            OritasyUi.EnsureLoaded();

            _modes = new ModeState
            {
                ElevAuto = RDA.Config.ElevAuto.Value
            };
            _rwr = new RwrPanel();
            _contacts = new ContactProvider(_modes, _rwr);
            _gui = new RadarGui(_modes, _contacts, _rwr);
            _settings = new SettingsMenu();

            // Heavy Harmony deferred to Start (one/two frames) so Oritasy / OritasyHud finish Awake first.
            // Visibility fixes (GUI.depth / HudGateMode) are the primary compat path — do not disable OritasyHud.

            bool biaPresent = IsBiaRuntimeLoaded();
            if (!biaPresent)
            {
                // Hard BepInDependency normally prevents load; soft message if somehow running without.
                Log.Warn($"{DisplayName} {DisplayVersion}: BIA Runtime ({BiaRuntimeGuid}) is required but was not found in Chainloader.PluginInfos. Install BIA.Runtime_*.dll into BepInEx/plugins.");
            }
            else
            {
                Log.Info($"{DisplayName} {DisplayVersion}: BIA Runtime present — coexistence OK (BIARadar / BIAMfd + RDA). Toggle overlay with {RDA.Config.ToggleHotkey.Value}. DisableVanillaRadar default=false so shared tracks are not starved.");
            }

            Log.Info($"{DisplayName} {DisplayVersion} — {FullExpansion}. BIA required (hard dep {BiaRuntimeGuid}). Toggle {RDA.Config.ToggleHotkey.Value}. HudGateMode={RDA.Config.HudGateMode.Value} ForceShowHud={RDA.Config.ForceShowHud.Value} GuiDepth={RDA.Config.GuiDepth.Value} DisableVanillaRadar={RDA.Config.DisableVanillaRadar.Value}. Soft-deps: oritasy / oritasyhud.");
        }

        /// <summary>True when Chainloader reports bia.runtime (or assembly name fallback).</summary>
        internal static bool IsBiaRuntimeLoaded()
        {
            try
            {
                if (Chainloader.PluginInfos != null && Chainloader.PluginInfos.ContainsKey(BiaRuntimeGuid))
                {
                    return true;
                }
            }
            catch
            {
                // fail soft
            }

            try
            {
                foreach (System.Reflection.Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
                {
                    string name = asm.GetName().Name ?? string.Empty;
                    if (name.IndexOf("BIA.Runtime", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        name.IndexOf("BIARuntime", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // fail soft
            }

            return false;
        }

        private void Start()
        {
            if (!_harmonyStarted)
            {
                StartCoroutine(DeferredHarmonyApply());
            }
        }

        private IEnumerator DeferredHarmonyApply()
        {
            // Let peer plugins (Oritasy / OritasyHud) finish their Awake/Start before we patch.
            yield return null;
            yield return null;
            if (_harmonyStarted || _rwr == null || _contacts == null || _modes == null)
            {
                yield break;
            }

            _harmonyStarted = true;
            try
            {
                _harmony = new Harmony(Guid + ".harmony");
                HarmonyHooks.Apply(_harmony, _rwr, _contacts, _modes);
            }
            catch (System.Exception ex)
            {
                Log.Warn("Deferred Harmony apply failed softly: " + ex.Message);
            }
        }

        private void Update()
        {
            try
            {
                if (RDA.Config.ToggleHotkey.Value.IsDown())
                {
                    RDA.Config.ShowWindow.Value = !RDA.Config.ShowWindow.Value;
                }

                if (RDA.Config.SettingsHotkey.Value.IsDown())
                {
                    _settings ??= new SettingsMenu();
                    _settings.Toggle();
                }

                if (_modes == null || _contacts == null || _rwr == null)
                {
                    return;
                }

                if (RDA.Config.CycleModeHotkey.Value.IsDown())
                {
                    _modes.Cycle(+1);
                }

                if (RDA.Config.NorthUpHotkey.Value.IsDown())
                {
                    RDA.Config.NorthUp.Value = !RDA.Config.NorthUp.Value;
                }

                if (RDA.Config.RangeUpHotkey.Value.IsDown())
                {
                    _modes.StepRange(+1);
                }

                if (RDA.Config.RangeDownHotkey.Value.IsDown())
                {
                    _modes.StepRange(-1);
                }

                // ToggleLockHotkey / key 6: do not invent LOCK — vanilla target select / paint / clear.
                if (RDA.Config.ToggleLockHotkey.Value.IsDown())
                {
                    Log.Info("Lock hotkey ignored — use game target lock (WeaponManager.targetList)");
                }

                if (RDA.Config.ElevUpHotkey.Value.IsDown())
                {
                    _modes.ElevUp();
                }

                if (RDA.Config.ElevDownHotkey.Value.IsDown())
                {
                    _modes.ElevDown();
                }

                if (RDA.Config.ElevAutoHotkey.Value.IsDown())
                {
                    _modes.ToggleElevAuto();
                    RDA.Config.ElevAuto.Value = _modes.ElevAuto;
                }

                if (RDA.Config.ScanLeftHotkey.Value.IsDown())
                {
                    _modes.SlewScan(-1);
                }

                if (RDA.Config.ScanRightHotkey.Value.IsDown())
                {
                    _modes.SlewScan(+1);
                }

                if (RDA.Config.ScanCenterHotkey.Value.IsDown())
                {
                    _modes.ResetScanCenter();
                }

                if (RDA.Config.CycleWaveformHotkey.Value.IsDown())
                {
                    _modes.CycleWaveform();
                }

                // R: cycle vanilla WeaponManager.targetList (missile guide) in ALL modes
                if (RDA.Config.CycleAcmDesignateHotkey.Value.IsDown() || Input.GetKeyDown(KeyCode.R))
                {
                    _contacts.CycleVanillaTargetList();
                }

                // Digit keys: only poll when anyKeyDown to skip idle frames.
                if (Input.anyKeyDown)
                {
                    HandleDigitKeys(_modes);
                }

                _contacts.Tick();
                MissileRadarSupport.Tick(_contacts.Player);
                _modes.EnginePowered = _contacts.EngineRunning;
                _modes.Tick(Time.unscaledDeltaTime);
                _rwr.OwnshipHeadingDeg = _contacts.OwnshipHeadingDeg;
                _rwr.PlayerAircraft = _contacts.Player;
                _rwr.Tick(Time.unscaledDeltaTime);

                if (Time.unscaledTime >= _nextHudDiag)
                {
                    _nextHudDiag = Time.unscaledTime + 5f;
                    LogHudDiagnostics();
                }
            }
            catch (System.Exception ex)
            {
                Log.Debug("Update failed softly: " + ex.Message);
            }
        }

        private void LogHudDiagnostics()
        {
            if (_contacts == null)
            {
                return;
            }

            Rect wr = _gui != null ? _gui.WindowRect : default;
            string ejected = _contacts.LastHasEjected.HasValue
                ? (_contacts.LastHasEjected.Value ? "true" : "false")
                : "n/a";
            Log.Info(
                "HUD diag: ShowWindow=" + RDA.Config.ShowWindow.Value
                + " InMission=" + _contacts.InMission
                + " InAircraft=" + _contacts.InAircraft
                + " playerResolved=" + (_contacts.Player != null)
                + " HasEjected=" + ejected
                + " HudGateMode=" + RDA.Config.HudGateMode.Value
                + " ForceShowHud=" + RDA.Config.ForceShowHud.Value
                + " ShouldDrawHud=" + _contacts.ShouldDrawHud
                + " window=(" + wr.x.ToString("F0") + "," + wr.y.ToString("F0")
                + "," + wr.width.ToString("F0") + "x" + wr.height.ToString("F0") + ")");
        }

        private static void HandleDigitKeys(ModeState modes)
        {
            if (DigitDown(KeyCode.Alpha1, KeyCode.Keypad1))
            {
                modes.SetMode(RadarMode.Standby);
            }
            else if (DigitDown(KeyCode.Alpha2, KeyCode.Keypad2))
            {
                modes.SetMode(RadarMode.SrcRws);
            }
            else if (DigitDown(KeyCode.Alpha3, KeyCode.Keypad3))
            {
                modes.SetMode(RadarMode.Tws);
            }
            else if (DigitDown(KeyCode.Alpha4, KeyCode.Keypad4))
            {
                modes.SetMode(RadarMode.Acm);
            }
            else if (DigitDown(KeyCode.Alpha5, KeyCode.Keypad5))
            {
                modes.SetMode(RadarMode.Trk);
            }
            else if (DigitDown(KeyCode.Alpha6, KeyCode.Keypad6))
            {
                // Repurposed: help cue only — real lock is vanilla CombatHUD / WeaponManager.targetList.
                Log.Info("Key 6: use game target select/paint/clear for lock (TRK is post-lock display)");
            }
            else if (DigitDown(KeyCode.Alpha7, KeyCode.Keypad7))
            {
                modes.ElevUp();
            }
            else if (DigitDown(KeyCode.Alpha8, KeyCode.Keypad8))
            {
                modes.ElevDown();
            }
            else if (DigitDown(KeyCode.Alpha9, KeyCode.Keypad9))
            {
                modes.ToggleElevAuto();
                RDA.Config.ElevAuto.Value = modes.ElevAuto;
            }
            else if (DigitDown(KeyCode.Alpha0, KeyCode.Keypad0))
            {
                modes.StepRange(+1);
            }
        }

        private static bool DigitDown(KeyCode alpha, KeyCode keypad)
        {
            return Input.GetKeyDown(alpha) || Input.GetKeyDown(keypad);
        }

        private void OnGUI()
        {
            int previousDepth = GUI.depth;
            try
            {
                // Draw on top of other IMGUI mods (e.g. OritasyHud / BIAMfd). Lower depth = later = on top.
                // Configurable; default -1000 may cover BIAMfd / YukikazeHud — lower magnitude if needed.
                GUI.depth = RDA.Config.GuiDepth.Value;

                if (!_stylesApplied)
                {
                    OritasyUi.ApplyToStyles();
                    _stylesApplied = true;
                }

                bool show = RDA.Config.ShowWindow.Value;
                bool drawHud = show && _contacts != null && _contacts.ShouldDrawHud;
                if (drawHud && _gui != null)
                {
                    _gui.Draw();
                    if (Time.unscaledTime >= _nextWindowPersist)
                    {
                        _nextWindowPersist = Time.unscaledTime + 2f;
                        _gui.PersistWindow();
                    }
                }

                // Gun funnel / world TRK / RWR (inside MFD) — same aircraft gate.
                if (drawHud && _contacts != null && RDA.Config.ShowGunFunnel.Value)
                {
                    GunFunnelHud.Draw(_contacts);
                }

                if (drawHud && _contacts != null && _modes != null)
                {
                    WorldTrkLabelHud.Draw(_modes, _contacts);
                }

                // Settings menu: available whenever `/` toggled — does not require aircraft.
                if (_settings != null && _settings.IsOpen)
                {
                    _settings.Draw(_gui);
                }
            }
            catch (System.Exception ex)
            {
                if (!_loggedOnGuiError)
                {
                    _loggedOnGuiError = true;
                    Log.Warn("OnGUI failed: " + ex);
                }
                else
                {
                    Log.Debug("OnGUI failed softly: " + ex.Message);
                }
            }
            finally
            {
                GUI.depth = previousDepth;
            }
        }

        private void OnDestroy()
        {
            if (_harmony != null)
            {
                _harmony.UnpatchSelf();
                _harmony = null;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
