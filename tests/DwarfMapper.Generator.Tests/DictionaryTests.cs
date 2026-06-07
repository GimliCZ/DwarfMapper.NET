// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper.Generator.Tests;

public class DictionaryTests
{
    private static void NoErrors(string s)
    {
        var (diagnostics, _) = GeneratorTestHarness.Run(s);
        Assert.DoesNotContain(diagnostics, d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(s));
    }

    [Fact]
    public void Dictionary_same_types_maps()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public Dictionary<string,int> M { get; set; } = new(); }
            public class B { public Dictionary<string,int> M { get; set; } = new(); }
            [DwarfMapper] public partial class X { public partial B Map(A a); }
            """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("__kv.Key", gen, StringComparison.Ordinal);
        Assert.Contains("__kv.Value", gen, StringComparison.Ordinal);
    }

    [Fact]
    public void Dictionary_converts_key_and_value()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Addr { public string City { get; set; } = ""; }
            public class AddrDto { public string City { get; set; } = ""; }
            public class A { public Dictionary<int,Addr> M { get; set; } = new(); }
            public class B { public Dictionary<long,AddrDto> M { get; set; } = new(); }
            [DwarfMapper] public partial class X
            {
                public partial B Map(A a);
                public partial AddrDto ToDto(Addr x);
            }
            """;
        NoErrors(s);
        var (_, gen) = GeneratorTestHarness.Run(s);
        Assert.Contains("ToDto(", gen, StringComparison.Ordinal); // value mapped via declared mapper
    }

    [Fact]
    public void ReadOnlyDictionary_source_maps()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class A { public IReadOnlyDictionary<string,int> M { get; set; } = new Dictionary<string,int>(); }
            public class B { public Dictionary<string,int> M { get; set; } = new(); }
            [DwarfMapper] public partial class X { public partial B Map(A a); }
            """;
        NoErrors(s);
    }

    [Fact]
    public void Dictionary_enum_value_maps()
    {
        const string s = """
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public enum C { Red, Green }
            public class A { public Dictionary<string,C> M { get; set; } = new(); }
            public class B { public Dictionary<string,int> M { get; set; } = new(); }
            [DwarfMapper] public partial class X { public partial B Map(A a); }
            """;
        NoErrors(s);
    }
}
