using System;
using System.Collections.Generic;
using System.Linq;

namespace LoveEditor;

static class Json
{
    public static object? Get(object? value, string key) =>
        value is Dictionary<string, object> map && map.TryGetValue(key, out var result) ? result : null;

    public static object? Get(object? value, params string[] path)
    {
        foreach (var key in path) value = Get(value, key);
        return value;
    }

    public static string? Str(object? value, string key) => Get(value, key) as string;

    public static int Int(object? value, string key, int fallback = 0) => Get(value, key) switch
    {
        int number => number,
        long number => (int)number,
        decimal number => (int)number,
        _ => fallback,
    };

    public static IEnumerable<object> Items(object? value) => value switch
    {
        object[] array => array,
        System.Collections.ArrayList list => list.Cast<object>(),
        _ => Enumerable.Empty<object>(),
    };

    public static IEnumerable<object> Items(object? value, string key) => Items(Get(value, key));
}
