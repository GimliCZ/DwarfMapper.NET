// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>One class-level option, discovered by scanning the assembly rather than listed by hand.</summary>
/// <param name="Name">The property name, as written inside <c>[DwarfMapper(...)]</c>.</param>
/// <param name="NonDefault">A C# initialiser setting it to something other than its default.</param>
/// <param name="Default">The value a caller gets when they do not set it.</param>
/// <param name="Types">
///     A DTO pair shaped to trigger this option, when one is needed. Null means the default flat pair.
/// </param>
public sealed record OptionInfo(string Name, string NonDefault, object? Default, string? Types);

/// <summary>
///     The class-level <c>[DwarfMapper]</c> options, SCANNED from the attribute and with their non-default
///     values DERIVED.
///     <para>
///         An earlier version kept a hand-written list of sixteen options and sixteen non-default strings.
///         That list was the weakest part of the whole arrangement: it existed only because someone typed it,
///         it had to be edited whenever an option was added or a default changed, and a matrix built from it
///         documented the list rather than the library. A growth ratchet caught omissions, which is not the
///         same as not having the problem.
///     </para>
///     <para>
///         Everything derivable is now derived. The option set is the attribute's writable properties; each
///         default is read from a fresh instance; each non-default is computed from the default (invert a
///         bool, pick another enum member, step an int). Add option 17 and it appears here, in the contract
///         tests, and in the generated matrix, with no list to remember to update.
///     </para>
/// </summary>
public static class OptionCatalog
{
    /// <summary>
    ///     Each option's fixture, READ OFF <see cref="DwarfMapperAttribute" />'s own
    ///     <c>[DwarfSurfaceProbe]</c> declarations rather than listed here.
    ///     <para>
    ///         This used to be a hand-written option → key map, kept here because the type-level
    ///         <c>[DwarfSurface(ProbeKey = ...)]</c> could not express one fixture per property. It can now,
    ///         so the map is derived and the option matrix and the surface matrix read the SAME declaration:
    ///         one way to say a thing. A second copy would have been free to drift, and a drifted copy points
    ///         one of the two matrices at a shape that cannot trigger the option it is measuring.
    ///     </para>
    /// </summary>
    internal static IReadOnlyDictionary<string, string> ProbeKeys { get; } =
        typeof(DwarfMapperAttribute).GetCustomAttributes<DwarfSurfaceProbeAttribute>(inherit: false)
            .Where(a => a.Property is not null && a.ProbeKey is not null)
            .ToDictionary(a => a.Property!, a => a.ProbeKey!, StringComparer.Ordinal);

    /// <summary>
    ///     Probe values that cannot be derived sensibly from the default — also read off the declaration,
    ///     where <c>[DwarfSurfaceProbe(nameof(MaxDepth), Value = "1")]</c> states it. The generic rule for an
    ///     int is "step it", which turns a default of 64 into 65 and binds on nothing; a depth BUDGET needs a
    ///     value below the graph to have any effect, and no amount of reflection over an <c>int</c> property
    ///     reveals that it is a limit rather than a count.
    /// </summary>
    private static readonly Dictionary<string, string> ProbeOverrides =
        typeof(DwarfMapperAttribute).GetCustomAttributes<DwarfSurfaceProbeAttribute>(inherit: false)
            .Where(a => a.Property is not null && a.Value is not null)
            .ToDictionary(a => a.Property!, a => $"{a.Property} = {a.Value}", StringComparer.Ordinal);

    public static IReadOnlyList<OptionInfo> Options { get; } = Build();

    private static List<OptionInfo> Build()
    {
        var probe = new DwarfMapperAttribute();

        return typeof(DwarfMapperAttribute)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, CanRead: true } && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p =>
            {
                var def = p.GetValue(probe);
                return new OptionInfo(
                    p.Name,
                    ProbeOverrides.TryGetValue(p.Name, out var over) ? over : NonDefaultFor(p, def),
                    def,
                    ProbeKeys.TryGetValue(p.Name, out var key) ? SurfaceFixtures.Get(key) : null);
            })
            .ToList();
    }

    /// <summary>
    ///     Builds an initialiser that differs from the default. Returning something equal to the default
    ///     would make every cell read "no change" and the matrix would look authoritative while measuring
    ///     nothing — which is exactly what the hand-written list did for <c>RequiredMapping</c>, where
    ///     someone had typed the default value as the probe.
    /// </summary>
    private static string NonDefaultFor(PropertyInfo p, object? def)
    {
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

        if (t == typeof(bool))
            return $"{p.Name} = {(def is true ? "false" : "true")}";

        if (t.IsEnum)
        {
            var alternative = Enum.GetValues(t).Cast<object>().FirstOrDefault(v => !v.Equals(def));
            if (alternative is null)
                throw new InvalidOperationException(
                    $"Enum option {p.Name} has only one value, so no non-default probe exists.");
            return $"{p.Name} = {t.Name}.{alternative}";
        }

        if (t == typeof(int))
            return string.Create(CultureInfo.InvariantCulture, $"{p.Name} = {(def is int i ? i + 1 : 1)}");

        if (t == typeof(string))
            return $"{p.Name} = \"probe\"";

        throw new InvalidOperationException(
            $"No non-default probe strategy for option {p.Name} of type {t.Name}. Add one rather than "
            + "letting it silently fall out of the matrix.");
    }
}
