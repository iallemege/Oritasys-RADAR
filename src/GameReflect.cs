using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RDA
{
    internal readonly struct MemberHandle
    {
        internal MemberHandle(MemberInfo member)
        {
            Member = member;
        }

        internal MemberInfo Member { get; }

        internal object? Get(object? instance)
        {
            try
            {
                switch (Member)
                {
                    case FieldInfo field:
                        return field.GetValue(field.IsStatic ? null : instance);
                    case PropertyInfo prop:
                        return prop.GetValue(prop.GetMethod != null && prop.GetMethod.IsStatic ? null : instance);
                    case MethodInfo method when method.GetParameters().Length == 0:
                        return method.Invoke(method.IsStatic ? null : instance, Array.Empty<object>());
                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Read " + Member.Name + " failed: " + ex.Message);
                return null;
            }
        }
    }

    internal static class GameReflect
    {
        private const BindingFlags Any =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static readonly Dictionary<string, MemberHandle?> MemberCache =
            new Dictionary<string, MemberHandle?>(256);

        internal static Assembly? GameAssembly { get; private set; }
        internal static Type? Aircraft { get; private set; }
        internal static Type? Unit { get; private set; }
        internal static Type? Radar { get; private set; }
        internal static Type? TargetDetector { get; private set; }
        internal static Type? RadarParams { get; private set; }
        internal static Type? TacScreen { get; private set; }
        internal static Type? RadarWarning { get; private set; }
        internal static Type? DetectorManager { get; private set; }
        internal static Type? CombatHud { get; private set; }
        internal static Type? SceneSingleton { get; private set; }
        internal static Type? GameManager { get; private set; }
        internal static Type? FlightHud { get; private set; }
        internal static Type? PowerSupply { get; private set; }
        internal static Type? AircraftParameters { get; private set; }
        internal static Type? UnitDefinition { get; private set; }
        internal static Type? RadarParameters { get; private set; }

        internal static void Discover()
        {
            try
            {
                GameAssembly = FindGameAssembly();
                if (GameAssembly == null)
                {
                    Log.Warn("Assembly-CSharp not loaded yet (menu is OK). Types will be retried in-mission.");
                    return;
                }

                Aircraft = FindType("Aircraft");
                Unit = FindType("Unit") ?? Aircraft?.BaseType;
                Radar = FindType("Radar");
                TargetDetector = FindType("TargetDetector") ?? Radar?.BaseType;
                RadarParams = FindType("RadarParams");
                TacScreen = FindType("TacScreen");
                RadarWarning = FindType("RadarWarning");
                DetectorManager = FindType("DetectorManager") ?? FindType("NuclearOption.Jobs.DetectorManager");
                CombatHud = FindType("CombatHUD");
                SceneSingleton = FindType("SceneSingleton");
                GameManager = FindType("GameManager");
                FlightHud = FindType("FlightHud");
                PowerSupply = FindType("PowerSupply");
                AircraftParameters = FindType("AircraftParameters");
                UnitDefinition = FindType("UnitDefinition");
                RadarParameters = FindType("RadarParameters") ?? RadarParams;
                MemberCache.Clear();

                Log.Info(
                    "Types: Aircraft=" + Name(Aircraft) +
                    " Radar=" + Name(Radar) +
                    " TargetDetector=" + Name(TargetDetector) +
                    " TacScreen=" + Name(TacScreen) +
                    " RadarWarning=" + Name(RadarWarning) +
                    " DetectorManager=" + Name(DetectorManager) +
                    " CombatHUD=" + Name(CombatHud) +
                    " SceneSingleton=" + Name(SceneSingleton) +
                    " GameManager=" + Name(GameManager));
            }
            catch (Exception ex)
            {
                Log.Warn("Type discovery failed softly: " + ex.Message);
            }
        }

        internal static void EnsureDiscovered()
        {
            if (GameAssembly == null || Aircraft == null)
            {
                Discover();
            }
        }

        internal static Type? FindType(string simpleOrFullName)
        {
            if (GameAssembly != null)
            {
                Type? exact = GameAssembly.GetType(simpleOrFullName) ??
                              GameAssembly.GetType("NuclearOption." + simpleOrFullName) ??
                              GameAssembly.GetType("NuclearOption.Jobs." + simpleOrFullName);
                if (exact != null)
                {
                    return exact;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsGameAssembly(assembly))
                {
                    continue;
                }

                Type? found = FindTypeIn(assembly, simpleOrFullName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        internal static MemberHandle? FindMember(Type? type, params string[] names)
        {
            if (type == null)
            {
                return null;
            }

            string cacheKey = type.FullName + "|" + string.Join(",", names);
            if (MemberCache.TryGetValue(cacheKey, out MemberHandle? cached))
            {
                return cached;
            }

            MemberHandle? found = null;
            for (Type? walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                foreach (string name in names)
                {
                    FieldInfo? field = walk.GetField(name, Any);
                    if (field != null)
                    {
                        Log.Debug(type.Name + "." + field.Name + " (field " + field.FieldType.Name + ")");
                        found = new MemberHandle(field);
                        break;
                    }

                    PropertyInfo? prop = walk.GetProperty(name, Any);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                    {
                        Log.Debug(type.Name + "." + prop.Name + " (prop " + prop.PropertyType.Name + ")");
                        found = new MemberHandle(prop);
                        break;
                    }

                    MethodInfo? method = walk.GetMethod(name, Any, null, Type.EmptyTypes, null);
                    if (method != null && method.ReturnType != typeof(void))
                    {
                        Log.Debug(type.Name + "." + method.Name + " (method " + method.ReturnType.Name + ")");
                        found = new MemberHandle(method);
                        break;
                    }
                }

                if (found != null)
                {
                    break;
                }
            }

            MemberCache[cacheKey] = found;
            return found;
        }

        internal static MemberHandle? FindMemberFuzzy(Type? type, string[] nameParts, Type? assignableTo = null)
        {
            if (type == null)
            {
                return null;
            }

            MemberHandle? best = null;
            int bestScore = int.MaxValue;
            foreach (MemberInfo member in EnumerateMembers(type))
            {
                string name = member.Name;
                if (name.IndexOf('<') >= 0)
                {
                    continue;
                }

                Type? memberType = TypeOf(member);
                if (assignableTo != null && memberType != null && !assignableTo.IsAssignableFrom(memberType) &&
                    !typeof(IEnumerable).IsAssignableFrom(memberType))
                {
                    continue;
                }

                foreach (string part in nameParts)
                {
                    if (name.IndexOf(part, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    int score = Math.Abs(name.Length - part.Length);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = new MemberHandle(member);
                    }
                }
            }

            if (best != null)
            {
                Log.Debug("Fuzzy " + type.Name + "." + best.Value.Member.Name);
            }

            return best;
        }

        internal static IEnumerable<MemberHandle> FindEnumerableMembers(Type type)
        {
            foreach (MemberInfo member in EnumerateMembers(type))
            {
                Type? memberType = TypeOf(member);
                if (memberType == null || memberType == typeof(string))
                {
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(memberType))
                {
                    string n = member.Name;
                    if (n.IndexOf("track", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("contact", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("detect", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("return", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("scan", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        yield return new MemberHandle(member);
                    }
                }
            }
        }

        internal static IEnumerable<object> Enumerate(object? value)
        {
            if (value == null || value is string)
            {
                yield break;
            }

            if (value is IDictionary dictionary)
            {
                foreach (object item in dictionary.Values)
                {
                    if (item != null)
                    {
                        yield return item;
                    }
                }

                yield break;
            }

            if (value is IEnumerable enumerable)
            {
                IEnumerator? enumerator = null;
                try
                {
                    enumerator = enumerable.GetEnumerator();
                    while (enumerator.MoveNext())
                    {
                        object? item = enumerator.Current;
                        if (item != null)
                        {
                            yield return item;
                        }
                    }
                }
                finally
                {
                    (enumerator as IDisposable)?.Dispose();
                }
            }
        }

        internal static Component? AsComponent(object? instance)
        {
            return instance as Component;
        }

        internal static Vector3 WorldPosition(object? instance)
        {
            if (instance is Component component)
            {
                return component.transform.position;
            }

            if (instance is GameObject go)
            {
                return go.transform.position;
            }

            object? transform = FindMember(instance?.GetType(), "transform", "Transform")?.Get(instance);
            if (transform is Transform t)
            {
                return t.position;
            }

            object? pos = FindMember(instance?.GetType(), "position", "Position", "worldPosition", "WorldPosition")?.Get(instance);
            if (pos is Vector3 v)
            {
                return v;
            }

            return Vector3.zero;
        }

        internal static float HeadingDeg(object? instance)
        {
            if (instance is Component component)
            {
                return component.transform.eulerAngles.y;
            }

            object? heading = FindMember(instance?.GetType(), "heading", "Heading", "yaw", "Yaw", "headingDeg")?.Get(instance);
            if (heading is float f)
            {
                return f;
            }

            object? transform = FindMember(instance?.GetType(), "transform")?.Get(instance);
            if (transform is Transform t)
            {
                return t.eulerAngles.y;
            }

            return 0f;
        }

        internal static float SpeedMps(object? instance)
        {
            object? speed = FindMember(instance?.GetType(), "speed", "Speed", "airspeed", "velocityMagnitude")?.Get(instance);
            switch (speed)
            {
                case float f:
                    return f;
                case Vector3 v:
                    return v.magnitude;
                default:
                    if (instance is Component c)
                    {
                        return GuessRigidbodySpeed(c);
                    }

                    return 0f;
            }
        }

        internal static string LabelOf(object? instance)
        {
            if (instance == null)
            {
                return "?";
            }

            object? definition = FindMember(instance.GetType(), "definition", "Definition", "unitDefinition")?.Get(instance);
            object? defName = FindMember(definition?.GetType(), "name", "Name", "displayName", "unitName")?.Get(definition);
            if (defName != null && !string.IsNullOrEmpty(defName.ToString()))
            {
                return defName.ToString() ?? instance.ToString() ?? "?";
            }

            if (instance is UnityEngine.Object uo && !string.IsNullOrEmpty(uo.name))
            {
                return uo.name;
            }

            return instance.GetType().Name;
        }

        /// <summary>
        /// True when name/label looks like editor / camera / cockpit clutter (not a real track).
        /// </summary>
        internal static bool IsClutterLabel(string? label)
        {
            if (string.IsNullOrEmpty(label))
            {
                return false;
            }

            return ContainsIgnoreCase(label, "cockpit") ||
                   ContainsIgnoreCase(label, "Camera") ||
                   ContainsIgnoreCase(label, "TargetCam") ||
                   ContainsIgnoreCase(label, "TargetON") ||
                   ContainsIgnoreCase(label, "debug") ||
                   ContainsIgnoreCase(label, "EditorOnly");
        }

        /// <summary>Skip units whose GameObject / definition / type name is cockpit/camera clutter.</summary>
        /// <summary>
        /// Soft seat gate (Nuclear Option): local aircraft resolved AND HasEjected() not true → seated.
        /// Prefer Aircraft.HasEjected() then ejected bool. No ownship name bans (eject/parachute).
        /// Generic disabled alone is NOT left-aircraft when !HasEjected. Spectating uncertain → fail-soft SHOW
        /// when live local aircraft !HasEjected. Hide when no aircraft / HasEjected / destroyed.
        /// null = unknown → caller fail-soft seated when a live aircraft ref exists.
        /// </summary>
        internal static bool? TryIsPlayerSeatedInAircraft(object? aircraft)
        {
            if (aircraft == null)
            {
                return false;
            }

            try
            {
                if (aircraft is UnityEngine.Object uo && uo == null)
                {
                    return false;
                }

                // Primary Nuclear Option gate: HasEjected() / ejected
                bool? ejected = TryHasEjected(aircraft);
                if (ejected == true)
                {
                    return false;
                }

                // Destroyed / dead only — NOT generic disabled (avionics may flip disabled while seated)
                if (TryIsUnitDestroyedOrDead(aircraft) == true)
                {
                    return false;
                }

                // Spectating is unreliable (false positives hid MFD after board). Fail-soft SHOW when
                // we have a live local aircraft and HasEjected is not true.
                if (ejected == false)
                {
                    return true;
                }

                // HasEjected unknown: still show unless we have a clear destroy. Do not hide on spectate alone.
                return true;
            }
            catch (Exception ex)
            {
                Log.Debug("TryIsPlayerSeatedInAircraft soft-fail: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Prefer parameterless HasEjected() / hasEjected / IsEjected, then ejected bool field/prop.
        /// null = unknown (member missing or soft-fail).
        /// </summary>
        internal static bool? TryHasEjected(object? aircraft)
        {
            if (aircraft == null)
            {
                return null;
            }

            try
            {
                if (aircraft is UnityEngine.Object uo && uo == null)
                {
                    return null;
                }

                Type type = aircraft.GetType();

                // Prefer methods first (Nuclear Option: Aircraft.HasEjected())
                string[] methodNames =
                {
                    "HasEjected", "hasEjected", "IsEjected", "isEjected", "PilotEjected", "pilotEjected"
                };
                foreach (string name in methodNames)
                {
                    MethodInfo? method = null;
                    for (Type? walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
                    {
                        method = walk.GetMethod(name, Any, null, Type.EmptyTypes, null);
                        if (method != null)
                        {
                            break;
                        }
                    }

                    if (method == null || method.ReturnType == typeof(void))
                    {
                        continue;
                    }

                    object? result = method.Invoke(method.IsStatic ? null : aircraft, Array.Empty<object>());
                    if (result is bool mb)
                    {
                        return mb;
                    }
                }

                // Then bool field / property ejected
                object? ejected = FindMember(type,
                    "ejected", "Ejected", "isEjected", "IsEjected", "pilotEjected", "PilotEjected",
                    "hasEjected", "HasEjected", "bailOut", "BailOut", "bailedOut", "BailedOut")?.Get(aircraft);
                if (ejected is bool eb)
                {
                    return eb;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryHasEjected soft-fail: " + ex.Message);
            }

            return null;
        }

        /// <summary>Soft: unit destroyed / dead (NOT generic disabled). null = unknown.</summary>
        internal static bool? TryIsUnitDestroyedOrDead(object? unit)
        {
            if (unit == null)
            {
                return true;
            }

            try
            {
                if (unit is UnityEngine.Object uo && uo == null)
                {
                    return true;
                }

                object? flag = FindMember(unit.GetType(),
                    "destroyed", "Destroyed", "isDestroyed", "IsDestroyed",
                    "dead", "Dead", "isDead", "IsDead")?.Get(unit);
                if (flag is bool b)
                {
                    return b;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Soft: unit destroyed / disabled / dead. null = unknown. Prefer TryIsUnitDestroyedOrDead for seat gate.</summary>
        internal static bool? TryIsUnitDeadOrDisabled(object? unit)
        {
            bool? destroyed = TryIsUnitDestroyedOrDead(unit);
            if (destroyed.HasValue)
            {
                return destroyed;
            }

            if (unit == null)
            {
                return true;
            }

            try
            {
                // Generic disabled alone is inconclusive for seat (caller should also check !HasEjected).
                object? flag = FindMember(unit.GetType(),
                    "disabled", "Disabled", "isDisabled", "IsDisabled",
                    "unitDisabled", "UnitDisabled")?.Get(unit);
                if (flag is bool b && b)
                {
                    // Only treat as dead when also ejected / destroyed unknown — keep null so seat fail-softs.
                    return null;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Soft: GameManager / CombatHUD spectator or freecam.</summary>
        internal static bool TryIsSpectating()
        {
            try
            {
                EnsureDiscovered();
                foreach (Type? t in new Type?[] { GameManager, CombatHud, SceneSingleton })
                {
                    if (t == null)
                    {
                        continue;
                    }

                    // Static / singleton flags
                    object? inst = null;
                    try
                    {
                        MemberHandle? sing = FindMember(t, "i", "Instance", "instance", "I");
                        inst = sing?.Get(null);
                    }
                    catch
                    {
                        inst = null;
                    }

                    object? probe = inst;
                    if (probe == null)
                    {
                        // Try static bools on the type itself
                        object? st = FindMember(t,
                            "isSpectating", "IsSpectating", "spectating", "Spectating",
                            "inSpectator", "InSpectator", "spectatorMode", "SpectatorMode",
                            "isObserver", "IsObserver")?.Get(null);
                        if (st is bool sb && sb)
                        {
                            return true;
                        }

                        continue;
                    }

                    object? flag = FindMember(probe.GetType(),
                        "isSpectating", "IsSpectating", "spectating", "Spectating",
                        "inSpectator", "InSpectator", "spectatorMode", "SpectatorMode",
                        "isObserver", "IsObserver", "freeCam", "FreeCam")?.Get(probe);
                    if (flag is bool b && b)
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("TryIsSpectating soft-fail: " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Soft-enumerate FactionHQ.missileAttacks (or similar) entries that target ownship.
        /// Yields attacker/missile units when reflection finds them. Fail-soft empty.
        /// </summary>
        internal static System.Collections.Generic.IEnumerable<object> EnumerateMissileAttacksOn(
            object? hq, object? ownship)
        {
            if (hq == null || ownship == null)
            {
                yield break;
            }

            object? bag = null;
            try
            {
                bag = FindMember(hq.GetType(),
                    "missileAttacks", "MissileAttacks", "missileAttackDatabase",
                    "MissileAttackDatabase", "incomingMissiles", "IncomingMissiles",
                    "activeMissileAttacks", "ActiveMissileAttacks")?.Get(hq);
            }
            catch
            {
                bag = null;
            }

            if (bag == null)
            {
                yield break;
            }

            int ownId = IdOf(ownship);
            foreach (object entry in Enumerate(bag))
            {
                if (entry == null)
                {
                    continue;
                }

                object? target = null;
                object? attacker = null;
                try
                {
                    target = FindMember(entry.GetType(),
                        "target", "Target", "victim", "Victim", "attackedUnit", "AttackedUnit")?.Get(entry);
                    attacker = FindMember(entry.GetType(),
                        "missile", "Missile", "attacker", "Attacker", "owner", "Owner",
                        "emitter", "Emitter", "source", "Source", "unit", "Unit")?.Get(entry);
                }
                catch
                {
                    continue;
                }

                bool targetsUs = false;
                if (target != null)
                {
                    if (ReferenceEquals(target, ownship) || IdOf(target) == ownId)
                    {
                        targetsUs = true;
                    }
                }
                else
                {
                    // Some bags are just missile units already aimed at us — keep if looks like missile
                    string n = entry.GetType().Name ?? string.Empty;
                    if (n.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        targetsUs = true;
                        attacker = entry;
                    }
                }

                if (!targetsUs)
                {
                    continue;
                }

                if (attacker != null)
                {
                    yield return attacker;
                }
                else if (entry != null)
                {
                    yield return entry;
                }
            }
        }

        internal static bool IsClutterUnit(object? instance)
        {
            if (instance == null)
            {
                return false;
            }

            try
            {
                if (instance is UnityEngine.Object uo)
                {
                    if (uo == null)
                    {
                        return true;
                    }

                    if (IsClutterLabel(uo.name))
                    {
                        return true;
                    }
                }

                if (IsClutterLabel(instance.GetType().Name) || IsClutterLabel(instance.GetType().FullName))
                {
                    return true;
                }

                if (IsClutterLabel(LabelOf(instance)))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        /// <summary>
        /// High-value strategic / infrastructure unit (HQ strategic flag, Building/Airbase/etc.).
        /// </summary>
        internal static bool IsHighValueUnit(object? instance, string? label = null, ContactKind kind = ContactKind.Unknown)
        {
            string blob = (label ?? string.Empty) + " " + kind;
            if (instance != null)
            {
                try
                {
                    blob += " " + LabelOf(instance) + " " + instance.GetType().Name;
                }
                catch
                {
                    // ignored
                }
            }

            if (ContainsIgnoreCase(blob, "Building") ||
                ContainsIgnoreCase(blob, "Airbase") ||
                ContainsIgnoreCase(blob, "RadarStation") ||
                ContainsIgnoreCase(blob, "Factory") ||
                ContainsIgnoreCase(blob, "Headquarters") ||
                ContainsIgnoreCase(blob, "Carrier") ||
                ContainsIgnoreCase(blob, "SAMSite") ||
                ContainsIgnoreCase(blob, "large"))
            {
                return true;
            }

            if (instance == null)
            {
                return false;
            }

            try
            {
                object? typeId = FindMember(instance.GetType(),
                    "typeIdentity", "TypeIdentity", "identity", "Identity", "unitIdentity")?.Get(instance);
                float strategic = ReadFloat(typeId, "strategic", "Strategic", "strategicValue", "StrategicValue", "value");
                if (!float.IsNaN(strategic) && strategic > 0f)
                {
                    return true;
                }

                object? definition = FindMember(instance.GetType(),
                    "definition", "Definition", "unitDefinition")?.Get(instance);
                float defStrat = ReadFloat(definition, "strategic", "Strategic", "strategicValue");
                if (!float.IsNaN(defStrat) && defStrat > 0f)
                {
                    return true;
                }

                object? nested = FindMember(definition?.GetType(),
                    "typeIdentity", "TypeIdentity", "identity")?.Get(definition);
                float nestedStrat = ReadFloat(nested, "strategic", "Strategic", "strategicValue");
                if (!float.IsNaN(nestedStrat) && nestedStrat > 0f)
                {
                    return true;
                }

                // HQ / faction HQ objects themselves
                string tn = instance.GetType().Name;
                if (ContainsIgnoreCase(tn, "FactionHQ") || ContainsIgnoreCase(tn, "Headquarters"))
                {
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        internal static bool ContainsIgnoreCase(string? haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
            {
                return false;
            }

            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static int IdOf(object? instance)
        {
            object? id = FindMember(instance?.GetType(), "persistentID", "PersistentID", "id", "Id", "unitID", "netId")?.Get(instance);
            switch (id)
            {
                case int i:
                    return i;
                case uint u:
                    return unchecked((int)u);
                case short s:
                    return s;
                default:
                    return instance?.GetHashCode() ?? 0;
            }
        }

        internal static object? FactionOf(object? instance)
        {
            return FindMember(instance?.GetType(),
                "NetworkHQ", "networkHQ", "hq", "HQ", "faction", "Faction", "team", "Team", "side", "Side",
                "owner", "Owner", "country")?.Get(instance);
        }

        internal static IffRelation CompareIff(object? player, object? other)
        {
            if (player == null || other == null)
            {
                return IffRelation.Unknown;
            }

            object? a = FactionOf(player);
            object? b = FactionOf(other);
            if (a == null || b == null)
            {
                return IffRelation.Unknown;
            }

            if (ReferenceEquals(a, b) || Equals(a, b))
            {
                return IffRelation.Friend;
            }

            string sa = a.ToString() ?? string.Empty;
            string sb = b.ToString() ?? string.Empty;
            if (sa.Length > 0 && string.Equals(sa, sb, StringComparison.OrdinalIgnoreCase))
            {
                return IffRelation.Friend;
            }

            object? idA = FindMember(a.GetType(), "id", "Id", "teamID", "factionID")?.Get(a);
            object? idB = FindMember(b.GetType(), "id", "Id", "teamID", "factionID")?.Get(b);
            if (idA != null && idB != null && Equals(idA, idB))
            {
                return IffRelation.Friend;
            }

            return IffRelation.Foe;
        }

        internal static ContactKind KindOf(object? instance)
        {
            if (instance == null)
            {
                return ContactKind.Unknown;
            }

            string typeName = instance.GetType().Name;
            if (typeName.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ContactKind.Missile;
            }

            if (Aircraft != null && Aircraft.IsInstanceOfType(instance))
            {
                return ContactKind.Air;
            }

            if (typeName.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Naval", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ContactKind.Naval;
            }

            if (typeName.IndexOf("Aircraft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                typeName.IndexOf("Helicopter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return ContactKind.Air;
            }

            return ContactKind.Ground;
        }

        internal static UnityEngine.Object[] FindAll(Type type)
        {
            try
            {
                return Resources.FindObjectsOfTypeAll(type);
            }
            catch (Exception ex)
            {
                Log.Debug("FindAll " + type.Name + " failed: " + ex.Message);
                return Array.Empty<UnityEngine.Object>();
            }
        }

        internal static IEnumerable<MethodInfo> FindMethods(Type? type, params string[] names)
        {
            if (type == null)
            {
                yield break;
            }

            HashSet<string> seen = new HashSet<string>();
            for (Type? walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                foreach (MethodInfo method in walk.GetMethods(Any))
                {
                    foreach (string name in names)
                    {
                        if (string.Equals(method.Name, name, StringComparison.Ordinal) ||
                            method.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string key = method.DeclaringType?.FullName + "." + method.Name + method.ToString();
                            if (seen.Add(key))
                            {
                                yield return method;
                            }
                        }
                    }
                }
            }
        }

        internal static float ReadFloat(object? instance, params string[] names)
        {
            object? value = FindMember(instance?.GetType(), names)?.Get(instance);
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

        internal static bool ReadBool(object? instance, bool fallback, params string[] names)
        {
            object? value = FindMember(instance?.GetType(), names)?.Get(instance);
            return value is bool b ? b : fallback;
        }

        /// <summary>
        /// Best-effort aircraft identity key: UnitDefinition.jsonKey / unitName / code,
        /// AircraftParameters.aircraftName, then GameObject.name.
        /// </summary>
        internal static string ResolveAircraftKey(object? aircraft)
        {
            if (aircraft == null)
            {
                return string.Empty;
            }

            object? definition = FindMember(aircraft.GetType(),
                "definition", "Definition", "unitDefinition", "UnitDefinition")?.Get(aircraft);
            if (definition != null)
            {
                string fromDef = FirstNonEmptyString(definition,
                    "jsonKey", "JsonKey", "unitName", "UnitName", "code", "Code",
                    "name", "Name", "displayName", "DisplayName");
                if (!string.IsNullOrEmpty(fromDef))
                {
                    return fromDef;
                }
            }

            object? parameters = FindMember(aircraft.GetType(),
                "parameters", "Parameters", "aircraftParameters", "AircraftParameters",
                "aircraftParams", "AircraftParams")?.Get(aircraft);
            if (parameters != null)
            {
                string fromParams = FirstNonEmptyString(parameters,
                    "aircraftName", "AircraftName", "name", "Name", "unitName", "code");
                if (!string.IsNullOrEmpty(fromParams))
                {
                    return fromParams;
                }
            }

            // Nested definition on parameters.
            object? nestedDef = FindMember(parameters?.GetType(),
                "definition", "Definition", "unitDefinition")?.Get(parameters);
            if (nestedDef != null)
            {
                string nested = FirstNonEmptyString(nestedDef,
                    "jsonKey", "unitName", "code", "name", "displayName");
                if (!string.IsNullOrEmpty(nested))
                {
                    return nested;
                }
            }

            if (aircraft is UnityEngine.Object uo && !string.IsNullOrEmpty(uo.name))
            {
                return uo.name;
            }

            object? goName = FindMember(aircraft.GetType(), "name", "Name")?.Get(aircraft);
            if (goName != null && !string.IsNullOrEmpty(goName.ToString()))
            {
                return goName.ToString() ?? string.Empty;
            }

            return LabelOf(aircraft);
        }


        /// <summary>
        /// Soft-reflect whether local aircraft engines are running.
        /// Returns null when unknown (caller must treat as ON — fail-soft).
        /// Do NOT treat idle throttle≈0 as engine off; prefer component running/rpm/n1 flags.
        /// Only return false when an engine collection exists and ALL probed engines are clearly stopped.
        /// </summary>
        internal static bool? TryIsEngineRunning(object? aircraft)
        {
            if (aircraft == null)
            {
                return null;
            }

            try
            {
                // Explicit aircraft-level engine-running flags (not generic "powered").
                object? flag = FindMember(aircraft.GetType(),
                    "enginesRunning", "EnginesRunning", "engineRunning", "EngineRunning",
                    "isEngineRunning", "IsEngineRunning", "enginesOn", "EnginesOn")?.Get(aircraft);
                if (flag is bool fb)
                {
                    return fb;
                }

                object? started = FindMember(aircraft.GetType(),
                    "started", "Started", "isStarted", "IsStarted", "ignition", "Ignition",
                    "ignited", "Ignited")?.Get(aircraft);
                if (started is bool sb)
                {
                    return sb;
                }

                // Prefer engine component collection — idle throttle is NOT off.
                object? engines = FindMember(aircraft.GetType(),
                    "engines", "Engines", "engine", "Engine", "powerplants", "Powerplants",
                    "jetEngines", "JetEngines")?.Get(aircraft);

                // Also try GetComponentsInChildren for Engine-like types when member missing.
                if (engines == null)
                {
                    engines = TryFindEngineComponents(aircraft);
                }

                if (engines != null)
                {
                    if (engines is System.Collections.IEnumerable enumerable && engines is not string)
                    {
                        int probed = 0;
                        int clearlyOff = 0;
                        int clearlyOn = 0;
                        foreach (object? eng in enumerable)
                        {
                            if (eng == null)
                            {
                                continue;
                            }

                            bool? st = ProbeSingleEngine(eng);
                            if (!st.HasValue)
                            {
                                continue;
                            }

                            probed++;
                            if (st.Value)
                            {
                                clearlyOn++;
                            }
                            else
                            {
                                clearlyOff++;
                            }
                        }

                        if (clearlyOn > 0)
                        {
                            return true;
                        }

                        // Only false when we saw engines and every probed one is clearly stopped.
                        if (probed > 0 && clearlyOff == probed)
                        {
                            return false;
                        }

                        // Ambiguous collection → unknown (fail-soft ON).
                        return null;
                    }

                    bool? single = ProbeSingleEngine(engines);
                    if (single.HasValue)
                    {
                        return single;
                    }
                }

                // PowerSupply alone is ambiguous — do not treat "powered" as engine running.
                // (Electrical bus can be live with engines shut down, or reflection may be wrong.)
                // Intentionally skip supply.powered / aircraft.powered here.
            }
            catch (System.Exception ex)
            {
                Log.Debug("TryIsEngineRunning soft-fail: " + ex.Message);
            }

            return null;
        }

        private static object? TryFindEngineComponents(object aircraft)
        {
            Component? comp = AsComponent(aircraft);
            if (comp == null)
            {
                return null;
            }

            try
            {
                Component[] all = comp.GetComponentsInChildren<Component>(true);
                var list = new System.Collections.Generic.List<object>();
                for (int i = 0; i < all.Length; i++)
                {
                    Component c = all[i];
                    if (c == null)
                    {
                        continue;
                    }

                    string n = c.GetType().Name;
                    if (ContainsIgnoreCase(n, "Engine") || ContainsIgnoreCase(n, "Powerplant") ||
                        ContainsIgnoreCase(n, "Turbine") || ContainsIgnoreCase(n, "Jet"))
                    {
                        // Skip electrical PowerSupply mis-hits.
                        if (ContainsIgnoreCase(n, "PowerSupply") || ContainsIgnoreCase(n, "Weapon"))
                        {
                            continue;
                        }

                        list.Add(c);
                    }
                }

                return list.Count > 0 ? list : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Probe one engine: running/isRunning/Started/ignited, or spool/n1/rpm above threshold.
        /// Throttle alone is never used as an off signal (idle ≈ 0 while spooled).
        /// </summary>
        private static bool? ProbeSingleEngine(object eng)
        {
            object? v = FindMember(eng.GetType(),
                "running", "Running", "isRunning", "IsRunning",
                "started", "Started", "isStarted", "IsStarted",
                "ignited", "Ignited", "ignition", "Ignition")?.Get(eng);
            if (v is bool b)
            {
                return b;
            }

            // Spool / N1 / RPM — definitive when present.
            float rpm = ReadFloat(eng,
                "n1", "N1", "rpm", "Rpm", "RPM", "spool", "Spool",
                "spoolRatio", "SpoolRatio", "coreRpm", "CoreRpm",
                "output", "Output", "thrust", "Thrust");
            if (!float.IsNaN(rpm))
            {
                // Normalized 0–1 spool or absolute RPM. Idle spool often ~0.2–0.6.
                if (rpm > 5f)
                {
                    // Absolute RPM scale
                    return rpm > 50f;
                }

                return rpm > 0.05f;
            }

            // Throttle input alone: never declare OFF (idle throttle≈0 with engines running).
            // If throttle is clearly advanced, treat as ON hint; otherwise unknown.
            float thr = ReadFloat(eng, "throttle", "Throttle", "throttleInput", "ThrottleInput");
            if (!float.IsNaN(thr) && thr > 0.05f)
            {
                return true;
            }

            return null;
        }

        /// <summary>
        /// Read Nuclear Option airframe unlock rank from AircraftDefinition / AircraftParameters.rankRequired.
        /// This is the airframe class proxy for AESA (rank ≥ 3), NOT the player's PlayerRank.
        /// </summary>
        internal static bool TryGetAircraftRankRequired(object? aircraft, out int rankRequired)
        {
            rankRequired = 0;
            if (aircraft == null)
            {
                return false;
            }

            try
            {
                // Aircraft.definition → UnitDefinition / AircraftDefinition
                object? definition = FindMember(aircraft.GetType(),
                    "definition", "Definition", "unitDefinition", "UnitDefinition",
                    "aircraftDefinition", "AircraftDefinition")?.Get(aircraft);

                // Prefer definition.aircraftParameters.rankRequired
                object? parameters = FindMember(definition?.GetType(),
                    "aircraftParameters", "AircraftParameters", "parameters", "Parameters",
                    "aircraftParams", "AircraftParams")?.Get(definition);

                if (parameters == null)
                {
                    parameters = FindMember(aircraft.GetType(),
                        "aircraftParameters", "AircraftParameters", "parameters", "Parameters",
                        "aircraftParams", "AircraftParams")?.Get(aircraft);
                }

                // Some builds nest parameters under definition.unitParameters / etc.
                if (parameters == null && definition != null)
                {
                    parameters = FindMember(definition.GetType(),
                        "unitParameters", "UnitParameters", "stats", "Stats")?.Get(definition);
                }

                float raw = FirstFiniteFloat(ReadFloat(parameters,
                    "rankRequired", "RankRequired", "aircraftRank", "AircraftRank",
                    "unlockRank", "UnlockRank", "techRank", "TechRank"));
                if (raw >= 1f)
                {
                    rankRequired = Mathf.Clamp(Mathf.RoundToInt(raw), 1, 20);
                    return true;
                }

                // Direct on definition
                raw = FirstFiniteFloat(ReadFloat(definition,
                    "rankRequired", "RankRequired", "unlockRank", "UnlockRank"));
                if (raw >= 1f)
                {
                    rankRequired = Mathf.Clamp(Mathf.RoundToInt(raw), 1, 20);
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("rankRequired soft-fail: " + ex.Message);
            }

            return false;
        }

        /// <summary>
        /// Read PowerSupply.maxCharge (电容量) and maxPower from a child component or member.
        /// </summary>
        internal static bool TryGetPowerSupply(object? aircraft, out float maxCharge, out float maxPower)
        {
            maxCharge = 0f;
            maxPower = 0f;
            if (aircraft == null)
            {
                return false;
            }

            object? supply = null;
            Component? comp = AsComponent(aircraft);
            if (comp != null && PowerSupply != null)
            {
                try
                {
                    supply = comp.GetComponentInChildren(PowerSupply, true);
                }
                catch (Exception ex)
                {
                    Log.Debug("PowerSupply GetComponent soft-fail: " + ex.Message);
                }
            }

            if (supply == null)
            {
                supply = FindMember(aircraft.GetType(),
                    "powerSupply", "PowerSupply", "electrical", "Electrical",
                    "battery", "Battery", "power", "Power")?.Get(aircraft);
            }

            // Sometimes nested under a systems / avionics holder.
            if (supply == null)
            {
                object? systems = FindMember(aircraft.GetType(),
                    "systems", "Systems", "avionics", "Avionics")?.Get(aircraft);
                supply = FindMember(systems?.GetType(),
                    "powerSupply", "PowerSupply", "electrical")?.Get(systems);
            }

            if (supply == null)
            {
                return false;
            }

            maxCharge = FirstFiniteFloat(ReadFloat(supply,
                "maxCharge", "MaxCharge", "capacity", "Capacity", "chargeCapacity", "maxEnergy"));
            maxPower = FirstFiniteFloat(ReadFloat(supply,
                "maxPower", "MaxPower", "powerOutput", "PowerOutput", "continuousPower", "maxOutput"));

            // Nested Battery / capacitor on the supply object.
            if (maxCharge <= 0f)
            {
                object? battery = FindMember(supply.GetType(),
                    "battery", "Battery", "capacitor", "Capacitor", "storage")?.Get(supply);
                if (battery != null)
                {
                    float nested = FirstFiniteFloat(ReadFloat(battery,
                        "maxCharge", "MaxCharge", "capacity", "Capacity"));
                    if (nested > 0f)
                    {
                        maxCharge = nested;
                    }
                }
            }

            return maxCharge > 0f || maxPower > 0f;
        }

        /// <summary>
        /// Native Radar.radarCone (full cone degrees, when present) and RadarParameters.maxRange.
        /// </summary>
        internal static bool TryGetNativeRadarStats(object? aircraft, out float radarConeDeg, out float maxRangeMeters)
        {
            radarConeDeg = float.NaN;
            maxRangeMeters = float.NaN;
            if (aircraft == null)
            {
                return false;
            }

            object? radar = null;
            Component? comp = AsComponent(aircraft);
            if (comp != null)
            {
                Type? radarType = Radar ?? TargetDetector;
                if (radarType != null)
                {
                    try
                    {
                        radar = comp.GetComponentInChildren(radarType, true);
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Radar GetComponent soft-fail: " + ex.Message);
                    }
                }
            }

            if (radar == null)
            {
                radar = FindMember(aircraft.GetType(),
                    "radar", "Radar", "targetDetector", "TargetDetector", "airRadar")?.Get(aircraft);
            }

            if (radar == null)
            {
                return false;
            }

            float cone = ReadFloat(radar,
                "radarCone", "RadarCone", "cone", "coneAngle", "horizontalFov", "fov", "FOV");
            object? param = FindMember(radar.GetType(),
                "RadarParams", "radarParams", "RadarParameters", "radarParameters",
                "params", "Params", "parameters", "Parameters")?.Get(radar);
            if (float.IsNaN(cone) || cone <= 0f)
            {
                cone = ReadFloat(param, "radarCone", "RadarCone", "cone", "coneAngle", "fov", "FOV");
            }

            float range = ReadFloat(radar,
                "maxRange", "MaxRange", "range", "Range", "radarRange", "detectionRange");
            if (float.IsNaN(range) || range <= 0f)
            {
                range = ReadFloat(param, "maxRange", "MaxRange", "range", "Range", "radarRange");
            }

            bool any = false;
            if (!float.IsNaN(cone) && cone > 0f)
            {
                radarConeDeg = cone;
                any = true;
            }

            if (!float.IsNaN(range) && range > 0f)
            {
                maxRangeMeters = range > 500f ? range : range * 1000f;
                any = true;
            }

            return any;
        }

        private static string FirstNonEmptyString(object instance, params string[] names)
        {
            object? value = FindMember(instance.GetType(), names)?.Get(instance);
            if (value == null)
            {
                return string.Empty;
            }

            string? s = value.ToString();
            return string.IsNullOrEmpty(s) ? string.Empty : s.Trim();
        }

        private static float FirstFiniteFloat(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
            {
                return 0f;
            }

            return value;
        }

        private static Assembly? FindGameAssembly()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == "Assembly-CSharp")
                {
                    return assembly;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (IsGameAssembly(assembly) && FindTypeIn(assembly, "Aircraft") != null)
                {
                    return assembly;
                }
            }

            return null;
        }

        private static bool IsGameAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name ?? string.Empty;
            if (name == "Assembly-CSharp" || name.IndexOf("NuclearOption", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return false;
        }

        private static Type? FindTypeIn(Assembly assembly, string simpleOrFullName)
        {
            Type? direct = assembly.GetType(simpleOrFullName);
            if (direct != null)
            {
                return direct;
            }

            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }

            foreach (Type? type in types)
            {
                if (type == null)
                {
                    continue;
                }

                if (string.Equals(type.Name, simpleOrFullName, StringComparison.Ordinal) ||
                    string.Equals(type.FullName, simpleOrFullName, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            return null;
        }

        private static IEnumerable<MemberInfo> EnumerateMembers(Type type)
        {
            for (Type? walk = type; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                foreach (FieldInfo field in walk.GetFields(Any))
                {
                    yield return field;
                }

                foreach (PropertyInfo prop in walk.GetProperties(Any))
                {
                    if (prop.GetIndexParameters().Length == 0)
                    {
                        yield return prop;
                    }
                }
            }
        }

        private static Type? TypeOf(MemberInfo member) => member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo prop => prop.PropertyType,
            MethodInfo method => method.ReturnType,
            _ => null
        };

        private static float GuessRigidbodySpeed(Component component)
        {
            try
            {
                Rigidbody body = component.GetComponent<Rigidbody>();
                return body != null ? body.velocity.magnitude : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        private static string Name(Type? type) => type != null ? type.FullName ?? type.Name : "—";
    }
}
