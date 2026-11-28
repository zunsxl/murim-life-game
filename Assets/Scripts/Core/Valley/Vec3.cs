using System;
using System.Globalization;

namespace Murim.Core.Valley
{
    /// <summary>
    /// Immutable 3D float vector for deterministic Core simulation and raycast queries.
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public Vec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vec3 Zero => new Vec3(0f, 0f, 0f);
        public static Vec3 One => new Vec3(1f, 1f, 1f);

        public Vec2 XZ => new Vec2(X, Z);
        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => MathF.Sqrt(LengthSquared);

        public Vec3 Normalized
        {
            get
            {
                float len = Length;
                return len > 0f ? new Vec3(X / len, Y / len, Z / len) : Zero;
            }
        }

        public static float Dot(Vec3 a, Vec3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        public static Vec3 Cross(Vec3 a, Vec3 b)
        {
            return new Vec3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X);
        }

        public static float DistanceSquared(Vec3 a, Vec3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public static float Distance(Vec3 a, Vec3 b)
        {
            return MathF.Sqrt(DistanceSquared(a, b));
        }

        public static Vec3 Lerp(Vec3 a, Vec3 b, float t)
        {
            return new Vec3(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t);
        }

        public static Vec3 operator +(Vec3 a, Vec3 b) => new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec3 operator -(Vec3 a, Vec3 b) => new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec3 operator -(Vec3 v) => new Vec3(-v.X, -v.Y, -v.Z);
        public static Vec3 operator *(Vec3 v, float s) => new Vec3(v.X * s, v.Y * s, v.Z * s);
        public static Vec3 operator *(float s, Vec3 v) => new Vec3(v.X * s, v.Y * s, v.Z * s);
        public static Vec3 operator /(Vec3 v, float s) => new Vec3(v.X / s, v.Y / s, v.Z / s);
        public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);
        public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

        public bool Equals(Vec3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
        }
    }
}
