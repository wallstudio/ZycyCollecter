using System;
using System.Collections.Generic;
using System.Linq;

namespace ZycyUtility
{
    public static class CollectionUtility
    {

        public static T Sum<T>(this IEnumerable<T> source, Func<T, T, T> adder)
        {
            var sum = default(T);
            foreach (var item in source)
            {
                sum = adder(sum, item);
            }
            return sum;
        }

        public static IEnumerable<(T, T)> GetCombination<T>(this IEnumerable<T> source)
        {
            var buf = new LinkedList<T>(source);
            var dst = new List<(T, T)>();
            while (buf.Count > 0)
            {
                var a = buf.First();
                buf.Remove(a);
                foreach (var b in buf)
                {
                    dst.Add((a, b));
                }
            }
            return dst;
        }

        public static string ToStringJoin<T>(this IEnumerable<T> enumrable, string separator)
            => string.Join(separator, enumrable.Select(e => e.ToString()));
    
        public static IEnumerable<IEnumerable<T>> GroupByCount<T>(this IEnumerable<T> source, int maxCount)
        {
            var taged = source.Select((e, i) => new { g = i / maxCount, e });
            var grouped = taged.GroupBy(e => e.g);
            var typeReveted = grouped.Select(g => g.Select(e => e.e));
            return typeReveted;
        }
    
    }

}
