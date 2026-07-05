// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Generator.Tests;

/// <summary>
///     End-to-end RUNTIME tests for the generated <c>DwarfMap.Validate()</c> and the AutoValidate module
///     initializer: emit a real validation-root assembly, load it, and invoke the generated code so we prove it
///     actually runs and reports correctly — passes when the consumed map is registered, throws a
///     <c>DwarfMapValidationException</c> naming the missing pair when it is not, and (AutoValidate) fails fast
///     from the module initializer.
/// </summary>
public sealed class AutoValidateRuntimeTests
{
    // Compile + run the generator + emit, WITHOUT loading (used for the provider metadata ref).
    private static byte[] EmitImage(string assemblyName, string source, params MetadataReference[] extra)
    {
        var compilation = GeneratorTestHarness.BuildCompilation(assemblyName, source).AddReferences(extra);
        var driver = CSharpGeneratorDriver.Create(new DwarfGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        using var ms = new MemoryStream();
        var result = output.Emit(ms);
        Assert.True(result.Success,
            assemblyName + " failed to emit:\n" + string.Join("\n",
                result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error)));
        return ms.ToArray();
    }

    private static object? InvokeStatic(Assembly rootAsm, string method, BindingFlags access)
    {
        var dwarfMap = rootAsm.GetType("DwarfMapper.DwarfMap");
        Assert.NotNull(dwarfMap); // the generated fail-fast entry point exists in the root
        var m = dwarfMap!.GetMethod(method, access | BindingFlags.Static);
        Assert.NotNull(m);
        return m!.Invoke(null, null);
    }

    private static Exception? Unwrap(Exception? e)
    {
        return e is TargetInvocationException tie ? tie.InnerException : e;
    }

    // Walks the InnerException chain (module-init failures wrap in TypeInitializationException, reflection in
    // TargetInvocationException) and returns the DwarfMapValidationException, or fails the test if absent.
    private static DwarfMapValidationException FindValidationException(Exception? e)
    {
        for (var cur = e; cur is not null; cur = cur.InnerException)
            if (cur is DwarfMapValidationException v)
                return v;

        Assert.Fail("expected a DwarfMapValidationException in the chain, got: " + (e?.ToString() ?? "<none>"));
        throw new InvalidOperationException(); // unreachable
    }

    /// <summary>
    ///     Builds a validation-root assembly that consumes a map whose provider assembly is referenced
    ///     METADATA-ONLY and never loaded — so the pair satisfies DWARF061 at compile time but is genuinely
    ///     unregistered at runtime. The shared POCOs live in a third assembly that IS loaded (so the root's
    ///     <c>typeof(Doc)</c> resolves without ever touching the provider). Runs <paramref name="body" /> with the
    ///     loaded root and the shared Doc/Model types.
    /// </summary>
    private static void WithUnregisteredPairRoot(bool autoValidate, Action<Assembly, Type, Type> body)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var sharedName = "Shared_" + suffix;

        var sharedImage = EmitImage(sharedName, $$"""
                                                  namespace {{sharedName}};
                                                  public class Doc { public int V { get; set; } }
                                                  public class Model { public int V { get; set; } }
                                                  """);
        var sharedRef = MetadataReference.CreateFromImage(sharedImage);
        var sharedAsm = Assembly.Load(sharedImage);
        var docType = sharedAsm.GetType(sharedName + ".Doc")!;
        var modelType = sharedAsm.GetType(sharedName + ".Model")!;

        // Byte-array-loaded assemblies aren't discoverable by name; hand the contracts assembly back on demand.
        ResolveEventHandler resolver = (_, e) =>
            e.Name.StartsWith(sharedName, StringComparison.Ordinal) ? sharedAsm : null;
        AppDomain.CurrentDomain.AssemblyResolve += resolver;
        try
        {
            // Provider: provides the pair (manifest + self-registration) but is referenced metadata-only.
            var providerRef = MetadataReference.CreateFromImage(EmitImage("Prov_" + suffix, $$"""
                  [global::DwarfMapper.DwarfMapper]
                  [global::DwarfMapper.GenerateMap<{{sharedName}}.Doc, {{sharedName}}.Model>]
                  public partial class Mapper { }
                  """, sharedRef));

            var autoAttr = autoValidate ? "(AutoValidate = true)" : "";
            var rootImage = EmitImage("RootMiss_" + suffix, $$"""
                                                              [assembly: global::DwarfMapper.DwarfMapperValidationRoot{{autoAttr}}]
                                                              namespace RootMiss;
                                                              public class Consumer
                                                              {
                                                                  public {{sharedName}}.Model C(global::DwarfMapper.IDwarfMapper m, {{sharedName}}.Doc d)
                                                                      => m.Map<{{sharedName}}.Model>(d);
                                                              }
                                                              """, sharedRef, providerRef);

            Assert.False(DwarfMapperRegistry.IsProvided(docType, modelType),
                "precondition: provider must not be registered before the root's code runs");

            body(Assembly.Load(rootImage), docType, modelType);
        }
        finally
        {
            AppDomain.CurrentDomain.AssemblyResolve -= resolver;
        }
    }

    [Fact]
    public void Validate_passes_at_runtime_when_the_consumed_map_is_registered()
    {
        // A root that both provides (self-registers via its module init at load) and consumes the pair.
        const string root = """
                            [assembly: global::DwarfMapper.DwarfMapperValidationRoot]
                            namespace RootPass;
                            public class Doc { public int V { get; set; } }
                            public class Model { public int V { get; set; } }
                            public class Consumer { public Model C(global::DwarfMapper.IDwarfMapper m, Doc d) => m.Map<Model>(d); }
                            [global::DwarfMapper.DwarfMapper]
                            [global::DwarfMapper.GenerateMap<Doc, Model>]
                            public partial class M { }
                            """;

        var asm = Assembly.Load(EmitImage("RootPass_" + Guid.NewGuid().ToString("N"), root));

        Assert.Null(Unwrap(Record.Exception(() => InvokeStatic(asm, "Validate", BindingFlags.Public))));
    }

    [Fact]
    public void Validate_throws_naming_the_missing_pair_then_passes_once_registered()
    {
        WithUnregisteredPairRoot(false, (rootAsm, docType, modelType) =>
        {
            var tie = Assert.Throws<TargetInvocationException>(() =>
                InvokeStatic(rootAsm, "Validate", BindingFlags.Public));
            var inner = Assert.IsType<DwarfMapValidationException>(tie.InnerException);
            Assert.Contains(docType.Name, inner.Message, StringComparison.Ordinal);
            Assert.Contains(modelType.Name, inner.Message, StringComparison.Ordinal);

            // Once the pair is registered, the same Validate() passes.
            DwarfMapperRegistry.Register(docType, modelType, s => s);
            Assert.Null(Unwrap(Record.Exception(() => InvokeStatic(rootAsm, "Validate", BindingFlags.Public))));
        });
    }

    [Fact]
    public void AutoValidate_module_initializer_fires_on_load_and_fails_fast_on_a_missing_map()
    {
        WithUnregisteredPairRoot(true, (rootAsm, docType, modelType) =>
        {
            // Force the root MODULE initializer to run — without naming __DwarfAutoValidate — so the test
            // actually exercises the [ModuleInitializer] auto-firing (a direct call would throw even if the
            // attribute were dropped). The AutoValidate initializer calls Validate(), which fails fast.
            var thrown = Record.Exception(() => RuntimeHelpers
                .RunModuleConstructor(rootAsm.ManifestModule.ModuleHandle));
            Assert.NotNull(thrown);
            var v = FindValidationException(thrown);
            Assert.Contains(docType.Name, v.Message, StringComparison.Ordinal);
            Assert.Contains(modelType.Name, v.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Validate_does_not_falsely_flag_the_roots_own_maps_as_ambiguous()
    {
        // Regression: Validate() re-invokes the root's __Register (for ordering-safety), and __Register also
        // runs as a [ModuleInitializer]. A run-once guard must stop the re-run from re-registering — otherwise
        // DwarfMapperRegistry.Register would treat the re-add as a competing provider and set IsAmbiguous=true
        // for the assembly's own single-provider maps.
        const string root = """
                            [assembly: global::DwarfMapper.DwarfMapperValidationRoot]
                            namespace RootAmbig;
                            public class Doc { public int V { get; set; } }
                            public class Model { public int V { get; set; } }
                            public class Consumer { public Model C(global::DwarfMapper.IDwarfMapper m, Doc d) => m.Map<Model>(d); }
                            [global::DwarfMapper.DwarfMapper]
                            [global::DwarfMapper.GenerateMap<Doc, Model>]
                            public partial class M { }
                            """;
        var asm = Assembly.Load(EmitImage("RootAmbig_" + Guid.NewGuid().ToString("N"), root));
        var docType = asm.GetType("RootAmbig.Doc")!;
        var modelType = asm.GetType("RootAmbig.Model")!;

        // Each Validate() triggers module-init __Register + the explicit __Register call. Twice for good measure.
        InvokeStatic(asm, "Validate", BindingFlags.Public);
        InvokeStatic(asm, "Validate", BindingFlags.Public);

        Assert.True(DwarfMapperRegistry.IsProvided(docType, modelType));
        Assert.False(DwarfMapperRegistry.IsAmbiguous(docType, modelType),
            "the root's own single-provider map must not be flagged ambiguous after Validate re-runs __Register");
    }
}
