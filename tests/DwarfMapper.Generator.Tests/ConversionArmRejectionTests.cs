// SPDX-License-Identifier: GPL-2.0-only

using Microsoft.CodeAnalysis;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     The paths where a conversion arm CLAIMS a pair and then rejects it — as distinct from declining it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>TryResolveConversion</c> is a dispatch chain with three outcomes per arm: resolved, rejected,
    ///         and not-mine. The first and third are exercised constantly. The second was not exercised at all:
    ///         a round-27 coverage measurement found the rejection lines of three arms executed by no test,
    ///         which means nothing distinguished "this arm owns the pair and it is wrong" from "this arm has
    ///         nothing to say". Those reach different code — one ends in DWARF005, the other continues down the
    ///         chain — so the difference is worth a test each.
    ///     </para>
    ///     <para>
    ///         Also here: two element-level paths the same measurement found unexercised, on the collection and
    ///         dictionary arms.
    ///     </para>
    /// </remarks>
    public class ConversionArmRejectionTests
    {
        /// <summary>A nullable source into a nullable-capable target whose INNER conversion does not exist.</summary>
        [Fact]
        public void Nullable_capable_target_with_an_unresolvable_inner_conversion_reports_DWARF005()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int? A { get; set; } }
                               public class Dst { public System.Guid? A { get; set; } }
                               [DwarfMapper]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            GeneratorAssert.Reports(src, "DWARF005");
        }

        /// <summary>A dictionary whose KEY type cannot be converted.</summary>
        [Fact]
        public void Dictionary_with_an_unresolvable_key_conversion_reports_DWARF005()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Src { public Dictionary<int, string> M { get; set; } = new(); }
                               public class Dst { public Dictionary<System.Guid, string> M { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class Mp { public partial Dst Map(Src s); }
                               """;

            GeneratorAssert.Reports(src, "DWARF005");
        }

        /// <summary>The same rejection, one type argument along: the VALUE cannot be converted.</summary>
        [Fact]
        public void Dictionary_with_an_unresolvable_value_conversion_reports_DWARF005()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Src { public Dictionary<string, int> M { get; set; } = new(); }
                               public class Dst { public Dictionary<string, System.Guid> M { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class Mp { public partial Dst Map(Src s); }
                               """;

            GeneratorAssert.Reports(src, "DWARF005");
        }

        /// <summary>
        ///     A nullable-annotated element copied into a non-nullable one: the element assignment must THROW on
        ///     a null rather than let it through the annotation.
        /// </summary>
        /// <remarks>
        ///     Requires nullable annotations to be ON, which is why it passes <c>NullableContextOptions.Enable</c>
        ///     — with them off the annotations carry no information and the branch cannot be reached at all.
        /// </remarks>
        [Fact]
        public void Nullable_source_element_into_a_non_nullable_element_throws_rather_than_smuggling_null()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class Src { public List<string?> Items { get; set; } = new(); }
                               public class Dst { public List<string> Items { get; set; } = new(); }
                               [DwarfMapper]
                               public partial class M { public partial Dst Map(Src s); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src, NullableContextOptions.Enable);

            // The guard is the point: without it a null element lands in a List<string> that says it holds none.
            Assert.Contains("throw", generated, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Under Preserve, a dictionary whose KEY is itself a mapped object must have its key converter made
        ///     recursion-capable, so the shared identity context threads through it.
        /// </summary>
        [Fact]
        public void Preserve_forces_a_dictionary_key_object_map_recursion_capable()
        {
            const string src = """
                               using DwarfMapper;
                               using System.Collections.Generic;
                               namespace Demo;
                               public class K { public int Id { get; set; } }
                               public class KD { public int Id { get; set; } }
                               public class V { public int N { get; set; } }
                               public class VD { public int N { get; set; } }
                               public class Src { public Dictionary<K, V> M { get; set; } = new(); }
                               public class Dst { public Dictionary<KD, VD> M { get; set; } = new(); }
                               [DwarfMapper(AutoNest = true, ReferenceHandling = ReferenceHandlingStrategy.Preserve)]
                               public partial class Mp { public partial Dst Map(Src s); }
                               """;

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            // Recursion-capable means the key mapper carries the shared context rather than being called bare.
            Assert.Contains("DwarfRefContext", generated, StringComparison.Ordinal);
        }
    }
}
