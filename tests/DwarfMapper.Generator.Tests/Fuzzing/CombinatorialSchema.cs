// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;

namespace DwarfMapper.Generator.Tests.Fuzzing;

// ── Cell descriptor ──────────────────────────────────────────────────────────

/// <summary>One cell in the combinatorial matrix: basic type × shape × variant.</summary>
public sealed record MatrixCell(
    string BasicType, // e.g. "int", "global::System.Guid"
    string ShapeName, // e.g. "raw", "array", "List", ...
    MatrixVariant Variant, // Identity or TypeDivergent
    string SrcType, // fully-qualified source type string
    string DstType, // fully-qualified destination type string (may differ from Src)
    string Source, // full C# source (using directives + types + mapper)
    int Seed // deterministic seed for this cell
);

public enum MatrixVariant
{
    /// <summary>Source and destination element/member type are identical.</summary>
    Identity,

    /// <summary>Destination element/member type is widened (e.g. int→long).</summary>
    TypeDivergent
}

// ── Basic type catalog ───────────────────────────────────────────────────────

/// <summary>
///     Enumerates the combinatorial matrix of basic types × shapes for Plan 19 Part E.
///     Each cell is self-contained: it includes all required C# source, the Src/Dst types,
///     and a deterministic seed.
/// </summary>
internal static class CombinatorialSchema
{
    // Every supported basic type (value types + string).
    // Also includes enums with int and non-int underlying types.
    private static readonly (string TypeName, string? WidenTo)[] BasicTypes =
    [
        // (typeName, widenTarget-or-null-if-same)
        ("bool", null),
        ("sbyte", "int"),
        ("byte", "int"),
        ("short", "int"),
        ("ushort", "uint"),
        ("int", "long"),
        ("uint", "long"),
        ("long", null), // no obvious widen inside supported set
        ("ulong", null),
        ("char", null),
        ("float", "double"),
        ("double", null),
        ("decimal", null),
        ("string", null),
        ("global::System.Guid", null),
        ("global::System.DateTime", null),
        ("global::System.DateTimeOffset", null),
        ("global::System.TimeSpan", null),
        ("Cmb_IntEnum", null), // enum : int
        ("Cmb_LongEnum", null) // enum : long
    ];

    /// <summary>
    /// Every generated cell is compiled in a NULLABLE-ANNOTATED context — what real consumers ship
    /// (<c>&lt;Nullable&gt;enable&lt;/Nullable&gt;</c> is the default for new .NET projects, and this repo's own
    /// Directory.Build.props sets it alongside TreatWarningsAsErrors).
    /// <para>
    /// Without this the whole combinatorial tier ran in the OBLIVIOUS world:
    /// <see cref="GeneratorTestHarness" /> defaults to <c>NullableContextOptions.Disable</c>, so every
    /// reference member came back with <c>NullableAnnotation.None</c> rather than Annotated/NotAnnotated.
    /// That is not a cosmetic difference — the generator branches on the annotation
    /// (<c>SourceMayBeNullRef</c>, the null-forgiving <c>!</c>, DWARF070), so the matrix was exercising code
    /// paths that production never takes and skipping the ones it does. Nullability has THREE states and we
    /// were only ever testing the third.
    /// </para>
    /// </summary>
    private const string NullableDirective = "#nullable enable";

    // All shapes at depth ≤1 (the exhaustive tier)
    private static readonly string[] DepthOneShapes =
    [
        "raw",
        "nullable",
        "array",
        "List",
        "IReadOnlyList",
        "HashSet",
        "ImmutableArray",

        // The rest of the supported System.* collection surface. The combinatorial tier previously covered
        // only a slice of it, so most supported targets were never crossed with the other axes (cycle mode,
        // update-into, null strategy…). Coverage of a target in ONE tier is not coverage of it in all of them.
        "IEnumerable",
        "ICollection",
        "IList",
        "IReadOnlyCollection",
        "ISet",
        "IReadOnlySet",
        "ImmutableList",
        "IImmutableList",
        "ImmutableHashSet",
        "IImmutableSet",

        "DictStringKey", // Dictionary<string, B>
        "DictStringValue", // Dictionary<B, string>  (only valid for value-types as key)
        "IDict", // IDictionary<string, B>
        "IReadOnlyDict", // IReadOnlyDictionary<string, B>
        "ImmutableDict", // ImmutableDictionary<string, B>
        "IImmutableDict", // IImmutableDictionary<string, B>

        "tuple", // (B, string) — ValueTuple was entirely absent from every schema
        "generic_box", // CmbBox<B> — a USER generic type, not a BCL one
        "nullable_ref", // string? — the nullable REFERENCE case (3-state NullableAnnotation)

        // string? SOURCE -> string TARGET. Probably the single commonest real-world DTO mapping there is, and
        // no schema had it: `nullable_ref` used string? on BOTH sides, so the nullability MISMATCH — the one
        // shape that makes the compiler emit CS8601 out of the generated file — was never generated at all.
        // This is the cell that DWARF070 exists for.
        "nullable_ref_mismatch",

        "nested_object",
        "record_type",
        "polymorphic_dispatch" // [MapDerivedType] dispatch — Plan 22 coverage
    ];

    // Depth-2 shapes (heavier — tagged exhaustive tier, sampled)
    private static readonly string[] DepthTwoShapes =
    [
        "ListOfList",
        "ListOfRecord",
        "DictStringListValue"
    ];

    // ── Public surface ───────────────────────────────────────────────────────

    /// <summary>
    ///     Enumerate all depth-≤1 cells (exhaustive default tier).
    ///     ~20 basic types × 11 shapes × 2 variants = ~440 cells, but some are pruned
    ///     (e.g. nullable is skipped for string and reference types; DictStringValue skipped
    ///     for non-value-type keys that can't be dict keys stably).
    /// </summary>
    public static IEnumerable<MatrixCell> DepthOneMatrix()
    {
        var seed = 0;
        foreach (var (bt, widenTo) in BasicTypes)
        foreach (var shape in DepthOneShapes)
        {
            var cell = TryBuildCell(bt, widenTo, shape, MatrixVariant.Identity, seed++);
            if (cell is not null) yield return cell;

            // Type-divergent variant: only when a widen target exists
            if (widenTo is not null)
            {
                var div = TryBuildCell(bt, widenTo, shape, MatrixVariant.TypeDivergent, seed++);
                if (div is not null) yield return div;
            }
        }
    }

    /// <summary>
    ///     Enumerate depth-2 cells — should be tagged [Trait("tier","exhaustive")].
    ///     Bounded: sample a small subset of basic types to keep the set manageable.
    /// </summary>
    public static IEnumerable<MatrixCell> DepthTwoMatrix()
    {
        // Sample a representative subset: int, string, Guid, DateTime, enum
        var sampledTypes = new[]
        {
            ("int", "long"),
            ("string", null),
            ("global::System.Guid", null),
            ("global::System.DateTime", null),
            ("Cmb_IntEnum", null)
        };
        var seed = 10_000;
        foreach (var (bt, widenTo) in sampledTypes)
        foreach (var shape in DepthTwoShapes)
        {
            var cell = TryBuildCell(bt, widenTo, shape, MatrixVariant.Identity, seed++);
            if (cell is not null) yield return cell;
        }
    }

    // ── Cell builder ────────────────────────────────────────────────────────────

    private static MatrixCell? TryBuildCell(
        string basicType, string? widenTo, string shape, MatrixVariant variant, int seed)
    {
        var srcElem = basicType;
        var dstElem = variant == MatrixVariant.TypeDivergent && widenTo is not null
            ? widenTo
            : basicType;

        // Filter: nullable is only valid for value types
        if (shape == "nullable" && !IsValueType(basicType)) return null;

        // Filter: DictStringValue requires a value-type or string key (to avoid duplicate key risk)
        if (shape == "DictStringValue" && !IsValidDictKey(basicType)) return null;

        // polymorphic_dispatch: special source builder — self-contained, not a member type
        if (shape == "polymorphic_dispatch")
        {
            var polySource = BuildPolymorphicDispatchSource(basicType, srcElem, dstElem, seed);
            if (polySource is null) return null;
            return new MatrixCell(basicType, shape, variant, "CmbPolyBase", "CmbPolyBaseDto", polySource, seed);
        }

        // Build source code
        var source = BuildSource(basicType, srcElem, dstElem, shape, seed);
        if (source is null) return null;

        var srcTypeStr = WrapShape(srcElem, shape, true);
        var dstTypeStr = WrapShape(dstElem, shape, false);

        return new MatrixCell(basicType, shape, variant, srcTypeStr, dstTypeStr, source, seed);
    }

    // ── Source builder ───────────────────────────────────────────────────────────

    private static string? BuildSource(
        string basicType, string srcElem, string dstElem, string shape, int seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// CombinatorialSchema auto-generated — do not edit");
        sb.AppendLine("// seed=" + seed.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine(NullableDirective);
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Collections.Immutable;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine();
        sb.AppendLine("namespace Cmb;");
        sb.AppendLine();

        // Shared enum declarations (always emitted so the type names resolve)
        sb.AppendLine("public enum Cmb_IntEnum  { A = 1, B = 2, C = 3 }");
        sb.AppendLine("public enum Cmb_LongEnum : long { A = 1, B = 2, C = 3 }");
        sb.AppendLine();

        // Emit nested helper types if needed
        if (shape is "nested_object" or "ListOfRecord" or "DictStringListValue")
        {
            // Src nested object
            sb.AppendLine("public class CmbNested_" + EscapeType(srcElem) + "_Src { public " + srcElem +
                          " Val { get; set; }" + DefaultInit(srcElem) + " }");
            // Dst nested object (may use dstElem)
            sb.AppendLine("public class CmbNested_" + EscapeType(dstElem) + "_Dst { public " + dstElem +
                          " Val { get; set; }" + DefaultInit(dstElem) + " }");
            sb.AppendLine();
        }

        if (shape == "record_type")
        {
            sb.AppendLine("public record CmbRecord_" + EscapeType(srcElem) + "_Src(" + srcElem + " Val);");
            sb.AppendLine("public record CmbRecord_" + EscapeType(dstElem) + "_Dst(" + dstElem + " Val);");
            sb.AppendLine();
        }

        if (shape == "generic_box")
        {
            // A USER-DEFINED generic. Every generic in every schema was a BCL type, so a user generic as a
            // member type — a completely ordinary thing to write — was never exercised anywhere.
            sb.AppendLine("public class CmbBox<T> { public T? Val { get; set; } }");
            sb.AppendLine();
        }

        if (shape == "ListOfRecord")
        {
            sb.AppendLine("public record CmbRecEl_" + EscapeType(srcElem) + "_Src(" + srcElem + " Val);");
            sb.AppendLine("public record CmbRecEl_" + EscapeType(dstElem) + "_Dst(" + dstElem + " Val);");
            sb.AppendLine();
        }

        // Src class
        var srcMember = ShapeMemberType(srcElem, shape, true);
        var dstMember = ShapeMemberType(dstElem, shape, false);
        if (srcMember is null || dstMember is null) return null;

        sb.AppendLine("public class CmbSrc");
        sb.AppendLine("{");
        sb.AppendLine("    public " + srcMember + " Val { get; set; }" + DefaultInit(srcMember));
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public class CmbDst");
        sb.AppendLine("{");
        sb.AppendLine("    public " + dstMember + " Val { get; set; }" + DefaultInit(dstMember));
        sb.AppendLine("}");
        sb.AppendLine();

        // Mapper
        sb.AppendLine("[global::DwarfMapper.DwarfMapper]");
        sb.AppendLine("public partial class CmbMapper");
        sb.AppendLine("{");
        sb.AppendLine("    public partial CmbDst Map(CmbSrc s);");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string? BuildPolymorphicDispatchSource(
        string basicType, string srcElem, string dstElem, int seed)
    {
        // For polymorphic_dispatch, the basic type is the member type on the concrete class.
        // We always use the same element type (srcElem==dstElem for Identity variant).
        // The abstract base and concrete source share the element type as a plain member.
        var sb = new StringBuilder();
        sb.AppendLine("// CombinatorialSchema auto-generated — do not edit");
        sb.AppendLine("// seed=" + seed.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("// shape=polymorphic_dispatch");
        sb.AppendLine(NullableDirective);
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Collections.Immutable;");
        sb.AppendLine();
        sb.AppendLine("namespace Cmb;");
        sb.AppendLine();
        sb.AppendLine("public enum Cmb_IntEnum  { A = 1, B = 2, C = 3 }");
        sb.AppendLine("public enum Cmb_LongEnum : long { A = 1, B = 2, C = 3 }");
        sb.AppendLine();

        // Abstract base source/dto
        sb.AppendLine("public abstract class CmbPolyBase { public string Tag { get; set; } = \"\"; }");
        sb.AppendLine("public class CmbPolyConcrete : CmbPolyBase { public " + srcElem + " Val { get; set; }" +
                      DefaultInit(srcElem) + " }");
        sb.AppendLine();
        sb.AppendLine("public abstract class CmbPolyBaseDto { public string Tag { get; set; } = \"\"; }");
        sb.AppendLine("public class CmbPolyConcreteDto : CmbPolyBaseDto { public " + dstElem + " Val { get; set; }" +
                      DefaultInit(dstElem) + " }");
        sb.AppendLine();

        // Src/Dst wrappers (for cell compatibility: CmbSrc/CmbDst still need to exist)
        sb.AppendLine("public class CmbSrc { public string Id { get; set; } = \"\"; }");
        sb.AppendLine("public class CmbDst { public string Id { get; set; } = \"\"; }");
        sb.AppendLine();

        sb.AppendLine("[global::DwarfMapper.DwarfMapper]");
        sb.AppendLine("public partial class CmbMapper");
        sb.AppendLine("{");
        sb.AppendLine("    // Plain map for CmbSrc→CmbDst (satisfies cell contract)");
        sb.AppendLine("    public partial CmbDst Map(CmbSrc s);");
        sb.AppendLine("    // Polymorphic dispatch: abstract base → base DTO");
        sb.AppendLine("    [global::DwarfMapper.MapDerivedType<CmbPolyConcrete, CmbPolyConcreteDto>]");
        sb.AppendLine("    public partial CmbPolyBaseDto MapPoly(CmbPolyBase b);");
        sb.AppendLine("    // Explicit overload for concrete type");
        sb.AppendLine("    public partial CmbPolyConcreteDto Map(CmbPolyConcrete c);");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Type helpers ─────────────────────────────────────────────────────────────

    private static string ShapeMemberType(string elem, string shape, bool src)
    {
        return shape switch
        {
            "raw" => elem,
            "nullable" => elem + "?",
            "array" => elem + "[]",
            "List" => $"global::System.Collections.Generic.List<{elem}>",
            "IReadOnlyList" => $"global::System.Collections.Generic.IReadOnlyList<{elem}>",
            "HashSet" => $"global::System.Collections.Generic.HashSet<{elem}>",
            "ImmutableArray" => $"global::System.Collections.Immutable.ImmutableArray<{elem}>",

            "IEnumerable" => $"global::System.Collections.Generic.IEnumerable<{elem}>",
            "ICollection" => $"global::System.Collections.Generic.ICollection<{elem}>",
            "IList" => $"global::System.Collections.Generic.IList<{elem}>",
            "IReadOnlyCollection" => $"global::System.Collections.Generic.IReadOnlyCollection<{elem}>",
            "ISet" => $"global::System.Collections.Generic.ISet<{elem}>",
            "IReadOnlySet" => $"global::System.Collections.Generic.IReadOnlySet<{elem}>",
            "ImmutableList" => $"global::System.Collections.Immutable.ImmutableList<{elem}>",
            "IImmutableList" => $"global::System.Collections.Immutable.IImmutableList<{elem}>",
            "ImmutableHashSet" => $"global::System.Collections.Immutable.ImmutableHashSet<{elem}>",
            "IImmutableSet" => $"global::System.Collections.Immutable.IImmutableSet<{elem}>",

            "DictStringKey" => $"global::System.Collections.Generic.Dictionary<string, {elem}>",
            "DictStringValue" => $"global::System.Collections.Generic.Dictionary<{elem}, string>",
            "IDict" => $"global::System.Collections.Generic.IDictionary<string, {elem}>",
            "IReadOnlyDict" => $"global::System.Collections.Generic.IReadOnlyDictionary<string, {elem}>",
            "ImmutableDict" => $"global::System.Collections.Immutable.ImmutableDictionary<string, {elem}>",
            "IImmutableDict" => $"global::System.Collections.Immutable.IImmutableDictionary<string, {elem}>",

            // ValueTuple: a structural type, not a named one — no schema generated it before.
            "tuple" => $"({elem}, string)",

            // A USER generic type. Every generic in the schemas was a BCL one, so a user-defined generic
            // member was never exercised at all.
            "generic_box" => $"CmbBox<{elem}>",

            // The nullable REFERENCE case. NullableAnnotation has THREE states (Annotated / NotAnnotated /
            // None-oblivious) and code that tests `== Annotated` silently drops the null guard on the third.
            "nullable_ref" => "string?",

            // The nullability MISMATCH: nullable on the source, non-nullable on the destination. The generated
            // assignment would raise CS8601 in the consumer's build if the emitter did not suppress it, and
            // DwarfMapper reports DWARF070 so the risk is not silent.
            "nullable_ref_mismatch" => src ? "string?" : "string",
            "nested_object" => $"CmbNested_{EscapeType(elem)}_{(src ? "Src" : "Dst")}",
            "record_type" => $"CmbRecord_{EscapeType(elem)}_{(src ? "Src" : "Dst")}",
            "ListOfList" => $"global::System.Collections.Generic.List<global::System.Collections.Generic.List<{elem}>>",
            "ListOfRecord" =>
                $"global::System.Collections.Generic.List<CmbRecEl_{EscapeType(elem)}_{(src ? "Src" : "Dst")}>",
            "DictStringListValue" =>
                $"global::System.Collections.Generic.Dictionary<string, global::System.Collections.Generic.List<{elem}>>",
            "polymorphic_dispatch" => src ? "CmbPolyBase" : "CmbPolyBaseDto", // handled by dedicated builder
            _ => null!
        };
    }

    private static string WrapShape(string elem, string shape, bool src)
    {
        return ShapeMemberType(elem, shape, src);
    }

    private static bool IsValueType(string t)
    {
        return t is not ("string" or "object")
               && (t.StartsWith("global::", StringComparison.Ordinal)
                   ? t is "global::System.Guid" or "global::System.DateTime"
                       or "global::System.DateTimeOffset" or "global::System.TimeSpan"
                   : t is "bool" or "sbyte" or "byte" or "short" or "ushort"
                       or "int" or "uint" or "long" or "ulong" or "char"
                       or "float" or "double" or "decimal"
                       or "Cmb_IntEnum" or "Cmb_LongEnum");
    }

    private static bool IsValidDictKey(string t)
    {
        // Only types that are valid Dictionary keys without risk of duplicates:
        // value types (each Create() call produces a different value) + string
        return IsValueType(t) || t == "string";
    }

    private static string EscapeType(string t)
    {
        return t.Replace("global::", "", StringComparison.Ordinal)
            .Replace(".", "_", StringComparison.Ordinal)
            .Replace("<", "_", StringComparison.Ordinal)
            .Replace(">", "_", StringComparison.Ordinal)
            .Replace("?", "N", StringComparison.Ordinal)
            .Replace("[", "_", StringComparison.Ordinal)
            .Replace("]", "_", StringComparison.Ordinal)
            .Replace(",", "_", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal);
    }

    /// <summary>The type arguments of a closed generic, verbatim (handles both 1-arg and K,V forms).</summary>
    private static string TypeArgsOf(string type)
    {
        var lt = type.IndexOf('<', StringComparison.Ordinal);
        var gt = type.LastIndexOf('>');
        return lt >= 0 && gt > lt ? type.Substring(lt + 1, gt - lt - 1).Trim() : "object";
    }

    private static string DefaultInit(string type)
    {
        if (type == "string") return " = \"\";";

        // `string?` — a nullable REFERENCE. No initialiser: null is the whole point of the shape.
        if (type.EndsWith('?')) return string.Empty;

        // ValueTuple is a struct; default(...) is valid and needs no initialiser.
        if (type.StartsWith('(')) return string.Empty;
        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            var elem = type[..^2];
            return " = global::System.Array.Empty<" + elem + ">();";
        }

        if (type.Contains("ImmutableArray<", StringComparison.Ordinal))
        {
            // ImmutableArray is a struct; default(ImmutableArray<T>) is valid (IsDefault==true)
            // Use ImmutableArray<T>.Empty for a safe non-null init
            // E.g. "global::System.Collections.Immutable.ImmutableArray<int>"
            // Extract the element type: everything between "<" and ">"
            var lt = type.LastIndexOf('<');
            var gt = type.LastIndexOf('>');
            if (lt >= 0 && gt > lt)
            {
                var elemT = type.Substring(lt + 1, gt - lt - 1).Trim();
                return " = global::System.Collections.Immutable.ImmutableArray<" + elemT + ">.Empty;";
            }

            return string.Empty;
        }

        if (type.StartsWith("global::System.Collections.Generic.List<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.HashSet<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.Dictionary<", StringComparison.Ordinal))
            return " = new();";
        // Interface collection types: cannot use new() — use concrete List<T> or new()
        // Ordered collection interfaces → back with a concrete List<T>; set interfaces → HashSet<T>.
        // (IReadOnlyList was the only one handled before, because it was the only one generated.)
        if (type.StartsWith("global::System.Collections.Generic.IEnumerable<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.ICollection<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IList<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlyCollection<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal))
        {
            var args = TypeArgsOf(type);
            return " = new global::System.Collections.Generic.List<" + args + ">();";
        }

        if (type.StartsWith("global::System.Collections.Generic.ISet<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlySet<", StringComparison.Ordinal))
        {
            var args = TypeArgsOf(type);
            return " = new global::System.Collections.Generic.HashSet<" + args + ">();";
        }

        if (type.StartsWith("global::System.Collections.Generic.IDictionary<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlyDictionary<", StringComparison.Ordinal))
        {
            var args = TypeArgsOf(type);
            return " = new global::System.Collections.Generic.Dictionary<" + args + ">();";
        }

        // The immutable family has NO public constructor — `new()` would not compile. They expose a static
        // Empty. (This block previously said `new()`, which was simply never exercised because none of these
        // shapes were ever generated.) The INTERFACE forms have no Empty of their own, so they are backed by
        // the corresponding concrete implementation.
        if (type.StartsWith("global::System.Collections.Immutable.", StringComparison.Ordinal))
        {
            var open = type.IndexOf('<', StringComparison.Ordinal);
            var name = type[..open];
            var concrete = name switch
            {
                "global::System.Collections.Immutable.IImmutableList" =>
                    "global::System.Collections.Immutable.ImmutableList",
                "global::System.Collections.Immutable.IImmutableSet" =>
                    "global::System.Collections.Immutable.ImmutableHashSet",
                "global::System.Collections.Immutable.IImmutableDictionary" =>
                    "global::System.Collections.Immutable.ImmutableDictionary",
                _ => name,
            };

            return " = " + concrete + "<" + TypeArgsOf(type) + ">.Empty;";
        }
        // Nested record types (have required ctor params) — cannot use new()
        if (type.StartsWith("CmbRecord_", StringComparison.Ordinal))
            return string.Empty; // will be set by the ctor; not initialized in class body
        // Other Cmb nested object types: use new()
        if (type.StartsWith("Cmb", StringComparison.Ordinal))
            return " = new();";
        return string.Empty;
    }
}
