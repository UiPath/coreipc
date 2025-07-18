#if NET461

namespace System.Linq;

internal static class EnumerableExtensions
{
    public static IEnumerable<T> Prepend<T>(this IEnumerable<T> enumerable, T element)
    {
        yield return element;
        foreach (var item in enumerable)
        {
            yield return item;
        }
    }
}

#endif