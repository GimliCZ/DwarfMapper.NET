// SPDX-License-Identifier: GPL-2.0-only
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Phase-4 tests: the ambient REQUIRES manifest — auto-detected from <c>IDwarfMapper.Map&lt;TDest&gt;(src)</c>
/// call sites and declared via <c>[UsesMap]</c>, emitted as <c>[assembly: DwarfRequiresMap(...)]</c>.
/// </summary>
public sealed class AmbientRequiresGeneratorTests
{
    private static string Requires(string source)
        => GeneratorTestHarness.RunAndGetSource(source, "DwarfMapper.AmbientRequires.g.cs");

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
        Assert.Contains("[assembly: global::DwarfMapper.DwarfRequiresMap(typeof(global::Demo.Doc), typeof(global::Demo.Model))]", req, System.StringComparison.Ordinal);
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

        Assert.Contains("typeof(global::Demo.Doc), typeof(global::Demo.Model)", Requires(s), System.StringComparison.Ordinal);
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
        Assert.Contains("typeof(global::Demo.Doc), typeof(global::Demo.Model)", req, System.StringComparison.Ordinal);
        Assert.Contains("typeof(global::Demo.Doc), typeof(global::Demo.Other)", req, System.StringComparison.Ordinal);
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
}
