using System;
using System.Reflection;
using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Soft-reflection: when an ARH/ARM missile seeker loses lock mid-flight, re-assign the
    /// local player's vanilla WeaponManager.targetList[0] (or HQ-tracked prior target) so
    /// SlowChecks does not treat null lock alone as suicide. Impact / proximity / timer /
    /// sea / ground / miss-kinematic detonation paths are not patched (no Missile.Detonate prefix).
    /// </summary>
    internal static class MissileRadarSupport
    {
        private const BindingFlags Any =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Type? _missileType;
        private static Type? _arhSeekerType;
        private static Type? _armSeekerType;
        private static Type? _unitRegistryType;
        private static MethodInfo? _tryGetUnit;
        private static MethodInfo? _setTarget;
        private static bool _typesProbed;
        private static bool _loggedReady;
        private static float _nextTickUnscaled;

        internal static void EnsureTypes()
        {
            if (_typesProbed)
            {
                return;
            }

            _typesProbed = true;
            try
            {
                GameReflect.EnsureDiscovered();
                _missileType = GameReflect.FindType("Missile");
                _arhSeekerType = GameReflect.FindType("ARHSeeker")
                    ?? GameReflect.FindType("ArhSeeker");
                _armSeekerType = GameReflect.FindType("ARMSeeker")
                    ?? GameReflect.FindType("ArmSeeker");
                _unitRegistryType = GameReflect.FindType("UnitRegistry");

                if (_missileType != null)
                {
                    foreach (MethodInfo method in GameReflect.FindMethods(_missileType,
                        "SetTarget", "set_Target", "AssignTarget"))
                    {
                        if (method.GetParameters().Length == 1)
                        {
                            _setTarget = method;
                            break;
                        }
                    }
                }

                if (_unitRegistryType != null)
                {
                    foreach (MethodInfo method in _unitRegistryType.GetMethods(Any))
                    {
                        if (method.Name.IndexOf("TryGetUnit", StringComparison.OrdinalIgnoreCase) < 0 &&
                            method.Name.IndexOf("TryGet", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        if (method.GetParameters().Length >= 1)
                        {
                            _tryGetUnit = method;
                            break;
                        }
                    }
                }

                if (!_loggedReady)
                {
                    _loggedReady = true;
                    Log.Info(
                        "MissileRadarSupport: Missile=" + NameOf(_missileType) +
                        " ARH=" + NameOf(_arhSeekerType) +
                        " ARM=" + NameOf(_armSeekerType) +
                        " UnitRegistry=" + NameOf(_unitRegistryType) +
                        " SetTarget=" + (_setTarget != null) +
                        " TryGetUnit=" + (_tryGetUnit != null));
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MissileRadarSupport EnsureTypes soft-fail: " + ex.Message);
            }
        }

        internal static Type? ArhSeekerType
        {
            get { EnsureTypes(); return _arhSeekerType; }
        }

        internal static Type? ArmSeekerType
        {
            get { EnsureTypes(); return _armSeekerType; }
        }

        /// <summary>
        /// ~2 Hz: local-owned missiles with null seeker lock try radar relock
        /// (covers datalink mid-flight clears between SlowChecks calls).
        /// </summary>
        internal static void Tick(object? player)
        {
            if (!Config.MissileRadarRelock.Value || player == null)
            {
                return;
            }

            if (Time.unscaledTime < _nextTickUnscaled)
            {
                return;
            }

            _nextTickUnscaled = Time.unscaledTime + 0.5f;

            try
            {
                EnsureTypes();
                if (_missileType == null)
                {
                    return;
                }

                bool hasPrimary = WeaponReflect.TryGetPrimaryTargetUnit(player, out _);
                if (!hasPrimary && DatalinkBridge.ResolveNetworkHq(player) == null)
                {
                    return;
                }

                foreach (UnityEngine.Object obj in GameReflect.FindAll(_missileType))
                {
                    if (obj == null)
                    {
                        continue;
                    }

                    object missile = obj;
                    if (!IsLocalOwnedMissile(missile, player))
                    {
                        continue;
                    }

                    object? seeker = ResolveSeeker(missile);
                    if (seeker == null || !IsDestroyedOrNull(ReadLockedTarget(seeker)))
                    {
                        continue;
                    }

                    TryRelockMissileFromRadar(missile, seeker);
                }
            }
            catch (Exception ex)
            {
                Log.Debug("MissileRadarSupport.Tick soft-fail: " + ex.Message);
            }
        }

        /// <summary>
        /// Fail-soft: ownerId → UnitRegistry; local player only; primary = targetList[0];
        /// SetTarget + seeker lockedTarget; refresh known pos. Returns true if restored.
        /// </summary>
        internal static bool TryRelockMissileFromRadar(object? missile, object? seeker)
        {
            if (!Config.MissileRadarRelock.Value || missile == null)
            {
                return false;
            }

            try
            {
                EnsureTypes();
                object? player = HarmonyHooks.LocalPlayerOrNull;
                if (player == null || !IsLocalOwnedMissile(missile, player))
                {
                    return false;
                }

                object? oldTarget = ReadLockedTarget(seeker) ?? ReadMissileTarget(missile);

                object? primary = null;
                if (WeaponReflect.TryGetPrimaryTargetUnit(player, out object? listPrimary) &&
                    !IsDestroyedOrNull(listPrimary))
                {
                    primary = listPrimary;
                }

                if (primary == null && !IsDestroyedOrNull(oldTarget) && IsHqTracking(player, oldTarget!))
                {
                    primary = oldTarget;
                }

                if (IsDestroyedOrNull(primary))
                {
                    return false;
                }

                if (GameReflect.CompareIff(player, primary) == IffRelation.Friend)
                {
                    return false;
                }

                bool assigned = false;
                if (_setTarget != null)
                {
                    try
                    {
                        _setTarget.Invoke(missile, new[] { primary });
                        assigned = true;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Missile.SetTarget soft-fail: " + ex.Message);
                    }
                }

                if (!assigned)
                {
                    assigned = TrySetMember(missile, primary,
                        "target", "Target", "lockedTarget", "LockedTarget", "currentTarget");
                }

                if (seeker != null)
                {
                    if (TrySetMember(seeker, primary,
                        "lockedTarget", "LockedTarget", "target", "Target",
                        "targetUnit", "TargetUnit", "currentTarget"))
                    {
                        assigned = true;
                    }

                    Vector3 world = GameReflect.WorldPosition(primary);
                    if (world.sqrMagnitude > 0.01f)
                    {
                        TrySetMember(seeker, world,
                            "knownPos", "KnownPos", "lastKnownPos", "LastKnownPos",
                            "lastKnownPosition", "LastKnownPosition",
                            "predictedPos", "PredictedPos");
                    }
                }

                if (assigned)
                {
                    Log.Debug("MissileRadarSupport relocked id=" + GameReflect.IdOf(primary));
                }

                return assigned;
            }
            catch (Exception ex)
            {
                Log.Debug("TryRelockMissileFromRadar soft-fail: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Harmony Prefix for ARHSeeker/ARMSeeker.SlowChecks.
        /// Relock when lockedTarget null. If BlockLockLostSuicide and ownship radar still has a
        /// usable primary, skip SlowChecks this frame (null-target Detonate suppressed only).
        /// Never prefixes Missile.Detonate.
        /// </summary>
        internal static bool SlowChecksRelockPrefix(object __instance)
        {
            try
            {
                if (!Config.MissileRadarRelock.Value && !Config.BlockLockLostSuicide.Value)
                {
                    return true;
                }

                object seeker = __instance;
                if (!IsDestroyedOrNull(ReadLockedTarget(seeker)))
                {
                    return true;
                }

                object? missile = ResolveMissileFromSeeker(seeker);
                bool relocked = false;
                if (Config.MissileRadarRelock.Value && missile != null)
                {
                    relocked = TryRelockMissileFromRadar(missile, seeker);
                }

                if (relocked || !Config.BlockLockLostSuicide.Value)
                {
                    return true;
                }

                object? player = HarmonyHooks.LocalPlayerOrNull;
                if (player == null)
                {
                    return true;
                }

                if (missile != null && !IsLocalOwnedMissile(missile, player))
                {
                    return true;
                }

                bool radarHasLock =
                    WeaponReflect.TryGetPrimaryTargetUnit(player, out object? primary) &&
                    !IsDestroyedOrNull(primary) &&
                    GameReflect.CompareIff(player, primary) != IffRelation.Friend;

                return !radarHasLock;
            }
            catch (Exception ex)
            {
                Log.Debug("SlowChecksRelockPrefix soft-fail: " + ex.Message);
                return true;
            }
        }

        internal static bool ArmSeekerLikelyHasNullTargetSuicide(Type? armType)
        {
            if (armType == null)
            {
                return false;
            }

            return GameReflect.FindMember(armType,
                "lockedTarget", "LockedTarget", "targetUnit", "TargetUnit", "target", "Target") != null;
        }

        private static object? ResolveSeeker(object missile)
        {
            object? seeker = GameReflect.FindMember(missile.GetType(),
                "seeker", "Seeker", "missileSeeker", "MissileSeeker", "guidance", "Guidance")?.Get(missile);
            if (seeker != null)
            {
                return seeker;
            }

            Component? comp = GameReflect.AsComponent(missile);
            if (comp == null)
            {
                return null;
            }

            try
            {
                if (_arhSeekerType != null)
                {
                    Component c = comp.GetComponentInChildren(_arhSeekerType, true);
                    if (c != null) return c;
                }

                if (_armSeekerType != null)
                {
                    Component c = comp.GetComponentInChildren(_armSeekerType, true);
                    if (c != null) return c;
                }

                Type? baseSeeker = GameReflect.FindType("MissileSeeker");
                if (baseSeeker != null)
                {
                    Component c = comp.GetComponentInChildren(baseSeeker, true);
                    if (c != null) return c;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("ResolveSeeker soft-fail: " + ex.Message);
            }

            return null;
        }

        private static object? ResolveMissileFromSeeker(object seeker)
        {
            object? missile = GameReflect.FindMember(seeker.GetType(),
                "missile", "Missile", "weapon", "Weapon", "parentMissile")?.Get(seeker);
            if (missile != null)
            {
                return missile;
            }

            Component? sc = GameReflect.AsComponent(seeker);
            if (sc == null)
            {
                return null;
            }

            try
            {
                if (_missileType != null)
                {
                    Component? parent = sc.GetComponentInParent(_missileType);
                    if (parent != null) return parent;
                }

                Component? same = sc.GetComponent("Missile");
                if (same != null) return same;
            }
            catch (Exception ex)
            {
                Log.Debug("ResolveMissileFromSeeker soft-fail: " + ex.Message);
            }

            return null;
        }

        private static object? ReadLockedTarget(object? seeker)
        {
            if (seeker == null) return null;
            return GameReflect.FindMember(seeker.GetType(),
                "lockedTarget", "LockedTarget", "targetUnit", "TargetUnit",
                "target", "Target", "currentTarget")?.Get(seeker);
        }

        private static object? ReadMissileTarget(object? missile)
        {
            if (missile == null) return null;
            return GameReflect.FindMember(missile.GetType(),
                "target", "Target", "lockedTarget", "LockedTarget", "currentTarget")?.Get(missile);
        }

        private static bool IsLocalOwnedMissile(object missile, object player)
        {
            object? owner = GameReflect.FindMember(missile.GetType(),
                "owner", "Owner", "aircraft", "Aircraft", "launcher", "Launcher",
                "source", "Source")?.Get(missile);
            if (owner != null)
            {
                if (ReferenceEquals(owner, player)) return true;

                Component? oc = GameReflect.AsComponent(owner);
                Component? pc = GameReflect.AsComponent(player);
                if (oc != null && pc != null && oc.transform.root == pc.transform.root) return true;

                int oid = GameReflect.IdOf(owner);
                int pid = GameReflect.IdOf(player);
                if (oid != 0 && oid == pid) return true;
            }

            object? ownerId = GameReflect.FindMember(missile.GetType(),
                "ownerID", "OwnerID", "ownerId", "OwnerId", "launcherID", "LauncherID")?.Get(missile);
            if (ownerId != null && TryResolveUnitById(ownerId, out object? resolved) && resolved != null)
            {
                if (ReferenceEquals(resolved, player)) return true;

                Component? rc = GameReflect.AsComponent(resolved);
                Component? pc2 = GameReflect.AsComponent(player);
                if (rc != null && pc2 != null && rc.transform.root == pc2.transform.root) return true;

                int rid = GameReflect.IdOf(resolved);
                int pid2 = GameReflect.IdOf(player);
                if (rid != 0 && rid == pid2) return true;
            }

            return GameReflect.ReadBool(missile, false,
                "isPlayer", "IsPlayer", "isLocalPlayer", "IsLocalPlayer",
                "localPlayer", "ownedByPlayer");
        }

        private static bool TryResolveUnitById(object ownerId, out object? unit)
        {
            unit = null;
            try
            {
                EnsureTypes();
                if (_tryGetUnit != null)
                {
                    ParameterInfo[] ps = _tryGetUnit.GetParameters();
                    if (ps.Length == 1)
                    {
                        object? r = _tryGetUnit.Invoke(null, new[] { CoerceIdArg(ownerId, ps[0].ParameterType) });
                        if (r != null) { unit = r; return true; }
                    }
                    else if (ps.Length >= 2)
                    {
                        object?[] args = new object?[ps.Length];
                        args[0] = CoerceIdArg(ownerId, ps[0].ParameterType);
                        object? ok = _tryGetUnit.Invoke(null, args);
                        object? resolved = args[args.Length - 1];
                        if ((ok is bool b ? b : resolved != null) && resolved != null)
                        {
                            unit = resolved;
                            return true;
                        }
                    }
                }

                MethodInfo? inst = ownerId.GetType().GetMethod("TryGetUnit", Any);
                if (inst != null)
                {
                    object?[] args2 = new object?[] { null };
                    if (inst.Invoke(ownerId, args2) is true && args2[0] != null)
                    {
                        unit = args2[0];
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryResolveUnitById soft-fail: " + ex.Message);
            }

            return false;
        }

        private static object? CoerceIdArg(object ownerId, Type expected)
        {
            if (expected.IsInstanceOfType(ownerId)) return ownerId;
            try
            {
                Type? underlying = Nullable.GetUnderlyingType(expected) ?? expected;
                if (underlying == typeof(int) || underlying == typeof(uint) || underlying == typeof(long))
                {
                    if (ownerId is int or uint or long or short or byte)
                    {
                        return Convert.ChangeType(ownerId, underlying);
                    }

                    object? nested = GameReflect.FindMember(ownerId.GetType(),
                        "id", "Id", "value", "Value")?.Get(ownerId);
                    if (nested != null) return Convert.ChangeType(nested, underlying);
                }
            }
            catch { /* soft */ }

            return ownerId;
        }

        private static bool IsHqTracking(object player, object target)
        {
            try
            {
                object? hq = DatalinkBridge.ResolveNetworkHq(player);
                if (hq == null) return false;

                foreach (MethodInfo method in GameReflect.FindMethods(hq.GetType(),
                    "IsTargetBeingTracked", "IsTracking", "IsTargetTracked", "HasTrack",
                    "IsTargetPositionAccurate"))
                {
                    ParameterInfo[] ps = method.GetParameters();
                    if (ps.Length == 1)
                    {
                        if (method.Invoke(hq, new[] { target }) is true) return true;
                    }
                    else if (ps.Length == 2 &&
                             (ps[1].ParameterType == typeof(float) || ps[1].ParameterType == typeof(double)))
                    {
                        if (method.Invoke(hq, new object[] { target, 20f }) is true) return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("IsHqTracking soft-fail: " + ex.Message);
            }

            return false;
        }

        private static bool TrySetMember(object instance, object? value, params string[] names)
        {
            try
            {
                MemberHandle? handle = GameReflect.FindMember(instance.GetType(), names);
                if (handle == null) return false;

                MemberInfo member = handle.Value.Member;
                switch (member)
                {
                    case FieldInfo field:
                        field.SetValue(field.IsStatic ? null : instance, value);
                        return true;
                    case PropertyInfo prop when prop.CanWrite:
                        prop.SetValue(prop.SetMethod != null && prop.SetMethod.IsStatic ? null : instance, value);
                        return true;
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TrySetMember soft-fail: " + ex.Message);
                return false;
            }
        }

        private static bool IsDestroyedOrNull(object? obj)
        {
            if (obj == null) return true;
            return obj is UnityEngine.Object uo && uo == null;
        }

        private static string NameOf(Type? type) => type != null ? (type.FullName ?? type.Name) : "—";
    }
}
