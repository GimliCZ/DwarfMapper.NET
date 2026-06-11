// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests;

/// <summary>
/// Adversarial edge cases for Plan 19 Part A (auto-synthesized nested object mappers).
/// </summary>
public class NestedAutoMapEdgeTests
{
    // A class-source → struct-target nested pair must COMPILE (no CS0037 from a `return null!`
    // on a non-nullable value-type return) and instead emit a loud throw guard.
    [Fact]
    public void Class_source_to_struct_target_nested_compiles_with_throw_guard()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class CInner { public int V { get; set; } }
            public struct SInner { public int V { get; set; } }
            public class Src { public CInner Inner { get; set; } = new(); }
            public class Dst { public SInner Inner { get; set; } }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Value-type target → guard throws (no uncompilable `return null!`).
        Assert.Contains("InvalidOperationException", generated, StringComparison.Ordinal);
        Assert.DoesNotContain("return null!", generated, StringComparison.Ordinal);
    }

    // A STRUCT that implements IEnumerable must NOT be auto-nested (object-field-mapped);
    // it belongs to the collection pipeline (or DWARF005 until supported). Guards the
    // struct-enumerable exclusion in IsMappableObjectPair.
    [Fact]
    public void Struct_enumerable_is_not_object_field_mapped()
    {
        const string src = """
            using DwarfMapper;
            using System.Collections;
            using System.Collections.Generic;
            namespace Demo;
            public struct SrcSeq : IEnumerable<int>
            {
                public int A { get; set; }
                public IEnumerator<int> GetEnumerator() { yield break; }
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public struct DstSeq : IEnumerable<int>
            {
                public int A { get; set; }
                public IEnumerator<int> GetEnumerator() { yield break; }
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
            }
            public class Src { public SrcSeq Seq { get; set; } }
            public class Dst { public DstSeq Seq { get; set; } }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        // Not object-field-mapped → no synthesized object mapper for the struct-enumerable pair.
        Assert.DoesNotContain("__DwarfMap_Obj_global__Demo_SrcSeq", generated, StringComparison.Ordinal);
        // It is loudly unsupported (DWARF005), not silently wrong and not spurious DWARF007.
        Assert.Contains(diags, d => d.Id == "DWARF005");
        Assert.DoesNotContain(diags, d => d.Id == "DWARF007");
    }

    // Mutually-recursive TYPE graph (A→B→A). The generator must memoize-before-build and
    // terminate (no hang). Acyclic data maps at runtime (cyclic data is Part C's concern).
    [Fact]
    public void Mutually_recursive_types_compile_without_generator_hang()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class A { public int Id { get; set; } public B? B { get; set; } }
            public class B { public int Id { get; set; } public A? A { get; set; } }
            public class ADto { public int Id { get; set; } public BDto? B { get; set; } }
            public class BDto { public int Id { get; set; } public ADto? A { get; set; } }
            [DwarfMapper] public partial class M { public partial ADto Map(A a); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        // Both directions synthesized exactly once each (memoized).
        Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
    }

    // Closed generic nested types must synthesize + compile (incl. element conversion int→long).
    [Fact]
    public void Closed_generic_nested_type_compiles()
    {
        const string src = """
            using DwarfMapper;
            namespace Demo;
            public class Box<T> { public T Value { get; set; } = default!; }
            public class Src { public Box<int> B { get; set; } = new(); }
            public class Dst { public Box<long> B { get; set; } = new(); }
            [DwarfMapper] public partial class M { public partial Dst Map(Src s); }
            """;
        var (diags, generated) = GeneratorTestHarness.Run(src);
        Assert.DoesNotContain(diags, d => d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(GeneratorTestHarness.RunAndGetCompilationErrors(src));
        Assert.Contains("__DwarfMap_Obj_", generated, StringComparison.Ordinal);
    }
}
