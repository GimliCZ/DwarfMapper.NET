// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;
using DwarfMapper.Testing;

namespace DwarfMapper.Generator.Tests.Fuzzing;

public class BehavioralFuzzTests
{
    public static IEnumerable<object[]> Seeds()
    {
        return Enumerable.Range(0, 60).Select(i => new object[] { i });
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Identity_shape_mapping_is_value_preserving(int seed)
    {
        // GenerateBehavioral excludes HashSet<T> so the positional IEnumerable
        // walk below does not produce false diffs from non-deterministic
        // HashSet enumeration order.
        var src = SyntheticSchema.GenerateBehavioral(seed);
        var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
        Assert.True(asm is not null,
            $"seed={seed} failed to emit: {string.Join(", ", errors.Select(e => e.Id))}\n{src}");

        var srcType = asm!.GetType("Fuzz.Src")!;
        var dstType = asm.GetType("Fuzz.Dst")!;
        var mapperType = asm.GetType("Fuzz.FuzzMapper")!;
        var mapper = Activator.CreateInstance(mapperType)!;
        var map = mapperType.GetMethod("Map")!;

        var srcInstance = ObjectFactory.Create(srcType, new Random(seed), 0)!;
        var dstInstance = map.Invoke(mapper, new[] { srcInstance })!;

        // StructuralComparer.Diff(src, dst) cannot be used directly when src and
        // dst are different runtime types (even if structurally identical): it
        // uses expected.GetType() to discover PropertyInfos and then calls
        // p.GetValue(actual), which throws TargetException when actual is a
        // different type. Instead we compare member-by-member via name lookup.
        var diffs = CrossTypeStructuralDiff("root", srcType, srcInstance, dstType, dstInstance, 0);

        Assert.True(diffs.Count == 0,
            $"seed={seed} not value-preserving:\n{RenderDiffs(diffs)}\n--- source ---\n{src}");
    }

    private static List<Diff> CrossTypeStructuralDiff(
        string path, Type srcType, object srcVal, Type dstType, object dstVal, int depth)
    {
        var diffs = new List<Diff>();
        CompareByName(path, srcType, srcVal, dstType, dstVal, diffs, depth);
        return diffs;
    }

    private static void CompareByName(
        string path, Type srcType, object srcVal, Type dstType, object dstVal,
        List<Diff> diffs, int depth)
    {
        const int MaxDepth = 8;
        if (depth > MaxDepth) return;

        foreach (var sp in srcType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!sp.CanRead || sp.GetIndexParameters().Length != 0) continue;

            var dp = dstType.GetProperty(sp.Name, BindingFlags.Public | BindingFlags.Instance);
            if (dp is null || !dp.CanRead) continue;

            var sv = sp.GetValue(srcVal);
            var dv = dp.GetValue(dstVal);

            CompareValues(path + "." + sp.Name, sv, dv, diffs, depth + 1);
        }

        // Also compare public fields (FuzzInner uses fields)
        foreach (var sf in srcType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var df = dstType.GetField(sf.Name, BindingFlags.Public | BindingFlags.Instance);
            if (df is null) continue;

            var sv = sf.GetValue(srcVal);
            var dv = df.GetValue(dstVal);

            CompareValues(path + "." + sf.Name, sv, dv, diffs, depth + 1);
        }
    }

    private static void CompareValues(string path, object? sv, object? dv, List<Diff> diffs, int depth)
    {
        const int MaxDepth = 8;
        if (depth > MaxDepth) return;

        if (sv is null && dv is null) return;
        if (sv is null || dv is null)
        {
            diffs.Add(new Diff(path, Fmt(sv), Fmt(dv)));
            return;
        }

        var svType = sv.GetType();
        var dvType = dv.GetType();

        // Scalar: compare directly
        if (IsScalar(svType))
        {
            if (!ScalarEquals(sv, dv))
                diffs.Add(new Diff(path, Fmt(sv), Fmt(dv)));
            return;
        }

        // Collections (arrays, List<T>) — compare element-by-element
        if (sv is IEnumerable se && dv is IEnumerable de)
        {
            var sl = ToList(se);
            var dl = ToList(de);
            if (sl.Count != dl.Count)
                diffs.Add(new Diff(path + ".Count",
                    sl.Count.ToString(CultureInfo.InvariantCulture),
                    dl.Count.ToString(CultureInfo.InvariantCulture)));
            var n = Math.Min(sl.Count, dl.Count);
            for (var i = 0; i < n; i++)
                CompareValues(
                    path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]",
                    sl[i], dl[i], diffs, depth + 1);
            return;
        }

        // Struct / class — recurse by member name
        if (svType == dvType)
        {
            // Same type: use StructuralComparer directly (it's safe when types match)
            var nested = StructuralComparer.Diff(sv, dv);
            // C9: strip the "root" prefix properly (was incorrectly stripping individual chars).
            foreach (var d in nested)
            {
                const string rootPrefix = "root";
                var strippedPath = d.Path.StartsWith(rootPrefix, StringComparison.Ordinal)
                    ? d.Path[rootPrefix.Length..]
                    : d.Path;
                diffs.Add(new Diff(path + strippedPath, d.Expected, d.Actual));
            }

            return;
        }

        // Different runtime types with same shape (e.g. nested FuzzInner vs FuzzInner from another seed asm)
        CompareByName(path, svType, sv, dvType, dv, diffs, depth + 1);
    }

    private static bool IsScalar(Type t)
    {
        return t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
               || t == typeof(Guid) || t == typeof(DateTime) || t == typeof(DateTimeOffset)
               || t == typeof(TimeSpan);
    }

    private static bool ScalarEquals(object a, object b)
    {
        if (a is double da && b is double db) return Math.Abs(da - db) < 1e-9;
        if (a is float fa && b is float fb) return Math.Abs(fa - fb) < 1e-6f;
        // Enums from different assemblies have the same underlying value but different Type;
        // compare by their underlying integral value.
        if (a.GetType().IsEnum && b.GetType().IsEnum)
            return Convert.ToInt64(a, CultureInfo.InvariantCulture)
                   == Convert.ToInt64(b, CultureInfo.InvariantCulture);
        return Equals(a, b);
    }

    private static List<object?> ToList(IEnumerable e)
    {
        var l = new List<object?>();
        foreach (var x in e) l.Add(x);
        return l;
    }

    private static string? Fmt(object? o)
    {
        return o switch
        {
            null => null,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => o.ToString()
        };
    }

    private static string RenderDiffs(IReadOnlyList<Diff> diffs)
    {
        var sb = new StringBuilder();
        foreach (var d in diffs)
            sb.Append("  ").Append(d.Path).Append(": expected ").Append(d.Expected ?? "<null>")
                .Append(", actual ").Append(d.Actual ?? "<null>").Append('\n');
        return sb.ToString();
    }

    // ── Cross-type structural diff ────────────────────────────────────────────

    private sealed record Diff(string Path, string? Expected, string? Actual);
}
