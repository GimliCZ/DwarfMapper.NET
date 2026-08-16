// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.Contracts;

/// <summary>One class-level option AT ONE VALUE, discovered by scanning the assembly rather than listed by hand.</summary>
/// <param name="Name">The property name, as written inside <c>[DwarfMapper(...)]</c>.</param>
/// <param name="ValueLabel">
///     The value text alone — <c>NullStrategy.SetDefault</c>, <c>true</c>, <c>1</c>. An option whose domain
///     holds more than one non-default value contributes one row PER value, and this is what tells those rows
///     apart: in test output, in the <c>TheoryData</c> key, and in the <c>Single</c> lookups that resolve a
///     theory argument back to its row. Keying on <see cref="Name" /> alone would collapse them.
/// </param>
/// <param name="NonDefault">A C# initialiser setting it to something other than its default.</param>
/// <param name="Default">The value a caller gets when they do not set it.</param>
/// <param name="Types">
///     A DTO pair shaped to trigger this option, when one is needed. Null means the default flat pair.
/// </param>
public sealed record OptionInfo(
    string Name, string ValueLabel, string NonDefault, object? Default, string? Types);

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
    ///     <para>
    ///         An override REPLACES the derived domain, so on a multi-member enum it would drop every member
    ///         it does not name. That is not re-checked here: the same declarations are validated once, in
    ///         <see cref="SurfaceCatalog.ValidateProbeClaims" />, which refuses a <c>Value</c> whose property's
    ///         derived domain holds more than one member. A second copy of the rule could only drift from it.
    ///     </para>
    /// </summary>
    private static readonly Dictionary<string, string> ProbeOverrides =
        typeof(DwarfMapperAttribute).GetCustomAttributes<DwarfSurfaceProbeAttribute>(inherit: false)
            .Where(a => a.Property is not null && a.Value is not null)
            .ToDictionary(a => a.Property!, a => a.Value!, StringComparer.Ordinal);

    public static IReadOnlyList<OptionInfo> Options { get; } = Build();

    private static List<OptionInfo> Build()
    {
        var probe = new DwarfMapperAttribute();

        return typeof(DwarfMapperAttribute)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanWrite: true, CanRead: true } && p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .SelectMany(p =>
            {
                var def = p.GetValue(probe);
                var types = ProbeKeys.TryGetValue(p.Name, out var key) ? SurfaceFixtures.Get(key) : null;
                IReadOnlyList<string> domain = ProbeOverrides.TryGetValue(p.Name, out var over)
                    ? [over]
                    : ValueDomain(p, def);

                return domain.Select(v => new OptionInfo(p.Name, v, $"{p.Name} = {v}", def, types));
            })
            .ToList();
    }

    /// <summary>
    ///     Every value of <paramref name="p" /> that DIFFERS from its default — the option's probe domain,
    ///     one matrix row each. Returning something equal to the default would make the cell read "no change"
    ///     and the matrix would look authoritative while measuring nothing, which is exactly what the
    ///     hand-written list did for <c>RequiredMapping</c>, where someone had typed the default as the probe.
    ///     <para>
    ///         The enum branch enumerates the WHOLE domain. It used to take
    ///         <c>FirstOrDefault(v =&gt; !v.Equals(def))</c> — complete for a two-member enum, which is every
    ///         option enum shipped today, and silently dropping members two and three the day one grows. A
    ///         partially-probed enum reads in the matrix exactly like a fully-covered one, so the omission
    ///         would have been invisible at the moment it was introduced.
    ///     </para>
    ///     <para>
    ///         The default value is deliberately NOT probed. A probe that sets an option to the value it
    ///         already has is byte-identical to the baseline at every endpoint, so it can only ever read
    ///         <c>Silent</c> — silent by construction, whatever the generator does. That is the same reason
    ///         <c>[DwarfMapper]</c>'s own zero-argument case carries an <c>Unmeasured</c> declaration.
    ///     </para>
    /// </summary>
    internal static IReadOnlyList<string> ValueDomain(PropertyInfo p, object? def)
    {
        var t = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType;

        if (t == typeof(bool))
            return [def is true ? "false" : "true"];

        if (t.IsEnum)
        {
            var alternatives = Enum.GetValues(t).Cast<object>()
                .Where(v => !v.Equals(def))
                .Select(v => $"{t.Name}.{v}")
                .ToList();
            if (alternatives.Count == 0)
                throw new InvalidOperationException(
                    $"Enum option {p.Name} has only one value, so no non-default probe exists.");
            return alternatives;
        }

        if (t == typeof(int))
            return [string.Create(CultureInfo.InvariantCulture, $"{(def is int i ? i + 1 : 1)}")];

        if (t == typeof(string))
            return ["\"probe\""];

        throw new InvalidOperationException(
            $"No non-default probe strategy for option {p.Name} of type {t.Name}. Add one rather than "
            + "letting it silently fall out of the matrix.");
    }
}
