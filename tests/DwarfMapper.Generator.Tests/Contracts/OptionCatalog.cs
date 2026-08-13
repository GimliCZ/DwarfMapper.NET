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
    ///     The class-level options are PROPERTIES of one type (<see cref="DwarfMapperAttribute" />), so the
    ///     type-level <c>[DwarfSurface(ProbeKey = ...)]</c> — which sits on <c>DwarfMapperAttribute</c> itself
    ///     — cannot express a different fixture per property. This map is that irreducible remainder: it is
    ///     not a duplicate of the element-level demand, it is the SECOND demand source, scoped to properties
    ///     rather than types. <see cref="SelfValidation.SurfaceDeclarationTests.Every_declared_ProbeKey_binds_to_exactly_one_fixture" />
    ///     reads it directly, unioned with the element-level demand, so an option pointed at a key with no
    ///     fixture is reported rather than quietly assumed fine.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ProbeKeys { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["AutoNest"] = "nested-pair",
        ["AllowNonPublic"] = "internal-member",
        ["NameConvention"] = "snake-case-member",
        ["CaseInsensitive"] = "case-mismatched-member",
        ["IgnoreObsoleteMembers"] = "obsolete-member",
        ["SkipNullSourceMembers"] = "nullable-source-nonnull-target",
        ["NullStrategy"] = "nullable-value-to-nonnull",
        ["RequiredMapping"] = "unconsumed-source-member",
        ["EnumStrategy"] = "divergent-order-enums",
        ["EnumStringSource"] = "described-enum-to-string",
        ["NullCollections"] = "nullable-collection-rebuild",
        ["OnCycle"] = "recursive-graph",
        ["MaxDepth"] = "recursive-graph",
        ["ImplicitConversions"] = "narrowing-conversion",
        ["ReferenceHandling"] = "shared-reference-graph"
    };

    /// <summary>
    ///     Probe values that cannot be derived sensibly from the default. Only <c>MaxDepth</c> so far: the
    ///     generic rule for an int is "step it", which turns a default of 64 into 65 and binds on nothing.
    ///     A depth BUDGET needs a value below the graph to have any effect, and no amount of reflection over
    ///     an <c>int</c> property reveals that it is a limit rather than a count.
    /// </summary>
    private static readonly Dictionary<string, string> ProbeOverrides = new(StringComparer.Ordinal)
    {
        ["MaxDepth"] = "MaxDepth = 1"
    };

    public static IReadOnlyList<OptionInfo> Options { get; } = Build();

    /// <summary>Options with a shape that makes them observable — the ones the matrix can actually judge.</summary>
    public static IReadOnlyList<string> WithTriggeringShape { get; } =
        ProbeKeys.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

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
