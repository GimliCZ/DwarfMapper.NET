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
    ///     The one thing that genuinely cannot be derived: the SHAPE that makes an option observable. No
    ///     amount of reflection over <c>AutoNest</c> yields "you need a nested class pair here". These are
    ///     inputs to the experiment, not a description of the API — and an option without one is reported as
    ///     "not probed" rather than quietly assumed fine.
    /// </summary>
    private static readonly Dictionary<string, string> TriggeringShapes = new(StringComparer.Ordinal)
    {
        ["AutoNest"] = """
            public sealed class Inner { public int X { get; set; } }
            public sealed class InnerDto { public int X { get; set; } }
            public sealed class Src { public int Id { get; set; } public Inner Child { get; set; } = new(); }
            public sealed class Dst { public int Id { get; set; } public InnerDto Child { get; set; } = new(); }
            """,

        ["AllowNonPublic"] = """
            public sealed class Src { public int Id { get; set; } internal string? Name { get; set; } }
            public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
            """,

        ["NameConvention"] = """
            public sealed class Src { public int Id { get; set; } public string? user_name { get; set; } }
            public sealed class Dst { public int Id { get; set; } public string? UserName { get; set; } }
            """,

        ["CaseInsensitive"] = """
            public sealed class Src { public int Id { get; set; } public string? name { get; set; } }
            public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
            """,

        ["IgnoreObsoleteMembers"] = """
            public sealed class Src { public int Id { get; set; } [System.Obsolete] public string? Name { get; set; } }
            public sealed class Dst { public int Id { get; set; } [System.Obsolete] public string? Name { get; set; } }
            """,

        ["SkipNullSourceMembers"] = """
            public sealed class Src { public int Id { get; set; } public string? Name { get; set; } }
            public sealed class Dst { public int Id { get; set; } public string Name { get; set; } = ""; }
            """,

        ["NullStrategy"] = """
            public sealed class Src { public int Id { get; set; } public int? Val { get; set; } }
            public sealed class Dst { public int Id { get; set; } public int Val { get; set; } }
            """,

        ["RequiredMapping"] = """
            public sealed class Src { public int Id { get; set; } public string? Name { get; set; } public int Extra { get; set; } }
            public sealed class Dst { public int Id { get; set; } public string? Name { get; set; } }
            """,

        ["ReferenceHandling"] = """
            public sealed class Inner { public int X { get; set; } }
            public sealed class InnerDto { public int X { get; set; } }
            public sealed class Src { public int Id { get; set; } public Inner? Child { get; set; } }
            public sealed class Dst { public int Id { get; set; } public InnerDto? Child { get; set; } }
            """
    };

    public static IReadOnlyList<OptionInfo> Options { get; } = Build();

    /// <summary>Options with a shape that makes them observable — the ones the matrix can actually judge.</summary>
    public static IReadOnlyList<string> WithTriggeringShape { get; } =
        TriggeringShapes.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

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
                    NonDefaultFor(p, def),
                    def,
                    TriggeringShapes.TryGetValue(p.Name, out var shape) ? shape : null);
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
