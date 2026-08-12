// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     The rules by which a user-written method becomes a converter — pinned, because they are
///     action-at-a-distance by nature and were undocumented until Round 18.
/// </summary>
/// <remarks>
///     <para>
///         The generator scans the mapper's methods for one whose signature converts <c>srcType</c> to
///         <c>tgtType</c> and adopts it automatically. That is a deliberate feature: it is the replacement for
///         AutoMapper's <c>ConvertUsing</c> / <c>ITypeConverter&lt;S,D&gt;</c>, and it is why a plain
///         <c>Dst Convert(Src s)</c> on the mapper "just works".
///     </para>
///     <para>
///         It is also the mechanism behind two silent-data-loss bugs Round 18 found, both fixed by RESERVING
///         methods the author had already dedicated to something: a <c>Use=</c> converter written for one
///         member was being applied to another, and a <c>[MapConstructor]</c> factory was being adopted as a
///         collection element converter.
///     </para>
///     <para>
///         <b>The decision (2026-08-12): keep the convenience, document it, and pin it.</b> Requiring an
///         attribute on every converter would break the AutoMapper migration story the feature exists to
///         serve, and the two real failures were both cases of a DEDICATED method being reused — which
///         reservation already prevents. Ambiguity was already a hard error. What was missing was that none of
///         this was written down or asserted, so a change here would have been invisible.
///     </para>
/// </remarks>
public class ConverterAdoptionPolicyTests
{
    [Fact]
    public void A_signature_matching_method_is_adopted_as_a_converter()
    {
        // The feature itself: no attribute, no Use=, just a method whose signature fits.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public Guid Code { get; set; } }
            public class Dst { public string Code { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M
            {
                private static string Render(Guid g) => "G:" + g;
            }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        Assert.Contains("Render(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_matching_methods_are_a_hard_error_not_a_silent_choice()
    {
        // Picking one arbitrarily would make the mapping depend on declaration order. DWARF013 refuses.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public Guid Code { get; set; } }
            public class Dst { public string Code { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M
            {
                private static string RenderA(Guid g) => "A:" + g;
                private static string RenderB(Guid g) => "B:" + g;
            }
            """;

        Assert.NotEmpty(GeneratorAssert.Reports(src, "DWARF013"));
    }

    [Fact]
    public void A_method_dedicated_by_Use_is_withheld_from_auto_adoption()
    {
        // Round-18 bug #1, pinned as policy rather than only as a regression: naming a converter for a member
        // says it belongs to THAT member. In the codebase that surfaced it, a `string BuildDocumentId(Guid)`
        // written for Document.Id was also serving Document.DonationId — every record would have stored the
        // date-prefixed id in the plain field.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public Guid Code { get; set; } }
            public class Dst
            {
                public string Tag { get; set; } = "";
                public string Code { get; set; } = "";
            }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            [MapProperty<Src, Dst>(nameof(Src.Code), nameof(Dst.Tag), Use = nameof(Decorate))]
            public partial class M
            {
                private static string Decorate(Guid g) => "X_" + g;
            }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        Assert.Contains("Tag = Decorate(", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Code = Decorate(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void Reservation_is_mapper_wide_not_per_method()
    {
        // The first fix reserved per-method, and a sibling method with no Use= of its own still stole the
        // converter. Pinned because the narrower version looked correct and was not.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Source { public Guid Code { get; set; } }
            public class TargetA { public string Code { get; set; } = ""; }
            public class TargetB { public string Code { get; set; } = ""; }

            [DwarfMapper]
            public partial class M
            {
                [MapProperty(nameof(Source.Code), nameof(TargetA.Code), Use = nameof(Decorate))]
                public partial TargetA ToA(Source s);

                public partial TargetB ToB(Source s);

                private static string Decorate(Guid g) => "X_" + g;
            }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        // ToA asked for it; ToB did not. Exactly one call site.
        Assert.Equal(1, generated.Split("Decorate(", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void A_MapConstructor_factory_is_withheld_too()
    {
        // Round-18 bug #2, same root cause, different attribute. A factory only CONSTRUCTS its pair's target;
        // adopted as a general element converter it produced objects with nothing filled in — a whole
        // collection of blanks, behind a green build.
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Item { public int V { get; set; } }
            // Ordinary and constructible on purpose: the point under test is that the FACTORY is not
            // adopted as a general element converter, not the separate (and known) limitation that a
            // synthesized element mapper cannot see a factory at all.
            public class ItemDto
            {
                public int V { get; set; }
                public static ItemDto Empty => new();
            }
            public class Box { public Item Only { get; set; } = new(); }
            public class BoxDto { public ItemDto Only { get; set; } = ItemDto.Empty; }

            [DwarfMapper]
            [GenerateMap<Item, ItemDto>]
            [MapConstructor<Item, ItemDto>(nameof(Create))]
            [GenerateMap<Box, BoxDto>]
            public partial class M
            {
                private static ItemDto Create(Item i) => ItemDto.Empty;
            }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        // The nested Box -> BoxDto mapping must not route through the factory: it would hand back Empty and
        // discard V. The factory belongs to the pair that named it.
        Assert.Contains("Only = ", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Only = Create(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_collection_METHOD_withholds_the_factory_too()
    {
        // The same bug at the other door, found by building a real consumer against this generator. The
        // [GenerateMap] collection path reserved the factory; the DECLARED-METHOD path did not, and emitted
        // `result.Add(CreateUserCommand(i))` — the bare factory, without the member assignments the real
        // element map performs afterwards. That factory ignores its argument, so the call returned a list of
        // blank objects. Silent, total data loss, no diagnostic. The consumer's own source carries a
        // fourteen-line comment describing it and telling readers not to rely on the map.
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class DbCommand { public int V { get; set; } }
            public class UserCommand
            {
                private UserCommand() { }
                public int V { get; init; }
                public static UserCommand Empty => new();
            }

            [DwarfMapper]
            [GenerateMap<DbCommand, UserCommand>]
            [MapConstructor<DbCommand, UserCommand>(nameof(CreateUserCommand))]
            public partial class M
            {
                public partial ICollection<UserCommand> ToUserCommands(List<DbCommand> source);

                private static UserCommand CreateUserCommand(DbCommand source) => UserCommand.Empty;
            }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        // The element route is the declared pair, which applies the factory AND the member assignments.
        Assert.Contains("__r.Add(Map(__item))", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("Add(CreateUserCommand(", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void A_declared_collection_method_does_not_resolve_to_itself()
    {
        // Its own signature is the perfect match for the pair it is declared for, so the whole-pair
        // resolution has to exclude it — otherwise `return ToItems(source);` inside ToItems.
        const string src = """
            using System.Collections.Generic;
            using DwarfMapper;
            namespace Demo;
            public class Item { public int V { get; set; } }
            public class ItemDto { public int V { get; set; } }

            [DwarfMapper]
            public partial class M
            {
                public partial List<ItemDto> ToItems(List<Item> source);
            }
            """;

        var generated = GeneratorAssert.CompilesClean(src);

        Assert.DoesNotContain("return ToItems(source)", generated, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrelated_helper_with_a_non_matching_signature_is_never_adopted()
    {
        // The boundary of the feature: adoption is by SIGNATURE, so a helper that does not convert the pair
        // is irrelevant no matter what it is called.
        const string src = """
            using System;
            using DwarfMapper;
            namespace Demo;
            public class Src { public Guid Code { get; set; } }
            public class Dst { public string Code { get; set; } = ""; }

            [DwarfMapper]
            [GenerateMap<Src, Dst>]
            public partial class M
            {
                private static int Unrelated(string s) => s.Length;
            }
            """;

        var generated = GeneratorAssert.EmitsCompilableCode(src);

        Assert.DoesNotContain("Unrelated(", generated, StringComparison.Ordinal);
    }
}
