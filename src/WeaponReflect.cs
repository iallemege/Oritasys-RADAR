using System;
using System.Reflection;
using UnityEngine;

namespace RDA
{
    /// <summary>Soft-reflected snapshot of the currently selected weapon station / ammo.</summary>
    internal struct WeaponSnapshot
    {
        internal bool Valid;
        internal WeaponKind Kind;
        internal string WeaponName;
        internal string ShortName;
        internal float MuzzleVelocity;
        internal float MaxSpeed;
        internal float DragCoef;
        internal float GravMult;
        internal float MaxRangeMeters;
        internal int Ammo;
        internal Vector3 MuzzleWorld;
        internal Vector3 BarrelDir;
        internal object? Station;
        internal object? Info;
    }

    /// <summary>
    /// Reflection probes for Aircraft → WeaponManager.currentWeaponStation / Unit.weaponStations /
    /// Weapon + WeaponInfo (no compile-time game refs). Member handles cached.
    /// </summary>
    internal static class WeaponReflect
    {
        private const BindingFlags Any =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type? _weaponManager;
        private static Type? _weaponStation;
        private static Type? _weaponInfo;
        private static Type? _weapon;
        private static Type? _targetRequirements;
        private static bool _discovered;

        // Cached member handles for hot path.
        private static MemberHandle? _aircraftWeaponManager;
        private static MemberHandle? _aircraftWeaponStations;
        private static MemberHandle? _wmCurrentStation;
        private static MemberHandle? _stationWeaponInfo;
        private static MemberHandle? _stationAmmo;
        private static MemberHandle? _stationWeapon;
        private static MemberHandle? _weaponAmmo;
        private static MemberHandle? _infoWeaponName;
        private static MemberHandle? _infoShortName;
        private static MemberHandle? _infoMuzzle;
        private static MemberHandle? _infoMaxSpeed;
        private static MemberHandle? _infoDrag;
        private static MemberHandle? _infoGrav;
        private static MemberHandle? _infoGun;
        private static MemberHandle? _infoMissile;
        private static MemberHandle? _infoBomb;
        private static MemberHandle? _infoTargetReq;
        private static MemberHandle? _reqMaxRange;
        private static MemberHandle? _unitRb;
        private static bool _stationMembersCached;
        private static bool _infoMembersCached;

        private static MethodInfo? _infoGetMaxSpeed;

        // Vanilla WeaponManager target list (CombatHUD primary = targetList[0]).
        private static MemberHandle? _wmGetTargetList;
        private static MemberHandle? _wmTargetListField;
        private static bool _wmTargetListCached;

        internal static void EnsureDiscovered()
        {
            if (_discovered && _weaponManager != null)
            {
                return;
            }

            GameReflect.EnsureDiscovered();
            _weaponManager = GameReflect.FindType("WeaponManager");
            _weaponStation = GameReflect.FindType("WeaponStation");
            _weaponInfo = GameReflect.FindType("WeaponInfo");
            _weapon = GameReflect.FindType("Weapon");
            _targetRequirements = GameReflect.FindType("TargetRequirements");
            _discovered = true;

            if (_weaponManager != null || _weaponStation != null)
            {
                Log.Info(
                    "Weapon types: WeaponManager=" + Name(_weaponManager) +
                    " WeaponStation=" + Name(_weaponStation) +
                    " WeaponInfo=" + Name(_weaponInfo) +
                    " Weapon=" + Name(_weapon));
            }
        }

        internal static WeaponSnapshot ResolveCurrent(object? aircraft)
        {
            EnsureDiscovered();
            WeaponSnapshot snap = default;
            snap.WeaponName = "—";
            snap.ShortName = "—";
            snap.GravMult = 1f;
            snap.BarrelDir = Vector3.forward;

            if (aircraft == null)
            {
                return snap;
            }

            object? station = ResolveStation(aircraft);
            if (station == null)
            {
                return snap;
            }

            CacheStationMembers(station.GetType());
            snap.Station = station;

            object? info = _stationWeaponInfo?.Get(station);
            // Sometimes WeaponInfo is nested under Weapon.
            if (info == null)
            {
                object? weapon = _stationWeapon?.Get(station);
                if (weapon != null)
                {
                    info = GameReflect.FindMember(weapon.GetType(),
                        "WeaponInfo", "weaponInfo", "info", "Info")?.Get(weapon);
                }
            }

            snap.Info = info;
            if (info != null)
            {
                CacheInfoMembers(info.GetType());
                PopulateFromInfo(ref snap, info);
            }

            snap.Ammo = ReadAmmo(station, info);
            snap.MuzzleWorld = ResolveMuzzle(aircraft, station);
            snap.BarrelDir = ResolveBarrelDir(aircraft, station);
            snap.Valid = info != null || snap.Ammo > 0 || !string.IsNullOrEmpty(snap.WeaponName) && snap.WeaponName != "—";
            if (snap.Kind == WeaponKind.Unknown && snap.Valid)
            {
                snap.Kind = WeaponKind.Other;
            }

            return snap;
        }

        internal static Vector3 ReadVelocity(object? unit)
        {
            if (unit == null)
            {
                return Vector3.zero;
            }

            // Prefer Unit.rb / Rigidbody.velocity (Nuclear Option pattern).
            try
            {
                _unitRb ??= GameReflect.FindMember(unit.GetType(), "rb", "Rb", "rigidbody", "Rigidbody");
                object? rbObj = _unitRb?.Get(unit);
                if (rbObj is Rigidbody rb)
                {
                    return rb.velocity;
                }

                if (unit is Component c)
                {
                    Rigidbody body = c.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        return body.velocity;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ReadVelocity soft-fail: " + ex.Message);
            }

            // Fallback: heading * speed.
            float speed = GameReflect.SpeedMps(unit);
            float heading = GameReflect.HeadingDeg(unit);
            return Quaternion.Euler(0f, heading, 0f) * Vector3.forward * speed;
        }

        internal static Vector3 ReadForward(object? unit)
        {
            if (unit is Component c)
            {
                return c.transform.forward;
            }

            object? transform = GameReflect.FindMember(unit?.GetType(), "transform", "Transform")?.Get(unit);
            if (transform is Transform t)
            {
                return t.forward;
            }

            float heading = GameReflect.HeadingDeg(unit);
            return Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
        }

        internal static Camera? FindMainCamera()
        {
            try
            {
                if (Camera.main != null)
                {
                    return Camera.main;
                }
            }
            catch
            {
                // ignored
            }

            // SceneSingleton<CameraStateManager>.i.mainCamera
            Type? camState = GameReflect.FindType("CameraStateManager");
            object? singleton = null;
            if (camState != null && GameReflect.SceneSingleton != null)
            {
                try
                {
                    // Non-generic SceneSingleton.i sometimes holds camera manager.
                    singleton = GameReflect.FindMember(GameReflect.SceneSingleton,
                        "i", "I", "instance", "Instance")?.Get(null);
                }
                catch (Exception ex)
                {
                    Log.Debug("CameraStateManager singleton soft-fail: " + ex.Message);
                }
            }

            object? mgr = singleton;
            if (mgr == null && camState != null)
            {
                foreach (UnityEngine.Object o in GameReflect.FindAll(camState))
                {
                    mgr = o;
                    break;
                }
            }

            object? cam = GameReflect.FindMember(mgr?.GetType(),
                "mainCamera", "MainCamera", "camera", "Camera", "activeCamera")?.Get(mgr);
            return cam as Camera;
        }


        /// <summary>
        /// Resolve Aircraft.weaponManager (member or GetComponentInChildren).
        /// </summary>
        internal static object? ResolveWeaponManager(object? aircraft)
        {
            EnsureDiscovered();
            if (aircraft == null)
            {
                return null;
            }

            Type acType = aircraft.GetType();
            _aircraftWeaponManager ??= GameReflect.FindMember(acType,
                "weaponManager", "WeaponManager", "weapons", "Weapons");
            object? wm = _aircraftWeaponManager?.Get(aircraft);
            if (wm == null && _weaponManager != null)
            {
                Component? comp = GameReflect.AsComponent(aircraft);
                if (comp != null)
                {
                    try
                    {
                        wm = comp.GetComponentInChildren(_weaponManager, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("WeaponManager GetComponent soft-fail: " + ex.Message);
                    }
                }
            }

            return wm;
        }

        /// <summary>
        /// Vanilla lock list: WeaponManager.GetTargetList() / targetList field.
        /// Primary paint target is index 0 (CombatHUD sync).
        /// </summary>
        internal static object? TryGetPrimaryTarget(object? aircraft)
        {
            return TryGetPrimaryTargetUnit(aircraft, out object? unit) ? unit : null;
        }

        /// <summary>
        /// Read targetList[0] / GetTargetList() primary unit every call (no ModeState / contact throttle).
        /// Returns false when the list is empty or the primary entry is destroyed.
        /// CombatHUD / missile guide primary remains index 0.
        /// </summary>
        internal static bool TryGetPrimaryTargetUnit(object? aircraft, out object? unit)
        {
            unit = null;
            foreach (object candidate in EnumerateTargetList(aircraft))
            {
                if (candidate == null)
                {
                    continue;
                }

                if (candidate is UnityEngine.Object uo && uo == null)
                {
                    continue;
                }

                unit = candidate;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Display lock for world TRK label: prefer <b>last</b> entry in targetList.
        /// PPI draws a line per EnumerateTargetList entry (multi-lock). Empty → false.
        /// </summary>
        internal static bool TryGetDisplayLockTargetUnit(object? aircraft, out object? unit)
        {
            unit = null;
            object? last = null;
            foreach (object candidate in EnumerateTargetList(aircraft))
            {
                if (candidate == null)
                {
                    continue;
                }

                if (candidate is UnityEngine.Object uo && uo == null)
                {
                    continue;
                }

                last = candidate;
            }

            if (last == null)
            {
                return false;
            }

            unit = last;
            return true;
        }

        /// <summary>True when vanilla targetList has no usable entries (unlock / clear).</summary>
        internal static bool IsTargetListEmpty(object? aircraft)
        {
            foreach (object _ in EnumerateTargetList(aircraft))
            {
                return false;
            }

            return true;
        }

        /// <summary>Enumerate WeaponManager targetList units (may be empty).</summary>
        internal static System.Collections.Generic.IEnumerable<object> EnumerateTargetList(object? aircraft)
        {
            EnsureDiscovered();
            object? wm = ResolveWeaponManager(aircraft);
            if (wm == null)
            {
                yield break;
            }

            if (!_wmTargetListCached)
            {
                Type wmType = wm.GetType();
                _wmGetTargetList = GameReflect.FindMember(wmType,
                    "GetTargetList", "get_TargetList", "getTargetList");
                _wmTargetListField = GameReflect.FindMember(wmType,
                    "targetList", "TargetList", "targets", "Targets");
                _wmTargetListCached = true;
            }

            object? list = _wmGetTargetList?.Get(wm);
            if (list == null)
            {
                list = _wmTargetListField?.Get(wm);
            }

            foreach (object item in GameReflect.Enumerate(list))
            {
                if (item == null)
                {
                    continue;
                }

                // List may hold Unit directly or wrappers.
                object? unit = item;
                if (GameReflect.Unit != null && !GameReflect.Unit.IsInstanceOfType(item) &&
                    !(GameReflect.Aircraft != null && GameReflect.Aircraft.IsInstanceOfType(item)))
                {
                    object? inner = GameReflect.FindMember(item.GetType(),
                        "unit", "Unit", "target", "Target", "aircraft")?.Get(item);
                    if (inner != null)
                    {
                        unit = inner;
                    }
                }

                if (unit != null)
                {
                    yield return unit;
                }
            }
        }

        /// <summary>
        /// WSO-style real lock: push <paramref name="unit"/> onto WeaponManager.targetList as primary ([0])
        /// via AddTargetList / list rewrite — same path as manual paint. Fail-soft.
        /// </summary>
        internal static bool ApplyPrimaryLock(object? aircraft, object? unit)
        {
            EnsureDiscovered();
            if (aircraft == null || unit == null)
            {
                return false;
            }

            if (unit is UnityEngine.Object uo && uo == null)
            {
                return false;
            }

            try
            {
                if (TryGetPrimaryTargetUnit(aircraft, out object? primary) && primary != null)
                {
                    int pid = GameReflect.IdOf(primary);
                    int nid = GameReflect.IdOf(unit);
                    if (pid != 0 && pid == nid)
                    {
                        return true; // already primary
                    }
                }

                object? wm = ResolveWeaponManager(aircraft);
                if (wm == null)
                {
                    return false;
                }

                if (!_wmTargetListCached)
                {
                    Type wmType = wm.GetType();
                    _wmGetTargetList = GameReflect.FindMember(wmType,
                        "GetTargetList", "get_TargetList", "getTargetList");
                    _wmTargetListField = GameReflect.FindMember(wmType,
                        "targetList", "TargetList", "targets", "Targets");
                    _wmTargetListCached = true;
                }

                object? listObj = _wmGetTargetList?.Get(wm) ?? _wmTargetListField?.Get(wm);

                // Prefer single primary: clear then AddTargetList (missile guide = [0]).
                bool hadEntries = false;
                foreach (object _ in EnumerateTargetList(aircraft))
                {
                    hadEntries = true;
                    break;
                }

                if (hadEntries)
                {
                    TryListClear(listObj);
                }

                bool ok = TryAddTarget(wm, listObj, unit);
                if (ok)
                {
                    InvokeTargetListChanged(wm);
                    Log.Info("ApplyPrimaryLock — vanilla targetList[0] set for missile guide");
                }

                return ok;
            }
            catch (Exception ex)
            {
                Log.Debug("ApplyPrimaryLock soft-fail: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Cycle vanilla WeaponManager.targetList so missiles guide on the new primary ([0]).
        /// count≥2: rotate [0] to end (index1 becomes primary). count==0: optionally Add ACM designate.
        /// count==1: replace primary with next ACM surface candidate when provided.
        /// Invokes TargetListChanged when present (CombatHUD sync).
        /// </summary>
        internal static bool CycleVanillaTargetList(
            object? aircraft,
            object? acmDesignateUnit = null,
            System.Collections.Generic.IReadOnlyList<object>? acmSurfaceUnits = null)
        {
            EnsureDiscovered();
            object? wm = ResolveWeaponManager(aircraft);
            if (wm == null)
            {
                return false;
            }

            if (!_wmTargetListCached)
            {
                Type wmType = wm.GetType();
                _wmGetTargetList = GameReflect.FindMember(wmType,
                    "GetTargetList", "get_TargetList", "getTargetList");
                _wmTargetListField = GameReflect.FindMember(wmType,
                    "targetList", "TargetList", "targets", "Targets");
                _wmTargetListCached = true;
            }

            object? listObj = _wmGetTargetList?.Get(wm) ?? _wmTargetListField?.Get(wm);
            var units = new System.Collections.Generic.List<object>();
            foreach (object u in EnumerateTargetList(aircraft))
            {
                units.Add(u);
            }

            int count = units.Count;
            bool changed = false;

            try
            {
                if (count >= 2)
                {
                    // Rotate: remove primary [0], append to end → former [1] becomes primary.
                    object primary = units[0];
                    if (TryListRemoveAt(listObj, 0) || TryListRemove(listObj, primary))
                    {
                        if (!TryListAdd(listObj, primary))
                        {
                            // Fallback: rewrite via Clear+Add if Remove worked but Add failed
                            TryRebuildList(listObj, units, rotate: true);
                        }

                        changed = true;
                    }
                    else
                    {
                        changed = TryRebuildList(listObj, units, rotate: true);
                    }
                }
                else if (count == 0)
                {
                    object? toAdd = acmDesignateUnit;
                    if (toAdd == null && acmSurfaceUnits != null && acmSurfaceUnits.Count > 0)
                    {
                        toAdd = acmSurfaceUnits[0];
                    }

                    if (toAdd != null)
                    {
                        changed = TryAddTarget(wm, listObj, toAdd);
                    }
                }
                else // count == 1
                {
                    if (acmSurfaceUnits != null && acmSurfaceUnits.Count > 0)
                    {
                        object current = units[0];
                        int curId = GameReflect.IdOf(current);
                        int idx = 0;
                        for (int i = 0; i < acmSurfaceUnits.Count; i++)
                        {
                            if (GameReflect.IdOf(acmSurfaceUnits[i]) == curId)
                            {
                                idx = i;
                                break;
                            }
                        }

                        object next = acmSurfaceUnits[(idx + 1) % acmSurfaceUnits.Count];
                        if (GameReflect.IdOf(next) != curId)
                        {
                            // Replace primary with next ACM surface candidate.
                            if (TryListClear(listObj))
                            {
                                changed = TryListAdd(listObj, next) || TryAddTarget(wm, listObj, next);
                            }
                            else
                            {
                                TryListRemoveAt(listObj, 0);
                                changed = TryListAdd(listObj, next) || TryAddTarget(wm, listObj, next);
                            }
                        }
                    }
                    // else no-op (single entry, nothing to cycle into)
                }

                if (changed)
                {
                    InvokeTargetListChanged(wm);
                    Log.Info("CycleVanillaTargetList — primary rotated/updated for missile guide");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("CycleVanillaTargetList soft-fail: " + ex.Message);
                return false;
            }

            return changed;
        }

        private static void InvokeTargetListChanged(object wm)
        {
            try
            {
                foreach (MethodInfo method in GameReflect.FindMethods(wm.GetType(),
                    "TargetListChanged", "OnTargetListChanged", "NotifyTargetListChanged"))
                {
                    ParameterInfo[] ps = method.GetParameters();
                    if (ps.Length == 0)
                    {
                        method.Invoke(method.IsStatic ? null : wm, Array.Empty<object>());
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TargetListChanged soft-fail: " + ex.Message);
            }
        }

        private static bool TryAddTarget(object wm, object? listObj, object unit)
        {
            // Prefer WeaponManager.AddTargetList / AddTarget when present.
            try
            {
                foreach (MethodInfo method in GameReflect.FindMethods(wm.GetType(),
                    "AddTargetList", "AddTarget", "AddToTargetList", "SetTarget"))
                {
                    ParameterInfo[] ps = method.GetParameters();
                    if (ps.Length == 1)
                    {
                        method.Invoke(wm, new object[] { unit });
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("AddTargetList soft-fail: " + ex.Message);
            }

            return TryListAdd(listObj, unit);
        }

        private static bool TryRebuildList(object? listObj, System.Collections.Generic.List<object> units, bool rotate)
        {
            if (listObj == null || units.Count == 0)
            {
                return false;
            }

            var ordered = new System.Collections.Generic.List<object>(units.Count);
            if (rotate && units.Count >= 2)
            {
                for (int i = 1; i < units.Count; i++)
                {
                    ordered.Add(units[i]);
                }

                ordered.Add(units[0]);
            }
            else
            {
                ordered.AddRange(units);
            }

            if (!TryListClear(listObj))
            {
                return false;
            }

            bool any = false;
            for (int i = 0; i < ordered.Count; i++)
            {
                if (TryListAdd(listObj, ordered[i]))
                {
                    any = true;
                }
            }

            return any;
        }

        private static bool TryListAdd(object? listObj, object item)
        {
            if (listObj == null)
            {
                return false;
            }

            try
            {
                if (listObj is System.Collections.IList ilist && !ilist.IsFixedSize)
                {
                    ilist.Add(item);
                    return true;
                }

                MethodInfo? add = listObj.GetType().GetMethod("Add", Any, null, new[] { item.GetType() }, null)
                    ?? listObj.GetType().GetMethod("Add", Any);
                if (add != null && add.GetParameters().Length == 1)
                {
                    add.Invoke(listObj, new object[] { item });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryListAdd soft-fail: " + ex.Message);
            }

            return false;
        }

        private static bool TryListRemove(object? listObj, object item)
        {
            if (listObj == null)
            {
                return false;
            }

            try
            {
                if (listObj is System.Collections.IList ilist && !ilist.IsFixedSize)
                {
                    ilist.Remove(item);
                    return true;
                }

                MethodInfo? rem = listObj.GetType().GetMethod("Remove", Any);
                if (rem != null && rem.GetParameters().Length == 1)
                {
                    rem.Invoke(listObj, new object[] { item });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryListRemove soft-fail: " + ex.Message);
            }

            return false;
        }

        private static bool TryListRemoveAt(object? listObj, int index)
        {
            if (listObj == null)
            {
                return false;
            }

            try
            {
                if (listObj is System.Collections.IList ilist && !ilist.IsFixedSize)
                {
                    if (index >= 0 && index < ilist.Count)
                    {
                        ilist.RemoveAt(index);
                        return true;
                    }

                    return false;
                }

                MethodInfo? remAt = listObj.GetType().GetMethod("RemoveAt", Any, null, new[] { typeof(int) }, null);
                if (remAt != null)
                {
                    remAt.Invoke(listObj, new object[] { index });
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryListRemoveAt soft-fail: " + ex.Message);
            }

            return false;
        }

        private static bool TryListClear(object? listObj)
        {
            if (listObj == null)
            {
                return false;
            }

            try
            {
                if (listObj is System.Collections.IList ilist && !ilist.IsFixedSize)
                {
                    ilist.Clear();
                    return true;
                }

                MethodInfo? clear = listObj.GetType().GetMethod("Clear", Any, null, Type.EmptyTypes, null);
                if (clear != null)
                {
                    clear.Invoke(listObj, Array.Empty<object>());
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryListClear soft-fail: " + ex.Message);
            }

            return false;
        }

        private static object? ResolveStation(object aircraft)
        {
            Type acType = aircraft.GetType();
            _aircraftWeaponManager ??= GameReflect.FindMember(acType,
                "weaponManager", "WeaponManager", "weapons", "Weapons");
            object? wm = _aircraftWeaponManager?.Get(aircraft);

            // Component path if member missing.
            if (wm == null && _weaponManager != null)
            {
                Component? comp = GameReflect.AsComponent(aircraft);
                if (comp != null)
                {
                    try
                    {
                        wm = comp.GetComponentInChildren(_weaponManager, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("WeaponManager GetComponent soft-fail: " + ex.Message);
                    }
                }
            }

            if (wm != null)
            {
                _wmCurrentStation ??= GameReflect.FindMember(wm.GetType(),
                    "currentWeaponStation", "CurrentWeaponStation", "currentStation",
                    "selectedStation", "SelectedStation", "activeStation");
                object? current = _wmCurrentStation?.Get(wm);
                if (current != null)
                {
                    return current;
                }
            }

            _aircraftWeaponStations ??= GameReflect.FindMember(acType,
                "weaponStations", "WeaponStations", "stations", "Stations");
            object? stations = _aircraftWeaponStations?.Get(aircraft);
            if (stations == null && wm != null)
            {
                stations = GameReflect.FindMember(wm.GetType(),
                    "weaponStations", "WeaponStations", "stations")?.Get(wm);
            }

            object? firstWithAmmo = null;
            foreach (object station in GameReflect.Enumerate(stations))
            {
                if (firstWithAmmo == null)
                {
                    firstWithAmmo = station;
                }

                int ammo = ReadAmmoQuick(station);
                if (ammo > 0)
                {
                    // Prefer a "selected" / current-looking station if flagged.
                    if (GameReflect.ReadBool(station, false,
                            "selected", "Selected", "isSelected", "IsSelected", "active", "Active"))
                    {
                        return station;
                    }
                }
            }

            return firstWithAmmo;
        }

        private static void CacheStationMembers(Type stationType)
        {
            if (_stationMembersCached)
            {
                return;
            }

            _stationWeaponInfo = GameReflect.FindMember(stationType,
                "WeaponInfo", "weaponInfo", "info", "Info", "weaponDefinition");
            _stationAmmo = GameReflect.FindMember(stationType,
                "Ammo", "ammo", "ammoCount", "AmmoCount", "rounds");
            _stationWeapon = GameReflect.FindMember(stationType,
                "Weapon", "weapon", "currentWeapon", "CurrentWeapon", "mountedWeapon");
            _stationMembersCached = true;
        }

        private static void CacheInfoMembers(Type infoType)
        {
            if (_infoMembersCached)
            {
                return;
            }

            _infoWeaponName = GameReflect.FindMember(infoType,
                "weaponName", "WeaponName", "name", "Name", "displayName", "DisplayName");
            _infoShortName = GameReflect.FindMember(infoType,
                "shortName", "ShortName", "code", "Code", "abbrev", "AbbreviatedName");
            _infoMuzzle = GameReflect.FindMember(infoType,
                "muzzleVelocity", "MuzzleVelocity", "muzzleSpeed", "exitVelocity");
            _infoMaxSpeed = GameReflect.FindMember(infoType,
                "maxSpeed", "MaxSpeed", "speed", "Speed", "topSpeed");
            _infoDrag = GameReflect.FindMember(infoType,
                "dragCoef", "DragCoef", "drag", "Drag", "dragCoefficient");
            _infoGrav = GameReflect.FindMember(infoType,
                "gravMult", "GravMult", "gravityMult", "gravityMultiplier", "gravity");
            _infoGun = GameReflect.FindMember(infoType, "gun", "Gun", "isGun", "IsGun");
            _infoMissile = GameReflect.FindMember(infoType, "missile", "Missile", "isMissile", "IsMissile");
            _infoBomb = GameReflect.FindMember(infoType, "bomb", "Bomb", "isBomb", "IsBomb", "unguided");
            _infoTargetReq = GameReflect.FindMember(infoType,
                "targetRequirements", "TargetRequirements", "requirements", "Requirements");

            try
            {
                _infoGetMaxSpeed = infoType.GetMethod("GetMaxSpeed", Any, null, Type.EmptyTypes, null);
            }
            catch
            {
                _infoGetMaxSpeed = null;
            }

            _infoMembersCached = true;
        }

        private static void PopulateFromInfo(ref WeaponSnapshot snap, object info)
        {
            snap.WeaponName = ReadString(_infoWeaponName?.Get(info)) ?? snap.WeaponName;
            snap.ShortName = ReadString(_infoShortName?.Get(info)) ?? snap.ShortName;
            if (snap.ShortName == "—" && snap.WeaponName != "—")
            {
                snap.ShortName = snap.WeaponName;
            }

            snap.MuzzleVelocity = ReadFloatVal(_infoMuzzle?.Get(info));
            snap.MaxSpeed = ReadFloatVal(_infoMaxSpeed?.Get(info));
            if ((float.IsNaN(snap.MaxSpeed) || snap.MaxSpeed <= 0f) && _infoGetMaxSpeed != null)
            {
                try
                {
                    object? sp = _infoGetMaxSpeed.Invoke(info, Array.Empty<object>());
                    snap.MaxSpeed = ReadFloatVal(sp);
                }
                catch (Exception ex)
                {
                    Log.Debug("GetMaxSpeed soft-fail: " + ex.Message);
                }
            }

            snap.DragCoef = ReadFloatVal(_infoDrag?.Get(info));
            if (float.IsNaN(snap.DragCoef) || snap.DragCoef < 0f)
            {
                snap.DragCoef = 0f;
            }

            float grav = ReadFloatVal(_infoGrav?.Get(info));
            snap.GravMult = !float.IsNaN(grav) && grav > 0f ? grav : 1f;

            object? req = _infoTargetReq?.Get(info);
            if (req != null)
            {
                _reqMaxRange ??= GameReflect.FindMember(req.GetType(),
                    "maxRange", "MaxRange", "range", "Range");
                float maxR = ReadFloatVal(_reqMaxRange?.Get(req));
                if (!float.IsNaN(maxR) && maxR > 0f)
                {
                    // Heuristic: values < 500 treated as kilometres.
                    snap.MaxRangeMeters = maxR > 500f ? maxR : maxR * 1000f;
                }
            }

            if (float.IsNaN(snap.MaxRangeMeters) || snap.MaxRangeMeters <= 0f)
            {
                float direct = GameReflect.ReadFloat(info, "maxRange", "MaxRange", "range");
                if (!float.IsNaN(direct) && direct > 0f)
                {
                    snap.MaxRangeMeters = direct > 500f ? direct : direct * 1000f;
                }
            }

            bool isGun = ReadBoolVal(_infoGun?.Get(info), false);
            bool isMissile = ReadBoolVal(_infoMissile?.Get(info), false);
            bool isBomb = ReadBoolVal(_infoBomb?.Get(info), false);

            // Type-name / weapon-name fallback.
            if (!isGun && !isMissile && !isBomb)
            {
                string n = (snap.WeaponName + " " + snap.ShortName + " " + info.GetType().Name).ToLowerInvariant();
                if (n.IndexOf("gun", StringComparison.Ordinal) >= 0 ||
                    n.IndexOf("cannon", StringComparison.Ordinal) >= 0 ||
                    n.IndexOf("vulcan", StringComparison.Ordinal) >= 0 ||
                    n.IndexOf("gau", StringComparison.Ordinal) >= 0)
                {
                    isGun = true;
                }
                else if (n.IndexOf("missile", StringComparison.Ordinal) >= 0 ||
                         n.IndexOf("aam", StringComparison.Ordinal) >= 0 ||
                         n.IndexOf("agm", StringComparison.Ordinal) >= 0 ||
                         n.IndexOf("sam", StringComparison.Ordinal) >= 0 ||
                         n.IndexOf("rocket", StringComparison.Ordinal) >= 0)
                {
                    isMissile = true;
                }
                else if (n.IndexOf("bomb", StringComparison.Ordinal) >= 0 ||
                         n.IndexOf("unguided", StringComparison.Ordinal) >= 0)
                {
                    isBomb = true;
                }
            }

            // Enum / type field named "type" / "weaponType".
            if (!isGun && !isMissile && !isBomb)
            {
                object? typeObj = GameReflect.FindMember(info.GetType(),
                    "type", "Type", "weaponType", "WeaponType", "category")?.Get(info);
                string tn = typeObj?.ToString() ?? string.Empty;
                if (tn.IndexOf("Gun", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isGun = true;
                }
                else if (tn.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isMissile = true;
                }
                else if (tn.IndexOf("Bomb", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isBomb = true;
                }
            }

            if (isGun)
            {
                snap.Kind = WeaponKind.Gun;
            }
            else if (isMissile)
            {
                snap.Kind = WeaponKind.Missile;
            }
            else if (isBomb)
            {
                snap.Kind = WeaponKind.Bomb;
            }
            else
            {
                snap.Kind = WeaponKind.Other;
            }
        }

        private static int ReadAmmo(object station, object? info)
        {
            int ammo = ReadAmmoQuick(station);
            if (ammo > 0)
            {
                return ammo;
            }

            object? weapon = _stationWeapon?.Get(station);
            if (weapon != null)
            {
                _weaponAmmo ??= GameReflect.FindMember(weapon.GetType(),
                    "ammo", "Ammo", "ammoCount", "AmmoCount", "roundsRemaining");
                int fromWeapon = ReadIntVal(_weaponAmmo?.Get(weapon));
                if (fromWeapon > 0)
                {
                    return fromWeapon;
                }
            }

            if (info != null)
            {
                int fromInfo = (int)GameReflect.ReadFloat(info, "ammo", "Ammo", "defaultAmmo", "magazine");
                if (fromInfo > 0)
                {
                    return fromInfo;
                }
            }

            return ammo;
        }

        private static int ReadAmmoQuick(object station)
        {
            if (_stationAmmo == null)
            {
                CacheStationMembers(station.GetType());
            }

            return ReadIntVal(_stationAmmo?.Get(station));
        }

        private static Vector3 ResolveMuzzle(object aircraft, object station)
        {
            // Prefer barrel / hardpoint transform on station.
            object? xf = GameReflect.FindMember(station.GetType(),
                "muzzle", "Muzzle", "barrel", "Barrel", "hardpoint", "Hardpoint",
                "launchPoint", "firePoint", "transform")?.Get(station);
            if (xf is Transform t)
            {
                return t.position;
            }

            if (xf is Component c)
            {
                return c.transform.position;
            }

            return GameReflect.WorldPosition(aircraft);
        }

        private static Vector3 ResolveBarrelDir(object aircraft, object station)
        {
            object? xf = GameReflect.FindMember(station.GetType(),
                "muzzle", "Muzzle", "barrel", "Barrel", "hardpoint", "launchPoint", "firePoint",
                "transform")?.Get(station);
            if (xf is Transform t)
            {
                return t.forward;
            }

            if (xf is Component c)
            {
                return c.transform.forward;
            }

            object? dir = GameReflect.FindMember(station.GetType(),
                "aimDirection", "AimDirection", "forward", "Forward", "fireDirection")?.Get(station);
            if (dir is Vector3 v && v.sqrMagnitude > 0.01f)
            {
                return v.normalized;
            }

            return ReadForward(aircraft);
        }

        private static string? ReadString(object? value)
        {
            if (value == null)
            {
                return null;
            }

            string? s = value.ToString();
            return string.IsNullOrEmpty(s) ? null : s.Trim();
        }

        private static float ReadFloatVal(object? value)
        {
            switch (value)
            {
                case float f:
                    return f;
                case double d:
                    return (float)d;
                case int i:
                    return i;
                default:
                    return float.NaN;
            }
        }

        private static int ReadIntVal(object? value)
        {
            switch (value)
            {
                case int i:
                    return i;
                case short s:
                    return s;
                case byte b:
                    return b;
                case float f:
                    return (int)f;
                case double d:
                    return (int)d;
                default:
                    return 0;
            }
        }

        private static bool ReadBoolVal(object? value, bool fallback)
        {
            return value is bool b ? b : fallback;
        }

        private static string Name(Type? type) => type != null ? type.FullName ?? type.Name : "—";
    }
}
