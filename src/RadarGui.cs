using System.Collections.Generic;
using UnityEngine;

namespace RDA
{
    internal sealed class RadarGui
    {
        private const int WindowId = 0x00524441;
        private const float HeaderH = 30f;
        private const float ModeBarH = 26f;
        private const float RangeChipH = 22f;
        private const float StripMinH = 72f;
        private const float StripLockH = 96f;
        private const float FooterH = 28f;

        private readonly ModeState _modes;
        private readonly ContactProvider _contacts;
        private readonly RwrPanel _rwr;
        private Rect _window;

        // Cheap sweep trail (previous angles).
        private readonly float[] _sweepTrail = new float[3];
        private int _sweepTrailCount;
        private float _lastSweepAngle = float.NaN;

        // ACM phosphor: contact stamp times (fade after scan bar passes).
        private readonly Dictionary<int, float> _acmContactStamp = new Dictionary<int, float>(64);

        internal RadarGui(ModeState modes, ContactProvider contacts, RwrPanel rwr)
        {
            _modes = modes;
            _contacts = contacts;
            _rwr = rwr;
            _window = new Rect(Config.WindowX.Value, Config.WindowY.Value, Config.WindowWidth.Value, Config.WindowHeight.Value);
        }

        internal Rect WindowRect => _window;

        internal void Draw()
        {
            EnsureWindowOnScreen();
            // Panel translucency via GL (ScopeDraw.PanelFill) — not GUI.color / stacked white fills.
            float opacity = Mathf.Clamp(Config.WindowOpacity != null ? Config.WindowOpacity.Value : 0.92f, 0.05f, 1f);
            ScopeDraw.UiOpacity = opacity;
            _window = GUI.Window(WindowId, _window, DrawContents, GUIContent.none, ScopeDraw.WindowStyle);
        }

        /// <summary>Live apply settings-menu position to the MFD rect.</summary>
        internal void ApplyWindowPosition(float x, float y)
        {
            _window.x = x;
            _window.y = y;
            EnsureWindowOnScreen();
        }

        /// <summary>Reset window if NaN or completely off Screen.width/height.</summary>
        internal void EnsureWindowOnScreen()
        {
            float sw = Screen.width;
            float sh = Screen.height;
            float cfgW = Mathf.Clamp(Config.WindowWidth.Value, 200f, 2000f);
            float cfgH = Mathf.Clamp(Config.WindowHeight.Value, 160f, 1600f);
            bool nan = float.IsNaN(_window.x) || float.IsNaN(_window.y)
                || float.IsNaN(_window.width) || float.IsNaN(_window.height)
                || float.IsInfinity(_window.x) || float.IsInfinity(_window.y);
            bool tiny = _window.width < 50f || _window.height < 50f;
            // Completely off-screen (no overlap with visible area).
            bool off = _window.xMax < 0f || _window.yMax < 0f
                || _window.x >= sw || _window.y >= sh;
            if (nan || tiny || off)
            {
                _window = new Rect(40f, 40f, cfgW, cfgH);
                Log.Info("MFD window reset to safe default (40,40) — was off-screen/NaN/tiny.");
            }
        }

        internal void PersistWindow()
        {
            Config.WindowX.Value = _window.x;
            Config.WindowY.Value = _window.y;
            Config.WindowWidth.Value = _window.width;
            Config.WindowHeight.Value = _window.height;
        }

        private void DrawContents(int id)
        {
            Rect outer = ScopeDraw.Snap(new Rect(0f, 0f, _window.width, _window.height));
            ScopeDraw.HudPanel(outer, accentTop: false);

            Rect header = ScopeDraw.Snap(new Rect(8f, 4f, outer.width - 16f, HeaderH));
            DrawHeader(header);

            Rect inner = ScopeDraw.Snap(new Rect(10f, header.yMax + 2f, outer.width - 20f, outer.height - header.yMax - 8f));
            bool showRwr = Config.ShowRwr.Value;
            float rwrWidth = showRwr ? 128f : 0f;
            float elevTapeW = 22f;

            Rect modeBar = ScopeDraw.Snap(new Rect(inner.x, inner.y, inner.width, ModeBarH));
            Rect rangeChips = ScopeDraw.Snap(new Rect(inner.x, modeBar.yMax + 3f, inner.width, RangeChipH));

            // Page routing (1.0):
            //  - ACM && !vanillaLock → A/G scan page (L-R bar inside dashed circle)
            //  - Any vanilla lock → classic TRK PPI (+ lock line); never invent lock
            //  - TRK without lock → classic PPI + NO TGT (no fake LK)
            //  - TWS / SRC / STBY → classic PPI
            bool acmScan = _modes.Mode == RadarMode.Acm && !_modes.Locked;

            float footerY = inner.yMax - FooterH;
            Rect footer = ScopeDraw.Snap(new Rect(inner.x, footerY, inner.width, FooterH));

            DrawModeBar(modeBar);
            DrawRangeChips(rangeChips);

            if (acmScan)
            {
                Rect page = ScopeDraw.Snap(new Rect(
                    inner.x,
                    rangeChips.yMax + 4f,
                    inner.width,
                    Mathf.Max(80f, footerY - rangeChips.yMax - 6f)));
                DrawAcmAgPage(page);
            }
            else
            {
                bool locked = _modes.Locked;
                bool etaLine = locked && Config.ShowWeaponEta.Value;
                // ≥64 unlocked / ≥88 locked; 3–4 short lines (+ optional ETA)
                float stripH = locked ? (etaLine ? Mathf.Max(StripLockH, 104f) : StripLockH) : StripMinH;
                Rect strip = ScopeDraw.Snap(new Rect(inner.x, rangeChips.yMax + 3f, inner.width, stripH));
                float ppiH = Mathf.Max(80f, footerY - strip.yMax - 6f);
                Rect ppi = ScopeDraw.Snap(new Rect(
                    inner.x,
                    strip.yMax + 4f,
                    inner.width - rwrWidth - elevTapeW - (showRwr ? 8f : 0f) - 4f,
                    ppiH));
                Rect elevTape = ScopeDraw.Snap(new Rect(ppi.xMax + 4f, ppi.y, elevTapeW, ppi.height));
                Rect rwr = ScopeDraw.Snap(new Rect(elevTape.xMax + (showRwr ? 4f : 0f), ppi.y, rwrWidth, ppi.height));

                if (locked)
                {
                    DrawLockDataStrip(strip);
                }
                else
                {
                    DrawDataStrip(strip);
                }

                DrawPpi(ppi);
                DrawElevTape(elevTape);
                if (showRwr)
                {
                    _rwr.Draw(rwr);
                }
            }

            DrawFooter(footer);

            // Engine-off: dim + freeze cue (scan/contacts already held by providers)
            if (_contacts.InMission && !_contacts.EngineRunning)
            {
                Rect freeze = ScopeDraw.Snap(new Rect(8f, HeaderH + 4f, _window.width - 16f, _window.height - HeaderH - FooterH - 8f));
                ScopeDraw.Fill(freeze, new Color(0f, 0f, 0f, 0.45f));
                ScopeDraw.ClippedLabel(
                    new Rect(freeze.x, freeze.y + freeze.height * 0.45f, freeze.width, 24f),
                    "ENGINE OFF  ·  RADAR FROZEN",
                    ScopeDraw.MutedStyle);
            }

            GUI.DragWindow(new Rect(0f, 0f, _window.width, HeaderH + 6f));
        }


        private void DrawHeader(Rect rect)
        {
            GUI.BeginGroup(rect);

            // No stacked semi-opaque fill — outer HudPanel GL already provides see-through dark.
            ScopeDraw.Fill(new Rect(0f, rect.height - 2f, rect.width, 2f), ScopeDraw.CyanAccent * new Color(1f, 1f, 1f, 0.45f));

            ScopeDraw.ClippedLabel(
                new Rect(6f, 0f, 220f, rect.height),
                "R.A.D.A.R",
                ScopeDraw.TitleStyle);

            // Status LEDs — PWR follows engine (fail-soft: unknown ⇒ on)
            bool powered = _contacts.InMission && _contacts.EngineRunning;
            ScopeDraw.StatusLed(new Vector2(rect.width - 48f, rect.height * 0.5f),
                powered ? ScopeDraw.LedGreen : ScopeDraw.PhosphorDim, 3.5f);
            ScopeDraw.StatusLed(new Vector2(rect.width - 28f, rect.height * 0.5f),
                _modes.Locked ? ScopeDraw.LedAmber : ScopeDraw.PhosphorDim, 3.5f);
            ScopeDraw.ClippedLabel(new Rect(rect.width - 90f, 2f, 36f, rect.height - 4f), "PWR", ScopeDraw.TinyStyle);
            ScopeDraw.ClippedLabel(new Rect(rect.width - 20f, 2f, 18f, rect.height - 4f), "LK", ScopeDraw.TinyStyle);

            GUI.EndGroup();
        }

        private void DrawModeBar(Rect rect)
        {
            GUI.BeginGroup(rect);
            float w = 74f;
            float h = ModeBarH;
            DrawModeChip(new Rect(0f, 0f, w, h), RadarMode.Standby);
            DrawModeChip(new Rect(w + 4f, 0f, w, h), RadarMode.SrcRws);
            DrawModeChip(new Rect((w + 4f) * 2f, 0f, w, h), RadarMode.Tws);
            DrawModeChip(new Rect((w + 4f) * 3f, 0f, 56f, h), RadarMode.Acm);
            DrawModeChip(new Rect((w + 4f) * 3f + 60f, 0f, 56f, h), RadarMode.Trk);

            string orient = Config.NorthUp.Value ? "N-UP" : "HDG-UP";
            Rect orientR = new Rect(rect.width - 156f, 0f, 70f, h);
            if (ScopeDraw.ChipButton(orientR, orient, Config.NorthUp.Value))
            {
                Config.NorthUp.Value = !Config.NorthUp.Value;
            }

            float scanC = _modes.EffectiveScanCenterAzimuthDeg();
            string scanTxt = "SCN " + scanC.ToString("+0;-0") + "°";
            ScopeDraw.ClippedLabel(new Rect(rect.width - 82f, 0f, 82f, h), scanTxt, ScopeDraw.TinyStyle);
            GUI.EndGroup();
        }

        private void DrawModeChip(Rect rect, RadarMode mode)
        {
            bool on = _modes.Mode == mode;
            if (ScopeDraw.ChipButton(rect, ModeState.Label(mode), on))
            {
                _modes.SetMode(mode);
            }
        }

        private void DrawRangeChips(Rect rect)
        {
            GUI.BeginGroup(rect);
            float chipW = 40f;
            float x = 0f;
            float current = _modes.DisplayRangeKm();
            float max = _modes.EffectiveMaxRangeKm();
            for (int i = 0; i < ModeState.RangeStepsKm.Length; i++)
            {
                float km = ModeState.RangeStepsKm[i];
                if (km > max + 0.01f)
                {
                    continue;
                }

                bool on = Mathf.Abs(current - km) < 0.1f;
                Rect chip = new Rect(x, 0f, chipW, RangeChipH);
                if (ScopeDraw.ChipButton(chip, km.ToString("0"), on))
                {
                    _modes.SetRangeKm(km);
                }

                x += chipW + 3f;
            }

            ScopeDraw.ClippedLabel(new Rect(x + 4f, 0f, 80f, RangeChipH), "RNG km", ScopeDraw.TinyStyle);
            GUI.EndGroup();
        }

        private void DrawDataStrip(Rect rect)
        {
            ScopeDraw.Fill(rect, new Color(0.02f, 0.09f, 0.06f, 0.92f));
            ScopeDraw.Border(rect, ScopeDraw.PanelBorder, 1.5f);

            RadarContact? primary = FindPrimary();
            AircraftRadarProfile prof = _modes.ActiveProfile;
            string elevAuto = _modes.ElevAuto ? "A" : "M";
            string wfTag = _modes.WaveformLabel;

            // Split into 3–4 short lines (avoid one mega-line that overflows / clips).
            string line1 =
                "MD " + _modes.StatusLabel +
                "  WF " + wfTag +
                "  " + prof.RankTag +
                "  PROF " + Abbreviate(prof.DisplayLabel, 14) +
                "  " + prof.SourceTag;
            string line2 =
                "RNG " + _modes.DisplayRangeKm().ToString("0") + "km" +
                "  FOV ±" + _contacts.EffectiveHalfFovDeg().ToString("0") + "°" +
                "  ELV " + _modes.AntennaElevationDeg.ToString("+0.0;-0.0") + "°" + elevAuto +
                "  SCN " + _modes.EffectiveScanCenterAzimuthDeg().ToString("+0;-0") + "°";
            string line3 =
                "HDG " + _contacts.OwnshipHeadingDeg.ToString("000") + "°" +
                "  ALT " + _contacts.OwnshipAltitudeMeters.ToString("0") + "m" +
                "  SPD " + _contacts.OwnshipSpeedMps.ToString("0") + "m/s" +
                "  N " + _contacts.Contacts.Count +
                "  DL " + _contacts.DatalinkCount +
                "  RWR " + _rwr.Threats.Count;
            string line4;
            if (primary != null && !IsOwnshipContact(primary))
            {
                line4 =
                    "TGT " + Abbreviate(primary.Label, 10) +
                    "  AZ " + primary.AzimuthDeg.ToString("+0.0;-0.0") + "°" +
                    "  EL " + primary.ElevationDeg.ToString("+0.0;-0.0") + "°" +
                    "  R " + (primary.RangeMeters / 1000f).ToString("0.0") + "km" +
                    "  " + FormatTerrainShort();
            }
            else
            {
                line4 = (_modes.Mode == RadarMode.Trk ? "NO TGT / NO LOCK  " : "NO TGT  ") + FormatTerrainShort();
            }

            // Clip all strip text inside the MFD data box (no overflow onto PPI border).
            GUI.BeginGroup(rect);
            float lineH = Mathf.Max(16f, (rect.height - 8f) / 4.05f);
            float y = 3f;
            float w = rect.width - 8f;
            float x = 4f;
            ScopeDraw.StripLabel(new Rect(x, y, w, lineH), line1, ScopeDraw.StripStyle);
            ScopeDraw.StripLabel(new Rect(x, y + lineH, w, lineH), line2, ScopeDraw.TinyStyle);
            ScopeDraw.StripLabel(new Rect(x, y + lineH * 2f, w, lineH), line3, ScopeDraw.TinyStyle);
            ScopeDraw.StripLabel(new Rect(x, y + lineH * 3f, w, lineH), line4, ScopeDraw.TinyStyle);
            GUI.EndGroup();
        }

        /// <summary>PW-style dense STT lock panel (vanilla lock only).</summary>
        private void DrawLockDataStrip(Rect rect)
        {
            ScopeDraw.Fill(rect, new Color(0.08f, 0.04f, 0.01f, 0.85f));
            ScopeDraw.Border(rect, ScopeDraw.AmberLock, 1.75f);

            RadarContact? tgt = FindPrimary();
            string badge = _modes.LockBadge;
            string name = (tgt != null && !IsOwnshipContact(tgt)) ? Abbreviate(tgt.Label, 12) : "—";
            string id = tgt != null ? tgt.Id.ToString() : "—";
            string rng = tgt != null ? (tgt.RangeMeters / 1000f).ToString("0.00") + "km" : "—";
            string az = tgt != null ? tgt.AzimuthDeg.ToString("+0.0;-0.0") + "°" : "—";
            string el = tgt != null ? tgt.ElevationDeg.ToString("+0.0;-0.0") + "°" : "—";
            string asp = tgt != null ? tgt.AspectDeg.ToString("0") + "°" : "—";
            string cls = tgt != null ? tgt.ClosingSpeedMps.ToString("+0;-0") + "m/s" : "—";
            string alt = tgt != null ? tgt.AltitudeMeters.ToString("0") + "m" : "—";
            string tlock = _modes.LockElapsedSec.ToString("0.0") + "s";

            Rect badgeRect = ScopeDraw.Snap(new Rect(rect.x + 3f, rect.y + 3f, 72f, rect.height - 6f));
            ScopeDraw.Fill(badgeRect, new Color(1f, 0.55f, 0.1f, 0.9f));
            ScopeDraw.StripLabel(badgeRect, badge, ScopeDraw.LockBadgeStyle);

            string line1 = name + "  #" + id + "  RNG " + rng + "  AZ " + az;
            string line2 = "EL " + el + "  ASP " + asp + "  CLS " + cls + "  ALT " + alt;
            string line3 =
                "T " + tlock +
                "  " + FormatTerrainShort() +
                "  WF " + _modes.WaveformLabel +
                "  " + _modes.ActiveProfile.RankTag +
                "  MD " + ModeState.Label(_modes.Mode);

            float textX = badgeRect.xMax + 6f;
            int lines = Config.ShowWeaponEta.Value ? 4 : 3;
            float lineH = Mathf.Max(18f, (rect.height - 10f) / (lines + 0.05f));
            float tw = rect.width - textX - 4f;
            ScopeDraw.StripLabel(new Rect(textX, rect.y + 4f, tw, lineH), line1, ScopeDraw.StripStyle);
            ScopeDraw.StripLabel(new Rect(textX, rect.y + 4f + lineH, tw, lineH), line2, ScopeDraw.TinyStyle);
            ScopeDraw.StripLabel(new Rect(textX, rect.y + 4f + lineH * 2f, tw, lineH), line3, ScopeDraw.TinyStyle);

            if (Config.ShowWeaponEta.Value)
            {
                HitSolution hit = _contacts.LastHitSolution;
                string wpn = hit != null && hit.Valid ? hit.FormatStripLine() : "WPN —  ETA —  —";
                Color prev = GUI.color;
                if (hit != null && hit.Valid)
                {
                    GUI.color = hit.Verdict switch
                    {
                        HitVerdict.Hit => new Color(0.35f, 1f, 0.5f, 1f),
                        HitVerdict.Marginal => new Color(0.95f, 0.85f, 0.25f, 1f),
                        HitVerdict.No => new Color(0.95f, 0.4f, 0.35f, 1f),
                        _ => new Color(0.7f, 0.8f, 0.75f, 1f)
                    };
                }

                ScopeDraw.StripLabel(
                    new Rect(textX, rect.y + 4f + lineH * 3f, tw, lineH),
                    wpn,
                    ScopeDraw.TinyStyle);
                GUI.color = prev;
            }
        }

        private string FormatTerrain()
        {
            float terEl = _contacts.TerrainElevDeg;
            string elPart = "TER EL " + terEl.ToString("+0.0;-0.0") + "°";
            if (float.IsNaN(_contacts.TerrainRangeMeters))
            {
                return "TER RNG —  " + elPart;
            }

            if (float.IsPositiveInfinity(_contacts.TerrainRangeMeters))
            {
                return "TER RNG ∞  " + elPart;
            }

            string src = _contacts.TerrainFromRaycast ? "" : "~";
            return "TER RNG " + src + (_contacts.TerrainRangeMeters / 1000f).ToString("0.0") + "km  " + elPart;
        }

        private RadarContact? FindPrimary()
        {
            IReadOnlyList<RadarContact> list = _contacts.Contacts;
            if (list.Count == 0)
            {
                return null;
            }

            // Prefer hard lock
            if (_modes.Locked && _modes.LockedContactId is int lid)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == lid)
                    {
                        return list[i];
                    }
                }
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Locked)
                {
                    return list[i];
                }
            }

            // ACM / designate candidate
            if (_modes.AcmCandidateId is int cid)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == cid)
                    {
                        return list[i];
                    }
                }
            }

            // Nearest in list (skip ownship / self ghosts)
            RadarContact? best = null;
            for (int i = 0; i < list.Count; i++)
            {
                if (IsOwnshipContact(list[i]))
                {
                    continue;
                }

                if (best == null || list[i].RangeMeters < best.RangeMeters)
                {
                    best = list[i];
                }
            }

            return best;
        }

        private void DrawElevTape(Rect rect)
        {
            ScopeDraw.PanelFill(rect);
            ScopeDraw.Border(rect, ScopeDraw.PanelBorder, 1.5f);
            ScopeDraw.CornerBrackets(rect, ScopeDraw.PanelBorder, 8f, 1.5f);

            float midY = rect.y + rect.height * 0.5f;
            ScopeDraw.GlowLine(new Vector2(rect.x + 2f, midY), new Vector2(rect.xMax - 2f, midY), ScopeDraw.PhosphorDim, 1.5f);

            float u = Mathf.Clamp(_modes.AntennaElevationDeg / 60f, -1f, 1f);
            float y = midY - u * (rect.height * 0.45f);
            ScopeDraw.Fill(new Rect(rect.x + 3f, y - 2f, rect.width - 6f, 4f), ScopeDraw.Phosphor);

            if (_modes.LockedElevationDeg is float tgt)
            {
                float tu = Mathf.Clamp(tgt / 60f, -1f, 1f);
                float ty = midY - tu * (rect.height * 0.45f);
                ScopeDraw.Dot(new Vector2(rect.x + rect.width * 0.5f, ty), 3f, ScopeDraw.AmberLock);
            }

            ScopeDraw.ClippedLabel(new Rect(rect.x, rect.y + 2f, rect.width, 14f), "+60", ScopeDraw.TinyStyle);
            ScopeDraw.ClippedLabel(new Rect(rect.x, rect.yMax - 16f, rect.width, 14f), "-60", ScopeDraw.TinyStyle);
        }

        
        /// <summary>
        /// ACM A/G B-scope / scroll: L↔R scan bar; phosphor trails fade after sweep (not static 360 PPI).
        /// Contacts are plotted by <b>true look-angle</b> (az relative to scan-center / aircraft nose
        /// within FOV × slant range) — never north-up plan-view world XZ.
        /// </summary>
        private void DrawAcmAgPage(Rect area)
        {
            ScopeDraw.PanelFill(area);
            ScopeDraw.Border(area, ScopeDraw.PhosphorDim, 1f);

            Vector2 center = ScopeDraw.Snap(new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.42f));
            float radius = Mathf.Min(area.width, area.height) * 0.36f;
            ScopeDraw.DashedCircle(center, radius, ScopeDraw.Phosphor, 1.25f, 56);
            ScopeDraw.Dot(center, 4f, ScopeDraw.Phosphor);

            float halfFov = Mathf.Max(5f, _contacts.EffectiveHalfFovDeg());
            float persist = Config.AcmPersistenceSec != null ? Config.AcmPersistenceSec.Value : 0.8f;
            persist = Mathf.Clamp(persist, 0.2f, 2.5f);
            float now = Time.unscaledTime;
            // ScanAzimuthDeg is offset within the FOV wedge (same space as contact relAz below).
            float barXn = Mathf.Clamp(_modes.ScanAzimuthDeg / halfFov, -1f, 1f);
            float scanCenter = _modes.EffectiveScanCenterAzimuthDeg();

            // Phosphor clutter trails (spawn at bar, fade after pass).
            RadarClutter.DrawAcm(center, radius, _modes, halfFov, _modes.ScanAzimuthDeg, persist);

            // Bright scan bar on top of fading trails
            ScopeDraw.VerticalScanBar(center, radius, _modes.ScanAzimuthDeg, halfFov, ScopeDraw.Phosphor, 2f);

            float rangeKm = Mathf.Max(0.1f, _modes.DisplayRangeKm());
            float rangeM = rangeKm * 1000f;
            IReadOnlyList<RadarContact> contacts = _contacts.Contacts;
            int? designate = _modes.AcmCandidateId;
            const float stampGate = 0.12f; // normalized az width around bar to stamp contacts

            for (int i = 0; i < contacts.Count; i++)
            {
                RadarContact c = contacts[i];

                // Hard skip ownship / ejecting pilot·crew — never paint or highlight on ACM
                if (IsOwnshipContact(c) || _contacts.IsOwnshipOrCrewContact(c))
                {
                    _acmContactStamp.Remove(c.Id);
                    continue;
                }

                bool surface = c.Kind == ContactKind.Ground || c.Kind == ContactKind.Naval || c.Kind == ContactKind.Unknown;
                if (!surface)
                {
                    continue;
                }

                // Same-height / ground-band gate (A/G mapping)
                if (!_contacts.PassesAcmAltitudeGate(c))
                {
                    continue;
                }

                // True look-angle B-scope: X = az within FOV relative to scan center (matches bar);
                // Y = slant range. NOT plan-view world XZ / north-up PPI polar.
                float relAz = ContactProvider.Normalize180(c.AzimuthDeg - scanCenter);
                float u = Mathf.Clamp01(c.RangeMeters / rangeM);
                float xn = Mathf.Clamp(relAz / halfFov, -1f, 1f);
                Vector2 pos = center + new Vector2(xn * radius * 0.92f, -u * radius * 0.92f);
                if ((pos - center).sqrMagnitude > radius * radius)
                {
                    continue;
                }

                bool isLock = c.Locked || (_modes.Locked && _modes.IsLockedContact(c.Id));
                bool isDes = designate == c.Id && designate != null;

                // Never designate highlight if candidate somehow is self
                if (isDes && (IsOwnshipContact(c) || _contacts.IsOwnshipOrCrewContact(c)))
                {
                    isDes = false;
                }

                // Stamp when instantaneous scan beam (look angle) passes this contact az
                if (isLock || isDes || Mathf.Abs(xn - barXn) <= stampGate)
                {
                    _acmContactStamp[c.Id] = now;
                }

                if (!_acmContactStamp.TryGetValue(c.Id, out float stamped))
                {
                    continue; // never swept — empty/dark
                }

                float age = now - stamped;
                if (age > persist && !isLock && !isDes)
                {
                    _acmContactStamp.Remove(c.Id);
                    continue;
                }

                float life = isLock || isDes ? 1f : Mathf.Clamp01(1f - age / persist);
                if (life < 0.04f)
                {
                    continue;
                }

                Color baseCol = ColorFor(c);
                Color col = new Color(baseCol.r, baseCol.g, baseCol.b, baseCol.a * life);
                if (isLock)
                {
                    ScopeDraw.LockPipBrackets(pos, 7f, ScopeDraw.Phosphor * new Color(1f, 1f, 1f, life), 1.5f);
                }
                else if (isDes)
                {
                    ScopeDraw.Dot(pos, 5f, ScopeDraw.Phosphor);
                    ScopeDraw.HollowBox(pos, 6f, ScopeDraw.Phosphor, 1f);
                }
                else
                {
                    ScopeDraw.Dot(pos, 3f + life, col);
                }

                if ((isLock || isDes) && !IsOwnshipContact(c))
                {
                    ScopeDraw.StripLabel(new Rect(pos.x + 8f, pos.y - 8f, 90f, 16f), Abbreviate(c.Label, 10), ScopeDraw.TinyStyle);
                }
            }

            // Cull stale stamps not seen this frame
            if (_acmContactStamp.Count > 0)
            {
                var dead = new List<int>();
                foreach (var kv in _acmContactStamp)
                {
                    if (now - kv.Value > persist * 1.25f)
                    {
                        dead.Add(kv.Key);
                    }
                }

                for (int i = 0; i < dead.Count; i++)
                {
                    _acmContactStamp.Remove(dead[i]);
                }
            }

            // Reserve CRT bottom-bar band so TER/HAT never overlap the left "ACM" chip.
            const float crtBarH = 20f;
            float barTop = area.yMax - crtBarH;
            string statusEn = _modes.Locked ? "LOCKED" : "SCANNING";
            float statusY = Mathf.Min(center.y + radius + 6f, barTop - 52f);
            ScopeDraw.StripLabel(new Rect(area.x, statusY, area.width, 18f), statusEn, ScopeDraw.HeaderStyle);

            ScopeDraw.StripLabel(new Rect(area.x + 6f, barTop - 34f, 200f, 14f), FormatTerrainShort(), ScopeDraw.TinyStyle);
            ScopeDraw.StripLabel(
                new Rect(area.x + 6f, barTop - 18f, 180f, 14f),
                "HAT " + Mathf.Max(0f, _contacts.OwnshipAltitudeMeters).ToString("0") + "m",
                ScopeDraw.TinyStyle);

            DrawCrtBottomBar(area, "ACM", _modes.WaveformLabel, rangeKm, "ACM");
        }

        /// <summary>
        /// Single-target STT cage (fig2) — ONLY for one lock (TRK / ACM A/G STT).
        /// Multi TWS / SRC overview must NOT use this page.
        /// </summary>
        private void DrawSingleSttPage(Rect area)
        {
            ScopeDraw.PanelFill(area);
            ScopeDraw.Border(area, ScopeDraw.PhosphorDim, 1f);

            RadarContact? tgt = null;
            if (_modes.Locked && _modes.LockedContactId is int lid)
            {
                IReadOnlyList<RadarContact> list = _contacts.Contacts;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Id == lid)
                    {
                        tgt = list[i];
                        break;
                    }
                }
            }

            if (tgt != null && _modes.Locked)
            {
                string rng = "RNG " + (tgt.RangeMeters < 1000f
                    ? tgt.RangeMeters.ToString("0") + "m"
                    : (tgt.RangeMeters / 1000f).ToString("0.00") + "km");
                string vc = "Vc  " + tgt.ClosingSpeedMps.ToString("+0;-0") + "m/s";
                string asp = "ASP " + tgt.AspectDeg.ToString("0") + "°";
                ScopeDraw.ClippedLabel(new Rect(area.x + 8f, area.y + 8f, 150f, 16f), rng, ScopeDraw.TinyStyle);
                ScopeDraw.ClippedLabel(new Rect(area.x + 8f, area.y + 24f, 150f, 16f), vc, ScopeDraw.TinyStyle);
                ScopeDraw.ClippedLabel(new Rect(area.x + 8f, area.y + 40f, 150f, 16f), asp, ScopeDraw.TinyStyle);
            }
            else
            {
                ScopeDraw.ClippedLabel(new Rect(area.x + 8f, area.y + 10f, 160f, 22f), "NO TGT", ScopeDraw.HeaderStyle);
                ScopeDraw.ClippedLabel(new Rect(area.x + 8f, area.y + 32f, 120f, 14f), "NO LOCK", ScopeDraw.TinyStyle);
            }

            Vector2 center = ScopeDraw.Snap(new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.5f));
            float halfH = area.height * 0.32f;
            ScopeDraw.ParallelVerticalStack(center, halfH, 5, 10f, ScopeDraw.Phosphor, 1f);

            if (tgt != null && _modes.Locked)
            {
                float halfFov = Mathf.Max(5f, _contacts.EffectiveHalfFovDeg());
                float x = Mathf.Clamp(tgt.AzimuthDeg / halfFov, -1f, 1f) * 36f;
                float y = Mathf.Clamp(-tgt.ElevationDeg / 30f, -1f, 1f) * halfH * 0.85f;
                Vector2 pip = ScopeDraw.Snap(center + new Vector2(x, y));
                ScopeDraw.LockPipBrackets(pip, 10f, ScopeDraw.Phosphor, 1.75f);
                ScopeDraw.HollowBox(pip, 14f, ScopeDraw.Phosphor, 1f);
                ScopeDraw.ClippedLabel(new Rect(pip.x + 16f, pip.y - 8f, 100f, 16f), Abbreviate(tgt.Label, 12), ScopeDraw.TinyStyle);
                ScopeDraw.ClippedLabel(new Rect(area.x, area.yMax - 48f, area.width, 20f), "LOCKED", ScopeDraw.HeaderStyle);
            }

            string modeTag = _modes.Mode == RadarMode.Acm ? "ACM" : "TRK";
            DrawCrtBottomBar(area, modeTag, _modes.WaveformLabel, _modes.DisplayRangeKm(), modeTag);
        }

        private void DrawCrtBottomBar(Rect area, string left, string waveform, float rangeKm, string right)
        {
            float y = area.yMax - 18f;
            ScopeDraw.Fill(new Rect(area.x + 4f, y - 2f, area.width - 8f, 1f), ScopeDraw.PhosphorDim);
            float w = area.width;
            ScopeDraw.ClippedLabel(new Rect(area.x + 8f, y, 60f, 16f), left, ScopeDraw.TinyStyle);
            ScopeDraw.ClippedLabel(new Rect(area.x + w * 0.28f, y, 70f, 16f), waveform, ScopeDraw.TinyStyle);
            ScopeDraw.ClippedLabel(new Rect(area.x + w * 0.55f, y, 70f, 16f), rangeKm.ToString("0") + "km", ScopeDraw.TinyStyle);
            ScopeDraw.ClippedLabel(new Rect(area.xMax - 68f, y, 60f, 16f), right, ScopeDraw.TinyStyle);
        }

        private string FormatTerrainShort()
        {
            if (float.IsNaN(_contacts.TerrainRangeMeters))
            {
                return "TER ---";
            }

            if (float.IsPositiveInfinity(_contacts.TerrainRangeMeters))
            {
                return "TER inf";
            }

            float km = _contacts.TerrainRangeMeters / 1000f;
            return "TER " + (km < 1f ? _contacts.TerrainRangeMeters.ToString("0") + "m" : km.ToString("0.00") + "km");
        }

private void DrawPpi(Rect area)
        {
            ScopeDraw.PanelFill(area);
            ScopeDraw.Border(area, ScopeDraw.PanelBorder, 1.75f);
            // CRT: no corner-bracket chrome on PPI

            Vector2 center = ScopeDraw.Snap(new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.5f));
            float radius = Mathf.Min(area.width, area.height) * 0.44f;
            Color ring = new Color(0.15f, 0.62f, 0.35f, 0.8f);
            float rangeKm = Mathf.Max(0.1f, _modes.DisplayRangeKm());

            for (int i = 1; i <= 4; i++)
            {
                float rr = radius * (i / 4f);
                ScopeDraw.Circle(center, rr, ring, 1.25f);
                // Range ring labels ¼ ½ ¾ max
                float ringKm = rangeKm * (i / 4f);
                string lab = ringKm >= 10f ? ringKm.ToString("0") : ringKm.ToString("0.#");
                Vector2 labPos = Polar(center, rr - 2f, 45f);
                ScopeDraw.ClippedLabel(
                    new Rect(labPos.x + 2f, labPos.y - 7f, 40f, 14f),
                    lab,
                    ScopeDraw.TinyStyle);
            }

            // Crosshairs
            ScopeDraw.Line(center + new Vector2(0f, -radius), center + new Vector2(0f, radius),
                new Color(0.12f, 0.45f, 0.25f, 0.55f), 1.5f);
            ScopeDraw.Line(center + new Vector2(-radius, 0f), center + new Vector2(radius, 0f),
                new Color(0.12f, 0.45f, 0.25f, 0.55f), 1.5f);

            // Outer tick marks N/E/S/W
            DrawCardinalTicks(center, radius);

            float heading = _contacts.OwnshipHeadingDeg;
            bool northUp = Config.NorthUp.Value;
            float ownRotate = 0f;
            DrawHeadingBug(center, radius, northUp ? heading : 0f);

            if (_modes.Mode != RadarMode.Standby && _contacts.InMission)
            {
                float half = _contacts.EffectiveHalfFovDeg();
                float scanCenter = _modes.EffectiveScanCenterAzimuthDeg();
                float coneCenter = northUp ? heading + scanCenter : scanCenter;
                // Never draw air-style 360° wedge for ACM (ACM uses dedicated L↔R scan page).
                if (_modes.Mode != RadarMode.Acm)
                {
                Color wedge = _modes.Mode == RadarMode.Tws
                        ? new Color(0.25f, 1f, 0.55f, 0.4f)
                        : new Color(0.22f, 1f, 0.42f, 0.32f);
                ScopeDraw.Wedge(center, radius, coneCenter - half, coneCenter + half, wedge);
                }
                if (_modes.Mode == RadarMode.Tws)
                {
                    ScopeDraw.ClippedLabel(
                        new Rect(area.x + 6f, area.y + 22f, 90f, 16f),
                        _modes.Locked ? "TWS LOCK" : "TWS AIR",
                        ScopeDraw.TinyStyle);
                }
                float sweep = coneCenter + _modes.ScanAzimuthDeg;
                PushSweepTrail(sweep);
                // Soft trail: 2–3 faded previous angles
                for (int t = 0; t < _sweepTrailCount; t++)
                {
                    float a = _sweepTrail[t];
                    float alpha = 0.12f + 0.1f * t;
                    Vector2 trailEnd = Polar(center, radius, a);
                    ScopeDraw.Line(center, trailEnd, new Color(0.25f, 0.85f, 0.45f, alpha), 1.5f);
                }

                Vector2 sweepEnd = Polar(center, radius, sweep);
                ScopeDraw.Line(center, sweepEnd, new Color(0.4f, 1f, 0.55f, 0.75f), 1.5f);

                if (Mathf.Abs(scanCenter) > 0.1f)
                {
                    Vector2 scTick = Polar(center, radius + 1f, coneCenter);
                    ScopeDraw.Dot(scTick, 3f, new Color(0.95f, 0.9f, 0.3f, 1f));
                }
            }

            
            ScopeDraw.Chevron(center, 8f, new Color(0.85f, 1f, 0.9f, 1f));

            // TRK lines: one line per vanilla WeaponManager.targetList entry.
            // Primary/display (last / LockedContactId) = thicker amber; others thinner.
            if (_modes.Locked && _contacts.ShouldDrawHud)
            {
                IReadOnlyList<RadarContact> snap = _contacts.Contacts;
                IReadOnlyList<int> lockIds = _modes.LockedContactIds;
                int displayId = _modes.LockedContactId ?? (lockIds.Count > 0 ? lockIds[lockIds.Count - 1] : -1);
                float rangeMLock = Mathf.Max(1f, rangeKm * 1000f);

                // Prefer explicit multi-id list; fall back to single LockedContactId.
                int n = lockIds.Count > 0 ? lockIds.Count : (displayId >= 0 ? 1 : 0);
                for (int li = 0; li < n; li++)
                {
                    int lockId = lockIds.Count > 0 ? lockIds[li] : displayId;
                    RadarContact? lockTgt = null;
                    for (int ci = 0; ci < snap.Count; ci++)
                    {
                        if (snap[ci].Id == lockId)
                        {
                            lockTgt = snap[ci];
                            break;
                        }
                    }

                    if (lockTgt == null)
                    {
                        continue;
                    }

                    float plotBearing = northUp ? lockTgt.AbsoluteBearingDeg : lockTgt.AzimuthDeg;
                    float u = Mathf.Clamp01(lockTgt.RangeMeters / rangeMLock);
                    float blipR = radius * u;
                    float extendR = Mathf.Min(radius, blipR + 10f);
                    Vector2 tip = Polar(center, extendR, plotBearing);
                    bool primary = lockId == displayId;
                    Color lineCol = primary
                        ? ScopeDraw.AmberLock
                        : new Color(ScopeDraw.AmberLock.r, ScopeDraw.AmberLock.g, ScopeDraw.AmberLock.b, 0.65f);
                    ScopeDraw.Line(center, tip, lineCol, primary ? 2.25f : 1.25f);
                }
            }

            // Scanlines over PPI only (after grid, before / with contacts — under labels)
            ScopeDraw.Scanlines(area, 0.035f, 4f);

            // Realistic clutter (draw-only) under contacts
            if (_modes.Mode != RadarMode.Standby && _contacts.InMission)
            {
                float halfClutter = _contacts.EffectiveHalfFovDeg();
                float scanCenterClutter = _modes.EffectiveScanCenterAzimuthDeg();
                float coneCenterClutter = northUp ? heading + scanCenterClutter : scanCenterClutter;
                float sweepClutter = coneCenterClutter + _modes.ScanAzimuthDeg;
                RadarClutter.DrawPpi(center, radius, _modes, halfClutter, coneCenterClutter, sweepClutter);
            }

            if (!_contacts.InMission)
            {
                ScopeDraw.ClippedLabel(area, "STANDBY  ·  no aircraft\nmenus fail soft", ScopeDraw.MutedStyle);
                return;
            }

            bool standby = _modes.Mode == RadarMode.Standby;
            if (standby && _contacts.DatalinkCount == 0)
            {
                ScopeDraw.ClippedLabel(area, "RADAR STANDBY", ScopeDraw.MutedStyle);
                return;
            }

            float rangeM = Mathf.Max(1f, rangeKm * 1000f);
            IReadOnlyList<RadarContact> contacts = _contacts.Contacts;

            // Precompute which contacts get text labels (clutter reduction).
            HashSet<int> labelIds = BuildLabelSet(contacts);

            // Clip contacts to PPI — local coords inside BeginGroup
            GUI.BeginGroup(area);
            Vector2 localCenter = new Vector2(center.x - area.x, center.y - area.y);

            bool perf = Config.PerfMode.Value;
            int drawnLabels = 0;
            int labelCap = labelIds.Count;
            for (int i = 0; i < contacts.Count; i++)
            {
                RadarContact contact = contacts[i];
                bool self = IsOwnshipContact(contact);
                float plotBearing = northUp ? contact.AbsoluteBearingDeg : contact.AzimuthDeg;
                float u = Mathf.Clamp01(contact.RangeMeters / rangeM);
                // Cull tiny / near-center self ghosts early (symbol-only ownship caret already drawn).
                if (self)
                {
                    continue;
                }

                Vector2 pos = Polar(localCenter, radius * u, plotBearing + ownRotate);
                float distFromCenter = (pos - localCenter).magnitude;
                if (distFromCenter > radius + 2f)
                {
                    continue;
                }

                // Perf / density: skip most unlocked DL ground speckles; keep air/missile/foe/HV.
                if (!contact.Locked && contact.FromDatalink &&
                    contact.Kind != ContactKind.Air && contact.Kind != ContactKind.Missile &&
                    contact.Iff != IffRelation.Foe)
                {
                    if (perf || u > 0.55f)
                    {
                        continue;
                    }
                }

                // PD aspect soft-hide: skip unlocked dimmed blips; locked stay but dim.
                if (contact.PdAspectDimmed && !contact.Locked)
                {
                    continue;
                }

                Color color = ColorFor(contact);
                if (contact.PdAspectDimmed)
                {
                    color = ScopeDraw.PhosphorDim;
                }

                if (contact.FromDatalink)
                {
                    DrawDatalinkSymbol(pos, contact, color);
                }
                else if (contact.Kind == ContactKind.Missile)
                {
                    ScopeDraw.Diamond(pos, 5f, color);
                }
                else if (contact.Locked)
                {
                    ScopeDraw.Diamond(pos, 7f, contact.PdAspectDimmed ? ScopeDraw.PhosphorDim : ScopeDraw.AmberLock);
                    ScopeDraw.Dot(pos, 4f, color);
                }
                else
                {
                    ScopeDraw.Dot(pos, contact.Kind == ContactKind.Air ? 5f : 4f, color);
                }

                if (labelIds.Contains(contact.Id) && !contact.PdAspectDimmed && drawnLabels < labelCap)
                {
                    // Never label near ownship caret (range≈0 plot).
                    if (distFromCenter < 14f)
                    {
                        continue;
                    }

                    string tag = FormatContactTag(contact);
                    ScopeDraw.ClippedLabel(
                        new Rect(pos.x + 6f, pos.y - 8f, 68f, 14f),
                        tag,
                        ScopeDraw.TinyStyle);
                    drawnLabels++;
                }
            }

            GUI.EndGroup();

            if (standby)
            {
                ScopeDraw.ClippedLabel(
                    new Rect(area.x + 6f, area.y + 4f, area.width - 12f, 18f),
                    "STBY · DL FEED  " + _contacts.Status,
                    ScopeDraw.TinyStyle);
            }
            else
            {
                ScopeDraw.ClippedLabel(
                    new Rect(area.x + 6f, area.y + 4f, area.width - 12f, 18f),
                    (_contacts.Radar != null ? "RADAR" : "NO HW") + "  " + _contacts.Status,
                    ScopeDraw.TinyStyle);
            }

            if (_modes.Mode == RadarMode.Trk && !_modes.Locked)
            {
                ScopeDraw.ClippedLabel(
                    new Rect(area.x, area.y + area.height * 0.42f, area.width, 24f),
                    "NO TGT / NO LOCK",
                    ScopeDraw.HeaderStyle);
            }
        }

        private void PushSweepTrail(float angle)
        {
            if (float.IsNaN(_lastSweepAngle) || Mathf.Abs(Mathf.DeltaAngle(_lastSweepAngle, angle)) > 0.15f)
            {
                // shift left
                for (int i = 0; i < _sweepTrail.Length - 1; i++)
                {
                    _sweepTrail[i] = _sweepTrail[i + 1];
                }

                _sweepTrail[_sweepTrail.Length - 1] = angle;
                if (_sweepTrailCount < _sweepTrail.Length)
                {
                    _sweepTrailCount++;
                }

                _lastSweepAngle = angle;
            }
            else
            {
                _lastSweepAngle = angle;
                if (_sweepTrailCount > 0)
                {
                    _sweepTrail[_sweepTrail.Length - 1] = angle;
                }
            }
        }

        private HashSet<int> BuildLabelSet(IReadOnlyList<RadarContact> contacts)
        {
            var set = new HashSet<int>();
            bool showAll = Config.ShowAllContactLabels.Value;
            int maxLabels = Mathf.Max(0, Config.MaxContactLabels.Value);
            if (Config.PerfMode.Value)
            {
                maxLabels = Mathf.Min(maxLabels, 12);
            }

            if (showAll)
            {
                for (int i = 0; i < contacts.Count && set.Count < maxLabels; i++)
                {
                    if (IsOwnshipContact(contacts[i]))
                    {
                        continue;
                    }

                    set.Add(contacts[i].Id);
                }

                return set;
            }

            // Always: locked, air/missile; DL ground only if locked — never ownship.
            var candidates = new List<RadarContact>(contacts.Count);
            for (int i = 0; i < contacts.Count; i++)
            {
                RadarContact c = contacts[i];
                if (IsOwnshipContact(c))
                {
                    continue;
                }

                if (c.Locked)
                {
                    set.Add(c.Id);
                    continue;
                }

                if (c.FromDatalink)
                {
                    // DL: very few labels — missiles always; air foes only; no ground DL text
                    if (c.Kind == ContactKind.Missile ||
                        (c.Kind == ContactKind.Air && c.Iff == IffRelation.Foe))
                    {
                        candidates.Add(c);
                    }

                    continue;
                }

                // Own-radar tracks: consider for top-N (skip ultra-near ghosts)
                if (c.RangeMeters < 40f)
                {
                    continue;
                }

                candidates.Add(c);
            }

            // Sort candidates by range ascending; under PerfMode prefer air/missile then closest.
            candidates.Sort((a, b) =>
            {
                int ka = PriorityKind(a);
                int kb = PriorityKind(b);
                int cmp = ka.CompareTo(kb);
                return cmp != 0 ? cmp : a.RangeMeters.CompareTo(b.RangeMeters);
            });
            for (int i = 0; i < candidates.Count && set.Count < maxLabels; i++)
            {
                set.Add(candidates[i].Id);
            }

            return set;
        }

        private static int PriorityKind(RadarContact c)
        {
            if (c.Kind == ContactKind.Missile) return 0;
            if (c.Kind == ContactKind.Air) return 1;
            return 2;
        }

        /// <summary>True for local player / own aircraft — draw symbol never name/range near caret.</summary>
        private bool IsOwnshipContact(RadarContact c)
        {
            if (c == null)
            {
                return false;
            }

            object? player = _contacts.Player;
            if (player != null)
            {
                if (ReferenceEquals(c.Raw, player))
                {
                    return true;
                }

                int pid = GameReflect.IdOf(player);
                if (pid != 0 && c.Id == pid)
                {
                    return true;
                }

                // Same Unity hierarchy root as ownship
                try
                {
                    var pc = GameReflect.AsComponent(player);
                    var cc = GameReflect.AsComponent(c.Raw);
                    if (pc != null && cc != null && pc.transform.root == cc.transform.root)
                    {
                        return true;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            // Near-zero range friend/self ghost sitting on the caret
            if (c.RangeMeters < 40f && c.Iff == IffRelation.Friend)
            {
                return true;
            }

            // Ejecting pilot / parachute / crew near ownship
            if (c.RangeMeters < 250f)
            {
                string lab = (c.Label ?? string.Empty) + " " + (c.Raw != null ? c.Raw.GetType().Name : string.Empty);
                if (lab.IndexOf("pilot", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    lab.IndexOf("parachute", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    lab.IndexOf("eject", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                    lab.IndexOf("bail", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string FormatContactTag(RadarContact contact)
        {
            if (contact.FromDatalink)
            {
                string code = Abbreviate(contact.Label, 6);
                char kind = contact.Kind switch
                {
                    ContactKind.Air => 'A',
                    ContactKind.Ground => 'G',
                    ContactKind.Naval => 'N',
                    ContactKind.Missile => 'M',
                    _ => '?'
                };
                if (string.IsNullOrEmpty(code) || code == "?")
                {
                    return "DL" + kind;
                }

                return "DL" + code;
            }

            return Abbreviate(contact.Label, 8);
        }

        private static string Abbreviate(string? label, int max)
        {
            if (string.IsNullOrEmpty(label))
            {
                return "?";
            }

            string raw = label.Trim();
            // Strip common prefixes
            if (raw.StartsWith("DL ", System.StringComparison.OrdinalIgnoreCase))
            {
                raw = raw.Substring(3).Trim();
            }

            // ASCII-only display — drop CJK / non-ASCII so font fallback never garbles the MFD.
            var sb = new System.Text.StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char ch = raw[i];
                if (ch >= 32 && ch < 127)
                {
                    sb.Append(ch);
                }
            }

            string t = sb.ToString().Trim();
            if (t.Length == 0)
            {
                return "?";
            }

            if (t.Length <= max)
            {
                return t;
            }

            return t.Substring(0, max);
        }

        private static readonly Color DatalinkCyan = ScopeDraw.CyanAccent;

        private static void DrawDatalinkSymbol(Vector2 pos, RadarContact contact, Color iffColor)
        {
            Color cyan = DatalinkCyan;
            Color sym = Color.Lerp(cyan, iffColor, 0.35f);
            float half = contact.Kind == ContactKind.Air ? 6f : 5f;
            if (contact.PositionQuality == PositionQuality.Fresh)
            {
                ScopeDraw.HollowBox(pos, half, sym, 1.75f);
                ScopeDraw.Dot(pos, 2.5f, sym);
            }
            else if (contact.PositionQuality == PositionQuality.Coast)
            {
                ScopeDraw.DashedBox(pos, half, sym, 1.5f);
            }
            else
            {
                ScopeDraw.DashedBox(pos, half * 0.9f, new Color(sym.r, sym.g, sym.b, 0.55f), 1.5f);
            }

            if (contact.Locked)
            {
                ScopeDraw.Diamond(pos, half + 2f, ScopeDraw.AmberLock);
            }
        }

        private static void DrawCardinalTicks(Vector2 center, float radius)
        {
            Color tick = new Color(0.35f, 0.95f, 0.55f, 0.85f);
            string[] labs = { "N", "E", "S", "W" };
            float[] degs = { 0f, 90f, 180f, 270f };
            for (int i = 0; i < 4; i++)
            {
                Vector2 outer = Polar(center, radius + 5f, degs[i]);
                Vector2 inner = Polar(center, radius - 2f, degs[i]);
                ScopeDraw.GlowLine(inner, outer, tick, 2f);
                Vector2 lab = Polar(center, radius + 14f, degs[i]);
                ScopeDraw.ClippedLabel(new Rect(lab.x - 6f, lab.y - 7f, 14f, 14f), labs[i], ScopeDraw.TinyStyle);
            }
        }

        private static void DrawHeadingBug(Vector2 center, float radius, float deg)
        {
            Vector2 tip = Polar(center, radius + 3f, deg);
            ScopeDraw.Dot(tip, 4f, new Color(0.85f, 1f, 0.35f, 1f));
        }

        private static Vector2 Polar(Vector2 center, float radius, float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return center + new Vector2(Mathf.Sin(r), -Mathf.Cos(r)) * radius;
        }

        private static Color ColorFor(RadarContact contact) => contact.Iff switch
        {
            IffRelation.Friend => Config.FriendColor,
            IffRelation.Foe => Config.FoeColor,
            IffRelation.Neutral => Config.NeutralColor,
            IffRelation.Unknown => Config.UnknownColor,
            _ => throw new System.ArgumentOutOfRangeException(nameof(contact), contact.Iff, null)
        };

        private void DrawFooter(Rect rect)
        {
            // Footer sits on HudPanel; thin border only (no second translucent green wash).
            ScopeDraw.Border(rect, ScopeDraw.PanelBorderOuter, 1.5f);

            string left =
                "[ ]rng  ; ' scan  / ctr  N n-up  F7 mode  R tgt  F8 wf  " +
                (_contacts.InMission ? "hdg " + _contacts.OwnshipHeadingDeg.ToString("000") + "°" : "menu");
            ScopeDraw.ClippedLabel(new Rect(rect.x + 4f, rect.y + 2f, rect.width - 140f, rect.height - 4f), left, ScopeDraw.TinyStyle);
            // Oritasy branding (footer only) + plain version (no chip)
            ScopeDraw.ClippedLabel(new Rect(rect.xMax - 140f, rect.y, 72f, rect.height), OritasyUi.Branding, ScopeDraw.FooterBrandStyle);
            ScopeDraw.ClippedLabel(new Rect(rect.xMax - 64f, rect.y, 60f, rect.height), "v" + Plugin.DisplayVersion, ScopeDraw.TinyStyle);
        }
    }
}
