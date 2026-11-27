using System;

namespace Murim.Core.World
{
    /// <summary>
    /// Deterministic back-to-front prop ordering (§3.2.5).
    /// </summary>
    public static class PropOrder
    {
        public static int[] BackToFront(PropInstance[] props)
        {
            if (props == null)
            {
                throw new ArgumentNullException(nameof(props));
            }

            int[] order = new int[props.Length];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = i;
            }

            Array.Sort(order, (a, b) => CompareIndices(props, a, b));
            return order;
        }

        private static int CompareIndices(PropInstance[] props, int a, int b)
        {
            PropInstance pa = props[a];
            PropInstance pb = props[b];

            int cmpZ = pb.ZCm.CompareTo(pa.ZCm);
            if (cmpZ != 0)
            {
                return cmpZ;
            }

            int cmpX = pa.XCm.CompareTo(pb.XCm);
            if (cmpX != 0)
            {
                return cmpX;
            }

            return a.CompareTo(b);
        }
    }
}
