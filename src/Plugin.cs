using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace RDA
{
    [BepInPlugin(Guid, DisplayName, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.iallemmege.RDA";
        public const string DisplayName = "Oritasy's RADAR";
        public const string Version = "0.0.1T";

        /// <summary>Full expansion for README / log only — never shown on the GUI chrome.</summary>
        public const string FullExpansion = "Realtime Aerial Detection And Ranging (R.A.D.A.R.)";

        internal static Plugin? Instance { get; private set; }

        private Harmony? _harmony;
        private RadarGui? _gui;
        private ContactProvider? _contacts;
        private ModeState? _modes;
        private RwrPanel? _rwr;
        private float _nextWindowPersist;
        private bool _stylesApplied;

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

            _harmony = new Harmony(Guid + ".harmony");
            HarmonyHooks.Apply(_harmony, _rwr, _contacts, _modes);

            Log.Info($"{DisplayName} {Version} — {FullExpansion}. Toggle {RDA.Config.ToggleHotkey.Value}. Independent standalone plugin.");
        }

        private void Update()
        {
            try
            {
                if (RDA.Config.ToggleHotkey.Value.IsDown())
                {
                    RDA.Config.ShowWindow.Value = !RDA.Config.ShowWindow.Value;
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
            }
            catch (System.Exception ex)
            {
                Log.Debug("Update failed softly: " + ex.Message);
            }
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
            try
            {
                if (!_stylesApplied)
                {
                    OritasyUi.ApplyToStyles();
                    _stylesApplied = true;
                }

                // Hotkey may still flip ShowWindow, but draw only while seated in aircraft
                // (hide on eject / die / spectate / menu; show again on board).
                bool drawHud = _contacts != null && _contacts.ShouldDrawHud;
                if (drawHud && _gui != null && RDA.Config.ShowWindow.Value)
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
            }
            catch (System.Exception ex)
            {
                Log.Debug("OnGUI failed softly: " + ex.Message);
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
