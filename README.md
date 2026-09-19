# Oritasy's RADAR

Standalone **BepInEx 5** (Unity Mono) plugin for **Nuclear Option**.

**Oritasy's RADAR** — **Realtime Aerial Detection And Ranging (R.A.D.A.R.)**

| | |
| --- | --- |
| GUID | `com.iallemmege.RDA` |
| Assembly | `RDA.dll` (kept for BepInEx install compatibility) |
| Plugin DisplayName | **Oritasy's RADAR** |
| Version | **0.0.8T** (`DisplayVersion`; BepInEx SemVer: `0.0.8`) |

Standalone plugin with no external Oritasy runtime dependency. Soft-loads optional `OritasyFonts` for branding (`Oritasy™` footer only).

Vanilla TacScreen can be suppressed for the **local player** via `DisableVanillaRadar` (default **off** for BIA coexistence). RDA draws its own IMGUI window titled **Oritasy's RADAR**. **Requires BIA Runtime** (`bia.runtime`) as a hard BepInEx prerequisite.

## BIA compatibility

RDA **0.0.5T+** requires **BIA Runtime** (plugin GUID `bia.runtime`, DisplayName **BIA Runtime**, e.g. `BIA.Runtime_1.0.23.dll`).

| Topic | Detail |
| --- | --- |
| Prerequisite | Hard `[BepInDependency("bia.runtime")]` — Chainloader refuses to load RDA without BIA and loads RDA **after** BIA |
| Soft deps | Still soft on `com.iallemmege.oritasy` / `com.iallemmege.oritasyhud` |
| SemVer | `Plugin.Version = "0.0.8"` (numeric only for BepInEx); UI/branding uses `DisplayVersion = "0.0.8T"` |
| `DisableVanillaRadar` | Default **false** so Prefix-skip on `TacScreen.ScanRadar` / `Radar.TargetSearch` does **not** starve **BIARadar** or shared tracks on BIA airframes. Enable in cfg if you want vanilla local-player scan suppressed |
| IMGUI / MFD | `GUI.depth` via `Display.GuiDepth` (default **-1000**) draws RDA above most IMGUI — including **BIAMfd** / **BIAYukikazeHud**. Raise toward `0` if RDA covers BIA MFD panels. Toggle RDA with **F6** |
| Harmony | Patches only vanilla Nuclear Option types (`Aircraft`, `Radar`, `TacScreen`, `ARHSeeker`, …). **Never** patches `BIA*` types (BIARadar, BIAMfd, BIAAirframe, …) |
| Reflection | Soft Aircraft/Radar reflection; BIA airframes typically extend vanilla `Aircraft` — fail-soft; one Info log when a BIA assembly is detected |
| Install order | Automatic via BepInEx dependency graph — place both DLLs in `plugins/`; no manual load-order hack needed |
| Coexistence | BIARadar / BIAMfd + RDA PPI overlay are both OK; use F6 to hide RDA when using BIA MFDs exclusively |

Known BIA modules (informational): BIAAirframe, BIARadar, BIAMfd, BIADrakenRoundMfd, BIACoffinCanopy, BIAYukikazeHud.

## Install

1. Install **BepInEx 5 x64** (Mono) into the Nuclear Option game folder.
2. Install **BIA Runtime** (`BIA.Runtime_*.dll`, GUID `bia.runtime`) into `BepInEx/plugins/` — **required** hard prerequisite. Chainloader will not load RDA without it (install order is automatic via dependency).
3. Build this project (or copy a Release `RDA.dll`).
4. Place `RDA.dll` in `Nuclear Option/BepInEx/plugins/`.
5. Optional: put `NotoSansSC-VF.ttf` (or any `.ttf`) in `BepInEx/plugins/OritasyFonts/` for CJK / branding font.
6. If you also use **Oritasy** / **OritasyHud**: ensure those plugin files end with **`.dll`** (not `.dl`). BepInEx typically will not load a `.dl` file. RDA does **not** hard-require Oritasy assemblies — fonts from `OritasyFonts/` are optional soft-load only.
7. Launch once. Config is written to `BepInEx/config/com.iallemmege.RDA.cfg`.
   - Default `Display.HudGateMode` = **AircraftPresent** (MFD should appear when boarded).
   - If MFD still missing: set `Display.ForceShowHud = true` (emergency) and check LogOutput for `HUD diag:` / `OnGUI failed:` lines.

**Versioning note:** BepInEx requires the plugin version in `[BepInPlugin]` to be numeric SemVer, so it uses `0.0.8`. The branded/user-facing version is `DisplayVersion = 0.0.8T`. Never use letter-only versions in `BepInPlugin`.

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
| **/** | Open / close settings (window position + opacity) |
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
| `DisableVanillaRadar` | **false** | Skip vanilla ScanRadar/TargetSearch for local player only (default off for BIA) |
| `GuiDepth` | **-1000** | IMGUI depth (lower = on top; may cover BIAMfd) |
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
src/Plugin.cs                BepInPlugin entry (Oritasy's RADAR 0.0.4T), digit hotkeys, IMGUI + funnel host
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
