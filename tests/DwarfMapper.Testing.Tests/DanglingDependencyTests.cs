// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DwarfMapper.Testing.Tests
{
    /// <summary>
    ///     Implemented only by types in an assembly this test compiles at run time. Nothing else implements it, so the
    ///     factory's candidate cache for it is empty until the test below asks, and the scan happens inside that test.
    /// </summary>
    public interface IDanglingProbe
    {
        int Probe { get; }
    }

    /// <summary>
    ///     The factory scans every loaded assembly for concrete candidates, and an assembly whose dependency cannot be
    ///     resolved throws <see cref="ReflectionTypeLoadException" /> from <c>GetTypes()</c>. Its loadable types must still
    ///     be offered, and the scan must not fail.
    ///     <para>
    ///         Owner ruling 2026-09-15 (T-Q2): that catch is reached through a real pathway, not exempted. The owner
    ///         wrote "we can make it hanging". This test reads that as a DANGLING DEPENDENCY: assembly A holds one type
    ///         deriving from a type in assembly B, and one type that needs nothing else. A is loaded without B.
    ///     </para>
    /// </summary>
    public class DanglingDependencyTests
    {
        private const string MissingSource = "namespace Dangling { public class MissingBase { } }";

        private const string ProbeSource =
            "namespace Dangling\n" +
            "{\n" +
            "    public sealed class Broken : MissingBase, DwarfMapper.Testing.Tests.IDanglingProbe { public int Probe => 1; }\n" +
            "    public sealed class Fine : DwarfMapper.Testing.Tests.IDanglingProbe { public int Probe => 2; }\n" +
            "}\n";

        [Fact]
        public void A_type_in_an_assembly_with_a_dangling_dependency_is_still_substituted()
        {
            var missing = Compile("DanglingBase", MissingSource, []);
            var probe = Compile("DanglingProbe", ProbeSource, [MetadataReference.CreateFromImage(missing)]);

            // Loaded WITHOUT DanglingBase. This context resolves nothing itself; the Default context supplies
            // DwarfMapper.Testing.Tests, so IDanglingProbe keeps its identity, and nothing supplies DanglingBase.
            using var image = new MemoryStream(probe);
            var loaded = new AssemblyLoadContext("dangling-dependency", isCollectible: false).LoadFromStream(image);

            // The precondition that makes this test mean something: the pathway really raises the exception.
            Assert.Throws<ReflectionTypeLoadException>(() => loaded.GetTypes());

            var made = ObjectFactoryV2.Create(typeof(IDanglingProbe), new Random(30), 0, false);

            Assert.Equal("Dangling.Fine", made?.GetType().FullName);
        }

        private static byte[] Compile(string assemblyName, string source, MetadataReference[] extra)
        {
            var compilation = CSharpCompilation.Create(
                assemblyName,
                [CSharpSyntaxTree.ParseText(source)],
                PlatformReferences().Concat(extra),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

            using var output = new MemoryStream();
            var emit = compilation.Emit(output);
            Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
            return output.ToArray();
        }

        /// <summary>
        ///     The runtime's trusted platform assemblies, plus this test assembly, which declares
        ///     <see cref="IDanglingProbe" />.
        /// </summary>
        private static IEnumerable<MetadataReference> PlatformReferences()
        {
            var paths = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Append(typeof(IDanglingProbe).Assembly.Location)
                .GroupBy(Path.GetFileNameWithoutExtension, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            return paths.Select(p => MetadataReference.CreateFromFile(p));
        }
    }
}
