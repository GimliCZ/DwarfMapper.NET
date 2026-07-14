// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Text;

namespace DwarfMapper.Generator.Tests.Fuzzing;

/// <summary>
///     Deterministic synthetic schema generator for property-based fuzz tests.
///     Given an integer seed, produces a complete, valid C# source string that
///     declares Src/Dst classes with identical members and a [DwarfMapper] mapper.
///     Same seed always produces the same source (only new Random(seed) is used).
/// </summary>
internal static class SyntheticSchema
{
    // ── Type pool ──────────────────────────────────────────────────────────
    // Weights: higher index = more likely to be picked (weighted pool via
    // flat-list expansion so we stay on plain Random).

    private static readonly string[] ScalarTypes =
    [
        "int", "int", "int", // weight 3 – most common
        "long",
        "short",
        "byte",
        "double", "double",
        "float",
        "bool",
        "string", "string", // weight 2
        "global::System.Guid",
        "global::System.DateTime",
        // ── Extended scalar pool ───────────────────────────────────────────
        "decimal",
        "char",
        "sbyte",
        "uint",
        "ushort",
        "ulong",
        "global::System.DateTimeOffset",
        "global::System.TimeSpan"
    ];
    // ScalarTypes has 21 entries (indices 0..20).

    // All value-type scalars (for T? and collection element types).
    // string is NOT included here — it is a reference type and is not valid for T?.
    // char, decimal, unsigned ints, DateTimeOffset, TimeSpan are all value types.
    private static readonly string[] ValueScalars =
    [
        "int", "long", "short", "byte", "double", "float", "bool",
        "global::System.Guid", "global::System.DateTime",
        "decimal",
        "char",
        "sbyte",
        "uint",
        "ushort",
        "ulong",
        "global::System.DateTimeOffset",
        "global::System.TimeSpan"
    ];

    // For collection element types: value scalars + string (ref is fine in collections)
    private static readonly string[] CollectionElements =
    [
        "int", "int",
        "long",
        "double",
        "float",
        "bool",
        "string",
        "global::System.Guid",
        "global::System.DateTime",
        "FuzzEnum",
        "FuzzEnumL",
        "decimal",
        "char",
        "sbyte",
        "uint",
        "ushort",
        "ulong",
        "global::System.DateTimeOffset",
        "global::System.TimeSpan"
    ];

    /// <summary>
    ///     Generates a schema suitable for behavioral (value-preserving) property tests.
    ///     Identical to <see cref="Generate" /> except <c>HashSet&lt;T&gt;</c> members are excluded:
    ///     <c>StructuralComparer</c> compares <c>IEnumerable</c> element-by-element in iteration
    ///     order, but <c>HashSet&lt;T&gt;</c> enumeration order is an implementation detail that may
    ///     diverge between the source and destination instances even when the sets are equal.
    ///     All other types (arrays, lists, nullable value types, enums, nested structs, scalars)
    ///     are included as normal.
    /// </summary>
    public static string GenerateBehavioral(int seed)
    {
        var rng = new Random(seed);

        var memberCount = rng.Next(1, 11); // 1..10 members

        var members = new (string Name, string Type)[memberCount];
        for (var i = 0; i < memberCount; i++) members[i] = ($"Member{i}", PickTypeBehavioral(rng));

        return BuildSource(members);
    }

    /// <summary>Item 5: the same behavioural Src/Dst shape emitted under a chosen reference-handling /
    /// update-into mode. Member SELECTION is identical for a given seed across modes, so an instance built
    /// from the same seed is structurally identical and the four modes' outputs can be compared directly.</summary>
    public static string GenerateBehavioralForMode(int seed, EmitMode mode)
    {
        var rng = new Random(seed);
        int memberCount = rng.Next(1, 11);
        var members = new (string Name, string Type)[memberCount];
        for (int i = 0; i < memberCount; i++)
            members[i] = ($"Member{i}", PickTypeBehavioral(rng));
        return BuildSource(members, mode);
    }

    /// <summary>
    ///     Generates a schema that sometimes emits advanced features:
    ///     [MapDerivedType] dispatch, [FlattenGraph] homo, or top-level collection method.
    ///     Deterministic from seed; always produces valid compilable C#.
    /// </summary>
    public static string GenerateWithAdvancedFeatures(int seed)
    {
        var rng = new Random(seed);
        // Pick feature variant based on seed
        var featureVariant = rng.Next(0, 4);
        // 0 = plain (same as Generate), 1 = MapDerivedType, 2 = FlattenGraph homo, 3 = top-level collection

        var memberCount = rng.Next(1, 6); // keep small for advanced features
        var members = new (string Name, string Type)[memberCount];
        for (var i = 0; i < memberCount; i++)
            members[i] = ($"Member{i}", PickTypeBehavioral(rng));

        return featureVariant switch
        {
            1 => BuildSourceWithMapDerivedType(members, seed),
            2 => BuildSourceWithFlattenGraph(members, seed),
            3 => BuildSourceWithTopLevelCollection(members, seed),
            _ => BuildSource(members)
        };
    }

    private static string BuildSourceWithMapDerivedType((string Name, string Type)[] members, int seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated by SyntheticSchema (advanced:MapDerivedType) — do not edit");
        sb.AppendLine("// seed=" + seed.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine();
        sb.AppendLine("namespace Fuzz;");
        sb.AppendLine();
        sb.AppendLine("public enum FuzzEnum { A, B, C }");
        sb.AppendLine("public enum FuzzEnumL : long { A, B, C }");
        sb.AppendLine();
        sb.AppendLine("public struct FuzzInner { public int A; public float B; }");
        // A nested REFERENCE type (FuzzInner is a struct, so the schema had none) plus records —
        // the dominant modern DTO shape, and the constructor-mapping path the value oracles never hit.
        sb.AppendLine("public sealed class FuzzRef { public int A { get; set; } public string B { get; set; } = \"\"; }");
        sb.AppendLine("public sealed record FuzzRec(int A, string B);");
        sb.AppendLine("public readonly record struct FuzzRecS(int A, double B);");
        // A USER-DEFINED generic. Every generic in every schema was a BCL type, so a user generic as a
        // member type — a completely ordinary thing to write — was never exercised anywhere.
        sb.AppendLine("public sealed class FuzzBox<T> { public T? Val { get; set; } }");
        sb.AppendLine();
        // Abstract base and concrete source
        sb.AppendLine("public abstract class Src { public string Tag { get; set; } = \"\"; }");
        sb.AppendLine("public class SrcConcrete : Src");
        sb.AppendLine("{");
        foreach (var (name, type) in members)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    public {type} {name} {{ get; set; }}{DefaultInit(type)}"));
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public abstract class Dst { public string Tag { get; set; } = \"\"; }");
        sb.AppendLine("public class DstConcrete : Dst");
        sb.AppendLine("{");
        foreach (var (name, type) in members)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    public {type} {name} {{ get; set; }}{DefaultInit(type)}"));
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("[global::DwarfMapper.DwarfMapper(AutoNest = true)]");
        sb.AppendLine("public partial class FuzzMapper");
        sb.AppendLine("{");
        sb.AppendLine("    [global::DwarfMapper.MapDerivedType<SrcConcrete, DstConcrete>]");
        sb.AppendLine("    public partial Dst Map(Src s);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildSourceWithFlattenGraph((string Name, string Type)[] members, int seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated by SyntheticSchema (advanced:FlattenGraph) — do not edit");
        sb.AppendLine("// seed=" + seed.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace Fuzz;");
        sb.AppendLine();
        sb.AppendLine("public enum FuzzEnum { A, B, C }");
        sb.AppendLine("public enum FuzzEnumL : long { A, B, C }");
        sb.AppendLine();
        sb.AppendLine("public struct FuzzInner { public int A; public float B; }");
        // A nested REFERENCE type (FuzzInner is a struct, so the schema had none) plus records —
        // the dominant modern DTO shape, and the constructor-mapping path the value oracles never hit.
        sb.AppendLine("public sealed class FuzzRef { public int A { get; set; } public string B { get; set; } = \"\"; }");
        sb.AppendLine("public sealed record FuzzRec(int A, string B);");
        sb.AppendLine("public readonly record struct FuzzRecS(int A, double B);");
        // A USER-DEFINED generic. Every generic in every schema was a BCL type, so a user generic as a
        // member type — a completely ordinary thing to write — was never exercised anywhere.
        sb.AppendLine("public sealed class FuzzBox<T> { public T? Val { get; set; } }");
        sb.AppendLine();
        // Node types (simple: only scalar members + a single-ref edge)
        sb.AppendLine("public class Src");
        sb.AppendLine("{");
        sb.AppendLine("    public string Name { get; set; } = \"\";");
        sb.AppendLine("    public Src? Next { get; set; }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public class Dst");
        sb.AppendLine("{");
        sb.AppendLine("    public string Name { get; set; } = \"\";");
        sb.AppendLine("    public Dst? Next { get; set; }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public class SrcRoot { public Src? Entry { get; set; } }");
        sb.AppendLine(
            "public class DstRoot { public global::System.Collections.Generic.List<Dst> Nodes { get; set; } = new(); }");
        sb.AppendLine();
        sb.AppendLine("[global::DwarfMapper.DwarfMapper]");
        sb.AppendLine("public partial class FuzzMapper");
        sb.AppendLine("{");
        sb.AppendLine("    [global::DwarfMapper.FlattenGraph(\"Entry\", \"Nodes\")]");
        sb.AppendLine("    public partial DstRoot Map(SrcRoot r);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string BuildSourceWithTopLevelCollection((string Name, string Type)[] members, int seed)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Auto-generated by SyntheticSchema (advanced:TopLevelCollection) — do not edit");
        sb.AppendLine("// seed=" + seed.ToString(CultureInfo.InvariantCulture));
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine();
        sb.AppendLine("namespace Fuzz;");
        sb.AppendLine();
        sb.AppendLine("public enum FuzzEnum { A, B, C }");
        sb.AppendLine("public enum FuzzEnumL : long { A, B, C }");
        sb.AppendLine();
        sb.AppendLine("public struct FuzzInner { public int A; public float B; }");
        // A nested REFERENCE type (FuzzInner is a struct, so the schema had none) plus records —
        // the dominant modern DTO shape, and the constructor-mapping path the value oracles never hit.
        sb.AppendLine("public sealed class FuzzRef { public int A { get; set; } public string B { get; set; } = \"\"; }");
        sb.AppendLine("public sealed record FuzzRec(int A, string B);");
        sb.AppendLine("public readonly record struct FuzzRecS(int A, double B);");
        // A USER-DEFINED generic. Every generic in every schema was a BCL type, so a user generic as a
        // member type — a completely ordinary thing to write — was never exercised anywhere.
        sb.AppendLine("public sealed class FuzzBox<T> { public T? Val { get; set; } }");
        sb.AppendLine();
        sb.AppendLine("public class Src");
        sb.AppendLine("{");
        foreach (var (name, type) in members)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    public {type} {name} {{ get; set; }}{DefaultInit(type)}"));
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("public class Dst");
        sb.AppendLine("{");
        foreach (var (name, type) in members)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    public {type} {name} {{ get; set; }}{DefaultInit(type)}"));
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("[global::DwarfMapper.DwarfMapper]");
        sb.AppendLine("public partial class FuzzMapper");
        sb.AppendLine("{");
        sb.AppendLine("    public partial Dst Map(Src s);");
        sb.AppendLine(
            "    public partial global::System.Collections.Generic.List<Dst> Map(global::System.Collections.Generic.List<Src> src);");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string Generate(int seed)
    {
        var rng = new Random(seed);

        var memberCount = rng.Next(1, 11); // 1..10 members

        var members = new (string Name, string Type)[memberCount];
        for (var i = 0; i < memberCount; i++) members[i] = ($"Member{i}", PickType(rng));

        return BuildSource(members);
    }

    // ── Shared source builder ─────────────────────────────────────────────

    /// <summary>Reference-handling / update-into / round-trip mode for <see cref="GenerateBehavioralForMode"/>
    /// (items 5, 11).</summary>
    public enum EmitMode { None, Preserve, SetNull, UpdateInto, RoundTrip }

    /// <summary>Item 18: the same behavioural Src/Dst shape with an explicit [DwarfMapper(...)] argument
    /// string and emit mode. Because Src/Dst have identical member types no conversion diagnostic can fire,
    /// so any combination of config options must compile cleanly and stay value-preserving on acyclic input.</summary>
    public static string GenerateBehavioralWithConfig(int seed, string attrArgs, EmitMode mode)
    {
        var rng = new Random(seed);
        int memberCount = rng.Next(1, 11);
        var members = new (string Name, string Type)[memberCount];
        for (int i = 0; i < memberCount; i++)
            members[i] = ($"Member{i}", PickTypeBehavioral(rng));
        return BuildSource(members, mode, attrArgs);
    }

    private static string BuildSource((string Name, string Type)[] members)
        => BuildSource(members, EmitMode.None);

    private static string BuildSource((string Name, string Type)[] members, EmitMode mode, string? attrArgs = null)
    {
        var sb = new StringBuilder();

        // ── File header ─────────────────────────────────────────────────
        sb.AppendLine("// Auto-generated by SyntheticSchema — do not edit");
        sb.AppendLine("using DwarfMapper;");
        sb.AppendLine();
        sb.AppendLine("namespace Fuzz;");
        sb.AppendLine();

        // ── Shared enums (always emitted so any member using FuzzEnum/FuzzEnumL compiles) ──
        sb.AppendLine("public enum FuzzEnum { A, B, C }");
        sb.AppendLine();
        // Non-default (long) underlying type: exercises by-name mapping across non-int underlying.
        // Member values are kept small (0,1,2) so Convert.ToInt64 in the behavioral oracle is safe.
        sb.AppendLine("public enum FuzzEnumL : long { A, B, C }");
        sb.AppendLine();

        // ── Shared nested struct ─────────────────────────────────────────
        sb.AppendLine("public struct FuzzInner { public int A; public float B; }");
        // A nested REFERENCE type (FuzzInner is a struct, so the schema had none) plus records —
        // the dominant modern DTO shape, and the constructor-mapping path the value oracles never hit.
        sb.AppendLine("public sealed class FuzzRef { public int A { get; set; } public string B { get; set; } = \"\"; }");
        sb.AppendLine("public sealed record FuzzRec(int A, string B);");
        sb.AppendLine("public readonly record struct FuzzRecS(int A, double B);");
        // A USER-DEFINED generic. Every generic in every schema was a BCL type, so a user generic as a
        // member type — a completely ordinary thing to write — was never exercised anywhere.
        sb.AppendLine("public sealed class FuzzBox<T> { public T? Val { get; set; } }");
        sb.AppendLine();

        // ── Src class ───────────────────────────────────────────────────
        sb.AppendLine("public class Src");
        sb.AppendLine("{");
        foreach (var (name, type) in members)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    public {type} {name} {{ get; set; }}{DefaultInit(type)}"));
        sb.AppendLine("}");
        sb.AppendLine();

        // ── Dst class (identical members) ───────────────────────────────
        sb.AppendLine("public class Dst");
        sb.AppendLine("{");
        foreach (var (name, type) in members)
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"    public {type} {name} {{ get; set; }}{DefaultInit(type)}"));
        sb.AppendLine("}");
        sb.AppendLine();

        // ── Mapper (mode-parameterized for item 5) ──────────────────────
        var attr = attrArgs is not null
            ? $"[global::DwarfMapper.DwarfMapper({attrArgs})]"
            : mode switch
            {
                EmitMode.Preserve => "[global::DwarfMapper.DwarfMapper(ReferenceHandling = global::DwarfMapper.ReferenceHandlingStrategy.Preserve)]",
                EmitMode.SetNull  => "[global::DwarfMapper.DwarfMapper(OnCycle = global::DwarfMapper.OnCycleStrategy.SetNull)]",
                _                  => "[global::DwarfMapper.DwarfMapper]",
            };
        sb.AppendLine(attr);
        sb.AppendLine("public partial class FuzzMapper");
        sb.AppendLine("{");
        if (mode == EmitMode.UpdateInto)
            // Update-into: map onto an EXISTING Dst. The oracle constructs the dest then calls Update.
            sb.AppendLine("    public partial void Update(Src s, Dst d);");
        else if (mode == EmitMode.RoundTrip)
        {
            // Forward + inverse for round-trip idempotence (item 11). Src/Dst have identical member types,
            // so the round trip is lossless.
            sb.AppendLine("    public partial Dst Forward(Src s);");
            sb.AppendLine("    public partial Src Backward(Dst d);");
        }
        else
            sb.AppendLine("    public partial Dst Map(Src s);");
        sb.AppendLine("}");

        return sb.ToString();
    }

    // ── Type picker ───────────────────────────────────────────────────────────
    //
    // Category layout (27 slots total, rng.Next(0, 27) → 0..26):
    //
    //   0..20  → raw scalar from ScalarTypes[category]  (21 slots)
    //   21     → T? (nullable value scalar)
    //   22     → T[] (array of scalar/enum element)
    //   23     → List<T>
    //   24     → HashSet<T>
    //   25     → FuzzEnum / FuzzEnumL variants
    //   26     → FuzzInner / FuzzInner[]
    //   27     → a collection INTERFACE: IEnumerable<T> / ICollection<T> / IList<T> /
    //            IReadOnlyCollection<T> / IReadOnlyList<T>
    //
    // Slot 27 exists because the generated space previously contained only concrete collections
    // (T[], List<T>, HashSet<T>). The interface-typed collection targets — which are exactly where an
    // aliasing/pass-through fast-path is tempting, and where one really did ship — were never generated at
    // all, so no amount of fuzzing could have found the bug. (ObjectFactory had the mirror-image hole: it
    // returned null for any interface type, so even a generated one would have been empty.)

    private static string PickType(Random rng)
    {
        var category = rng.Next(0, 30);

        return category switch
        {
            // 0-20 → raw scalar
            >= 0 and <= 20 => ScalarTypes[category],

            // 21 → T? (nullable value scalar)
            21 => PickValueScalar(rng) + "?",

            // 22 → T[] (array of scalar/enum element)
            22 => PickScalarElement(rng) + "[]",

            // 23 → List<T>
            23 => $"global::System.Collections.Generic.List<{PickScalarElement(rng)}>",

            // 24 → HashSet<T>
            24 => $"global::System.Collections.Generic.HashSet<{PickScalarElement(rng)}>",

            // 27 → collection interface (the family that used to be unreachable)
            27 => rng.Next(5) switch
            {
                0 => $"global::System.Collections.Generic.IEnumerable<{PickScalarElement(rng)}>",
                1 => $"global::System.Collections.Generic.ICollection<{PickScalarElement(rng)}>",
                2 => $"global::System.Collections.Generic.IList<{PickScalarElement(rng)}>",
                3 => $"global::System.Collections.Generic.IReadOnlyCollection<{PickScalarElement(rng)}>",
                _ => $"global::System.Collections.Generic.IReadOnlyList<{PickScalarElement(rng)}>",
            },

            // 28 → set interfaces + the whole IMMUTABLE family. CollectionConverter.TargetKind lists all of
            // these as supported, yet not one of them was ever generated — 7 of the 15 supported collection
            // targets were unreachable by any fuzzer. The coverage self-validation test now fails if that
            // regresses.
            28 => rng.Next(7) switch
            {
                0 => $"global::System.Collections.Generic.ISet<{PickScalarElement(rng)}>",
                1 => $"global::System.Collections.Generic.IReadOnlySet<{PickScalarElement(rng)}>",
                2 => $"global::System.Collections.Immutable.ImmutableArray<{PickScalarElement(rng)}>",
                3 => $"global::System.Collections.Immutable.ImmutableList<{PickScalarElement(rng)}>",
                4 => $"global::System.Collections.Immutable.IImmutableList<{PickScalarElement(rng)}>",
                5 => $"global::System.Collections.Immutable.ImmutableHashSet<{PickScalarElement(rng)}>",
                _ => $"global::System.Collections.Immutable.IImmutableSet<{PickScalarElement(rng)}>",
            },

            // 29 → dictionaries, including the immutable ones DictionaryConverter accepts.
            29 => rng.Next(5) switch
            {
                0 => $"global::System.Collections.Generic.Dictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>",
                1 => $"global::System.Collections.Generic.IDictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>",
                2 =>
                    $"global::System.Collections.Generic.IReadOnlyDictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>",
                3 =>
                    $"global::System.Collections.Immutable.ImmutableDictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>",
                _ =>
                    $"global::System.Collections.Immutable.IImmutableDictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>",
            },

            // 25 → FuzzEnum or FuzzEnumL variants
            25 => rng.Next(6) switch
            {
                0 => "FuzzEnum",
                1 => "FuzzEnum?",
                2 => "FuzzEnum[]",
                3 => "FuzzEnumL",
                4 => "FuzzEnumL?",
                _ => "FuzzEnumL[]"
            },

            // 26 → FuzzInner or FuzzInner[]
            _ => rng.Next(2) == 0 ? "FuzzInner" : "FuzzInner[]"
        };
    }

    /// <summary>
    ///     Type picker for behavioral (value-preserving) tests: identical to <see cref="PickType" />
    ///     but category 24 (HashSet) is redirected to List.  <c>HashSet&lt;T&gt;</c> is excluded
    ///     because <c>StructuralComparer</c> walks <c>IEnumerable</c> by position; two equal
    ///     <c>HashSet&lt;T&gt;</c> instances may enumerate in different order after a copy.
    /// </summary>
    private static string PickTypeBehavioral(Random rng)
    {
        var category = rng.Next(0, 30);

        return category switch
        {
            >= 0 and <= 20 => ScalarTypes[category],
            21 => PickValueScalar(rng) + "?",
            22 => PickScalarElement(rng) + "[]",
            // 23 and 24 both → List<T> (HashSet excluded for ordering reasons)
            23 or 24 => $"global::System.Collections.Generic.List<{PickScalarElement(rng)}>",
            25 => rng.Next(6) switch
            {
                0 => "FuzzEnum",
                1 => "FuzzEnum?",
                2 => "FuzzEnum[]",
                3 => "FuzzEnumL",
                4 => "FuzzEnumL?",
                _ => "FuzzEnumL[]"
            },

            // 27 → collection INTERFACES. These were unreachable from the behavioural picker, which is why the
            // value oracles never exercised them — and an interface-typed destination is exactly where a
            // pass-through/aliasing fast-path is tempting (and where one really did ship). Unlike HashSet<T>
            // these are safe for the position-walking comparer: ObjectFactory backs them with a List<T>, so
            // enumeration order is stable across a copy.
            27 => rng.Next(5) switch
            {
                0 => $"global::System.Collections.Generic.IEnumerable<{PickScalarElement(rng)}>",
                1 => $"global::System.Collections.Generic.ICollection<{PickScalarElement(rng)}>",
                2 => $"global::System.Collections.Generic.IList<{PickScalarElement(rng)}>",
                3 => $"global::System.Collections.Generic.IReadOnlyCollection<{PickScalarElement(rng)}>",
                _ => $"global::System.Collections.Generic.IReadOnlyList<{PickScalarElement(rng)}>"
            },

            // 28 → DICTIONARIES. DwarfMapper ships a whole DictionaryConverter that the fuzzers never
            // exercised at all — the single largest unfuzzed feature in the library. Key is a scalar (a
            // dictionary key must be non-null and hashable); the value is a scalar or a nested type.
            28 => rng.Next(3) switch
            {
                0 => $"global::System.Collections.Generic.Dictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>",
                1 => $"global::System.Collections.Generic.IDictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>",
                _ =>
                    $"global::System.Collections.Generic.IReadOnlyDictionary<{PickValueScalar(rng)}, {PickScalarElement(rng)}>"
            },

            // 29 → a nested REFERENCE type, RECORDS, TUPLES, a USER GENERIC, and a nullable REFERENCE.
            //
            // FuzzInner is a struct, so until now the behavioural schema had no nested reference type at all —
            // yet that is the commonest DTO shape and the one where identity, aliasing and cycles actually
            // live. Records (class and struct) are the dominant modern DTO shape and drive the constructor
            // mapping path. Tuples, user-defined generics, and nullable REFERENCE types were absent from every
            // schema — the last of those matters because NullableAnnotation has THREE states, and the
            // oblivious one is silently treated as non-null by any check written as `== Annotated`.
            29 => rng.Next(10) switch
            {
                0 => "FuzzRef",
                1 => "FuzzRef[]",
                2 => "global::System.Collections.Generic.List<FuzzRef>",
                3 => "FuzzRec",
                4 => "FuzzRec[]",
                5 => "FuzzRecS",
                6 => $"({PickValueScalar(rng)}, string)", // ValueTuple
                7 => $"FuzzBox<{PickScalarElement(rng)}>", // user-defined generic
                8 => "string?", // nullable reference
                _ => "FuzzRef?" // nullable reference to a nested type
            },

            _ => rng.Next(2) == 0 ? "FuzzInner" : "FuzzInner[]"
        };
    }

    private static string PickValueScalar(Random rng)
    {
        return ValueScalars[rng.Next(ValueScalars.Length)];
    }

    private static string PickScalarElement(Random rng)
    {
        return CollectionElements[rng.Next(CollectionElements.Length)];
    }

    // ── Default initialiser ───────────────────────────────────────────────

    private static string DefaultInit(string type)
    {
        if (type == "string")
            return " = \"\";";

        // A nullable REFERENCE (`string?`, `FuzzRef?`) is meant to be null — that is the shape. No initialiser.
        if (type.EndsWith('?') && !type.StartsWith("global::", StringComparison.Ordinal))
            return string.Empty;

        // ValueTuple is a struct: default(...) is valid, no initialiser needed.
        if (type.StartsWith('('))
            return string.Empty;

        // A user-defined generic still needs an instance.
        if (type.StartsWith("FuzzBox<", StringComparison.Ordinal))
            return " = new();";

        if (type.EndsWith("[]", StringComparison.Ordinal))
        {
            // Derive element type by stripping trailing []
            var elem = type[..^2];
            // global:: prefix is fine here — Array.Empty<T> just needs the fully-qualified T
            return $" = global::System.Array.Empty<{elem}>();";
        }

        if (type.StartsWith("global::System.Collections.Generic.List<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.HashSet<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.Dictionary<", StringComparison.Ordinal))
            return " = new();";

        // Collection/dictionary INTERFACES cannot be `new()`d — back them with a concrete instance, matching
        // what ObjectFactory builds for them.
        if (type.StartsWith("global::System.Collections.Generic.IEnumerable<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.ICollection<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IList<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlyCollection<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlyList<", StringComparison.Ordinal))
        {
            var open = type.IndexOf('<', StringComparison.Ordinal);
            var elem = type[(open + 1)..^1];
            return $" = new global::System.Collections.Generic.List<{elem}>();";
        }

        if (type.StartsWith("global::System.Collections.Generic.IDictionary<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlyDictionary<", StringComparison.Ordinal))
        {
            var open = type.IndexOf('<', StringComparison.Ordinal);
            var args = type[(open + 1)..^1];
            return $" = new global::System.Collections.Generic.Dictionary<{args}>();";
        }

        // Sets by interface, and the immutable family: none of these can be `new()`d.
        if (type.StartsWith("global::System.Collections.Generic.ISet<", StringComparison.Ordinal) ||
            type.StartsWith("global::System.Collections.Generic.IReadOnlySet<", StringComparison.Ordinal))
        {
            var open = type.IndexOf('<', StringComparison.Ordinal);
            var elem = type[(open + 1)..^1];
            return $" = new global::System.Collections.Generic.HashSet<{elem}>();";
        }

        // Immutable collections expose a static Empty rather than a public constructor. ImmutableArray<T> is a
        // struct whose `default` is the "uninitialised" state (enumerating it throws), so it too must be given
        // Empty explicitly rather than left to default(T).
        if (type.StartsWith("global::System.Collections.Immutable.", StringComparison.Ordinal))
        {
            var open = type.IndexOf('<', StringComparison.Ordinal);
            var name = type[..open];
            var args = type[(open + 1)..^1];

            // The interface forms (IImmutableList/IImmutableSet/IImmutableDictionary) have no Empty of their
            // own — back them with the corresponding concrete implementation.
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

            return $" = {concrete}<{args}>.Empty;";
        }

        // A nested reference type needs an instance; a POSITIONAL record has no parameterless ctor, so it must
        // be constructed with arguments.
        if (type == "FuzzRef")
            return " = new();";
        if (type == "FuzzRec")
            return " = new(0, \"\");";

        // Nullable value types, structs (incl. record structs), enums, scalars: no explicit init needed
        return string.Empty;
    }
}
