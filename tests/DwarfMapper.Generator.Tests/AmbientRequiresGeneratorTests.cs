// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Phase-4 tests: the ambient REQUIRES manifest — auto-detected from <c>IDwarfMapper.Map&lt;TDest&gt;(src)</c>
    ///     call sites and declared via <c>[UsesMap]</c>, emitted as <c>[assembly: DwarfRequiresMap(...)]</c>.
    /// </summary>
    public sealed class AmbientRequiresGeneratorTests
    {
        private static string Requires(string source)
        {
            return GeneratorTestHarness.RunAndGetSource(source, "DwarfMapper.AmbientRequires.g.cs");
        }

        [Fact]
        public void Facade_call_with_explicit_destination_is_detected()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Doc { }
                             public class Model { }
                             public class Consumer
                             {
                                 private readonly IDwarfMapper _mapper;
                                 public Consumer(IDwarfMapper mapper) { _mapper = mapper; }
                                 public Model Convert(Doc d) => _mapper.Map<Model>(d);
                             }
                             """;

            var req = Requires(s);
            Assert.Contains(
                "[assembly: global::DwarfMapper.DwarfRequiresMap(typeof(global::Demo.Doc), typeof(global::Demo.Model))]",
                req,
                StringComparison.Ordinal);
        }

        [Fact]
        public void Facade_call_two_type_args_is_detected()
        {
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Doc { }
                             public class Model { }
                             public class Consumer
                             {
                                 public Model Convert(IDwarfMapper m, Doc d) => m.Map<Doc, Model>(d);
                             }
                             """;

            Assert.Contains("typeof(global::Demo.Doc), typeof(global::Demo.Model)", Requires(s), StringComparison.Ordinal);
        }

        [Fact]
        public void Explicit_UsesMap_assembly_and_class_are_detected()
        {
            const string s = """
                             using DwarfMapper;
                             [assembly: UsesMap(typeof(Demo.Doc), typeof(Demo.Model))]
                             namespace Demo;
                             public class Doc { }
                             public class Model { }
                             public class Other { }
                             [UsesMap<Doc, Other>]
                             public class Consumer { }
                             """;

            var req = Requires(s);
            Assert.Contains("typeof(global::Demo.Doc), typeof(global::Demo.Model)", req, StringComparison.Ordinal);
            Assert.Contains("typeof(global::Demo.Doc), typeof(global::Demo.Other)", req, StringComparison.Ordinal);
        }

        [Fact]
        public void Non_facade_Map_call_and_object_source_are_not_detected()
        {
            // A `Map` method on something OTHER than IDwarfMapper, and a facade call whose source is `object`,
            // must NOT be recorded as a Requires (no manifest emitted at all here).
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public class Model { }
                             public class NotAMapper { public T Map<T>(object o) => default!; }
                             public class Consumer
                             {
                                 public Model A(NotAMapper n, object o) => n.Map<Model>(o);
                                 public Model B(IDwarfMapper m, object o) => m.Map<Model>(o);
                             }
                             """;

            Assert.Equal(string.Empty, Requires(s));
        }

        private const string Types = """
                                     using DwarfMapper;
                                     namespace Demo;
                                     public class Doc { }
                                     public class Model { }
                                     internal class HiddenDoc { }
                                     internal class HiddenModel { }

                                     """;

        /// <summary>
        ///     A <c>Map&lt;T&gt;</c> call on a <c>dynamic</c> receiver is bound at run time, so there is no method to
        ///     recognise as the facade and no pair to record.
        /// </summary>
        [Fact]
        public void Facade_shaped_call_on_a_dynamic_receiver_is_not_detected()
        {
            Assert.Equal(string.Empty, Requires(Types + "public class C { public Model A(dynamic m, Doc d) => m.Map<Model>(d); }"));
        }

        /// <summary>
        ///     A facade call passed a typeless <c>null</c> has no source type, so it names no pair to validate.
        /// </summary>
        [Fact]
        public void Facade_call_passed_a_typeless_null_is_not_detected()
        {
            Assert.Equal(string.Empty, Requires(Types + "public class C { public Model A(IDwarfMapper m) => m.Map<Model>(null); }"));
        }

        /// <summary>
        ///     A non-generic <c>[UsesMap]</c> given <c>null</c> for either type names no pair to validate.
        /// </summary>
        [Theory]
        [InlineData("typeof(Demo.Doc), null", "Doc")]
        [InlineData("null, typeof(Demo.Model)", "Model")]
        public void UsesMap_given_null_for_a_type_is_not_recorded(string arguments, string declared)
        {
            Assert.Equal(string.Empty,
                Requires("using DwarfMapper;\n[assembly: UsesMap(" + arguments + ")]\nnamespace Demo;\npublic class " + declared + " { }\n"));
        }

        /// <summary>
        ///     A pair whose source or destination cannot be written in <c>typeof(...)</c> on an assembly attribute is
        ///     not an ambient key: a type parameter, <c>dynamic</c>, a pointer or a function pointer, on either side.
        /// </summary>
        [Theory]
        [InlineData("public class C { public Model A<T>(IDwarfMapper m, T item) => m.Map<Model>(item!); }")]
        [InlineData("public class C { public T A<T>(IDwarfMapper m, Doc d) => m.Map<T>(d); }")]
        [InlineData("public class C { public dynamic A(IDwarfMapper m, Doc d) => m.Map<dynamic>(d); }")]
        [InlineData("public class C { public Model A(IDwarfMapper m, Doc d) => m.Map<dynamic, Model>(d); }")]
        [InlineData("public unsafe class C { public void A(IDwarfMapper m, Doc d) { m.Map<int*>(d); } }")]
        [InlineData("public unsafe class C { public Model A(IDwarfMapper m, Doc d) => m.Map<delegate*<void>, Model>(d); }")]
        public void A_pair_with_an_unnameable_type_is_not_recorded(string consumer)
        {
            Assert.Equal(string.Empty, Requires(Types + consumer));
        }

        /// <summary>
        ///     Only effectively-public pairs cross an assembly boundary, so an internal source or destination is
        ///     in-assembly consumption and records nothing.
        /// </summary>
        [Theory]
        [InlineData("public class C { internal Model A(IDwarfMapper m, HiddenDoc d) => m.Map<Model>(d); }")]
        [InlineData("public class C { internal HiddenModel A(IDwarfMapper m, Doc d) => m.Map<HiddenModel>(d); }")]
        public void A_pair_with_a_non_public_type_is_not_recorded(string consumer)
        {
            Assert.Equal(string.Empty, Requires(Types + consumer));
        }
    }
}
