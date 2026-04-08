using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;

namespace OtterApi.Converters;

public class OtterApiTypeConverter
{
    // One Func<string, object> per Type — built once on first use, reused forever.
    private static readonly ConcurrentDictionary<Type, Func<string, object>> ParseCache = new();

    public static object ChangeType(string value, Type type)
        => ParseCache.GetOrAdd(type, BuildParser)(value);

    private static Func<string, object> BuildParser(Type type)
    {
        // Fast paths: no reflection, no boxing-via-TypeConverter for the most common key types.
        if (type == typeof(string))   return static s => s;
        if (type == typeof(int))      return static s => (object)int.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(long))     return static s => (object)long.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(Guid))     return static s => (object)Guid.Parse(s);
        if (type == typeof(bool))     return static s => (object)bool.Parse(s);
        if (type == typeof(decimal))  return static s => (object)decimal.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(double))   return static s => (object)double.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(float))    return static s => (object)float.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(short))    return static s => (object)short.Parse(s, CultureInfo.InvariantCulture);
        if (type == typeof(DateTime)) return static s => (object)DateTime.Parse(s, CultureInfo.InvariantCulture);

        // Fallback: resolve the TypeConverter once and capture it in the closure.
        // Subsequent calls reuse the cached Func without touching TypeDescriptor again.
        var converter = TypeDescriptor.GetConverter(type);
        return s => converter.ConvertFromInvariantString(s)!;
    }
}