namespace Murim.Core.World
{
    /// <summary>
    /// Deterministic PCG-XSH-RR 64/32 pseudo-random number generator (§3.1.2).
    /// </summary>
    public struct Pcg32
    {
        private ulong _state;
        private readonly ulong _inc;

        public Pcg32(ulong seed, ulong stream)
        {
            unchecked
            {
                _inc = (stream << 1) | 1UL;
                _state = 0UL;
                Next();
                _state += seed;
                Next();
            }
        }

        public uint Next()
        {
            unchecked
            {
                ulong old = _state;
                _state = old * 6364136223846793005UL + _inc;
                uint xs = (uint)(((old >> 18) ^ old) >> 27);
                int rot = (int)(old >> 59);
                return (xs >> rot) | (xs << ((-rot) & 31));
            }
        }

        public uint Bounded(uint n)
        {
            unchecked
            {
                uint thr = (0u - n) % n;
                while (true)
                {
                    uint r = Next();
                    if (r >= thr)
                    {
                        return r % n;
                    }
                }
            }
        }

        public int R(int lo, int hi)
        {
            uint span = (uint)(hi - lo + 1);
            return lo + (int)Bounded(span);
        }
    }

    /// <summary>
    /// Integer-only deterministic math, hash, noise, and curve primitives (§3.1.2).
    /// </summary>
    public static class Primitives
    {
        public static int FloorDiv(int a, int b)
        {
            int q = a / b;
            int r = a % b;
            if (r != 0 && ((r < 0) != (b < 0)))
            {
                q--;
            }

            return q;
        }

        public static long FloorDiv(long a, long b)
        {
            long q = a / b;
            long r = a % b;
            if (r != 0 && ((r < 0) != (b < 0)))
            {
                q--;
            }

            return q;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }

        public static int Clamp01(int t)
        {
            return Clamp(t, 0, 1024);
        }

        public static uint Hash32(int x, int z, uint salt)
        {
            unchecked
            {
                uint h = ((uint)x * 0x9E3779B1u) ^ ((uint)z * 0x85EBCA77u) ^ (salt * 0xC2B2AE3Du);
                h ^= h >> 16;
                h *= 0x7FEB352Du;
                h ^= h >> 15;
                h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        public static uint Salt(int k, uint seed)
        {
            return Hash32(k, 0x51ED, seed);
        }

        public static int S(int t)
        {
            long tl = t;
            return (int)((tl * tl * (3072L - 2L * tl)) >> 20);
        }

        public static long Isqrt(long a)
        {
            if (a <= 0)
            {
                return 0;
            }

            ulong n = (ulong)a;
            ulong res = 0;
            ulong bit = 1UL << 62;
            while (bit > n)
            {
                bit >>= 2;
            }

            while (bit != 0)
            {
                if (n >= res + bit)
                {
                    n -= res + bit;
                    res = (res >> 1) + bit;
                }
                else
                {
                    res >>= 1;
                }

                bit >>= 2;
            }

            return (long)res;
        }

        public static int Noise(int x, int z, int cell, uint salt)
        {
            int ix = FloorDiv(x, cell);
            int iz = FloorDiv(z, cell);
            int rx = x - ix * cell;
            int rz = z - iz * cell;
            int fx = FloorDiv(rx * 1024, cell);
            int fz = FloorDiv(rz * 1024, cell);

            int c00 = Corner(ix, iz, salt);
            int c10 = Corner(ix + 1, iz, salt);
            int c01 = Corner(ix, iz + 1, salt);
            int c11 = Corner(ix + 1, iz + 1, salt);

            int sx = S(fx);
            int sz = S(fz);
            int n0 = c00 + (((c10 - c00) * sx) >> 10);
            int n1 = c01 + (((c11 - c01) * sx) >> 10);
            return n0 + (((n1 - n0) * sz) >> 10);
        }

        public static int Bump(int d, int inner, int outer)
        {
            if (d <= inner)
            {
                return 1024;
            }

            if (d >= outer)
            {
                return 0;
            }

            return S(FloorDiv((outer - d) * 1024, outer - inner));
        }

        internal static int FindFirstZAtLeast(short[] field, int x, int threshold)
        {
            for (int z = 0; z < ValleyWorld.VertsPerSide; z++)
            {
                if (field[z * ValleyWorld.VertsPerSide + x] >= threshold)
                {
                    return z;
                }
            }

            // Unreachable for valid WorldConfig inputs and thresholds <= 1024: at z = 512,
            // v - V2 >= 15350 cm > hwv2, so both s1 and s2 saturate to 1024.
            return ValleyWorld.SizeM;
        }

        private static int Corner(int i, int j, uint salt)
        {
            return (int)(Hash32(i, j, salt) & 2047u) - 1024;
        }
    }
}
