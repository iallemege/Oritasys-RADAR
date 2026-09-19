# Oritasy's RADAR

Standalone **BepInEx 5** (Unity Mono) plugin for **Nuclear Option**.

**Oritasy's RADAR** — **Realtime Aerial Detection And Ranging (R.A.D.A.R.)**

Independent PPI radar overlay inspired by PanzerWar-DE FlightRadar (RDR) *modes and layout*, not a port of PW IL2CPP.

| | |
| --- | --- |
| GUID | `com.iallemmege.RDA` |
| Assembly | `RDA.dll` (kept for BepInEx install compatibility) |
| Plugin DisplayName | **Oritasy's RADAR** |
| Version | **0.0.2T** |

Standalone plugin with no external Oritasy runtime dependency. Soft-loads optional `OritasyFonts` for branding (`Oritasy™` footer only).

Vanilla TacScreen can be suppressed for the **local player** via `DisableVanillaRadar` (default on). RDA draws its own IMGUI window titled **Oritasy's RADAR**.

## Features (v0.0.2T)

- **Boarding HUD fix**: MFD seat gate uses Nuclear Option `Aircraft.HasEjected()` / `ejected` (prefer method). Live local aircraft + `!HasEjected` → draw HUD. Removed loose ownship name bans (`eject`/`parachute`) and generic-`disabled`-alone hide. Spectating uncertain fail-soft SHOW. Logs seat-gate flips; does **not** force `ShowWindow=false` on board
- **DatalinkMaxMarkers**: BepInEx `Datalink.DatalinkMaxMarkers` (default **24**, range **4–128**) hard-caps ingested and drawn DL-only contacts after priority cull
- **RWR accuracy**: tighten to real illuminate/lock on player — `OnRadarWarning.isTarget` / `detected`, `FactionHQ.missileAttacks` on ownship, seeker `lockedTarget`; cut false positives from loose AI `target`; better relative bearing. Soft-fail

## Features (v0.0.1T)

- **Hide HUD off-aircraft**: MFD / gun funnel / world TRK / RWR hidden on eject, death, spectate, or menu; shown again when boarding. Re-enter clears stale lock/TRK/RWR
- **Datalink denseness**: `DatalinkRefreshHz` (default 4), `DatalinkMaxMarkers` (default 24), priority cull (threats/HV/foes first), fewer DL labels/symbols
- **Range filters**: `FriendlyDisplayRangeKm` **10**, `AirDisplayRangeKm` **20** — apply to PPI/ACM/TWS/datalink (+ designate). Locked/RWR/missiles exempt
- **RWR when locked (被锁定)**: unwrap `OnRadarWarning` (`emitter`/`isTarget`); poll `FactionHQ.missileAttacks`; seeker-on-ownship; Lock/Missile flash + threat color. Soft-fail reflection
- **TRK after unlock**: world TRK + PPI lock line clear **immediately** when `targetList` empty (no sticky hold)
- **Single lock line**: only the **last** `targetList` entry drives PPI pointing line + world TRK (多个锁定只显示最后一条线). Missile primary remains `[0]`
- DisplayName **Oritasy's RADAR**

## Features (v1.0.6)

- **ACM true look-angle**: B-scope plots contacts by body/scan-relative azimuth × range (matches L↔R beam); not north-up / plan-view XY
- **ACM same-height gate**: `AcmAltitudeBandM` (default **1200** m) — keep A/G contacts near ground band or within ±band of ownship altitude; high air tracks do not dominate
- **ACM never self**: hard-skip local player / ownship / near-ownship ejecting pilot·crew from ACM designate, highlight, R-cycle AddTargetList, and auto-candidate
- DisplayName remains **Oritasy's RADAR**

## Features (v1.0.5)

- **English-only UI**: removed Chinese/CJK draw strings (`NO TGT` / `NO LOCK` / `SCANNING` / `LOCKED`); ASCII-only contact name abbreviate
- **MFD clip**: data strip / footer / labels clipped inside fixed rects (`GUI.BeginGroup` + `TextClipping.Clip`) — no `TGT …` overflow onto PPI border
- **No ownship name**: never label local player / own aircraft on PPI/ACM/TRK caret (symbol only)
- **Perf**: `DrawScanlines` default **false**; `PerfMode` default **true** (lighter clutter, max ~12 contact labels); scanline/grain gated
- **Scan slew keys**: **`;`** / **`'`** (was `,` / `.`); `/` still centers
- **Smooth scan sweep**: ping-pong continuous angular velocity (no wrap teleport / flash); ACM bar uses linear az/FOV mapping

## Features (v1.0.4)

- **Missile radar re-lock**: when an ARH/ARM seeker clears `lockedTarget` mid-flight, soft-reflect `Missile.SetTarget` onto local `WeaponManager.targetList[0]` (or HQ-tracked prior target) if the missile is local-player owned
- **`MissileRadarRelock`** (default true) + **`BlockLockLostSuicide`** (default true): Harmony Prefix on `ARHSeeker.SlowChecks` (ARM if it has null-target SD) — suppresses **only** the null-lock self-destruct while ownship radar still has a usable lock. Impact / proximity / timer / sea / ground / miss-kinematic / speed detonation paths are **not** patched (no `Missile.Detonate` prefix)
- Optional ~2 Hz tick covers datalink mid-flight clears between SlowChecks
- DisplayName remains **Oritasy's RADAR**

## Features (v1.0.3)

- **Engine detection rewrite**: idle throttle≈0 no longer false ENGINE OFF; prefer `running`/`n1`/`rpm`/`Started`; PowerSupply `powered` alone ignored when ambiguous; null→ON fail-soft; ContactProvider requires ≥0.75s clear off before freeze
- **R = cycle vanilla targetList**: rotates `WeaponManager.targetList` (primary=[0]), calls `TargetListChanged`; ACM empty list may `AddTargetList` designate; SyncVanillaTargetLock after cycle so missiles guide
- **ACM phosphor scroll**: B-scope L↔R bar with fading trails (`AcmPersistenceSec` default 0.8s) — scanned area disappears; not static 360 PPI
- **Data strip**: StripMinH≥64 / StripLockH≥88, Overflow labels, 3–4 short lines, slightly wider default window

## Features (v1.0.2)

- **RadarRank / AESA**: per-profile `RadarRank` (1–5+). Source: builtin table, JSON `radarRank`, or live `AircraftDefinition.aircraftParameters.rankRequired` (airframe unlock rank — **not** player PlayerRank). Capacity heuristic as fallback (higher maxCharge → higher rank; default mid = 2). **Rank ≥ `AesaMinRank` (default 3) = AESA-like** — PD aspect gate waived
- **PD aspect gate** (non-AESA, SRC/TWS/TRK air tracks): head-on (`AspectDeg`≈0 / strong closing) kept; beam (~90°) hard unless &lt;`PdMinCloseRangeKm` flicker; tail (~180°) rarely unless very close. Vanilla-locked off-aspect soft-dimmed on PPI (`PdAspectDimmed`) rather than deleted. **PULSE**: no gate (volume search). Config: `PdHeadOnAspectDeg` (45°), `PdMinCloseRangeKm` (6), `AesaMinRank` (3)
- **Data strip**: shows `WF PD` + `RK3 AESA` (or `RK2`) tags when applicable

## Features (v1.0.1)

- **World TRK on vanilla lock**: every OnGUI frame reads `WeaponManager.GetTargetList()` / `targetList[0]` directly (HUD works even if MFD sync lags); hide when list empty; never ownship / TargetCam / cockpit clutter
- **Cockpit declutter**: reject units whose name/label contains `cockpit` / `Camera` / `TargetCam` / `TargetON` / `debug` / `EditorOnly`; footer uses `menu` instead of `out of cockpit`
- **Out-of-scan HV/threat only**: `ShowOutOfRangeHighValueOnly` (default true) — outside mode cone/range keep strategic / Building/Airbase/RadarStation/Factory/large **or** RWR/missile/attacking; drop DL spam (QuadVT/storage). STBY DL feed unchanged
- **Realistic radar clutter** (draw-only): PPI/ACM ground speckles, scan-wedge noise, radial grain; PULSE denser look-down, PD suppresses ground clutter; STBY none. `ShowRadarClutter` (default true), `ClutterIntensity` (default 0.45)

## Features (v1.0)

- **Vanilla lock only**: RDA mirrors `WeaponManager.GetTargetList()` / `targetList` (primary = `[0]`, same as CombatHUD). Key **6** no longer invents LOCK; use game target select / paint / clear
- **TRK = post-lock UI**: when vanilla targetList nonempty → soft-switch display to TRK PPI + amber lock line + LK LED; empty list → `ClearLock` and restore prior mode. Selecting TRK without a vanilla lock shows PPI + `没有目标` (no fake LK)
- **TWS ACQ highlight only**: dwell no longer calls `EngageLock` / invents WeaponManager targets. R cycles ACM designate **highlight** only
- **Data strip taller**: ≥48px unlocked / ≥64px locked (two readable lines; ETA adds height)
- **Su-27 gun funnel**: tapering diamond/chevron LCOS ladder + gravity-drop curve (not crude 4 rings)
- **ACM scan**: L↔R vertical bar **clipped inside** dashed circle; B-scope-ish contact plot; no 360° air wedge on PPI while ACM searching
- **World TRK label**: caches locked unit transform/pos; draws every OnGUI frame; holds ≤2s if contact list briefly empty

## Features (v0.9)

- **Header**: title `R.A.D.A.R` (Oritasy™ stays footer-only); plain version bottom-right (no chip)
- **Engine power gate**: soft-reflect engines/throttle/PowerSupply; PWR LED off + frozen PPI/ACM (hold last frame) when engines off; unknown ⇒ powered (fail-soft). Still requires InMission to draw
- **TRK = classic PPI** (strip + elev + RWR + PPI) — never fig2 STT cage; bright amber lock line from ownship to locked blip
- **ACM A/G envelope separate from air**: HalfFov ~50–60°, MaxRangeKm ~20–30 (not SRC/TWS 80/160 scales); range chips clamp to AcmMaxRangeKm
- **Data strip**: two lines unlocked (ownship HDG/ALT/SPD + WF/PROF/RNG/FOV…; designate or `没有目标 / NO TGT`); lock strip includes WF + full numerics; FindPrimary prefers Locked → AcmCandidate → nearest
- **RWR overhaul**: foe red diamond, friend blue circle, missile yellow triangle, unknown white square; only RadarWarning-on-self ingest (not PPI mirror); IFF via GameReflect.CompareIff

## Features (v0.8)

- **CRT green-on-black** restyle (`#00FF66`): strip heavy bezel/glow/bracket sci-fi chrome from 0.7
- **ACM A/G page** (fig1): dashed range circle, ownship pip, animated vertical scan bar, `扫描中`/`已锁定`, TER/HAT, bottom `ACM | PULSE|PD|CW | range | ACM`
- **Single-lock STT page** (fig2): ONLY for one locked target (TRK or ACM A/G STT) — top-left RNG/Vc/ASP or `没有目标`, vertical line stack, corner-bracket pip + name, `已锁定`. Multi TWS/SRC stays classic PPI
- **Manual lock (superseded in 1.0)**: 0.8 used key 6; 1.0 uses vanilla targetList only
- **Key R**: cycle ACM ground designate/lock among surface candidates (wrap)
- **Waveform** enum PULSE/PD/CW — cycle with **F8**; ACM defaults PULSE; bottom bar shows waveform
- **World TRK label**: red `TRK` above locked target head in 3D (screen overlay, not MFD); `ShowWorldTrkLabel`
- ACM keeps out-of-cone/out-of-range surface contacts (dim); lock still allowed

## Features (v0.7)

- **Sci-fi MFD / HUD restyle**: frameless custom panel (dark bezel `#020B10`, double border, corner brackets, accent top bar `ORITASY // RADAR` + version chip + PWR/LK LEDs)
- Crisp **monospace HUD font** (Consolas / Cascadia Mono / Lucida Console / Courier New); OritasyFonts TTF kept for CJK footer branding only
- **Clipping / padding fix**: taller header (~30px), mode bar 26px, range chips 22px, data strip ≥24px (lock strip ~52–56), footer ≥24px; `GUI.BeginGroup` + `TextClipping.Clip`; integer-snapped fills
- Custom mode/range **chips** (truncated bars, not stock IMGUI buttons); thicker scope strokes + optional glow underlay; PPI scanlines, N/E/S/W ticks, range-ring km labels, sweep trail
- **Contact label declutter**: default no text on every DL ground contact; always symbol; labels for locked / air-missile / top-N closest own-radar; `ShowAllContactLabels` (default false), `MaxContactLabels` (default 12), `DrawScanlines`/`PerfMode`; abbreviated `DL` + short code
- Default window size raised to **640×480** (saved larger sizes unchanged)

## Features (v0.6)

- **Vanilla faction datalink** (FactionHQ `trackingDatabase`): ingest HQ tracks as `Source=datalink` contacts; contribute own detections back via `RpcUpdateTrackingInfo` / `CmdUpdateTrackingInfo` (helps when `DisableVanillaRadar` suppresses shared radar)
- DL symbology: cyan hollow / dashed boxes + `DL` tag; strip shows `DL n`; optional STBY AWACS-style feed (`DatalinkInStandby`)
- Coasting: Fresh (&lt;4s) → Coast (velocity coast + noise) → Stale → drop at `DatalinkMaxAgeSec`

## Features (v0.5)

- Circular heading-up PPI (toggle north-up) with green phosphor / Oritasy-like panels
- Range rings + **RNG preset chips** (5 / 10 / 20 / 40 / 80 / 160 / 240 km)
- Radar cone wedge with soft per-mode FOV / range / elevation gates
- **Manual scan direction** (`ScanCenterAzimuthDeg`): slew SRC/TWS/ACM wedge off the nose (`;` `'`); `/` resets
- Contact blips with **elevation**, **closing speed**, and **aspect** estimates
- Modes: **STBY | SRC/RWS | TWS | ACM | ~55° | ~25 km | multi (A/G surface; envelope ≠ air SRC/TWS) |NO|MARG` + range-to-Rmax (`R`/`Rmax`, `ΔRmax`)
- **Gun funnel HUD** (screen overlay via `OnGUI` / `WorldToScreenPoint`): boresight pip, lead pip, 3–5 dashed converging rings + spokes; green HIT / yellow MARG / red NO. Config `ShowGunFunnel` (default true)
- Terrain scan readout: ground range along antenna boresight (Physics raycast when available, else flat-earth). Soft-fail if no Physics
- Antenna elevation tape with auto-slave (±60°) toward locked target (or 0° in ACM search)
- RWR panel fed from `TacScreen.OnRadarWarning` / `RadarWarning` (and similar) via Harmony postfixes
- **`DisableVanillaRadar`** (default **true**): Harmony Prefix on `TacScreen.ScanRadar` / `Radar.TargetSearch` skips vanilla scan for the **local player aircraft only** (AI unaffected). Fail-soft
- **Per-aircraft radar profiles**: builtin envelopes for known Nuclear Option airframes; unknown/mod aircraft derive from `PowerSupply.maxCharge` / `maxPower`, optionally blended with native `Radar.radarCone` + `RadarParameters.maxRange` (Hybrid)
- Optional override file `BepInEx/config/OritasyRadar.profiles.json` (soft-fail if missing)
- Performance: ContactProvider + weapon solve throttled (~15 Hz); GUI/funnel draw from cached `HitSolution`; reflection FieldInfo cached; OnGUI styles applied once

## Install

1. Install **BepInEx 5 x64** (Mono) into the Nuclear Option game folder.
2. Build this project (or copy a Release `RDA.dll`).
3. Place `RDA.dll` in `Nuclear Option/BepInEx/plugins/`.
4. Optional: put `NotoSansSC-VF.ttf` (or any `.ttf`) in `BepInEx/plugins/OritasyFonts/` for CJK / branding font.
5. Launch once. Config is written to `BepInEx/config/com.iallemmege.RDA.cfg`.

## Controls

| Input | Action |
| --- | --- |
| **F6** | Show / hide overlay |
| **F7** | Cycle radar mode |
| **F8** | Cycle waveform PULSE → PD → CW |
| **R** | ACM: cycle ground designate highlight |
| **N** | Heading-up / north-up |
| **[** / **]** | Range down / up |
| **;** / **'** | Slew scan center left / right |
| **/** | Reset scan center |
| **1** | STBY |
| **2** | SRC/RWS |
| **3** | TWS |
| **4** | ACM |
| **5** | TRK |
| **6** | Help cue only — use **vanilla** target lock |
| **7** / **8** | Antenna elev up / down (disables elev-auto) |
| **9** | Toggle elev-auto |
| **0** | Step range (+1) |
| RNG chips | Click 5/10/20/40/80/160… on the strip |
| Keypad **0–9** | Same as Alpha digits |
| Drag title bar | Move window |

Hotkeys are remappable in the config file (Configuration Manager works if you have it).

## Modes (UI / state emulation)

Soft clamps only — RDA does not rewrite vanilla radar IL (except optional local-player scan skip).
Mode FOV / range / track caps **scale relative to the active aircraft profile** (e.g. TWS half-FOV ≈ 0.65× SRC; STBY = 0). The table below is the generic mid-tier default.


| Mode | Half FOV | Max range | Tracks |
| --- | --- | --- | --- |
| STBY | — | — | none |
| SRC/RWS | 60° | 160 km | 32 |
| TWS | 40° | 80 km | 12 |
| TWS | 40° | 80 km | 12 (air auto-lock by angular error) |
| ACM | ~40° | ~40 km | multi (A/G surface mapping; soft designate) |
| TRK | 6° | 80 km | 1 lock |

### Mode semantics (v0.7)

| Mode | Role |
| --- | --- |
| **STBY** | Radar silent; local-player RCS × `StandbyRcsFactor` (default **0.8** = −20%) via Harmony on `GetRadarReturn` |
| **SRC/RWS** | Search — mixed contacts |
| **TWS** | **对空自动锁定** — air/missile only; dwell auto-lock on best angular error to scan center; multi-track kept; stays TWS |
| **ACM** | **对地 A/G** — ground/naval/unknown (+ DL factories etc.); wide FOV; look-down; soft designate; **no** air STT auto-lock |
| **TRK** | Post-lock display — soft-entered when **vanilla** `targetList` has an entry |

- Vanilla lock sync: `WeaponManager.GetTargetList()[0]` → `ApplyVanillaLock` → **TRK** display; empty list → `ClearVanillaLock`.
- TWS auto-lock: `EngageLock(id, switchToTrk: false)` — remains TWS until player confirms.
- Break lock: `AcmBreakSec` (default **0.8 s**) out of gate, or **6** / `ClearLock()`.

### Terrain scan caveat

`TER RNG` / `TER EL` estimate ground range along the antenna boresight. When Unity Physics is available, a raycast is used. Otherwise a flat-earth formula from ownship altitude and elevation angle is used (`~` prefix). Looking up / level may show `—` or `∞`. This is a display aid, not a precision ranging system.

## Config (radar)

| Key | Default | Meaning |
| --- | --- | --- |
| `TwsLockDwellSec` | 0.25 | Dwell before TWS air auto-lock |
| `StandbyRcsFactor` | 0.8 | Local-player GetRadarReturn scale in STBY (−20% RCS) |
| `AcmAltitudeBandM` | 1200 | ACM A/G same-height gate (m): keep contacts near ground (≤band MSL) **or** within ±band of ownship alt |
| `AcmLockDwellSec` | 0.2 | Legacy alias (ACM is A/G designate, not air auto-lock) |
| `AcmBreakSec` | 0.8 | Out-of-gate time before unlock |
| `ElevStepDeg` | 5 | Manual elev step |
| `ElevAuto` | true | Slave antenna elev |
| `ScanSlewStepDeg` | 5 | Manual scan-center slew step |
| `ContactUpdateHz` | 15 | ContactProvider throttle (GUI uses snapshot) |
| `DisableVanillaRadar` | **true** | Skip vanilla ScanRadar/TargetSearch for local player only |
| `UseCapacityFallback` | **true** | Derive profile from PowerSupply capacity when no builtin/override matches |
| `PreferNativeRadarStats` | **true** | Blend capacity formula 50/50 with native radarCone/maxRange (Hybrid) |
| `ShowWeaponEta` | **true** | Lock-strip weapon ETA / HIT|MARG|NO line |
| `ShowGunFunnel` | **true** | Screen gun funnel overlay when current weapon is a gun |
| `ShowAllContactLabels` | **false** | Label every PPI contact (otherwise locked / air-missile / top-N) |
| `MaxContactLabels` | 12 | Cap on PPI text labels when not showing all (`PerfMode` caps ≤12) |
| `DrawScanlines` | **false** | CRT scanline fill over PPI (expensive; off by default) |
| `PerfMode` | **true** | Lighter clutter / fewer labels / skip radial grain |
| `FriendlyDisplayRangeKm` | **10** | Friendly contacts only within this range (km) |
| `AirDisplayRangeKm` | **20** | Air contacts only within this range (km) |
| `WindowWidth` / `WindowHeight` | 680 / 520 | Default overlay size |

## Config (datalink)

| Key | Default | Meaning |
| --- | --- | --- |
| `UseVanillaDatalink` | **true** | Ingest `FactionHQ.trackingDatabase` into the PPI |
| `ContributeToDatalink` | **true** | Push RDA foe detections/locks to local HQ (~1.5 Hz/id) |
| `DatalinkFreshSec` | 4 | Fresh window (vanilla `LAST_SPOTTED_EXTRA_TIME`) |
| `DatalinkCoastSec` | 30 | Coast until this age |
| `DatalinkMaxAgeSec` | 60 | Drop older tracks |
| `DatalinkPositionNoiseMeters` | 50 | Coast/stale position noise (grows with age) |
| `ShowDatalinkOnlyOutsideCone` | false | If true, keep DL only when outside own-radar cone |
| `DatalinkInStandby` | **true** | Show DL tracks while radar is STBY |
| `DatalinkRefreshHz` | **4** | Throttle HQ trackingDatabase ingest |
| `DatalinkMaxMarkers` | **24** (4–128) | Hard cap ingested **and** drawn DL-only markers after priority cull |
| `DatalinkPreferHighValue` | **true** | Prefer threats / HV / foes when culling dense DL |

Own-radar / harmony contacts win over DL for the same Id. Friendly same-HQ units and self are skipped on ingest.

## How contacts are built

`ContactProvider` resolves ownship, then radar, then DTOs (throttled):

1. `GameManager.GetLocalAircraft`
2. `SceneSingleton.i.aircraft` / `CombatHUD.aircraft` / `FlightHud.aircraft`
3. `Aircraft` instances with a player flag

Tracks are read by **name probe + fuzzy scan**, not a hardcoded private field:

- `tracks`, `trackedTargets`, `targets`, `contacts`, `detections`, and any enumerable whose name contains track / target / contact / detect / return / scan / lock
- `SceneSingleton` / `CombatHUD.GetTargetList()`
- Harmony extras from `TargetSearch`, `ScanRadar`, `RequestRadarCheck`
- Optional geometric scene fallback (`Debug.FallbackSceneScan`, default on) if lists are empty
- **Datalink**: `Aircraft.NetworkHQ` → `FactionHQ.trackingDatabase` values (`TrackingInfo`); Fresh uses live unit when `Observed`/age&lt;FreshSec, else coast `lastKnownPosition` (+ velocity) with optional noise

Each contact carries azimuth, elevation from ownship, closing-speed estimate, and aspect when headings/speeds are readable.

RWR kinds **SEARCH / LOCK / MISSILE** are classified from warning objects and method arguments (icons + audio stay vanilla).


## Per-aircraft radar profiles

Each ownship gets an `AircraftRadarProfile` resolved **once when the ownship instance changes** (cached by instance id — not every tick):

1. **Override** — `BepInEx/config/OritasyRadar.profiles.json` (optional; soft-fail if missing)
2. **Builtin** — soft match on `UnitDefinition.jsonKey` / `unitName` / `code`, `AircraftParameters.aircraftName`, or `GameObject.name` (normalized: lowercase, strip spaces). Seeds include Darkreach, Compass, Chicane, Revoker, Medusa, Tundra, Cricket, QuadVTOL, SmallFighter, Fighter, HeloTransport (tiered interceptor / strike / light / helo stats).
3. **Capacity / Hybrid** — if `UseCapacityFallback` (default true): map `PowerSupply.maxCharge` (电容量; fallback `maxPower`) → MaxRangeKm ≈ 25–220, HalfFov 70→35 as capacity rises, AcmRange 8–25, MaxTracks 4–32. If `PreferNativeRadarStats` (default true) and reflection finds `Radar.radarCone` + `RadarParameters.maxRange`, blend range 50/50 and take cone as **full** cone degrees (half-FOV = cone/2; if value &lt; 45° treat as already half).

### Override JSON shape

```json
[
  {
    "keys": ["darkreach", "b81"],
    "displayLabel": "Darkreach",
    "maxRangeKm": 150,
    "halfFovDeg": 50,
    "twsHalfFov": 32,
    "acmHalfFov": 10,
    "acmMaxRangeKm": 18,
    "maxTracks": 24,
    "scanRateDegPerSec": 60
  }
]
```

Source tags on the data strip: `BLT` builtin, `CAP` capacity, `HYB` hybrid, `OVR` override. Lock strip is unchanged.


## Weapon ETA + gun funnel (v0.5)

Reflection path (fail-soft, member-cached):

1. `Aircraft.weaponManager` → `WeaponManager.currentWeaponStation`
2. else `Aircraft.weaponStations` / selected station with ammo
3. `WeaponStation.WeaponInfo` (+ `Weapon.ammo` / `WeaponStation.Ammo`)
4. Probe `WeaponInfo`: `weaponName` / `shortName`, `muzzleVelocity`, `maxSpeed` (or `GetMaxSpeed()`), `dragCoef`, `gravMult`, `gun` / `missile` / `bomb`, `targetRequirements.maxRange`

Solve runs with the contact throttle (~15 Hz). Lock strip shows the ETA line when `ShowWeaponEta` is on. Gun funnel draws in `Plugin.OnGUI` after camera `WorldToScreenPoint` when `ShowGunFunnel` is on, current weapon is a gun, and a solution exists (locked target preferred; nearest contact used as optional boresight aid). Points with `z ≤ 0` (behind camera) are skipped.

## Build

Requires the .NET SDK. Game install is optional for compile: without it, the project uses NuGet `BepInEx.Core` + `UnityEngine.Modules`.

```bash
dotnet build RDA.csproj -c Release
```

Output: `bin/Release/RDA.dll`

`Directory.Build.props` sets:

```text
NuclearOptionDir = D:/Steam/steamapps/common/Nuclear Option
```

If your Steam library differs, copy `Directory.Build.user.props.example` to `Directory.Build.user.props` and change the path. When that folder contains `BepInEx/core` and `NuclearOption_Data/Managed`, those assemblies are referenced instead of NuGet.

Target framework: **netstandard2.1** (nullable enabled).

## Layout

```text
src/AircraftRadarProfile.cs   Per-aircraft envelope DTO + source tag
src/RadarProfileCatalog.cs    Builtin / capacity / JSON override resolve
src/Plugin.cs                BepInPlugin entry (Oritasy's RADAR 0.0.2T), digit hotkeys, IMGUI + funnel host
src/Config.cs                BepInEx config (labels, datalink, ShowGunFunnel, DisableVanillaRadar, …)
src/DatalinkBridge.cs        FactionHQ trackingDatabase ingest + Rpc/Cmd contribute
src/RadarGui.cs              MFD chrome PPI + DL declutter + lock strip + custom chips
src/ContactProvider.cs       Ownship + tracks + DL merge + ACM A/G designate + TWS air auto-lock + weapon solve (~15 Hz)
src/HitSolution.cs           HitSolution DTO + HitSolver (gun ballistic / missile / bomb)
src/WeaponReflect.cs         WeaponManager / WeaponStation / WeaponInfo soft reflection
src/GunFunnelHud.cs          Screen-space gun funnel (boresight, lead, rings)
src/ModeState.cs             Modes + lock / elev / ScanCenterAzimuthDeg
src/RadarContact.cs          Contact DTO (+ FromDatalink / TrackAgeSec / PositionQuality)
src/OritasyUi.cs             Mono HUD font + OritasyFonts branding soft-load
src/ScopeDraw.cs             MFD panel / brackets / scanlines / chips / glow strokes
src/RwrPanel.cs              Warning ingest + matching RWR bezel
src/HarmonyHooks.cs          Postfixes + vanilla-skip Prefix (local player)
src/PhysicsRaycastHelper.cs  Soft Physics.Raycast for terrain
src/GameReflect.cs           Assembly-CSharp type / member probe (cached)
```

## Notes

- Display-only overlay (optional vanilla scan skip is local-player only).
- Job-thread warning callbacks are queued and drained on the Unity main thread.
- Live field names live in `Assembly-CSharp.dll`; dumped stubs are hints only.
- Expansion **Realtime Aerial Detection And Ranging (R.A.D.A.R.)** appears in README / log only — never in the GUI title.
