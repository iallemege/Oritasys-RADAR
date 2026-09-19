using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RDA
{
    internal static class HarmonyHooks
    {
        private static RwrPanel? _rwr;
        private static ContactProvider? _contacts;
        private static ModeState? _modes;
        private static readonly HashSet<string> Patched = new HashSet<string>();

        /// <summary>Local player aircraft from ContactProvider (null if not in mission).</summary>
        internal static object? LocalPlayerOrNull => _contacts?.Player;


        private static readonly string[] VanillaSkipMethods =
        {
            "ScanRadar", "TargetSearch"
        };

        internal static void Apply(Harmony harmony, RwrPanel rwr, ContactProvider contacts, ModeState modes)
        {
            _rwr = rwr;
            _contacts = contacts;
            _modes = modes;

            try
            {
                GameReflect.EnsureDiscovered();
                PatchType(harmony, GameReflect.TacScreen, "OnRadarWarning", "RadarWarning", "ScanRadar");
                PatchType(harmony, GameReflect.RadarWarning, "OnRadarWarning", "ShowWarning", "Trigger", "Play");
                // MissileWarning / Aircraft radar-warning paths for 被锁定 / inbound missile
                PatchType(harmony, GameReflect.FindType("MissileWarning"),
                    "OnMissileWarning", "ShowWarning", "Trigger", "Play", "Warn", "Notify");
                PatchType(harmony, GameReflect.Aircraft,
                    "OnRadarWarning", "RadarWarning", "InvokeRadarWarning");
                PatchType(harmony, GameReflect.Radar, "CanSeeRadarReturn", "TargetSearch", "ScanRadar");
                PatchType(harmony, GameReflect.TargetDetector, "TargetSearch", "CanSeeRadarReturn");
                PatchType(harmony, GameReflect.DetectorManager, "RequestRadarCheck");
                PatchType(harmony, GameReflect.FindType("ThreatItem"), "AnimateItem", "Show");

                // Prefix: skip vanilla scan/UI updates for local player only when DisableVanillaRadar.
                PatchVanillaSkipPrefix(harmony, GameReflect.TacScreen, "ScanRadar");
                PatchVanillaSkipPrefix(harmony, GameReflect.Radar, "ScanRadar", "TargetSearch");
                PatchVanillaSkipPrefix(harmony, GameReflect.TargetDetector, "TargetSearch");

                // STBY: local-player GetRadarReturn scaled by StandbyRcsFactor (default 0.8 = −20% RCS).
                PatchStandbyRcs(harmony);

                PatchMissileRelock(harmony);

                Log.Info("Harmony hooks applied (" + Patched.Count + " methods). Missing types fail soft.");
            }
            catch (Exception ex)
            {
                Log.Warn("Harmony apply failed softly: " + ex.Message);
            }
        }


        /// <summary>
        /// STBY RCS reduction for local player: Postfix on Aircraft/Unit GetRadarReturn (float).
        /// Multiplies __result by StandbyRcsFactor (default 0.8 = −20%). Fail-soft if methods missing.
        /// </summary>
        private static void PatchStandbyRcs(Harmony harmony)
        {
            try
            {
                HarmonyMethod postfix = new HarmonyMethod(typeof(HarmonyHooks), nameof(StandbyRcsPostfix));
                string[] names = { "GetRadarReturn", "get_RadarReturn" };
                foreach (Type? type in new Type?[] { GameReflect.Aircraft, GameReflect.Unit })
                {
                    if (type == null)
                    {
                        continue;
                    }

                    foreach (MethodInfo method in GameReflect.FindMethods(type, names))
                    {
                        if (method.ReturnType != typeof(float))
                        {
                            continue;
                        }

                        string key = "rcs:" + method.DeclaringType?.FullName + "::" + method.Name + "/" + method.GetParameters().Length;
                        if (!Patched.Add(key))
                        {
                            continue;
                        }

                        try
                        {
                            harmony.Patch(method, postfix: postfix);
                            Log.Info("Patched STBY RCS on " + key);
                        }
                        catch (Exception ex)
                        {
                            Patched.Remove(key);
                            Log.Debug("STBY RCS soft-fail " + key + ": " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("PatchStandbyRcs soft-fail: " + ex.Message);
            }
        }

        private static void StandbyRcsPostfix(object __instance, ref float __result)
        {
            try
            {
                if (_modes == null || _modes.Mode != RadarMode.Standby)
                {
                    return;
                }

                if (_contacts == null || !_contacts.InMission)
                {
                    return;
                }

                object? player = _contacts.Player;
                if (player == null || __instance == null)
                {
                    return;
                }

                if (!IsOwnedByLocalPlayer(__instance, player))
                {
                    return;
                }

                float factor = Config.StandbyRcsFactor.Value;
                if (factor > 0.01f && factor < 0.999f)
                {
                    __result *= factor;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("StandbyRcsPostfix soft-fail: " + ex.Message);
            }
        }

        private static void PatchVanillaSkipPrefix(Harmony harmony, Type? type, params string[] methodNames)
        {
            if (type == null)
            {
                return;
            }

            HarmonyMethod prefix = new HarmonyMethod(typeof(HarmonyHooks), nameof(SkipVanillaForLocalPlayerPrefix));
            foreach (MethodInfo method in GameReflect.FindMethods(type, methodNames))
            {
                string key = "prefix:" + method.DeclaringType?.FullName + "::" + method.Name + "/" + method.GetParameters().Length;
                if (!Patched.Add(key))
                {
                    continue;
                }

                // Only prefix the named skip methods (avoid CanSeeRadarReturn etc. if FindMethods fuzzy-matched).
                bool wanted = false;
                foreach (string name in VanillaSkipMethods)
                {
                    if (string.Equals(method.Name, name, StringComparison.Ordinal))
                    {
                        wanted = true;
                        break;
                    }
                }

                if (!wanted)
                {
                    Patched.Remove(key);
                    continue;
                }

                try
                {
                    harmony.Patch(method, prefix: prefix);
                    Log.Info("Vanilla-skip prefix " + key);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not prefix " + key + ": " + ex.Message);
                    Patched.Remove(key);
                }
            }
        }

        /// <summary>
        /// Harmony Prefix: return false to skip original. Only for local player aircraft; AI unaffected.
        /// </summary>
        private static bool SkipVanillaForLocalPlayerPrefix(object __instance)
        {
            try
            {
                if (!Config.DisableVanillaRadar.Value)
                {
                    return true;
                }

                if (_contacts == null || !_contacts.InMission)
                {
                    return true;
                }

                object? player = _contacts.Player;
                if (player == null || __instance == null)
                {
                    return true;
                }

                if (IsOwnedByLocalPlayer(__instance, player))
                {
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Vanilla-skip prefix soft-fail: " + ex.Message);
            }

            return true;
        }

        private static bool IsOwnedByLocalPlayer(object instance, object player)
        {
            if (ReferenceEquals(instance, player))
            {
                return true;
            }

            if (ReferenceEquals(instance, _contacts?.Radar) || ReferenceEquals(instance, _contacts?.TacScreen))
            {
                return true;
            }

            try
            {
                if (instance is Component comp)
                {
                    Component? playerComp = GameReflect.AsComponent(player);
                    if (playerComp != null)
                    {
                        Transform root = playerComp.transform.root;
                        if (comp.transform.root == root)
                        {
                            return true;
                        }

                        // Child of local aircraft
                        if (comp.transform.IsChildOf(playerComp.transform) ||
                            playerComp.transform.IsChildOf(comp.transform))
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // fail soft → do not skip
            }

            object? owner = GameReflect.FindMember(instance.GetType(),
                "aircraft", "Aircraft", "owner", "Owner", "unit", "Unit", "parent")?.Get(instance);
            if (owner != null && (ReferenceEquals(owner, player) ||
                                  (owner is Component oc && player is Component pc && oc.transform.root == pc.transform.root)))
            {
                return true;
            }

            return false;
        }

        private static void PatchType(Harmony harmony, Type? type, params string[] methodNames)
        {
            if (type == null)
            {
                Log.Debug("Skip patch; type not found for " + string.Join("/", methodNames));
                return;
            }

            HarmonyMethod postfix = new HarmonyMethod(typeof(HarmonyHooks), nameof(GenericPostfix));
            foreach (MethodInfo method in GameReflect.FindMethods(type, methodNames))
            {
                string key = method.DeclaringType?.FullName + "::" + method.Name + "/" + method.GetParameters().Length;
                if (!Patched.Add(key))
                {
                    continue;
                }

                try
                {
                    harmony.Patch(method, postfix: postfix);
                    Log.Info("Patched " + key);
                }
                catch (Exception ex)
                {
                    Log.Debug("Could not patch " + key + ": " + ex.Message);
                    Patched.Remove(key);
                }
            }
        }

        private static void GenericPostfix(object __instance, object[] __args, MethodBase __originalMethod)
        {
            try
            {
                string methodName = __originalMethod?.Name ?? string.Empty;
                bool warningPath = IsRadarWarningMethod(methodName);

                // RWR: only RadarWarning / OnRadarWarning-style paths (illuminating the player).
                // Do NOT mirror every PPI/scan contact onto RWR.
                if (warningPath)
                {
                    _rwr?.Ingest(__instance, __args);
                }

                if (__args == null)
                {
                    return;
                }

                foreach (object arg in __args)
                {
                    if (arg == null || arg is float || arg is int || arg is bool || arg is string)
                    {
                        continue;
                    }

                    if (LooksLikeUnit(arg))
                    {
                        _contacts?.IngestDetected(arg);
                    }

                    if (warningPath && LooksLikeWarning(arg))
                    {
                        _rwr?.Ingest(arg, Array.Empty<object>());
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("Harmony postfix swallowed: " + ex.Message);
            }
        }

        private static bool IsRadarWarningMethod(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("RadarWarning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("OnRadarWarning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("MissileWarning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("OnMissileWarning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("ShowWarning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (name.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    name.IndexOf("Scan", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private static bool LooksLikeUnit(object value)
        {
            Type type = value.GetType();
            if (GameReflect.Unit != null && GameReflect.Unit.IsInstanceOfType(value))
            {
                return true;
            }

            if (GameReflect.Aircraft != null && GameReflect.Aircraft.IsInstanceOfType(value))
            {
                return true;
            }

            string name = type.Name;
            return name.IndexOf("Aircraft", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Unit", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("Missile", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeWarning(object value)
        {
            if (GameReflect.RadarWarning != null && GameReflect.RadarWarning.IsInstanceOfType(value))
            {
                return true;
            }

            string name = value.GetType().Name;
            return name.IndexOf("Warning", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("RWR", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    
        /// <summary>
        /// Prefix ARHSeeker.SlowUpdate (and ARMSeeker when it has null-target SD):
        /// try radar relock; optionally skip SlowUpdate while ownship radar still locks
        /// so null-target Detonate does not fire. Never prefixes Missile.Detonate.
        /// </summary>

        /// <summary>
        /// Prefix ARHSeeker.SlowChecks (and ARMSeeker when it has null-target SD):
        /// try radar relock; optionally skip SlowChecks while ownship radar still locks
        /// so null-target Detonate does not fire. Never prefixes Missile.Detonate.
        /// </summary>
        private static void PatchMissileRelock(Harmony harmony)
        {
            try
            {
                MissileRadarSupport.EnsureTypes();
                HarmonyMethod prefix = new HarmonyMethod(
                    typeof(MissileRadarSupport),
                    nameof(MissileRadarSupport.SlowChecksRelockPrefix));

                TryPatchSeekerSlowChecks(harmony, MissileRadarSupport.ArhSeekerType, prefix, "ARHSeeker");

                Type? arm = MissileRadarSupport.ArmSeekerType;
                if (MissileRadarSupport.ArmSeekerLikelyHasNullTargetSuicide(arm))
                {
                    TryPatchSeekerSlowChecks(harmony, arm, prefix, "ARMSeeker");
                }
                else if (arm != null)
                {
                    Log.Debug("ARMSeeker present but no lockedTarget field — skip SlowChecks prefix");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("PatchMissileRelock soft-fail: " + ex.Message);
            }
        }

        private static void TryPatchSeekerSlowChecks(
            Harmony harmony, Type? seekerType, HarmonyMethod prefix, string label)
        {
            if (seekerType == null)
            {
                Log.Debug("Missile relock: " + label + " type not found (fail soft)");
                return;
            }

            foreach (MethodInfo method in GameReflect.FindMethods(seekerType,
                "SlowChecks", "slowChecks", "SlowUpdate", "slowUpdate"))
            {
                // Prefer SlowChecks; accept SlowUpdate as fallback name
                if (!string.Equals(method.Name, "SlowChecks", StringComparison.Ordinal) &&
                    !string.Equals(method.Name, "SlowUpdate", StringComparison.Ordinal))
                {
                    continue;
                }

                if (method.GetParameters().Length > 0)
                {
                    continue;
                }

                string key = "missileRelock:" + method.DeclaringType?.FullName + "::" + method.Name;
                if (!Patched.Add(key))
                {
                    continue;
                }

                try
                {
                    harmony.Patch(method, prefix: prefix);
                    Log.Info("Missile relock prefix on " + key);
                }
                catch (Exception ex)
                {
                    Patched.Remove(key);
                    Log.Debug("Could not prefix " + key + ": " + ex.Message);
                }
            }
        }

    }
}
