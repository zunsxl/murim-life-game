using System;
using System.Globalization;

namespace Murim.Core.Valley
{
    /// <summary>
    /// Immutable 2D float vector for deterministic Core simulation and layout queries.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;

        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public static Vec2 Zero => new Vec2(0f, 0f);
        public static Vec2 One => new Vec2(1f, 1f);

        public float LengthSquared => X * X + Y * Y;
        public float Length => MathF.Sqrt(LengthSquared);

        public Vec2 Normalized
        {
            get
            {
                float len = Length;
                return len > 0f ? new Vec2(X / len, Y / len) : Zero;
            }
        }

        public static float Dot(Vec2 a, Vec2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        public static float DistanceSquared(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static float Distance(Vec2 a, Vec2 b)
        {
            return MathF.Sqrt(DistanceSquared(a, b));
        }

        public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
        {
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator -(Vec2 v) => new Vec2(-v.X, -v.Y);
        public static Vec2 operator *(Vec2 v, float s) => new Vec2(v.X * s, v.Y * s);
        public static Vec2 operator *(float s, Vec2 v) => new Vec2(v.X * s, v.Y * s);
        public static Vec2 operator /(Vec2 v, float s) => new Vec2(v.X / s, v.Y / s);
        public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
        public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);

        public bool Equals(Vec2 other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is Vec2 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1})", X, Y);
        }
    }
}
