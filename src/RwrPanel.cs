using System;
using System.Collections.Generic;
using UnityEngine;

namespace RDA
{
    internal enum RwrKind
    {
        Search,
        Lock,
        Missile,
        Unknown
    }

    internal sealed class RwrThreat
    {
        internal int Id;
        internal string Label = "?";
        internal float BearingDeg;
        internal RwrKind Kind;
        internal float Age;
        internal float Ttl;
        internal IffRelation Iff;
        internal bool Flash;
    }

    internal sealed class RwrPanel
    {
        private readonly object _gate = new object();
        private readonly List<Pending> _pending = new List<Pending>(16);
        private readonly List<RwrThreat> _threats = new List<RwrThreat>(16);

        internal float OwnshipHeadingDeg { get; set; }

        /// <summary>Local player aircraft — used to filter self-noise and resolve IFF.</summary>
        internal object? PlayerAircraft { get; set; }
        internal IReadOnlyList<RwrThreat> Threats => _threats;

        internal bool ContainsId(int id)
        {
            for (int i = 0; i < _threats.Count; i++)
            {
                if (_threats[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        internal void Clear()
        {
            lock (_gate)
            {
                _pending.Clear();
            }

            _threats.Clear();
        }

        internal void Ingest(object? instance, object[]? args)
        {
            lock (_gate)
            {
                _pending.Add(new Pending { Instance = instance, Args = args });
            }
        }

        /// <summary>Direct lock / missile threat from soft poll (HQ missileAttacks, seeker on ownship).</summary>
        internal void IngestOwnshipThreat(object? emitterOrMissile, RwrKind kind, bool flash)
        {
            if (emitterOrMissile == null)
            {
                return;
            }

            try
            {
                RwrThreat threat = FromEmitter(emitterOrMissile, kind, flash);
                if (threat.Iff == IffRelation.Friend && kind != RwrKind.Missile)
                {
                    return;
                }

                Upsert(threat);
            }
            catch (Exception ex)
            {
                Log.Debug("RWR IngestOwnshipThreat: " + ex.Message);
            }
        }

        internal void Tick(float dt)
        {
            Drain();
            for (int i = _threats.Count - 1; i >= 0; i--)
            {
                _threats[i].Age += dt;
                if (_threats[i].Age >= _threats[i].Ttl)
                {
                    _threats.RemoveAt(i);
                }
            }
        }

        internal void Draw(Rect area)
        {
            Rect r = ScopeDraw.Snap(area);
            ScopeDraw.Fill(r, ScopeDraw.PanelBg);
            ScopeDraw.Border(r, ScopeDraw.PanelBorder, 1.75f);
            ScopeDraw.CornerBrackets(r, ScopeDraw.CyanAccent * new Color(1f, 1f, 1f, 0.65f), 10f, 1.75f);
            ScopeDraw.ClippedLabel(new Rect(r.x + 6f, r.y + 4f, r.width - 12f, 18f), "RWR", ScopeDraw.HeaderStyle);

            Vector2 center = ScopeDraw.Snap(new Vector2(r.x + r.width * 0.5f, r.y + r.height * 0.52f));
            float radius = Mathf.Min(r.width, r.height) * 0.36f;
            ScopeDraw.GlowCircle(center, radius, new Color(0.2f, 0.7f, 0.35f, 0.75f), 1.6f);
            ScopeDraw.GlowCircle(center, radius * 0.5f, new Color(0.2f, 0.7f, 0.35f, 0.4f), 1.5f);
            ScopeDraw.GlowLine(center + new Vector2(0f, -radius), center + new Vector2(0f, radius),
                new Color(0.2f, 0.5f, 0.25f, 0.45f), 1.5f);
            ScopeDraw.GlowLine(center + new Vector2(-radius, 0f), center + new Vector2(radius, 0f),
                new Color(0.2f, 0.5f, 0.25f, 0.45f), 1.5f);
            ScopeDraw.ClippedLabel(new Rect(center.x - 6f, r.y + 22f, 16f, 14f), "N", ScopeDraw.TinyStyle);

            if (_threats.Count == 0)
            {
                ScopeDraw.ClippedLabel(new Rect(r.x + 8f, r.yMax - 36f, r.width - 16f, 28f), "CLEAR", ScopeDraw.MutedStyle);
                return;
            }

            GUI.BeginGroup(r);
            Vector2 lc = new Vector2(center.x - r.x, center.y - r.y);
            float flashPhase = (Time.unscaledTime * 6f) % 1f;
            bool flashOn = flashPhase < 0.55f;
            foreach (RwrThreat threat in _threats)
            {
                float rad = threat.BearingDeg * Mathf.Deg2Rad;
                Vector2 pos = lc + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * (radius * 0.82f);
                Color color = ColorFor(threat);
                if (threat.Flash || threat.Kind == RwrKind.Lock || threat.Kind == RwrKind.Missile)
                {
                    if (!flashOn)
                    {
                        color = new Color(color.r, color.g, color.b, 0.35f);
                    }
                    else
                    {
                        // Threat emphasis
                        color = Color.Lerp(color, Color.white, 0.25f);
                    }
                }

                DrawThreatSymbol(pos, threat, color);
                ScopeDraw.ClippedLabel(new Rect(pos.x + 7f, pos.y - 7f, 40f, 16f), Short(threat), ScopeDraw.TinyStyle);
            }

            GUI.EndGroup();

            bool anyLock = false;
            for (int i = 0; i < _threats.Count; i++)
            {
                if (_threats[i].Kind == RwrKind.Lock || _threats[i].Kind == RwrKind.Missile)
                {
                    anyLock = true;
                    break;
                }
            }

            string footer = anyLock
                ? (flashOn ? "LOCKED" : "LOCK!")
                : (_threats.Count + "  S/L/M");
            ScopeDraw.ClippedLabel(new Rect(r.x + 8f, r.yMax - 22f, r.width - 16f, 18f), footer, ScopeDraw.TinyStyle);
        }

        private void Drain()
        {
            List<Pending> batch;
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    return;
                }

                batch = new List<Pending>(_pending);
                _pending.Clear();
            }

            foreach (Pending pending in batch)
            {
                try
                {
                    Absorb(pending.Instance, pending.Args);
                }
                catch (Exception ex)
                {
                    Log.Debug("RWR ingest: " + ex.Message);
                }
            }
        }

        private void Absorb(object? instance, object[]? args)
        {
            // Prefer unwrapping Aircraft.OnRadarWarning-like payloads: emitter + isTarget + detected.
            if (TryAbsorbRadarWarningPayload(instance, args))
            {
                return;
            }

            List<object> nodes = new List<object>(8);
            if (instance != null)
            {
                nodes.Add(instance);
            }

            if (args != null)
            {
                foreach (object arg in args)
                {
                    if (arg != null)
                    {
                        nodes.Add(arg);
                    }
                }
            }

            foreach (object node in nodes)
            {
                if (TryAbsorbRadarWarningPayload(node, null))
                {
                    continue;
                }

                RwrThreat threat = FromNode(node);
                if (Mathf.Abs(threat.BearingDeg) < 0.01f && threat.Kind == RwrKind.Unknown && threat.Label == "?")
                {
                    continue;
                }

                // Drop friendly self / wingman noise — RWR is for emitters scanning/illuminating US
                if (threat.Iff == IffRelation.Friend && threat.Kind != RwrKind.Missile)
                {
                    continue;
                }

                Upsert(threat);
            }
        }

        /// <summary>
        /// Nuclear Option: Aircraft.OnRadarWarning { emitter, detected, isTarget }.
        /// isTarget == true → player is being locked / painted (被锁定).
        /// </summary>
        private bool TryAbsorbRadarWarningPayload(object? node, object[]? args)
        {
            object? payload = node;
            if (payload == null && args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != null && LooksLikeRadarWarning(args[i]))
                    {
                        payload = args[i];
                        break;
                    }
                }
            }

            if (payload == null)
            {
                // Flat args: (emitter, detected, isTarget) style
                if (args != null && args.Length >= 1 && args[0] != null &&
                    (GameReflect.Unit != null && GameReflect.Unit.IsInstanceOfType(args[0]) ||
                     GameReflect.Aircraft != null && GameReflect.Aircraft.IsInstanceOfType(args[0])))
                {
                    bool flatIsTarget = false;
                    for (int i = 1; i < args.Length; i++)
                    {
                        if (args[i] is bool b)
                        {
                            // Last bool wins as isTarget (emitter, detected, isTarget)
                            flatIsTarget = b;
                        }
                    }

                    if (args.Length >= 3 && args[args.Length - 1] is bool lastB)
                    {
                        flatIsTarget = lastB;
                    }

                    RwrKind kind = flatIsTarget ? RwrKind.Lock : RwrKind.Search;
                    IngestOwnshipThreat(args[0], kind, flash: flatIsTarget);
                    return true;
                }

                return false;
            }

            if (!LooksLikeRadarWarning(payload) &&
                GameReflect.FindMember(payload.GetType(), "emitter", "Emitter", "isTarget", "IsTarget") == null)
            {
                return false;
            }

            object? emitter = GameReflect.FindMember(payload.GetType(),
                "emitter", "Emitter", "source", "Source", "radar", "Radar", "unit", "Unit")?.Get(payload);
            object? isTargetObj = GameReflect.FindMember(payload.GetType(),
                "isTarget", "IsTarget", "targeted", "Targeted", "locked", "Locked", "hardLock", "HardLock")?.Get(payload);
            object? detectedObj = GameReflect.FindMember(payload.GetType(),
                "detected", "Detected", "search", "Search")?.Get(payload);

            bool isTarget = isTargetObj is bool it && it;
            bool detected = detectedObj is not bool d || d;

            if (emitter == null)
            {
                // MissileWarning payload
                object? missile = GameReflect.FindMember(payload.GetType(),
                    "missile", "Missile")?.Get(payload);
                if (missile != null)
                {
                    IngestOwnshipThreat(missile, RwrKind.Missile, flash: true);
                    return true;
                }

                return false;
            }

            RwrKind k = isTarget ? RwrKind.Lock : (detected ? RwrKind.Search : RwrKind.Unknown);
            IngestOwnshipThreat(emitter, k, flash: isTarget);
            return true;
        }

        private static bool LooksLikeRadarWarning(object value)
        {
            string name = value.GetType().Name ?? string.Empty;
            return name.IndexOf("RadarWarning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("MissileWarning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("OnRadar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private RwrThreat FromEmitter(object node, RwrKind kind, bool flash)
        {
            RwrThreat t = FromNode(node);
            t.Kind = kind;
            t.Flash = flash || kind == RwrKind.Lock || kind == RwrKind.Missile;
            t.Ttl = kind switch
            {
                RwrKind.Missile => 8f,
                RwrKind.Lock => 6f,
                RwrKind.Search => 3.5f,
                _ => 2.5f
            };
            t.Age = 0f;
            return t;
        }

        private RwrThreat FromNode(object node)
        {
            Vector3 world = GameReflect.WorldPosition(node);
            float bearing = 0f;
            if (world.sqrMagnitude > 1f)
            {
                Vector3 own = Vector3.zero;
                if (PlayerAircraft != null)
                {
                    own = GameReflect.WorldPosition(PlayerAircraft);
                }
                else
                {
                    Camera? cam = Camera.main;
                    own = cam != null ? cam.transform.position : world;
                }

                Vector3 delta = world - own;
                if (delta.sqrMagnitude > 1f)
                {
                    bearing = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                }
            }

            float explicitBearing = GameReflect.ReadFloat(node, "bearing", "Bearing", "azimuth", "Azimuth", "heading");
            if (!float.IsNaN(explicitBearing))
            {
                bearing = explicitBearing;
            }

            string text = (node.ToString() ?? string.Empty) + " " + GameReflect.LabelOf(node);
            object? kindObj = GameReflect.FindMember(node.GetType(), "type", "Type", "warningType", "WarningType", "kind", "Kind")?.Get(node);
            if (kindObj != null)
            {
                text += " " + kindObj;
            }

            RwrKind kind = Classify(text);
            bool flash = kind == RwrKind.Lock || kind == RwrKind.Missile;
            return new RwrThreat
            {
                Id = GameReflect.IdOf(node),
                Label = GameReflect.LabelOf(node),
                BearingDeg = ContactProvider.Normalize180(bearing - OwnshipHeadingDeg),
                Kind = kind,
                Age = 0f,
                Flash = flash,
                Ttl = kind switch
                {
                    RwrKind.Missile => 8f,
                    RwrKind.Lock => 6f,
                    RwrKind.Search => 3.5f,
                    RwrKind.Unknown => 2.5f,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                },
                Iff = PlayerAircraft != null
                    ? GameReflect.CompareIff(PlayerAircraft, node)
                    : IffRelation.Unknown
            };
        }

        private void Upsert(RwrThreat incoming)
        {
            for (int i = 0; i < _threats.Count; i++)
            {
                if (_threats[i].Id == incoming.Id ||
                    (Mathf.Abs(ContactProvider.Normalize180(_threats[i].BearingDeg - incoming.BearingDeg)) < 8f &&
                     _threats[i].Kind == incoming.Kind))
                {
                    // Escalate kind if incoming is more severe
                    if (KindRank(incoming.Kind) >= KindRank(_threats[i].Kind))
                    {
                        incoming.Age = 0f;
                        _threats[i] = incoming;
                    }
                    else
                    {
                        _threats[i].Age = 0f;
                        _threats[i].Flash = _threats[i].Flash || incoming.Flash;
                    }

                    return;
                }
            }

            _threats.Add(incoming);
            if (_threats.Count > 12)
            {
                _threats.RemoveAt(0);
            }
        }

        private static int KindRank(RwrKind k) => k switch
        {
            RwrKind.Missile => 3,
            RwrKind.Lock => 2,
            RwrKind.Search => 1,
            _ => 0
        };

        private static RwrKind Classify(string text)
        {
            if (Contains(text, "missile", "launch", "pitbull", "arh", "sarh"))
            {
                return RwrKind.Missile;
            }

            if (Contains(text, "lock", "trk", "stt", "spike", "illum", "isTarget", "paint"))
            {
                return RwrKind.Lock;
            }

            if (Contains(text, "search", "scan", "rws", "src", "twr", "warn"))
            {
                return RwrKind.Search;
            }

            return RwrKind.Unknown;
        }

        private static bool Contains(string text, params string[] parts)
        {
            foreach (string part in parts)
            {
                if (text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Short(RwrThreat threat) => threat.Kind switch
        {
            RwrKind.Search => "S",
            RwrKind.Lock => "L",
            RwrKind.Missile => "M",
            RwrKind.Unknown => "?",
            _ => throw new ArgumentOutOfRangeException(nameof(threat), threat.Kind, null)
        };

        private static Color ColorFor(RwrThreat threat)
        {
            if (threat.Kind == RwrKind.Missile)
            {
                return new Color(1f, 0.92f, 0.15f, 1f); // yellow
            }

            if (threat.Kind == RwrKind.Lock)
            {
                // Locked-on-us: vivid red regardless of IFF unknown
                return new Color(1f, 0.12f, 0.08f, 1f);
            }

            return threat.Iff switch
            {
                IffRelation.Foe => new Color(1f, 0.2f, 0.15f, 1f),
                IffRelation.Friend => new Color(0.25f, 0.55f, 1f, 1f),
                IffRelation.Neutral => new Color(0.75f, 0.75f, 0.8f, 1f),
                _ => new Color(1f, 1f, 1f, 1f)
            };
        }

        private static void DrawThreatSymbol(Vector2 pos, RwrThreat threat, Color color)
        {
            if (threat.Kind == RwrKind.Missile)
            {
                ScopeDraw.Triangle(pos, 7f, color);
                return;
            }

            if (threat.Kind == RwrKind.Lock)
            {
                ScopeDraw.Diamond(pos, 7f, color);
                ScopeDraw.HollowBox(pos, 9f, color, 1.25f);
                return;
            }

            switch (threat.Iff)
            {
                case IffRelation.Foe:
                    ScopeDraw.Diamond(pos, 6f, color);
                    break;
                case IffRelation.Friend:
                    ScopeDraw.Circle(pos, 5f, color, 1.75f);
                    break;
                default:
                    ScopeDraw.HollowBox(pos, 5f, color, 1.5f);
                    break;
            }
        }

        private struct Pending
        {
            internal object? Instance;
            internal object[]? Args;
        }
    }
}
