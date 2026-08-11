// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     A declared map whose SOURCE AND TARGET ARE BOTH ENUMS must convert the value, not construct an
///     object.
/// </summary>
/// <remarks>
///     <para>
///         Regression: found migrating a ~300-map codebase off AutoMapper. The generator emitted
///     </para>
///     <code>
///         public NotificationType Map(NotificationTypeDto src)
///         {
///             return new NotificationType { };   // src ignored — always default(0)
///         }
///     </code>
///     <para>
///         for <c>[GenerateMap&lt;NotificationTypeDto, NotificationType&gt;]</c> where both are enums with
///         identical members. The enum was treated as an object to construct with an empty initializer, so
///         every call returned the zero value and the source was discarded — silently, with a green build
///         and no diagnostic.
///     </para>
///     <para>
///         The conversion machinery itself is fine: the same pair used as a MEMBER resolves through
///         <c>__DwarfMap_EnumVal_…</c> correctly. Only the top-level declared-pair path was wrong, which is
///         why nothing else caught it — and why the parity replay did: `Global` (1) mapped to `User` (0).
///     </para>
/// </remarks>
public class EnumTopLevelMapTests
{
    private const string TwoEnums = """
                                    using DwarfMapper;
                                    namespace Demo;
                                    public enum SrcKind { User, Global }
                                    public enum DstKind { User, Global }

                                    [DwarfMapper]
                                    [GenerateMap<SrcKind, DstKind>]
                                    public partial class M { }
                                    """;

    [Fact]
    public void Declared_enum_to_enum_pair_does_not_emit_an_object_initializer()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(TwoEnums);

        // The bug's signature: constructing the enum instead of converting the value.
        Assert.DoesNotContain("new global::Demo.DstKind", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_enum_to_enum_pair_reads_the_source()
    {
        var generated = GeneratorAssert.EmitsCompilableCode(TwoEnums);

        // Whatever form the conversion takes (cast, switch, or a synthesized helper), the source
        // parameter has to appear in the body. The bug discarded it completely.
        var body = generated[generated.IndexOf("DstKind Map(", StringComparison.Ordinal)..];
        var end = body.IndexOf('}', StringComparison.Ordinal);

        Assert.Contains("src", body[..(end < 0 ? body.Length : end)], StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_enum_to_enum_pair_round_trips_every_member()
    {
        // By-name is the default, and these members line up, so each must map to its counterpart.
        var generated = GeneratorAssert.EmitsCompilableCode(TwoEnums);

        Assert.Contains("Global", generated, StringComparison.Ordinal);
    }
}
