// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The one reader of ASSEMBLY-level DwarfMapper configuration — <c>[assembly: DwarfMapperDefaults]</c> and
    ///     <c>[assembly: DwarfMapperOptions]</c> — for every front door that has to honour it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         There are three front doors and they used to find this configuration three different ways: the
    ///         <c>[DwarfMapper]</c> class model looked the defaults attribute up in
    ///         <see cref="MapperExtractor.Extract" />, the aggregate outputs read <c>PublicExtensions</c> with an
    ///         inline loop of their own in <c>DwarfGenerator</c>, and the <c>[MapTo]</c> registry did not look at
    ///         all. The third is why an assembly that had switched <c>AutoMatchMembers</c> off still had every
    ///         registry map auto-matching, and why <c>PublicExtensions</c> was honoured for a caller's mapper
    ///         classes and silently overridden for their <c>[MapTo]</c> types.
    ///     </para>
    ///     <para>
    ///         So this is a HOIST, not a fourth reader: the lookup lives here once and each front door calls it.
    ///         The alternative — threading the individual options through by hand — is how the two forms of
    ///         <c>[MapNullSkip]</c> ended up exact inverses of each other, and a second copy of a merge rule is
    ///         invisibly wrong until something measures both copies.
    ///     </para>
    /// </remarks>
    internal static class AssemblyConfiguration
    {
        /// <summary>
        ///     The <c>[assembly: DwarfMapperDefaults(...)]</c> application, or <c>null</c> when there is none.
        /// </summary>
        /// <remarks>
        ///     Returned as the raw <see cref="AttributeData" /> rather than as parsed values, because the mapper
        ///     path's precedence rule IS the attribute list: every option reader takes the first matching named
        ///     argument across the list, so appending this attribute after the class's own <c>[DwarfMapper]</c>
        ///     gives mapper &gt; assembly defaults &gt; built-in default with no reader changes. A parsed record
        ///     here would have to re-implement that merge, which is the copy this class exists to avoid.
        /// </remarks>
        public static AttributeData? Defaults(Compilation compilation)
        {
            foreach (var attribute in compilation.Assembly.GetAttributes())
                if (KnownNames.IsAttributeClass(attribute.AttributeClass, KnownNames.DwarfMapperDefaultsFqn))
                {
                    return attribute;
                }

            return null;
        }

        /// <summary>
        ///     The option list a front door with NO mapper class of its own resolves against: the assembly
        ///     defaults alone, or empty. Every <c>Read*</c> reader then answers with the assembly default where one
        ///     was written and with its own built-in default where none was — the same fallback chain the mapper
        ///     path has, minus the layer that does not exist here.
        /// </summary>
        public static ImmutableArray<AttributeData> OptionsFor(Compilation compilation)
        {
            var defaults = Defaults(compilation);
            return defaults is null ? ImmutableArray<AttributeData>.Empty : ImmutableArray.Create(defaults);
        }

        /// <summary>
        ///     Whether <c>[assembly: DwarfMapperOptions(PublicExtensions = true)]</c> opts the generated
        ///     convenience extensions public. Defaults to <c>false</c> — every generated extension is
        ///     assembly-internal unless the assembly asks otherwise.
        /// </summary>
        /// <remarks>
        ///     Read here rather than at each emitter because BOTH extension emitters must agree: the aggregate
        ///     facade (<c>DwarfMapper.Extensions</c>) and the registry's per-source extension class
        ///     (<c>__DwarfRegistry_&lt;Source&gt;</c>). Answering <c>true</c> is a ceiling, not a decision — each
        ///     emitter still narrows to <c>internal</c> for a pair involving a non-public type, since a public
        ///     member over an internal type does not compile.
        /// </remarks>
        public static bool PublicExtensions(Compilation compilation)
        {
            var publicExtensions = false;
            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                if (!KnownNames.IsAttributeClass(attribute.AttributeClass, KnownNames.DwarfMapperOptionsFqn))
                {
                    continue;
                }

                // PublicExtensions is the attribute's only settable property, and the compiler drops a named
                // argument naming anything else, so every named argument here IS PublicExtensions. A second option
                // on DwarfMapperOptionsAttribute must bring back a test of the key. A wrong-typed value is kept as
                // an error constant, which is not a bool and changes nothing.
                foreach (var named in attribute.NamedArguments)
                    if (named.Value.Value is bool b)
                    {
                        publicExtensions = b;
                    }
            }

            return publicExtensions;
        }
    }
}
