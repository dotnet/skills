using System;
using System.Collections.Generic;
using System.Linq;

public static class TimeFormatting
{
    public static string SetPrecision(IEnumerable<TimeSpan> parts) =>
        string.Join(", ", parts.Where(p => p != TimeSpan.Zero).Select(p => p.ToString()).ToList());

    public static string FormatCollection(IEnumerable<object> values) =>
        string.Join(", ", values.Select(v => v.ToString()).Where(v => v is not null).ToArray());

    public static string FormatObjects(IEnumerable<object> values) =>
        string.Concat(values.Select(v => v.GetType()).Select(t => t.Name));
}

public static class TransformExtensions
{
    public static string Transform(this string value, params Func<string, string>[] transforms) =>
        transforms.Aggregate(value, (current, transform) => transform(current));
}