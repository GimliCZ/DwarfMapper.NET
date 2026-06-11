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
}
