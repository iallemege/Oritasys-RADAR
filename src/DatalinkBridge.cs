using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace RDA
{
    /// <summary>
    /// Soft reflection bridge to Nuclear Option faction datalink:
    /// Aircraft.NetworkHQ → FactionHQ.trackingDatabase (TrackingInfo),
    /// plus RpcUpdateTrackingInfo / CmdUpdateTrackingInfo contribute path.
    /// </summary>
    internal static class DatalinkBridge
    {
        private const BindingFlags Any =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private const float ContributeIntervalSec = 0.6f; // ~1.5 Hz per id
        private const int MaxContributePerTick = 4;

        private static readonly Dictionary<int, float> LastContributeById = new Dictionary<int, float>(32);
        private static readonly Dictionary<int, Vector3> CachedVelocityById = new Dictionary<int, Vector3>(64);

        private static Type? _factionHqType;
        private static Type? _trackingInfoType;
        private static bool _typesProbed;
        private static bool _loggedHq;
        private static bool _loggedDb;
        private static bool _contributeLogged;
        private static MethodInfo? _rpcUpdate;
        private static MethodInfo? _cmdUpdate;
        private static bool _updateMethodsProbed;

        internal static int LastIngestCount { get; private set; }

        internal static void EnsureTypes()
        {
            if (_typesProbed && _factionHqType != null)
            {
                return;
            }

            _typesProbed = true;
            GameReflect.EnsureDiscovered();
            _factionHqType = GameReflect.FindType("FactionHQ");
            _trackingInfoType = GameReflect.FindType("TrackingInfo");
            if (_factionHqType != null)
            {
                Log.Debug("Datalink FactionHQ=" + (_factionHqType.FullName ?? _factionHqType.Name));
            }
        }

        internal static object? ResolveNetworkHq(object? aircraft)
        {
            if (aircraft == null)
            {
                return null;
            }

            EnsureTypes();
            object? hq = GameReflect.FindMember(aircraft.GetType(), "NetworkHQ", "networkHQ", "NetworkHq")?.Get(aircraft);
            if (hq == null)
            {
                return null;
            }

            if (!_loggedHq)
            {
                _loggedHq = true;
                Log.Info("Datalink HQ via " + hq.GetType().FullName);
            }

            return hq;
        }

        /// <summary>
        /// Enumerate HQ trackingDatabase into RadarContact list (Source=datalink).
        /// Skips friendly same-HQ units and self. Fail-soft.
        /// </summary>
        internal static void IngestTracks(
            object? player,
            object? hq,
            Vector3 ownshipPos,
            float ownshipHeadingDeg,
            float ownshipSpeedMps,
            List<RadarContact> into)
        {
            LastIngestCount = 0;
            if (player == null || hq == null || !Config.UseVanillaDatalink.Value)
            {
                return;
            }

            try
            {
                EnsureTypes();
                object? db = GameReflect.FindMember(hq.GetType(),
                    "trackingDatabase", "TrackingDatabase", "trackingDb", "TrackingDb")?.Get(hq);
                if (db == null)
                {
                    return;
                }

                if (!_loggedDb)
                {
                    _loggedDb = true;
                    Log.Info("Datalink trackingDatabase " + db.GetType().Name);
                }

                float freshSec = Mathf.Max(0.5f, Config.DatalinkFreshSec.Value);
                float coastSec = Mathf.Max(freshSec, Config.DatalinkCoastSec.Value);
                float maxAge = Mathf.Max(coastSec, Config.DatalinkMaxAgeSec.Value);
                float noiseBase = Mathf.Max(0f, Config.DatalinkPositionNoiseMeters.Value);
                float now = Time.time;
                int selfId = GameReflect.IdOf(player);
                object? playerHq = hq;

                foreach (object info in GameReflect.Enumerate(db))
                {
                    try
                    {
                        if (!TryBuildDatalinkContact(
                                info, player, playerHq, selfId, ownshipPos, ownshipHeadingDeg, ownshipSpeedMps,
                                freshSec, coastSec, maxAge, noiseBase, now, out RadarContact? contact) ||
                            contact == null)
                        {
                            continue;
                        }

                        into.Add(contact);
                        LastIngestCount++;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Datalink track soft-fail: " + ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Datalink ingest soft-fail: " + ex.Message);
            }
        }

        private static bool TryBuildDatalinkContact(
            object info,
            object player,
            object playerHq,
            int selfId,
            Vector3 ownshipPos,
            float ownshipHeadingDeg,
            float ownshipSpeedMps,
            float freshSec,
            float coastSec,
            float maxAge,
            float noiseBase,
            float now,
            out RadarContact? contact)
        {
            contact = null;
            Type infoType = info.GetType();

            float age = ReadTrackAgeSec(info, infoType, now);
            if (age > maxAge || float.IsNaN(age))
            {
                return false;
            }

            object? unit = TryGetUnit(info, infoType);
            int id = ReadTrackId(info, infoType, unit);
            if (id == 0 || id == selfId)
            {
                return false;
            }

            if (unit != null && ReferenceEquals(unit, player))
            {
                return false;
            }

            // Skip friendly same-HQ units (and anything IFF-Friend vs player).
            if (unit != null)
            {
                object? unitHq = GameReflect.FactionOf(unit);
                if (unitHq != null && (ReferenceEquals(unitHq, playerHq) || Equals(unitHq, playerHq)))
                {
                    return false;
                }

                if (GameReflect.CompareIff(player, unit) == IffRelation.Friend)
                {
                    return false;
                }
            }

            bool observed = IsObserved(info, infoType, age, freshSec);
            PositionQuality quality;
            Vector3 world;
            Vector3 velocity = Vector3.zero;
            float speed = 0f;
            float heading = 0f;

            if (observed && unit != null)
            {
                quality = PositionQuality.Fresh;
                world = GameReflect.WorldPosition(unit);
                velocity = ReadVelocity(unit);
                speed = velocity.sqrMagnitude > 0.01f ? velocity.magnitude : GameReflect.SpeedMps(unit);
                heading = GameReflect.HeadingDeg(unit);
                if (velocity.sqrMagnitude > 0.01f)
                {
                    CachedVelocityById[id] = velocity;
                }
            }
            else
            {
                Vector3 lastKnown = ReadLastKnownPosition(info, infoType);
                if (lastKnown.sqrMagnitude < 0.0001f && unit != null)
                {
                    lastKnown = GameReflect.WorldPosition(unit);
                }

                CachedVelocityById.TryGetValue(id, out Vector3 cachedVel);
                Vector3 infoVel = ReadVelocityFromInfo(info, infoType);
                if (infoVel.sqrMagnitude > 0.01f)
                {
                    cachedVel = infoVel;
                    CachedVelocityById[id] = cachedVel;
                }

                if (age <= coastSec)
                {
                    quality = PositionQuality.Coast;
                    world = lastKnown;
                    if (cachedVel.sqrMagnitude > 0.01f)
                    {
                        // Coast from last spot: age beyond fresh window.
                        float coastAge = Mathf.Max(0f, age - Mathf.Min(age, freshSec));
                        // Prefer coasting full age when we never had live unit this tick.
                        if (!observed)
                        {
                            coastAge = age;
                        }

                        world = lastKnown + cachedVel * coastAge;
                        velocity = cachedVel;
                        speed = cachedVel.magnitude;
                        heading = Mathf.Atan2(cachedVel.x, cachedVel.z) * Mathf.Rad2Deg;
                    }
                    else if (unit != null)
                    {
                        velocity = ReadVelocity(unit);
                        speed = GameReflect.SpeedMps(unit);
                        heading = GameReflect.HeadingDeg(unit);
                    }

                    if (noiseBase > 0f)
                    {
                        float grow = Mathf.Clamp01(age / Mathf.Max(0.01f, coastSec));
                        world += StableNoiseOffset(id, now, noiseBase * grow);
                    }
                }
                else
                {
                    quality = PositionQuality.Stale;
                    world = lastKnown;
                    if (noiseBase > 0f)
                    {
                        float grow = Mathf.Clamp01(age / Mathf.Max(0.01f, maxAge));
                        world += StableNoiseOffset(id, now, noiseBase * (0.5f + grow));
                    }

                    if (cachedVel.sqrMagnitude > 0.01f)
                    {
                        velocity = cachedVel;
                        speed = cachedVel.magnitude;
                        heading = Mathf.Atan2(cachedVel.x, cachedVel.z) * Mathf.Rad2Deg;
                    }
                }
            }

            Vector3 delta = world - ownshipPos;
            float range = delta.magnitude;
            if (range < 1f)
            {
                return false;
            }

            float absBearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            float az = ContactProvider.Normalize180(absBearing - ownshipHeadingDeg);
            float elev = Mathf.Asin(Mathf.Clamp(delta.y / range, -1f, 1f)) * Mathf.Rad2Deg;

            Vector3 los = delta / range;
            Vector3 ownVel = Quaternion.Euler(0f, ownshipHeadingDeg, 0f) * Vector3.forward * ownshipSpeedMps;
            Vector3 tgtVel = velocity.sqrMagnitude > 0.01f
                ? velocity
                : Quaternion.Euler(0f, heading, 0f) * Vector3.forward * speed;
            float closing = Vector3.Dot(ownVel - tgtVel, los);
            float bearingToOwnFromTgt = Mathf.Atan2(-delta.x, -delta.z) * Mathf.Rad2Deg;
            float aspect = Mathf.Abs(ContactProvider.Normalize180(bearingToOwnFromTgt - heading));

            IffRelation iff = unit != null ? GameReflect.CompareIff(player, unit) : IffRelation.Unknown;
            // HQ tracks of non-friend same-HQ-skipped are typically foes when unknown.
            if (iff == IffRelation.Unknown)
            {
                iff = IffRelation.Foe;
            }

            string label = unit != null ? GameReflect.LabelOf(unit) : "DL#" + id;
            if (GameReflect.IsClutterLabel(label) || (unit != null && GameReflect.IsClutterUnit(unit)))
            {
                return false;
            }

            ContactKind kind = unit != null ? GameReflect.KindOf(unit) : ContactKind.Air;

            contact = new RadarContact
            {
                Id = id,
                Label = label,
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
                Iff = iff,
                Kind = kind,
                Source = "datalink",
                Raw = unit ?? info,
                FromDatalink = true,
                TrackAgeSec = age,
                PositionQuality = quality
            };
            return true;
        }

        /// <summary>
        /// Merge DL contacts into existing list: prefer own-radar / harmony / scene over DL for same Id.
        /// </summary>
        internal static void MergePreferOwnRadar(List<RadarContact> contacts, List<RadarContact> datalink)
        {
            if (datalink.Count == 0)
            {
                return;
            }

            HashSet<int> existing = new HashSet<int>();
            for (int i = 0; i < contacts.Count; i++)
            {
                existing.Add(contacts[i].Id);
            }

            bool onlyOutside = Config.ShowDatalinkOnlyOutsideCone.Value;
            for (int i = 0; i < datalink.Count; i++)
            {
                RadarContact dl = datalink[i];
                if (existing.Contains(dl.Id))
                {
                    continue; // own-radar wins
                }

                if (onlyOutside && dl.InCone)
                {
                    continue;
                }

                contacts.Add(dl);
                existing.Add(dl.Id);
            }
        }

        /// <summary>
        /// Push own detections into vanilla HQ datalink (fail-soft, ~1–2 Hz per id).
        /// </summary>
        internal static void ContributeTracks(object? hq, IReadOnlyList<RadarContact> contacts)
        {
            if (hq == null || !Config.ContributeToDatalink.Value)
            {
                return;
            }

            try
            {
                EnsureUpdateMethods(hq.GetType());
                MethodInfo? method = _rpcUpdate ?? _cmdUpdate;
                if (method == null)
                {
                    return;
                }

                float now = Time.unscaledTime;
                int sent = 0;
                for (int i = 0; i < contacts.Count && sent < MaxContributePerTick; i++)
                {
                    RadarContact c = contacts[i];
                    if (c.FromDatalink || c.Source == "datalink")
                    {
                        continue;
                    }

                    if (c.Iff != IffRelation.Foe)
                    {
                        continue;
                    }

                    // Prefer own-radar / harmony / lock sources.
                    if (!(c.Source == "harmony" ||
                          c.Locked ||
                          c.Tracked ||
                          IsOwnRadarSource(c.Source)))
                    {
                        continue;
                    }

                    if (LastContributeById.TryGetValue(c.Id, out float last) && now - last < ContributeIntervalSec)
                    {
                        continue;
                    }

                    object? persistentId = ResolvePersistentIdArg(c, method);
                    if (persistentId == null)
                    {
                        continue;
                    }

                    try
                    {
                        method.Invoke(hq, new[] { persistentId });
                        LastContributeById[c.Id] = now;
                        sent++;
                        if (!_contributeLogged)
                        {
                            _contributeLogged = true;
                            Log.Info("Datalink contribute via " + method.Name);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("Datalink contribute invoke soft-fail: " + ex.Message);
                    }
                }

                // Prune stale rate-limit entries.
                if (LastContributeById.Count > 64)
                {
                    List<int> drop = new List<int>();
                    foreach (KeyValuePair<int, float> kv in LastContributeById)
                    {
                        if (now - kv.Value > 30f)
                        {
                            drop.Add(kv.Key);
                        }
                    }

                    for (int i = 0; i < drop.Count; i++)
                    {
                        LastContributeById.Remove(drop[i]);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Datalink contribute soft-fail: " + ex.Message);
            }
        }

        private static bool IsOwnRadarSource(string source)
        {
            if (string.IsNullOrEmpty(source) || source == "datalink" || source == "unknown")
            {
                return false;
            }

            // scene-fallback is own geometric scan; treat as own for contribute when foe.
            return true;
        }

        private static void EnsureUpdateMethods(Type hqType)
        {
            if (_updateMethodsProbed)
            {
                return;
            }

            _updateMethodsProbed = true;
            _rpcUpdate = FindUpdateMethod(hqType, "RpcUpdateTrackingInfo");
            _cmdUpdate = FindUpdateMethod(hqType, "CmdUpdateTrackingInfo");
            if (_rpcUpdate == null && _cmdUpdate == null)
            {
                // Fuzzy fallback
                foreach (MethodInfo m in GameReflect.FindMethods(hqType, "UpdateTrackingInfo"))
                {
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 1)
                    {
                        if (m.Name.IndexOf("Rpc", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _rpcUpdate = m;
                        }
                        else if (m.Name.IndexOf("Cmd", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            _cmdUpdate = m;
                        }
                        else
                        {
                            _cmdUpdate ??= m;
                        }
                    }
                }
            }
        }

        private static MethodInfo? FindUpdateMethod(Type hqType, string name)
        {
            for (Type? walk = hqType; walk != null && walk != typeof(object); walk = walk.BaseType)
            {
                foreach (MethodInfo m in walk.GetMethods(Any))
                {
                    if (!string.Equals(m.Name, name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (m.GetParameters().Length == 1)
                    {
                        return m;
                    }
                }
            }

            return null;
        }

        private static object? ResolvePersistentIdArg(RadarContact contact, MethodInfo method)
        {
            ParameterInfo[] ps = method.GetParameters();
            if (ps.Length != 1)
            {
                return null;
            }

            Type need = ps[0].ParameterType;

            // Prefer unit.persistentID / TrackingInfo.id object.
            object? fromRaw = null;
            if (contact.Raw != null)
            {
                fromRaw = GameReflect.FindMember(contact.Raw.GetType(),
                    "persistentID", "PersistentID", "id", "Id")?.Get(contact.Raw);
            }

            if (fromRaw != null)
            {
                if (need.IsInstanceOfType(fromRaw))
                {
                    return fromRaw;
                }

                if (need == typeof(int) && fromRaw is int i)
                {
                    return i;
                }

                if (need == typeof(uint) && fromRaw is uint u)
                {
                    return u;
                }

                // Try convert boxed PersistentID numeric fields.
                object? boxed = TryCoercePersistentId(fromRaw, need);
                if (boxed != null)
                {
                    return boxed;
                }
            }

            if (need == typeof(int))
            {
                return contact.Id;
            }

            if (need == typeof(uint))
            {
                return unchecked((uint)contact.Id);
            }

            // Construct PersistentID if it has a ctor(int) / ctor(uint).
            try
            {
                ConstructorInfo? ctor = need.GetConstructor(Any, null, new[] { typeof(int) }, null) ??
                                        need.GetConstructor(Any, null, new[] { typeof(uint) }, null);
                if (ctor != null)
                {
                    ParameterInfo[] cps = ctor.GetParameters();
                    object arg = cps[0].ParameterType == typeof(uint)
                        ? (object)unchecked((uint)contact.Id)
                        : contact.Id;
                    return ctor.Invoke(new[] { arg });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("PersistentID ctor soft-fail: " + ex.Message);
            }

            return null;
        }

        private static object? TryCoercePersistentId(object fromRaw, Type need)
        {
            if (need.IsAssignableFrom(fromRaw.GetType()))
            {
                return fromRaw;
            }

            object? value = GameReflect.FindMember(fromRaw.GetType(),
                "Value", "value", "id", "Id", "m_Value")?.Get(fromRaw);
            if (value == null)
            {
                return null;
            }

            try
            {
                if (need == typeof(int))
                {
                    return Convert.ToInt32(value);
                }

                if (need == typeof(uint))
                {
                    return Convert.ToUInt32(value);
                }

                ConstructorInfo? ctor = need.GetConstructor(Any, null, new[] { value.GetType() }, null);
                if (ctor != null)
                {
                    return ctor.Invoke(new[] { value });
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static object? TryGetUnit(object info, Type infoType)
        {
            // TryGetUnit(out Unit) pattern
            foreach (MethodInfo m in infoType.GetMethods(Any))
            {
                if (!string.Equals(m.Name, "TryGetUnit", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length == 1 && ps[0].ParameterType.IsByRef)
                {
                    object?[] args = { null };
                    try
                    {
                        object? ok = m.Invoke(info, args);
                        if (ok is bool b && b && args[0] != null)
                        {
                            return args[0];
                        }

                        if (args[0] != null && (ok == null || (ok is bool b2 && b2)))
                        {
                            return args[0];
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("TryGetUnit soft-fail: " + ex.Message);
                    }
                }
            }

            object? unit = GameReflect.FindMember(infoType,
                "unit", "Unit", "target", "Target", "trackedUnit")?.Get(info);
            return unit;
        }

        private static bool IsObserved(object info, Type infoType, float age, float freshSec)
        {
            try
            {
                MethodInfo? observed = infoType.GetMethod("Observed", Any, null, Type.EmptyTypes, null);
                if (observed != null && observed.ReturnType == typeof(bool))
                {
                    object? r = observed.Invoke(info, Array.Empty<object>());
                    if (r is bool b)
                    {
                        return b;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Observed() soft-fail: " + ex.Message);
            }

            return age < freshSec;
        }

        private static float ReadTrackAgeSec(object info, Type infoType, float now)
        {
            object? spotted = GameReflect.FindMember(infoType,
                "lastSpottedTime", "LastSpottedTime", "lastSeenTime", "LastSeenTime",
                "spottedTime", "time")?.Get(info);
            float t = ToFloat(spotted);
            if (!float.IsNaN(t))
            {
                // Prefer Time.time-compatible values; also tolerate Time.realtimeSinceStartup.
                float age = now - t;
                if (age < -1f)
                {
                    // Maybe unscaled / network time mismatch — try unscaled.
                    age = Time.unscaledTime - t;
                }

                if (age < 0f)
                {
                    age = 0f;
                }

                return age;
            }

            // Age field directly?
            float direct = GameReflect.ReadFloat(info, "age", "Age", "trackAge", "TrackAge");
            if (!float.IsNaN(direct) && direct >= 0f)
            {
                return direct;
            }

            return 0f;
        }

        private static int ReadTrackId(object info, Type infoType, object? unit)
        {
            if (unit != null)
            {
                int uid = GameReflect.IdOf(unit);
                if (uid != 0)
                {
                    return uid;
                }
            }

            object? idObj = GameReflect.FindMember(infoType, "id", "Id", "persistentID", "PersistentID")?.Get(info);
            return IdFromObject(idObj);
        }

        private static int IdFromObject(object? idObj)
        {
            switch (idObj)
            {
                case int i:
                    return i;
                case uint u:
                    return unchecked((int)u);
                case short s:
                    return s;
                case null:
                    return 0;
                default:
                    object? inner = GameReflect.FindMember(idObj.GetType(),
                        "Value", "value", "id", "Id", "m_Value")?.Get(idObj);
                    switch (inner)
                    {
                        case int i2:
                            return i2;
                        case uint u2:
                            return unchecked((int)u2);
                        default:
                            return idObj.GetHashCode();
                    }
            }
        }

        private static Vector3 ReadLastKnownPosition(object info, Type infoType)
        {
            // GetPosition() preferred when present.
            try
            {
                MethodInfo? getPos = infoType.GetMethod("GetPosition", Any, null, Type.EmptyTypes, null);
                if (getPos != null)
                {
                    object? r = getPos.Invoke(info, Array.Empty<object>());
                    if (TryToVector3(r, out Vector3 gp))
                    {
                        return gp;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("GetPosition soft-fail: " + ex.Message);
            }

            object? last = GameReflect.FindMember(infoType,
                "lastKnownPosition", "LastKnownPosition", "position", "Position",
                "knownPosition", "KnownPosition")?.Get(info);
            if (TryToVector3(last, out Vector3 v))
            {
                return v;
            }

            return Vector3.zero;
        }

        private static Vector3 ReadVelocityFromInfo(object info, Type infoType)
        {
            object? vel = GameReflect.FindMember(infoType,
                "lastKnownVelocity", "LastKnownVelocity", "velocity", "Velocity",
                "knownVelocity", "lastVelocity")?.Get(info);
            if (TryToVector3(vel, out Vector3 v))
            {
                return v;
            }

            return Vector3.zero;
        }

        private static Vector3 ReadVelocity(object unit)
        {
            try
            {
                Vector3 v = WeaponReflect.ReadVelocity(unit);
                if (v.sqrMagnitude > 0.01f)
                {
                    return v;
                }
            }
            catch
            {
                // WeaponReflect may not be ready; fall through.
            }

            if (unit is Component c)
            {
                try
                {
                    Rigidbody body = c.GetComponent<Rigidbody>();
                    if (body != null)
                    {
                        return body.velocity;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            float speed = GameReflect.SpeedMps(unit);
            float heading = GameReflect.HeadingDeg(unit);
            if (speed > 0.1f)
            {
                return Quaternion.Euler(0f, heading, 0f) * Vector3.forward * speed;
            }

            return Vector3.zero;
        }

        private static bool TryToVector3(object? value, out Vector3 result)
        {
            result = Vector3.zero;
            if (value == null)
            {
                return false;
            }

            if (value is Vector3 v3)
            {
                result = v3;
                return true;
            }

            if (value is Vector2 v2)
            {
                result = new Vector3(v2.x, 0f, v2.y);
                return true;
            }

            Type t = value.GetType();

            // GlobalPosition / similar: ToVector3 / vector / position
            object? converted = GameReflect.FindMember(t,
                "ToVector3", "toVector3", "vector", "Vector", "position", "Position",
                "asVector3", "AsVector3")?.Get(value);
            if (converted is Vector3 cv)
            {
                result = cv;
                return true;
            }

            float x = ReadCoord(value, t, "x", "X", "east", "East", "lon");
            float y = ReadCoord(value, t, "y", "Y", "alt", "Alt", "altitude", "Altitude", "up", "Up");
            float z = ReadCoord(value, t, "z", "Z", "north", "North", "lat");
            if (!float.IsNaN(x) && !float.IsNaN(z))
            {
                result = new Vector3(x, float.IsNaN(y) ? 0f : y, z);
                return true;
            }

            return false;
        }

        private static float ReadCoord(object instance, Type type, params string[] names)
        {
            object? v = GameReflect.FindMember(type, names)?.Get(instance);
            return ToFloat(v);
        }

        private static float ToFloat(object? value)
        {
            switch (value)
            {
                case float f:
                    return f;
                case double d:
                    return (float)d;
                case int i:
                    return i;
                case long l:
                    return l;
                default:
                    return float.NaN;
            }
        }

        private static Vector3 StableNoiseOffset(int id, float now, float meters)
        {
            if (meters <= 0.01f)
            {
                return Vector3.zero;
            }

            // Slow-changing Perlin so coast blips don't flicker every frame.
            float t = now * 0.15f;
            float n1 = Mathf.PerlinNoise((id % 997) * 0.17f + t, (id % 389) * 0.13f) * 2f - 1f;
            float n2 = Mathf.PerlinNoise((id % 641) * 0.19f, (id % 271) * 0.11f + t) * 2f - 1f;
            float n3 = Mathf.PerlinNoise((id % 457) * 0.23f + t * 0.5f, (id % 149) * 0.29f) * 2f - 1f;
            return new Vector3(n1, n3 * 0.35f, n2) * meters;
        }
    }
}
