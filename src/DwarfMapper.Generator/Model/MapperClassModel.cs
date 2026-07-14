// SPDX-License-Identifier: GPL-2.0-only

using System.Linq;
using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;

namespace DwarfMapper.Generator.Model;

/// <summary>The full, value-equatable description of one [DwarfMapper] class.</summary>
public sealed record MapperClassModel(
    string Namespace,
    string ClassName,
    string Accessibility,
    EquatableArray<MapMethodModel> Methods,
    EquatableArray<DiagnosticInfo> Diagnostics,
    EquatableArray<SynthesizedMethod> SynthesizedMethods,
    EquatableArray<RoundTripPair> RoundTrips,
    /// <summary>
    /// Class-level <c>[DwarfMapper(GenerateExtensions = …)]</c> value (default <c>true</c>). When false,
    /// the aggregate facade emitter skips this mapper's convenience extension methods.
    /// </summary>
    bool GenerateExtensions = true,
    /// <summary>
    /// Whether the mapper class has an accessible parameterless constructor, so the aggregate facade can
    /// cache a <c>new()</c> singleton of it. A mapper with only parameterized constructors is skipped by the
    /// facade (its convenience extensions can't be backed by a cached instance).
    /// </summary>
    bool HasParameterlessCtor = true,
    /// <summary>
    /// The declaration headers of the types this mapper is NESTED INSIDE, outermost first — e.g.
    /// <c>["public partial class Outer"]</c>. Empty for the usual namespace-level mapper.
    /// <para>
    /// A partial class can only be completed inside the same containing type(s). Emitting the generated half
    /// at namespace scope while the user's half sits inside <c>Outer</c> does not produce a partial pair at
    /// all — it produces two unrelated types, and the compiler says so with CS0759 / CS8795, from generated
    /// code, with no DwarfMapper diagnostic. So the containing chain has to be reproduced verbatim.
    /// </para>
    /// </summary>
    EquatableArray<string> ContainingTypes = default) : IEquatable<MapperClassModel>
{
    /// <summary>
    /// Unique per generated file. Includes the containing types: <c>Outer.M</c> and a namespace-level <c>M</c>
    /// are different mappers and must not collide on one hint name (AddSource throws on a duplicate).
    /// </summary>
    public string HintName
    {
        get
        {
            var nested = string.Join(".", ContainingTypes.Select(TypeNameOf));
            var local = string.IsNullOrEmpty(nested) ? ClassName : nested + "." + ClassName;
            return string.IsNullOrEmpty(Namespace) ? local : $"{Namespace}.{local}";
        }
    }

    /// <summary>
    /// The mapper's fully-qualified, <c>global::</c>-rooted name — including any containing types. Everything
    /// that has to NAME this mapper in emitted code (the convenience facade, the DI registration, the ambient
    /// registry) must go through this: three call sites previously each rebuilt it by hand as
    /// <c>Namespace + "." + ClassName</c>, which silently dropped the containing type and emitted references
    /// to a <c>Demo.M</c> that does not exist (CS0234) whenever the mapper was nested.
    /// </summary>
    public string FullyQualifiedName
    {
        get
        {
            var nested = string.Join(".", ContainingTypes.Select(TypeNameOf));
            var local = string.IsNullOrEmpty(nested) ? ClassName : nested + "." + ClassName;
            return "global::" + (string.IsNullOrEmpty(Namespace) ? local : Namespace + "." + local);
        }
    }

    /// <summary>The bare type name out of a declaration header ("public partial class Outer" -> "Outer").</summary>
    private static string TypeNameOf(string declaration)
    {
        var parts = declaration.Split(' ');
        return parts[parts.Length - 1];
    }

    public bool HasBlockingError => Diagnostics.Any(d => d.IsError);
}
