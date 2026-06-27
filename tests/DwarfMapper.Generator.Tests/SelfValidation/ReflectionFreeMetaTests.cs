// SPDX-License-Identifier: GPL-2.0-only
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Fuzzing;
using Xunit;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
/// STRUCTURAL reflection-free / AOT-safety invariant. DwarfMapper's headline guarantee is that the
/// emitted mapping code uses NO runtime reflection — that is what makes it NativeAOT- and trim-safe and
/// suitable for regulated/embedded targets. This is enforced by the CI AOT publish gate, but that is
/// coarse; this meta-test asserts it directly at the source level across a broad feature surface (the
/// fuzz seed generators + every advanced feature + a hand-picked spread): the generated code must contain
/// none of the tokens that would indicate reflection-based mapping. A future change that reached for
/// reflection (instead of emitting concrete member access) would trip this immediately, with no AOT
/// toolchain required.
///
/// Note: a bare <c>GetType()</c> is allowed — it appears only inside the [MapDerivedType] "no arm matched"
/// exception MESSAGE (runtime type name for a helpful error), never to drive a mapping.
/// </summary>
public class ReflectionFreeMetaTests
{
    // Unambiguous reflection-FOR-MAPPING indicators. Each would mean the generator emitted runtime member
    // discovery instead of concrete, AOT-safe member access.
    private static readonly Regex Forbidden = new(
        @"System\.Reflection|Activator\.|\.GetProperty\(|\.GetField\(|\.GetMethod\(|\.GetMembers\(|" +
        @"MakeGenericType|MakeGenericMethod|GetRuntimeProperty|GetRuntimeMethod|PropertyInfo|FieldInfo|" +
        @"MethodInfo|\bdynamic\b|InvokeMember|Type\.GetType\(",
        RegexOptions.Compiled);

    public static IEnumerable<object[]> Sources()
    {
        for (var seed = 0; seed < 40; seed++) yield return new object[] { SyntheticSchema.GenerateBehavioral(seed), $"behavioral#{seed}" };
        for (var seed = 0; seed < 24; seed++) yield return new object[] { SyntheticSchema.GenerateWithAdvancedFeatures(seed), $"advanced#{seed}" };
    }

    [Theory]
    [MemberData(nameof(Sources))]
    public void Generated_code_uses_no_runtime_reflection(string source, string label)
    {
        var (_, generated) = GeneratorTestHarness.Run(source);
        var match = Forbidden.Match(generated);
        Assert.True(!match.Success,
            $"Generated code for {label} contains a reflection token '{match.Value}' — DwarfMapper must " +
            "emit concrete member access only (reflection breaks the NativeAOT/trim guarantee). " +
            "Replace the reflective construct with emitted concrete code.");
    }

    // Explicit spread over the features most likely to be tempted into reflection (polymorphism, graph
    // degradation, projection, generic-ish shapes).
    [Theory]
    [InlineData("""
        using DwarfMapper;
        namespace Demo;
        public class Animal { public string Name { get; set; } = ""; }
        public class Cat : Animal { public int Lives { get; set; } }
        public class AnimalDto { public string Name { get; set; } = ""; }
        public class CatDto : AnimalDto { public int Lives { get; set; } }
        [DwarfMapper] public partial class M {
            [MapDerivedType<Cat, CatDto>] public partial AnimalDto Map(Animal a);
        }
        """)]
    [InlineData("""
        using DwarfMapper;
        using System.Linq;
        namespace Demo;
        public class S { public int Id { get; set; } public string Name { get; set; } = ""; }
        public class D { public int Id { get; set; } public string Name { get; set; } = ""; }
        [DwarfMapper] public partial class M {
            public partial System.Linq.IQueryable<D> Project(System.Linq.IQueryable<S> src);
        }
        """)]
    public void Feature_specific_output_uses_no_runtime_reflection(string source)
    {
        var (_, generated) = GeneratorTestHarness.Run(source);
        Assert.False(Forbidden.IsMatch(generated),
            $"reflection token found: {Forbidden.Match(generated).Value}");
    }
}
