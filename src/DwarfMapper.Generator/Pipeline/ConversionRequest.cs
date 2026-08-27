// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    internal static partial class MapperExtractor
    {
        /// <summary>
        ///     The pair being converted and everything an arm of <c>TryResolveConversion</c> needs to judge it.
        ///     No arm reassigns any of it.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <c>TryResolveConversion</c> is a DISPATCH CHAIN: each arm asks "is this pair mine?", and the
        ///         first that claims it decides the answer. Five of the six arms read 19 or 20 of the method's 23
        ///         parameters, which is why they take this instead of a parameter list nobody could read.
        ///     </para>
        ///     <para>
        ///         <c>diagnostics</c> and <c>synthesized</c> are deliberately NOT here. A write-site grep over this
        ///         method reports both as read-only, and for <c>synthesized</c> that is true of the text and false
        ///         in effect -- the arms hand it to helpers that write into it. It is a sink, so it stays a
        ///         parameter alongside <c>diagnostics</c> rather than posing as an input. See
        ///         <see cref="MemberRequest" />, where the same blind spot was found from the other direction.
        ///     </para>
        ///     <para>
        ///         <see cref="NestedRegistry" /> is the one collaborator: arms consult it and reserve nested maps
        ///         in it. Same judgement as <see cref="MemberRequest" /> and for the same reason -- ask-and-register
        ///         is not the same relationship as fill-for-the-caller.
        ///     </para>
        /// </remarks>
        private sealed record ConversionRequest(
            Compilation Compilation,
            ITypeSymbol SrcType,
            ITypeSymbol TgtType,
            string? UseMethod,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> AllMethods,
            IReadOnlyList<(string Name, ITypeSymbol ParamType, ITypeSymbol ReturnType)> AutoCandidates,
            EnumPolicy EnumPolicy,
            NullStrategy NullStrategy,
            LocationInfo? Location,
            string TargetName,
            bool AutoNest,
            NestedMappingRegistry? NestedRegistry,
            bool NullAsNull,
            bool IsPreserve,
            bool AllowInterfaceSrc,
            bool IsSetNull,
            bool ImplicitConversions,
            IReadOnlyCollection<string>? ReservedConverters);
    }
}
