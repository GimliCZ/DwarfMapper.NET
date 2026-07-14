// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Coverage suite for DwarfMapper.Generator.Pipeline.TypeInterfaces.
// Techniques: unit (Roslyn symbol-based), adversary, defensive, fuzzy (oracle), fixture.
namespace DwarfMapper.Generator.Tests.Coverage;

/// <summary>
///     Unit tests for <c>TypeInterfaces</c> symbol predicates,
///     covering IsIntegral / ImplementsIParsable / ImplementsIFormattable / IsINumberBase.
/// </summary>
public class TypeInterfacesCoverageTests
{
    // ─── ImplementsIParsable ──────────────────────────────────────────────────

    private static readonly Dictionary<string, string> ClrNameMap = new(StringComparer.Ordinal)
    {
        ["int"] = "System.Int32",
        ["long"] = "System.Int64",
        ["double"] = "System.Double",
        ["float"] = "System.Single",
        ["bool"] = "System.Boolean",
        ["byte"] = "System.Byte",
        ["short"] = "System.Int16",
        ["decimal"] = "System.Decimal"
    };
    // ─── Roslyn compilation helper ────────────────────────────────────────────

    /// <summary>
    ///     Builds a minimal Roslyn compilation containing <paramref name="source" />
    ///     with the same references as GeneratorTestHarness.
    /// </summary>
    private static (Compilation Compilation, IReadOnlyDictionary<string, INamedTypeSymbol> Types)
        BuildCompilation(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var refs = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .Cast<MetadataReference>()
            .Append(MetadataReference.CreateFromFile(typeof(DwarfMapperAttribute).Assembly.Location))
            .Append(MetadataReference.CreateFromFile(typeof(Queryable).Assembly.Location));

        var compilation = CSharpCompilation.Create(
            "TypeInterfacesTestAsm_" + Guid.NewGuid().ToString("N"),
            new[] { tree },
            refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Collect all types declared in our snippet by walking the syntax tree + SemanticModel.
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var types = new Dictionary<string, INamedTypeSymbol>(StringComparer.Ordinal);
        foreach (var decl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            var sym = model.GetDeclaredSymbol(decl);
            if (sym is INamedTypeSymbol named)
                types[named.Name] = named;
        }

        return (compilation, types);
    }

    /// <summary>Returns the ITypeSymbol for a well-known BCL special type via the compilation.</summary>
    private static ITypeSymbol SpecialTypeSymbol(Compilation compilation, SpecialType st)
    {
        return compilation.GetSpecialType(st);
    }

    // ─── IsIntegral ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SpecialType.System_SByte)]
    [InlineData(SpecialType.System_Byte)]
    [InlineData(SpecialType.System_Int16)]
    [InlineData(SpecialType.System_UInt16)]
    [InlineData(SpecialType.System_Int32)]
    [InlineData(SpecialType.System_UInt32)]
    [InlineData(SpecialType.System_Int64)]
    [InlineData(SpecialType.System_UInt64)]
    [InlineData(SpecialType.System_IntPtr)]
    [InlineData(SpecialType.System_UIntPtr)]
    public void IsIntegral_returns_true_for_integral_special_types(SpecialType st)
    {
        var source = "namespace T { public class Dummy {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = SpecialTypeSymbol(compilation, st);
        Assert.True(TypeInterfaces.IsIntegral(t), $"Expected IsIntegral=true for {st}");
    }

    [Theory]
    [InlineData(SpecialType.System_Single)]
    [InlineData(SpecialType.System_Double)]
    [InlineData(SpecialType.System_Decimal)]
    [InlineData(SpecialType.System_Boolean)]
    [InlineData(SpecialType.System_Char)]
    [InlineData(SpecialType.System_String)]
    public void IsIntegral_returns_false_for_non_integral_special_types(SpecialType st)
    {
        var source = "namespace T { public class Dummy {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = SpecialTypeSymbol(compilation, st);
        Assert.False(TypeInterfaces.IsIntegral(t), $"Expected IsIntegral=false for {st}");
    }

    [Fact]
    public void IsIntegral_returns_false_for_plain_class()
    {
        var source = "namespace T { public class MyClass {} }";
        var (_, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.IsIntegral(types["MyClass"]));
    }

    [Fact]
    public void IsIntegral_returns_false_for_enum()
    {
        var source = "namespace T { public enum Color { R, G, B } }";
        var (compilation, _) = BuildCompilation(source);
        var model = compilation.GetSemanticModel(compilation.SyntaxTrees.First());
        var root = compilation.SyntaxTrees.First().GetRoot();
        foreach (var decl in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            var sym = model.GetDeclaredSymbol(decl);
            if (sym != null)
            {
                Assert.False(TypeInterfaces.IsIntegral(sym));
                return;
            }
        }

        Assert.Fail("Could not find enum symbol");
    }

    [Fact]
    public void IsIntegral_returns_false_for_struct_without_special_type()
    {
        var source = "namespace T { public struct MyStruct { public int X; } }";
        var (_, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.IsIntegral(types["MyStruct"]));
    }

    [Fact]
    public void IsIntegral_returns_false_for_Guid()
    {
        var source = "namespace T { public class Dummy {} }";
        var (compilation, _) = BuildCompilation(source);
        var guid = compilation.GetTypeByMetadataName("System.Guid");
        Assert.NotNull(guid);
        Assert.False(TypeInterfaces.IsIntegral(guid!));
    }

    [Theory]
    [InlineData("int")]
    [InlineData("long")]
    [InlineData("double")]
    [InlineData("float")]
    [InlineData("bool")]
    [InlineData("byte")]
    [InlineData("short")]
    [InlineData("decimal")]
    public void ImplementsIParsable_true_for_numeric_types(string typeName)
    {
        var source = $"namespace T {{ public class Dummy {{ {typeName} x; }} }}";
        var (compilation, _) = BuildCompilation(source);
        var t = compilation.GetTypeByMetadataName(ClrNameMap[typeName]);
        Assert.NotNull(t);
        Assert.True(TypeInterfaces.ImplementsIParsable(compilation, t!),
            $"Expected ImplementsIParsable=true for {typeName}");
    }

    [Fact]
    public void ImplementsIParsable_true_for_Guid()
    {
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = compilation.GetTypeByMetadataName("System.Guid")!;
        Assert.True(TypeInterfaces.ImplementsIParsable(compilation, t));
    }

    [Fact]
    public void ImplementsIParsable_true_for_DateTime()
    {
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = compilation.GetTypeByMetadataName("System.DateTime")!;
        Assert.True(TypeInterfaces.ImplementsIParsable(compilation, t));
    }

    [Fact]
    public void ImplementsIParsable_false_for_plain_class()
    {
        var source = "namespace T { public class Plain {} }";
        var (compilation, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.ImplementsIParsable(compilation, types["Plain"]));
    }

    [Fact]
    public void ImplementsIParsable_for_string_matches_actual_runtime()
    {
        // In .NET 7+, string implements IParsable<string>.
        // This test validates the predicate doesn't crash and returns whatever the runtime provides.
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = compilation.GetSpecialType(SpecialType.System_String);
        // Just assert the method doesn't throw; the boolean result depends on .NET version.
        var result = TypeInterfaces.ImplementsIParsable(compilation, t);
        // On .NET 7+ (including .NET 10), string DOES implement IParsable<string>.
        Assert.True(result);
    }

    [Fact]
    public void ImplementsIParsable_false_when_IParsable_symbol_absent()
    {
        // Build a compilation with no BCL references so System.IParsable`1 is absent.
        var minimalTree = CSharpSyntaxTree.ParseText("namespace T { public class D {} }");
        var minimalCompilation = CSharpCompilation.Create(
            "MinimalAsm_" + Guid.NewGuid().ToString("N"),
            new[] { minimalTree },
            Array.Empty<MetadataReference>(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        // Confirm IParsable`1 is absent in this compilation.
        var parsableOpen = minimalCompilation.GetTypeByMetadataName("System.IParsable`1");
        Assert.Null(parsableOpen);

        // GetSpecialType(System_Object) still works on empty compilation but gives an error type.
        var obj = minimalCompilation.GetSpecialType(SpecialType.System_Object);
        Assert.False(TypeInterfaces.ImplementsIParsable(minimalCompilation, obj));
    }

    // ─── ImplementsIFormattable ───────────────────────────────────────────────

    [Theory]
    [InlineData("System.Int32")]
    [InlineData("System.Double")]
    [InlineData("System.Guid")]
    [InlineData("System.DateTime")]
    [InlineData("System.Decimal")]
    public void ImplementsIFormattable_true_for_numeric_and_guid(string clrName)
    {
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = compilation.GetTypeByMetadataName(clrName)!;
        Assert.NotNull(t);
        Assert.True(TypeInterfaces.ImplementsIFormattable(t),
            $"Expected ImplementsIFormattable=true for {clrName}");
    }

    [Fact]
    public void ImplementsIFormattable_false_for_plain_class()
    {
        var source = "namespace T { public class Plain {} }";
        var (_, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.ImplementsIFormattable(types["Plain"]));
    }

    [Fact]
    public void ImplementsIFormattable_false_for_string()
    {
        // string does NOT implement System.IFormattable.
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = compilation.GetSpecialType(SpecialType.System_String);
        Assert.False(TypeInterfaces.ImplementsIFormattable(t));
    }

    // ─── IsINumberBase ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SpecialType.System_Int32)]
    [InlineData(SpecialType.System_UInt64)]
    [InlineData(SpecialType.System_Byte)]
    [InlineData(SpecialType.System_SByte)]
    [InlineData(SpecialType.System_Int16)]
    [InlineData(SpecialType.System_IntPtr)]
    [InlineData(SpecialType.System_UIntPtr)]
    public void IsINumberBase_true_for_integral_types(SpecialType st)
    {
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = SpecialTypeSymbol(compilation, st);
        Assert.True(TypeInterfaces.IsINumberBase(compilation, t),
            $"Expected IsINumberBase=true for {st}");
    }

    [Fact]
    public void IsINumberBase_true_for_float_via_AllInterfaces()
    {
        // System.Single / Double implement INumberBase<T> through System.Numerics.
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);
        var t = compilation.GetTypeByMetadataName("System.Single")!;
        // float is not integral, so it falls through to AllInterfaces walk.
        var numberBase = compilation.GetTypeByMetadataName("System.Numerics.INumberBase`1");
        if (numberBase is null)
            Assert.False(TypeInterfaces.IsINumberBase(compilation, t));
        else
            Assert.True(TypeInterfaces.IsINumberBase(compilation, t));
    }

    [Fact]
    public void IsINumberBase_false_for_plain_class()
    {
        var source = "namespace T { public class Plain {} }";
        var (compilation, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.IsINumberBase(compilation, types["Plain"]));
    }

    [Fact]
    public void IsINumberBase_false_when_INumberBase_symbol_absent()
    {
        // Build a compilation without System.Numerics.INumberBase`1.
        var minimalTree = CSharpSyntaxTree.ParseText("namespace T { public class D {} }");
        var minimalCompilation = CSharpCompilation.Create(
            "MinimalNum_" + Guid.NewGuid().ToString("N"),
            new[] { minimalTree },
            Array.Empty<MetadataReference>(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var obj = minimalCompilation.GetSpecialType(SpecialType.System_Object);
        // IsIntegral returns false for Object, INumberBase`1 lookup returns null → false.
        Assert.False(TypeInterfaces.IsINumberBase(minimalCompilation, obj));
    }

    // ─── Seeded oracle fuzz: IsIntegral must agree with known-set ────────────

    [Fact]
    public void Fuzz_seeded_IsIntegral_matches_oracle_known_set()
    {
        var source = "namespace T { public class D {} }";
        var (compilation, _) = BuildCompilation(source);

        var integralTypes = new HashSet<SpecialType>
        {
            SpecialType.System_SByte, SpecialType.System_Byte,
            SpecialType.System_Int16, SpecialType.System_UInt16,
            SpecialType.System_Int32, SpecialType.System_UInt32,
            SpecialType.System_Int64, SpecialType.System_UInt64,
            SpecialType.System_IntPtr, SpecialType.System_UIntPtr
        };

        var candidates = new[]
        {
            SpecialType.System_SByte, SpecialType.System_Byte,
            SpecialType.System_Int16, SpecialType.System_UInt16,
            SpecialType.System_Int32, SpecialType.System_UInt32,
            SpecialType.System_Int64, SpecialType.System_UInt64,
            SpecialType.System_IntPtr, SpecialType.System_UIntPtr,
            SpecialType.System_Single, SpecialType.System_Double,
            SpecialType.System_Decimal, SpecialType.System_Boolean,
            SpecialType.System_Char, SpecialType.System_String
        };

        foreach (var st in candidates)
        {
            var t = SpecialTypeSymbol(compilation, st);
            var expected = integralTypes.Contains(st);
            var actual = TypeInterfaces.IsIntegral(t);
            Assert.True(expected == actual, $"IsIntegral mismatch for {st}: expected={expected}, actual={actual}");
        }
    }

    // ─── Adversary: enum type (SpecialType.None) ──────────────────────────────

    [Fact]
    public void IsIntegral_false_for_plain_struct_with_None_SpecialType()
    {
        var source = "namespace T { public struct Vec { public int X; public int Y; } }";
        var (_, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.IsIntegral(types["Vec"]));
    }

    [Fact]
    public void ImplementsIParsable_false_for_user_struct()
    {
        var source = "namespace T { public struct MyStruct { public int X; } }";
        var (compilation, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.ImplementsIParsable(compilation, types["MyStruct"]));
    }

    [Fact]
    public void ImplementsIFormattable_false_for_user_class_and_struct()
    {
        // User-defined types without explicit IFormattable implementation return false.
        var source = "namespace T { public class Cls {} public struct St { public int X; } }";
        var (_, types) = BuildCompilation(source);
        Assert.False(TypeInterfaces.ImplementsIFormattable(types["Cls"]));
        Assert.False(TypeInterfaces.ImplementsIFormattable(types["St"]));
    }
}
