// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Core;
using DwarfMapper.Generator.Diagnostics;

namespace DwarfMapper.Generator.Model
{
    /// <summary>The full, value-equatable description of one [DwarfMapper] class.</summary>
    /// <param name="GenerateExtensions">
    ///     Class-level <c>[DwarfMapper(GenerateExtensions = …)]</c> value (default <c>true</c>). When false,
    ///     the aggregate facade emitter skips this mapper's convenience extension methods.
    /// </param>
    /// <param name="HasParameterlessCtor">
    ///     Whether the mapper class has an accessible parameterless constructor, so the aggregate facade can
    ///     cache a <c>new()</c> singleton of it. A mapper with only parameterized constructors is skipped by the
    ///     facade (its convenience extensions can't be backed by a cached instance).
    /// </param>
    /// <param name="ContainingTypes">
    ///     The declaration headers of the types this mapper is NESTED INSIDE, outermost first — e.g.
    ///     <c>["public partial class Outer"]</c>. Empty for the usual namespace-level mapper.
    ///     <para>
    ///     A partial class can only be completed inside the same containing type(s). Emitting the generated half
    ///     at namespace scope while the user's half sits inside <c>Outer</c> does not produce a partial pair at
    ///     all — it produces two unrelated types, and the compiler says so with CS0759 / CS8795, from generated
    ///     code, with no DwarfMapper diagnostic. So the containing chain has to be reproduced verbatim.
    ///     </para>
    /// </param>
    /// <param name="ConventionMethodNames">
    ///     Names of <c>MapConfig&lt;S,T&gt;</c> convention methods on this mapper. The generator reads them but never
    ///     calls them, so the emitter references each via <c>nameof</c> (in a generated static constructor) to keep
    ///     a consumer's IDE0051-as-error build from flagging its own compile-time config as an unused member. Empty
    ///     unless the class both declares convention methods AND has no user-declared static constructor (the
    ///     static-ctor slot is free).
    /// </param>
    /// <param name="RegisterCollectionShapes">
    ///     Class-level <c>[DwarfMapper(RegisterCollectionShapes = …)]</c> value (default <c>true</c>). When true,
    ///     the ambient registration emitter also registers each declared object map under the common collection
    ///     shapes, so a facade call over a collection resolves without a separately declared collection pair.
    /// </param>
    /// <param name="HandWrittenProvides">
    ///     Hand-written methods marked <c>[ProvidesMap]</c>: shapes the generator cannot express (an object that
    ///     HOLDS a collection mapped to the collection, say) which the author wants reachable through the ambient
    ///     facade anyway. Registered exactly like a generated map — a declaration, not reflection.
    /// </param>
    public sealed record MapperClassModel(
        string Namespace,
        string ClassName,
        string Accessibility,
        EquatableArray<MapMethodModel> Methods,
        EquatableArray<DiagnosticInfo> Diagnostics,
        EquatableArray<SynthesizedMethod> SynthesizedMethods,
        EquatableArray<RoundTripPair> RoundTrips,
        bool GenerateExtensions = true,
        bool HasParameterlessCtor = true,
        EquatableArray<string> ContainingTypes = default,
        EquatableArray<string> ConventionMethodNames = default,
        bool RegisterCollectionShapes = true,
        EquatableArray<HandWrittenProvide> HandWrittenProvides = default) : IEquatable<MapperClassModel>
    {
        /// <summary>
        ///     Unique per generated file. Includes the containing types: <c>Outer.M</c> and a namespace-level <c>M</c>
        ///     are different mappers and must not collide on one hint name (AddSource throws on a duplicate).
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
        ///     The mapper's fully-qualified, <c>global::</c>-rooted name — including any containing types. Everything
        ///     that has to NAME this mapper in emitted code (the convenience facade, the DI registration, the ambient
        ///     registry) must go through this: three call sites previously each rebuilt it by hand as
        ///     <c>Namespace + "." + ClassName</c>, which silently dropped the containing type and emitted references
        ///     to a <c>Demo.M</c> that does not exist (CS0234) whenever the mapper was nested.
        /// </summary>
        /// <remarks>
        ///     Escaped, unlike <see cref="HintName" />, and the difference is the whole reason they are two
        ///     properties that look alike: this one is C# and that one is a FILENAME. <see cref="Namespace" />
        ///     arrives from <c>ToDisplayString</c> and is already escaped by the compiler's own format; the class
        ///     name and the containing chain arrive from <c>ISymbol.Name</c> and are not.
        /// </remarks>
        public string FullyQualifiedName
        {
            get
            {
                var nested = string.Join(".", ContainingTypes.Select(c => Identifiers.EscapeTypeName(TypeNameOf(c))));
                var local = string.IsNullOrEmpty(nested) ? EmitClassName : nested + "." + EmitClassName;
                return "global::" + (string.IsNullOrEmpty(Namespace) ? local : Namespace + "." + local);
            }
        }

        /// <summary>
        ///     <see cref="ClassName" /> as it must be written into emitted C# — the mapper's own declaration
        ///     header, its generated static constructor, and every reference to it.
        /// </summary>
        /// <remarks>
        ///     <see cref="Identifiers.EscapeTypeName" /> rather than <see cref="Identifiers.Escape" />, because
        ///     this is a type-DECLARATION position: a consumer may write <c>public partial class @record</c>, and
        ///     <c>ISymbol.Name</c> hands back <c>record</c>, which declares fine and then breaks wherever the name
        ///     is READ. This is that helper's first production call site.
        /// </remarks>
        public string EmitClassName => Identifiers.EscapeTypeName(ClassName);

        /// <summary>
        ///     <see cref="ContainingTypes" /> as they must be written into emitted C# — each declaration header
        ///     with its TYPE NAME escaped and its modifiers left alone.
        /// </summary>
        /// <remarks>
        ///     The escape belongs here rather than in the header the extractor stores, because that same stored
        ///     string feeds <see cref="TypeNameOf" /> into <see cref="HintName" />, which is a filename: escaping
        ///     at construction would rename every generated file of a nested mapper to <c>@class.M.g.cs</c>.
        /// </remarks>
        public IEnumerable<string> EmitContainingTypes
        {
            get
            {
                foreach (var declaration in ContainingTypes)
                {
                    var cut = declaration.LastIndexOf(' ');
                    yield return cut < 0
                        ? Identifiers.EscapeTypeName(declaration)
                        : declaration.Substring(0, cut + 1) + Identifiers.EscapeTypeName(declaration.Substring(cut + 1));
                }
            }
        }

        /// <summary>
        ///     <see cref="ConventionMethodNames" /> as they must be written into the generated static
        ///     constructor's <c>nameof</c> discards. <c>nameof(@class)</c> is legal and evaluates to
        ///     <c>"class"</c>, so escaping changes nothing but whether the file parses.
        /// </summary>
        public IEnumerable<string> EmitConventionMethodNames => ConventionMethodNames.Select(Identifiers.Escape);

        /// <summary>
        ///     Whether an error on this class suppresses the WHOLE class's emission.
        ///     <para>
        ///         Method-scoped errors are excluded. A projection member that cannot be translated is a fact
        ///         about one <c>Project</c> method, and MapperExtractor has already dropped that method from
        ///         <see cref="Methods" /> — taking the class's <c>Map</c> methods down with it left the consumer
        ///         with nothing generated at all and a pile of CS8795 on methods that were perfectly fine
        ///         (TASKS.md I14). Class-level errors — an ambiguous member, an unknown destination, a bad hook
        ///         signature — still suppress everything: those describe a model the emitter cannot trust.
        ///     </para>
        /// </summary>
        public bool HasBlockingError => Diagnostics.Any(d => d.IsError && !d.ScopedToMethod);

        /// <summary>The bare type name out of a declaration header ("public partial class Outer" -> "Outer").</summary>
        private static string TypeNameOf(string declaration)
        {
            var parts = declaration.Split(' ');
            return parts[parts.Length - 1];
        }
    }
}
