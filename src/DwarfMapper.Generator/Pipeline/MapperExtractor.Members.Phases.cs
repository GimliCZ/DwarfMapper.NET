// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using System.Linq;
using DwarfMapper.Generator.Diagnostics;
using DwarfMapper.Generator.Model;
using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>
    ///     The resolution passes lifted out of <c>ResolveMembers</c>.
    /// </summary>
    /// <remarks>
    ///     <c>ResolveMembers</c> carried no seam comments, so its phases were DERIVED rather than read: every
    ///     top-level construct in the body was listed with its size, and each candidate was then braced in place
    ///     and built. A clean build means the span declares nothing the rest of the method needs; CS0103 names
    ///     what it shares. Only spans the compiler certified that way appear here.
    ///     See <c>Issues/round27/SEAM-STAGE.md</c> for the derivation and its measurements.
    /// </remarks>
    internal static partial class MapperExtractor
    {

        // [DwarfMapper(SkipNullSourceMembers = true)]: a null source member must keep the destination's
        // default rather than overwrite it. Mark each simple, nullable-source, post-construction-settable
        // member so the emitter guards it with `if (src.X is not null) dst.X = …;`. Non-nullable value-type
        // sources (never null) and required/init-only/read-only targets (cannot be deferred) are left as-is.
        /// <remarks>
        ///     Certified separable before it moved: bracing the span in place and building reported no escaping
        ///     local, so the pass declares nothing the rest of <c>ResolveMembers</c> reads. It runs LAST among the
        ///     passes for a reason the code cannot state on its own — every earlier pass may still add a member,
        ///     and this one has to see the finished list.
        /// </remarks>
        private static void ApplySkipNullSourceMembers(
            ITypeSymbol sourceType,
            INamedTypeSymbol targetType,
            Compilation compilation,
            in MapperOptions options,
            MemberLookups lookups,
            MemberAccumulators acc)
        {
            if (options.SkipNullSourceMembers && acc.Result.Count > 0)
            {
                var srcTypeByName = new Dictionary<string, ITypeSymbol>(lookups.Comparer);
                foreach (var (sName, sType) in ReadableMembers(sourceType, compilation, options.AllowNonPublic))
                    srcTypeByName[sName] = sType;

                var deferrableTargets = new HashSet<string>(StringComparer.Ordinal);
                for (var t = targetType; t is not null && t.SpecialType != SpecialType.System_Object; t = t.BaseType)
                    foreach (var tm in t.GetMembers())
                        if (tm is IPropertySymbol p && p.SetMethod is { IsInitOnly: false } && !p.IsRequired)
                        {
                            deferrableTargets.Add(p.Name);
                        }
                        else if (tm is IFieldSymbol f && !f.IsReadOnly && !f.IsConst && !f.IsRequired)
                        {
                            deferrableTargets.Add(f.Name);
                        }

                for (var i = 0; i < acc.Result.Count; i++)
                {
                    var m = acc.Result[i];
                    if (string.IsNullOrEmpty(m.SourceName) ||
                        m.SourceName.IndexOf('.') >= 0 ||
                        m.ValueExpression is not null ||
                        m.UnflattenIntermediateFqn is not null ||
                        m.WhenPredicate is not null ||
                        m.SkipIfSourceNull ||
                        !deferrableTargets.Contains(m.TargetName))
                    {
                        continue;
                    }

                    if (srcTypeByName.TryGetValue(m.SourceName, out var st) && (st.IsReferenceType || IsNullableValue(st, out _)))
                        // The emitter now guards this with `if (src.X is not null) dst.X = …;`, so inside that
                        // guard flow analysis already proves non-null: no CS8601, hence no '!' and no DWARF070.
                        // SkipNullSourceMembers IS the fix DWARF070 would have told them to apply.
                    {
                        acc.Result[i] = m with
                        {
                            SkipIfSourceNull = true,
                            NullRefIntoNonNullable = false
                        };
                    }
                }
            }
        }

        // MAPVALUE: constant / computed values assigned to a destination member (no source). Processed
        // after [MapProperty] (so conflicts are caught) and before AUTO matching. A [MapValue]'d target
        // counts as mapped, suppressing DWARF001. The projection resolver reads the directive in the SAME
        // position for the same reason, through the SAME validation below.
        /// <remarks>
        ///     Its POSITION is the contract, not just its content: after [MapProperty] so a conflict is caught,
        ///     before AUTO so a valued target suppresses DWARF001. Moving the call moves the behaviour.
        /// </remarks>
        private static void ResolveMapValues(
            Compilation compilation,
            LocationInfo? location,
            List<DiagnosticInfo> diagnostics,
            HashSet<string> ignores,
            IReadOnlyList<(string Target, bool IsConstant, TypedConstant Value, string? Use, string? ConstLiteral)>?
                mapValues,
            IReadOnlyList<(string Name, ITypeSymbol ReturnType)>? valueProviders,
            HashSet<string>? consumedCtorParams,
            MemberLookups lookups,
            MemberAccumulators acc)
        {
            foreach (var mv in mapValues ??
                               Array.Empty<(string Target, bool IsConstant, TypedConstant Value,
                                   string? Use, string? ConstLiteral)>())
            {
                var mvTgt = mv.Target;
                if (!TryValidateMapValueTarget(mvTgt,
                        acc.HandledTargets,
                        ignores,
                        name => consumedCtorParams is not null && consumedCtorParams.Contains(name),
                        lookups.WritableByName,
                        name => lookups.SourceGroups.ContainsKey(lookups.Flexible ? NormalizeName(name) : name),
                        location,
                        diagnostics,
                        out var mvTgtType))
                {
                    continue;
                }

                if (mv.IsConstant)
                {
                    string literal;
                    if (mv.ConstLiteral is not null)
                    {
                        literal = mv.ConstLiteral;
                    }
                    else if (!TryFormatConstant(mv.Value, mvTgtType, compilation, out literal, out var why))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueTypeMismatch, location, why));
                        continue;
                    }

                    acc.Result.Add(new MemberMap(mvTgt, "", ValueExpression: literal));
                }
                else if (mv.Use is not null)
                {
                    var provider = (valueProviders ?? Array.Empty<(string Name, ITypeSymbol ReturnType)>())
                        .FirstOrDefault(p => StringComparer.Ordinal.Equals(p.Name, mv.Use));
                    if (provider.Name is null || !HasImplicitConversion(compilation, provider.ReturnType, mvTgtType))
                    {
                        diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueUseInvalid,
                            location,
                            $"[MapValue(Use = \"{mv.Use}\")] for '{mvTgt}' must name a parameterless method whose return type is assignable to '{mvTgtType.ToDisplayString()}'"));
                        continue;
                    }

                    acc.Result.Add(new MemberMap(mvTgt, "", ValueExpression: mv.Use + "()"));
                }
                else
                {
                    diagnostics.Add(new DiagnosticInfo(DiagnosticDescriptors.MapValueInvalid,
                        location,
                        $"[MapValue] for '{mvTgt}' provides neither a constant value nor Use="));
                }
            }
        }
    }
}
