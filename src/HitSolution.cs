using UnityEngine;

namespace RDA
{
    internal enum WeaponKind
    {
        Unknown,
        Gun,
        Missile,
        Bomb,
        Other
    }

    internal enum HitVerdict
    {
        Unknown,
        /// <summary>Solid solution — in range, ammo, lead within ~8°.</summary>
        Hit,
        /// <summary>Borderline — lead ~8–12° or near Rmax.</summary>
        Marginal,
        No
    }

    /// <summary>Cached weapon ETA / lead / CanHit for the locked (or primary) contact.</summary>
    internal sealed class HitSolution
    {
        internal bool Valid;
        internal WeaponKind Kind = WeaponKind.Unknown;
        internal HitVerdict Verdict = HitVerdict.Unknown;
        internal bool CanHit;
        internal float EtaSec = float.NaN;
        internal Vector3 LeadWorld;
        internal Vector3 MuzzleWorld;
        internal Vector3 BoresightDir = Vector3.forward;
        internal float LeadAngleDeg;
        internal float RangeMeters;
        internal float MaxRangeMeters;
        /// <summary>Remaining distance to Rmax (meters). Negative if beyond Rmax.</summary>
        internal float RangeToRmaxMeters;
        internal string WeaponName = "—";
        internal string ShortName = "—";
        internal int Ammo;
        internal float ClosingSpeedMps;

        internal static HitSolution None { get; } = new HitSolution();

        internal string VerdictTag => Verdict switch
        {
            HitVerdict.Hit => "HIT",
            HitVerdict.Marginal => "MARG",
            HitVerdict.No => "NO",
            _ => "—"
        };

        /// <summary>Lock-strip line: WPN xxx  ETA n.n s  HIT|NO|MARG  R/Rmax …</summary>
        internal string FormatStripLine()
        {
            if (!Valid)
            {
                return "WPN —  ETA —  —";
            }

            string name = !string.IsNullOrEmpty(ShortName) && ShortName != "—"
                ? ShortName
                : (!string.IsNullOrEmpty(WeaponName) ? WeaponName : "—");
            if (name.Length > 12)
            {
                name = name.Substring(0, 12);
            }

            string eta;
            if (float.IsNaN(EtaSec) || float.IsInfinity(EtaSec))
            {
                eta = Kind == WeaponKind.Bomb || Kind == WeaponKind.Other ? "N/A" : "—";
            }
            else
            {
                eta = EtaSec.ToString("0.0") + " s";
            }

            string rmax;
            if (MaxRangeMeters > 1f)
            {
                float rKm = RangeMeters / 1000f;
                float rMaxKm = MaxRangeMeters / 1000f;
                float remKm = RangeToRmaxMeters / 1000f;
                rmax = "  R " + rKm.ToString("0.0") + "/" + rMaxKm.ToString("0.0") + "km"
                       + "  ΔRmax " + remKm.ToString("+0.0;-0.0") + "km";
            }
            else
            {
                rmax = "  R " + (RangeMeters / 1000f).ToString("0.0") + "km";
            }

            return "WPN " + name + "  ETA " + eta + "  " + VerdictTag + rmax
                   + "  AMMO " + Ammo;
        }
    }

    /// <summary>Gun / missile / bomb hit prediction from a weapon snapshot + contact kinematics.</summary>
    internal static class HitSolver
    {
        private const float GravityBase = 9.81f;
        private const float HitLeadDeg = 8f;
        private const float MargLeadDeg = 12f;
        private const float DefaultMissileSpeed = 700f;
        private const int BallisticIters = 10;

        internal static HitSolution Solve(
            in WeaponSnapshot weapon,
            Vector3 ownPos,
            Vector3 ownVel,
            Vector3 ownForward,
            Vector3 tgtPos,
            Vector3 tgtVel,
            float closingSpeedMps)
        {
            HitSolution sol = new HitSolution
            {
                Valid = weapon.Valid,
                Kind = weapon.Kind,
                WeaponName = weapon.WeaponName,
                ShortName = weapon.ShortName,
                Ammo = weapon.Ammo,
                RangeMeters = Vector3.Distance(ownPos, tgtPos),
                MaxRangeMeters = weapon.MaxRangeMeters > 0f ? weapon.MaxRangeMeters : float.NaN,
                ClosingSpeedMps = closingSpeedMps,
                MuzzleWorld = weapon.MuzzleWorld.sqrMagnitude > 0.01f ? weapon.MuzzleWorld : ownPos,
                BoresightDir = weapon.BarrelDir.sqrMagnitude > 0.01f
                    ? weapon.BarrelDir.normalized
                    : (ownForward.sqrMagnitude > 0.01f ? ownForward.normalized : Vector3.forward)
            };

            if (!weapon.Valid)
            {
                sol.Verdict = HitVerdict.Unknown;
                return sol;
            }

            sol.RangeToRmaxMeters = (!float.IsNaN(sol.MaxRangeMeters) ? sol.MaxRangeMeters : 0f) - sol.RangeMeters;

            switch (weapon.Kind)
            {
                case WeaponKind.Gun:
                    SolveGun(ref sol, weapon, tgtPos, tgtVel);
                    break;
                case WeaponKind.Missile:
                    SolveMissile(ref sol, weapon, ownPos, ownForward, tgtPos, closingSpeedMps);
                    break;
                case WeaponKind.Bomb:
                case WeaponKind.Other:
                default:
                    SolveBombOrOther(ref sol, weapon, ownPos, ownForward, tgtPos);
                    break;
            }

            return sol;
        }

        private static void SolveGun(ref HitSolution sol, in WeaponSnapshot weapon, Vector3 tgtPos, Vector3 tgtVel)
        {
            Vector3 muzzle = sol.MuzzleWorld;
            float v0 = weapon.MuzzleVelocity > 10f
                ? weapon.MuzzleVelocity
                : (weapon.MaxSpeed > 10f ? weapon.MaxSpeed : 900f);
            float g = GravityBase * (weapon.GravMult > 0f ? weapon.GravMult : 1f);
            float drag = Mathf.Max(0f, weapon.DragCoef);

            float t = sol.RangeMeters / Mathf.Max(50f, v0);
            Vector3 lead = tgtPos;
            for (int i = 0; i < BallisticIters; i++)
            {
                lead = tgtPos + tgtVel * t;
                Vector3 delta = lead - muzzle;
                float dist = delta.magnitude;
                if (dist < 0.5f)
                {
                    t = 0f;
                    break;
                }

                // Optional simple drag: effective mean speed decays with time.
                float vEff = v0;
                if (drag > 1e-6f)
                {
                    // Approximate average of v0 * e^{-k t} → v0 * (1 - e^{-kt}) / (kt)
                    float kt = drag * t;
                    if (kt > 1e-4f && kt < 20f)
                    {
                        vEff = v0 * (1f - Mathf.Exp(-kt)) / kt;
                    }
                    else if (kt >= 20f)
                    {
                        vEff = v0 / kt;
                    }
                }

                vEff = Mathf.Max(40f, vEff);

                // Gravity-aware TOF along slant: solve |p + v*dir*t + 0.5*g*t^2| ≈ use range/v with drop correction.
                Vector3 flat = delta;
                // Time estimate ignoring gravity then correct for vertical drop.
                float tGeom = dist / vEff;
                float drop = 0.5f * g * tGeom * tGeom;
                // Extra path for drop (small); re-estimate.
                float tNew = Mathf.Sqrt(dist * dist + drop * drop) / vEff;
                if (float.IsNaN(tNew) || float.IsInfinity(tNew) || tNew < 0f)
                {
                    t = float.NaN;
                    break;
                }

                t = Mathf.Lerp(t, tNew, 0.65f);
            }

            sol.EtaSec = t;
            sol.LeadWorld = lead;

            Vector3 aim = lead - muzzle;
            float leadAngle = 0f;
            if (aim.sqrMagnitude > 0.01f && sol.BoresightDir.sqrMagnitude > 0.01f)
            {
                leadAngle = Vector3.Angle(sol.BoresightDir, aim.normalized);
            }

            sol.LeadAngleDeg = leadAngle;

            bool ammoOk = weapon.Ammo > 0;
            bool finite = !float.IsNaN(t) && !float.IsInfinity(t) && t >= 0f && t < 30f;
            bool rangeOk = float.IsNaN(sol.MaxRangeMeters) || sol.RangeMeters <= sol.MaxRangeMeters;
            bool rangeComfort = float.IsNaN(sol.MaxRangeMeters) || sol.RangeMeters <= sol.MaxRangeMeters * 0.85f;
            bool leadHit = leadAngle <= HitLeadDeg;
            bool leadMarg = leadAngle <= MargLeadDeg;

            sol.CanHit = ammoOk && finite && rangeOk && leadMarg;
            if (!ammoOk || !finite || !rangeOk || !leadMarg)
            {
                sol.Verdict = HitVerdict.No;
            }
            else if (leadHit && rangeComfort)
            {
                sol.Verdict = HitVerdict.Hit;
            }
            else
            {
                sol.Verdict = HitVerdict.Marginal;
            }
        }

        private static void SolveMissile(
            ref HitSolution sol,
            in WeaponSnapshot weapon,
            Vector3 ownPos,
            Vector3 ownForward,
            Vector3 tgtPos,
            float closingSpeedMps)
        {
            float speed = weapon.MuzzleVelocity > 50f
                ? weapon.MuzzleVelocity
                : (weapon.MaxSpeed > 50f ? weapon.MaxSpeed : DefaultMissileSpeed);
            speed = Mathf.Clamp(speed, 100f, 2500f);

            float range = sol.RangeMeters;
            sol.EtaSec = range / speed;
            sol.LeadWorld = tgtPos; // missile seeker; lead ≈ current for display

            Vector3 los = (tgtPos - ownPos);
            float forwardDot = 0f;
            if (los.sqrMagnitude > 0.01f && ownForward.sqrMagnitude > 0.01f)
            {
                forwardDot = Vector3.Dot(ownForward.normalized, los.normalized);
            }

            sol.LeadAngleDeg = forwardDot > 0.999f
                ? 0f
                : Mathf.Acos(Mathf.Clamp(forwardDot, -1f, 1f)) * Mathf.Rad2Deg;

            // Closing factor: stretch Rmax when closing, shrink when fleeing.
            float closingFactor = 1f;
            if (closingSpeedMps > 20f)
            {
                closingFactor = 1.15f;
            }
            else if (closingSpeedMps < -20f)
            {
                closingFactor = 0.75f;
            }

            float rmax = float.IsNaN(sol.MaxRangeMeters) || sol.MaxRangeMeters <= 0f
                ? 40000f
                : sol.MaxRangeMeters;
            bool ammoOk = weapon.Ammo > 0;
            bool hemisphere = forwardDot > 0f; // roughly forward
            bool inRange = range < rmax * closingFactor;
            bool comfort = range < rmax * closingFactor * 0.85f;

            sol.CanHit = ammoOk && hemisphere && inRange;
            if (!sol.CanHit)
            {
                sol.Verdict = HitVerdict.No;
            }
            else if (comfort && forwardDot > 0.3f)
            {
                sol.Verdict = HitVerdict.Hit;
            }
            else
            {
                sol.Verdict = HitVerdict.Marginal;
            }
        }

        private static void SolveBombOrOther(
            ref HitSolution sol,
            in WeaponSnapshot weapon,
            Vector3 ownPos,
            Vector3 ownForward,
            Vector3 tgtPos)
        {
            // Rough freefall-ish ETA when looking down / near; otherwise N/A.
            Vector3 delta = tgtPos - ownPos;
            float horiz = new Vector3(delta.x, 0f, delta.z).magnitude;
            float ownSpeed = 200f;
            sol.LeadWorld = tgtPos;
            if (horiz > 5f)
            {
                sol.EtaSec = horiz / ownSpeed;
            }
            else
            {
                sol.EtaSec = float.NaN;
            }

            float forwardDot = 0f;
            if (delta.sqrMagnitude > 0.01f && ownForward.sqrMagnitude > 0.01f)
            {
                forwardDot = Vector3.Dot(ownForward.normalized, delta.normalized);
            }

            sol.LeadAngleDeg = forwardDot > 0.999f
                ? 0f
                : Mathf.Acos(Mathf.Clamp(forwardDot, -1f, 1f)) * Mathf.Rad2Deg;

            bool ammoOk = weapon.Ammo > 0;
            bool rangeOk = float.IsNaN(sol.MaxRangeMeters) || sol.RangeMeters <= sol.MaxRangeMeters;
            // Conservative CanHit for bombs.
            sol.CanHit = ammoOk && rangeOk && forwardDot > 0.5f && sol.RangeMeters < 8000f;
            sol.Verdict = sol.CanHit ? HitVerdict.Marginal : HitVerdict.No;
        }
    }
}
