// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline;

/// <summary>
/// Whole-graph cross-assembly linkage validation, performed ONLY in the compilation marked
/// <c>[assembly: DwarfMapperValidationRoot]</c> (the one that references every provider and consumer). It
/// aggregates the <c>Provides</c> / <c>Requires</c> manifests of all referenced assemblies with this
/// compilation's own provided/required pairs, and reports DWARF061 for any required ambient map that nothing
/// provides — turning the otherwise-runtime "no map registered" failure into a compile-time error.
/// </summary>
internal static class AmbientValidator
{
    private static readonly SymbolDisplayFormat Fq = SymbolDisplayFormat.FullyQualifiedFormat;

    /// <summary>True when this assembly is marked as the ambient validation root.</summary>
    public static bool IsValidationRoot(Compilation compilation)
    {
        foreach (var a in compilation.Assembly.GetAttributes())
        {
            if (a.AttributeClass?.ToDisplayString() == KnownNames.ValidationRootFqn)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Reads the <c>DwarfProvidesMap</c> / <c>DwarfRequiresMap</c> manifests from every referenced assembly
    /// (these live in metadata as assembly attributes — readable; the generator cannot see its OWN
    /// not-yet-emitted manifests, so the root supplies its own provided/required separately).
    /// </summary>
    public static (ImmutableArray<(string Source, string Destination)> Provided,
                   ImmutableArray<(string Source, string Destination)> Required) ReadReferenced(Compilation compilation)
    {
        var provided = ImmutableArray.CreateBuilder<(string, string)>();
        var required = ImmutableArray.CreateBuilder<(string, string)>();

        foreach (var asm in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            foreach (var a in asm.GetAttributes())
            {
                switch (a.AttributeClass?.ToDisplayString())
                {
                    case KnownNames.DwarfProvidesMapFqn:
                        if (ReadPair(a) is { } p) provided.Add(p);
                        break;
                    case KnownNames.DwarfRequiresMapFqn:
                        if (ReadPair(a) is { } r) required.Add(r);
                        break;
                }
            }
        }

        return (provided.ToImmutable(), required.ToImmutable());
    }

    private static (string Source, string Destination)? ReadPair(AttributeData a)
    {
        if (a.ConstructorArguments.Length != 2)
            return null;
        if (a.ConstructorArguments[0].Value is not ITypeSymbol source ||
            a.ConstructorArguments[1].Value is not ITypeSymbol destination)
            return null;
        return (source.ToDisplayString(Fq), destination.ToDisplayString(Fq));
    }

    /// <summary>
    /// The required ambient pairs (this compilation's own + all referenced) that nothing in the graph
    /// provides — each becomes a DWARF061. Deterministically ordered.
    /// </summary>
    public static IReadOnlyList<(string Source, string Destination)> MissingRequires(
        IEnumerable<(string Source, string Destination)> ownProvided,
        IEnumerable<(string Source, string Destination)> ownRequired,
        IEnumerable<(string Source, string Destination)> referencedProvided,
        IEnumerable<(string Source, string Destination)> referencedRequired)
    {
        var provided = new HashSet<(string, string)>();
        foreach (var p in ownProvided) provided.Add(p);
        foreach (var p in referencedProvided) provided.Add(p);

        var required = new SortedSet<(string, string)>();
        foreach (var r in ownRequired) required.Add(r);
        foreach (var r in referencedRequired) required.Add(r);

        var missing = new List<(string, string)>();
        foreach (var r in required)
        {
            if (!provided.Contains(r))
                missing.Add(r);
        }
        return missing;
    }

    /// <summary>
    /// Pairs provided by MORE THAN ONE assembly in the graph (this compilation + referenced) — the ambient
    /// registry keeps the first registration and ignores the rest, so these become DWARF063 warnings.
    /// </summary>
    public static IReadOnlyList<(string Source, string Destination)> AmbiguousProviders(
        IEnumerable<(string Source, string Destination)> ownProvided,
        IEnumerable<(string Source, string Destination)> referencedProvided)
    {
        var counts = new Dictionary<(string, string), int>();
        foreach (var p in ownProvided)
            counts[p] = counts.TryGetValue(p, out var c) ? c + 1 : 1;
        foreach (var p in referencedProvided)
            counts[p] = counts.TryGetValue(p, out var c) ? c + 1 : 1;

        var result = new SortedSet<(string, string)>();
        foreach (var kv in counts)
        {
            if (kv.Value > 1)
                result.Add(kv.Key);
        }
        return new List<(string, string)>(result);
    }

    /// <summary>
    /// Emits the root-only <c>DwarfMapper.DwarfMap.Validate()</c> — a hard-coded (reflection-free) runtime
    /// fail-fast that throws <c>DwarfMapValidationException</c> if any ambient map the app consumes is not
    /// registered in the live registry (defense-in-depth against trimming / unloaded assemblies). Returns the
    /// empty string when nothing is consumed.
    /// </summary>
    public static string EmitValidateMethod(IEnumerable<(string Source, string Destination)> required)
    {
        var pairs = new SortedSet<(string, string)>();
        foreach (var r in required)
            pairs.Add(r);
        if (pairs.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.Append("// <auto-generated/>\n// SPDX-License-Identifier: GPL-2.0-only\n#nullable enable\n\n");
        sb.AppendLine("namespace DwarfMapper;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>Generated in the validation-root assembly: a runtime fail-fast check that every ambient");
        sb.AppendLine("/// map the application consumes is actually registered (defense-in-depth against trimming or");
        sb.AppendLine("/// not-yet-loaded assemblies). Call it once at startup.</summary>");
        sb.AppendLine("public static class DwarfMap");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Throws <see cref=\"DwarfMapValidationException\"/> if any consumed ambient map is missing.</summary>");
        sb.AppendLine("    public static void Validate()");
        sb.AppendLine("    {");
        sb.AppendLine("        var __missing = new global::System.Collections.Generic.List<string>();");
        foreach (var (source, destination) in pairs)
        {
            sb.Append("        if (!global::DwarfMapper.DwarfMapperRegistry.IsProvided(typeof(").Append(source)
              .Append("), typeof(").Append(destination).Append("))) __missing.Add(\"")
              .Append(source).Append(" -> ").Append(destination).Append("\");\n");
        }
        sb.AppendLine("        if (__missing.Count > 0)");
        sb.AppendLine("            throw new global::DwarfMapper.DwarfMapValidationException(");
        sb.AppendLine("                \"DwarfMapper: required ambient map(s) not registered at runtime: \" + global::System.String.Join(\", \", __missing));");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
