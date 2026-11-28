using System;

namespace Murim.Core.Valley
{
    /// <summary>
    /// Deterministic ray-against-heightfield and cutaway-cap raycaster per spec §3.1.
    /// </summary>
    public static class ValleyRaycaster
    {
        private const float Eps = 1e-5f;

        private struct DdaAxis
        {
            public int Cell;
            public int Step;
            public float TMax;
            public float TDelta;
        }

        public static RayHit Raycast(ValleyLayout layout, Vec3 origin, Vec3 dir, float cutY)
        {
            if (layout == null || dir.LengthSquared < 1e-12f)
            {
                return RayHit.None;
            }
            if (!ClipRayToGridXZ(origin, dir, layout.Width, layout.Depth, out float tMin, out float tMax))
            {
                return RayHit.None;
            }

            DdaAxis ax = InitAxis(origin.X, dir.X, tMin, layout.Width);
            DdaAxis az = InitAxis(origin.Z, dir.Z, tMin, layout.Depth);
            float tCurr = tMin;

            while (ax.Cell >= 0 && ax.Cell < layout.Width && az.Cell >= 0 && az.Cell < layout.Depth)
            {
                float tNext = MathF.Min(MathF.Min(ax.TMax, az.TMax), tMax);
                if (TryHitCell(layout, origin, dir, cutY, ax.Cell, az.Cell, tMin, tCurr, tNext, out RayHit hit))
                {
                    return hit;
                }
                if (tNext >= tMax - Eps)
                {
                    break;
                }
                AdvanceAxis(ref ax, ref az, ref tCurr);
            }
            return RayHit.None;
        }

        private static bool ClipRayToGridXZ(Vec3 origin, Vec3 dir, int width, int depth, out float tMin, out float tMax)
        {
            tMin = 0f;
            tMax = float.PositiveInfinity;
            return ClipSlab(origin.X, dir.X, width, ref tMin, ref tMax)
                && ClipSlab(origin.Z, dir.Z, depth, ref tMin, ref tMax);
        }

        private static bool ClipSlab(float originCoord, float dirCoord, float maxCoord, ref float tMin, ref float tMax)
        {
            if (MathF.Abs(dirCoord) < 1e-8f)
            {
                return originCoord >= 0f && originCoord <= maxCoord;
            }
            float inv = 1f / dirCoord;
            float t0 = -originCoord * inv;
            float t1 = (maxCoord - originCoord) * inv;
            if (t0 > t1)
            {
                float tmp = t0;
                t0 = t1;
                t1 = tmp;
            }
            tMin = MathF.Max(tMin, t0);
            tMax = MathF.Min(tMax, t1);
            return tMin <= tMax;
        }

        private static DdaAxis InitAxis(float originCoord, float dirCoord, float tMin, int maxCellCount)
        {
            float entry = originCoord + dirCoord * tMin;
            int cell = (int)MathF.Floor(entry);
            if (dirCoord < 0f && entry == cell && cell > 0)
            {
                cell--;
            }
            cell = Math.Clamp(cell, 0, maxCellCount - 1);

            if (MathF.Abs(dirCoord) < 1e-8f)
            {
                return new DdaAxis { Cell = cell, Step = 0, TMax = float.PositiveInfinity, TDelta = float.PositiveInfinity };
            }

            int step = dirCoord > 0f ? 1 : -1;
            float boundary = dirCoord > 0f ? cell + 1f : cell;
            float tMax = tMin + (boundary - entry) / dirCoord;
            float tDelta = MathF.Abs(1f / dirCoord);
            return new DdaAxis { Cell = cell, Step = step, TMax = tMax, TDelta = tDelta };
        }

        private static void AdvanceAxis(ref DdaAxis ax, ref DdaAxis az, ref float tCurr)
        {
            if (ax.TMax <= az.TMax)
            {
                tCurr = ax.TMax;
                ax.Cell += ax.Step;
                ax.TMax += ax.TDelta;
            }
            else
            {
                tCurr = az.TMax;
                az.Cell += az.Step;
                az.TMax += az.TDelta;
            }
        }

        private static bool TryHitCell(
            ValleyLayout layout,
            Vec3 origin,
            Vec3 dir,
            float cutY,
            int cx,
            int cz,
            float tMin,
            float tCurr,
            float tNext,
            out RayHit bestHit)
        {
            float bestT = float.PositiveInfinity;
            bestHit = RayHit.None;

            if (TryHitCap(layout, origin, dir, cutY, cx, cz, tMin, tCurr, tNext, out float capT, out Vec3 capPt))
            {
                bestT = capT;
                bestHit = new RayHit(RayHitKind.Cap, capPt, cx, cz);
            }

            if (TryHitGroundTriangles(layout, origin, dir, cutY, cx, cz, tMin, out float gT, out Vec3 gPt) && gT < bestT)
            {
                bestHit = new RayHit(RayHitKind.Ground, gPt, cx, cz);
            }
            return bestHit.Kind != RayHitKind.None;
        }

        private static bool TryHitCap(
            ValleyLayout layout,
            Vec3 origin,
            Vec3 dir,
            float cutY,
            int cx,
            int cz,
            float tMin,
            float tCurr,
            float tNext,
            out float hitT,
            out Vec3 hitPoint)
        {
            hitT = 0f;
            hitPoint = Vec3.Zero;
            if (float.IsPositiveInfinity(cutY) || MathF.Abs(dir.Y) < 1e-8f)
            {
                return false;
            }

            float t = (cutY - origin.Y) / dir.Y;
            if (t < tMin - Eps || t < tCurr - Eps || t > tNext + Eps)
            {
                return false;
            }

            float clampedT = MathF.Max(tMin, t);
            float hx = origin.X + dir.X * clampedT;
            float hz = origin.Z + dir.Z * clampedT;
            if (hz >= ValleyConstants.NorthUncutZ)
            {
                return false;
            }
            if (hx < cx - Eps || hx > cx + 1f + Eps || hz < cz - Eps || hz > cz + 1f + Eps)
            {
                return false;
            }
            if (layout.HeightAt(hx, hz) <= cutY)
            {
                return false;
            }

            hitT = clampedT;
            hitPoint = new Vec3(hx, cutY, hz);
            return true;
        }

        private static bool TryHitGroundTriangles(
            ValleyLayout layout,
            Vec3 origin,
            Vec3 dir,
            float cutY,
            int cx,
            int cz,
            float tMin,
            out float bestT,
            out Vec3 bestPoint)
        {
            bestT = float.PositiveInfinity;
            bestPoint = Vec3.Zero;

            Vec3 v00 = new Vec3(cx, layout.VertexHeight(cx, cz), cz);
            Vec3 v10 = new Vec3(cx + 1, layout.VertexHeight(cx + 1, cz), cz);
            Vec3 v01 = new Vec3(cx, layout.VertexHeight(cx, cz + 1), cz + 1);
            Vec3 v11 = new Vec3(cx + 1, layout.VertexHeight(cx + 1, cz + 1), cz + 1);

            CheckTriangle(origin, dir, cutY, tMin, v00, v10, v11, ref bestT, ref bestPoint);
            CheckTriangle(origin, dir, cutY, tMin, v00, v11, v01, ref bestT, ref bestPoint);
            return !float.IsPositiveInfinity(bestT);
        }

        private static void CheckTriangle(
            Vec3 origin,
            Vec3 dir,
            float cutY,
            float tMin,
            Vec3 a,
            Vec3 b,
            Vec3 c,
            ref float bestT,
            ref Vec3 bestPoint)
        {
            if (!IntersectTriangle(origin, dir, a, b, c, out float t) || t < tMin - Eps || t >= bestT)
            {
                return;
            }
            float clampedT = MathF.Max(tMin, t);
            Vec3 pt = origin + dir * clampedT;
            if (pt.Y <= cutY + Eps || pt.Z >= ValleyConstants.NorthUncutZ)
            {
                bestT = clampedT;
                bestPoint = pt;
            }
        }

        private static bool IntersectTriangle(Vec3 origin, Vec3 dir, Vec3 a, Vec3 b, Vec3 c, out float t)
        {
            t = 0f;
            Vec3 ab = b - a;
            Vec3 ac = c - a;
            Vec3 pVec = Vec3.Cross(dir, ac);
            float det = Vec3.Dot(ab, pVec);
            if (MathF.Abs(det) < 1e-8f)
            {
                return false;
            }

            float invDet = 1f / det;
            Vec3 tVec = origin - a;
            float u = Vec3.Dot(tVec, pVec) * invDet;
            if (u < -Eps || u > 1f + Eps)
            {
                return false;
            }

            Vec3 qVec = Vec3.Cross(tVec, ab);
            float v = Vec3.Dot(dir, qVec) * invDet;
            if (v < -Eps || u + v > 1f + Eps)
            {
                return false;
            }

            t = Vec3.Dot(ac, qVec) * invDet;
            return true;
        }
    }
}
