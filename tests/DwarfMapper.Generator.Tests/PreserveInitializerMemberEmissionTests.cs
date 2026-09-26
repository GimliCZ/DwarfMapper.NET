// SPDX-License-Identifier: GPL-2.0-only

// Under ReferenceHandling = Preserve a recursion-capable method is emitted in register-before-populate form: construct,
// register, then assign members. That emitter assigned EVERY member after construction — so any init-only or
// required destination member was CS8852 / CS9035 in the consumer's .g.cs, accepted without a word, as soon as the
// method had one auto-nested object member (which forces it recursion-capable under Preserve). No cycle was needed:
// `Dst { int V { get; init; } AddrDto A { get; init; } }` was enough. The main path writes an object initializer and
// never had the problem; a7e39c1 found the same split for deferred members and the combination fuzz never crossed
// Preserve with init-only.
namespace DwarfMapper.Generator.Tests
{
    public class PreserveInitializerMemberEmissionTests
    {
        private const string Preserve = "[DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.Preserve)]";

        [Fact]
        public void Acyclic_init_only_members_beside_a_nested_object_compile_under_Preserve()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Addr { public string City { get; set; } = ""; }
                               public class AddrDto { public string City { get; init; } = ""; }
                               public class Src { public int V { get; set; } public Addr A { get; set; } = new(); }
                               public class Dst { public int V { get; init; } public AddrDto A { get; init; } = new(); }
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.DoesNotContain("__dwarf_t.V =", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__dwarf_t.A =", generated, StringComparison.Ordinal);
            Assert.DoesNotContain("__dwarf_t.City =", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void An_init_only_scalar_beside_a_settable_cycle_is_initialized_and_the_cycle_still_populates_after_registration()
        {
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Src { public int V { get; set; } public Src? Next { get; set; } }
                               public class Dst { public int V { get; init; } public Dst? Next { get; set; } }
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            var construct = generated.IndexOf("var __dwarf_t = new global::Demo.Dst()", StringComparison.Ordinal);
            var register = generated.IndexOf(".SetReference(s, __dwarf_t);", construct, StringComparison.Ordinal);
            var next = generated.IndexOf("__dwarf_t.Next = ", register, StringComparison.Ordinal);
            Assert.True(construct >= 0 && register > construct && next > register, generated);
            Assert.Contains("V = s.V,", generated.Substring(construct, register - construct), StringComparison.Ordinal);
        }

        [Fact]
        public void A_required_member_bound_by_a_constructor_is_also_initialized_under_Preserve()
        {
            // Without [SetsRequiredMembers], C# still demands the member in the initializer.
            const string src = """
                               using DwarfMapper;
                               namespace Demo;
                               public class Addr { public string City { get; set; } = ""; }
                               public class AddrDto { public string City { get; set; } = ""; }
                               public class Src { public string Name { get; set; } = ""; public Addr A { get; set; } = new(); }
                               public class Dst
                               {
                                   public Dst(string name) { Name = name; }
                                   public required string Name { get; init; }
                                   public AddrDto A { get; set; } = new();
                               }
                               """ + Preserve + " public partial class M { public partial Dst Map(Src s); }";

            var generated = GeneratorAssert.EmitsCompilableCode(src);

            Assert.Contains("name: s.Name)", generated, StringComparison.Ordinal);
            Assert.Contains("Name = s.Name,", generated, StringComparison.Ordinal);
        }
    }
}
