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

    /// <summary>Implemented only by types whose base class lives in an assembly the test below never loads.</summary>
    public interface IOnlyBrokenProbe
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

        /// <summary>
        ///     Record and report (owner ruling 2026-10-27). When the only implementation of an interface is a type that
        ///     failed to load, the factory used to drop the loader's error and build the member as null, so a fixture
        ///     quietly lost the subtype it was meant to exercise. It now fails, naming the type that did not load, its
        ///     assembly and the loader's reason. The assembly is loaded from a FILE: that is what lets the factory read
        ///     which types it declares, so it names only an implementation it can prove is missing.
        /// </summary>
        [Fact]
        public void An_interface_whose_only_implementation_failed_to_load_is_reported_not_left_null()
        {
            var missing = Compile("OnlyBrokenBase",
                "namespace OnlyBroken { public class MissingBase { } public static class Holder { public interface INested { } } }", []);
            // Broken and Outer.Inner are the only implementations, and neither can load. FromGlobal and FromLocal cannot
            // load either: they implement an interface of the missing assembly. The factory reads the supertypes of every
            // type that did not load, so theirs are the shapes its metadata reader has to name: a base type in the same
            // assembly, in the global namespace or not, a generic instantiation, and an interface nested in a type of
            // another assembly.
            var probe = Compile("OnlyBrokenProbe",
                "public class GlobalBase { }\n" +
                "public sealed class FromGlobal : GlobalBase, OnlyBroken.Holder.INested { }\n" +
                "namespace OnlyBroken\n" +
                "{\n" +
                "    public sealed class Broken : MissingBase, DwarfMapper.Testing.Tests.IOnlyBrokenProbe { public int Probe => 3; }\n" +
                "    public static class Outer { public sealed class Inner : MissingBase, DwarfMapper.Testing.Tests.IOnlyBrokenProbe { public int Probe => 4; } }\n" +
                "    public class LocalBase { }\n" +
                "    public interface IGeneric<T> { }\n" +
                "    public sealed class FromLocal : LocalBase, IGeneric<int>, Holder.INested { }\n" +
                "}\n",
                [MetadataReference.CreateFromImage(missing)]);
            var path = Path.Combine(Path.GetTempPath(), "dwarfmapper-only-broken-" + Guid.NewGuid().ToString("N"), "OnlyBrokenProbe.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, probe);
            var loaded = new AssemblyLoadContext("only-broken", isCollectible: false).LoadFromAssemblyPath(path);
            Assert.Throws<ReflectionTypeLoadException>(() => loaded.GetTypes());

            // The same image loaded from memory as well: it has no file to read, so it is recorded but never named.
            using var image = new MemoryStream(probe);
            var inMemory = new AssemblyLoadContext("only-broken-in-memory", isCollectible: false).LoadFromStream(image);
            Assert.Throws<ReflectionTypeLoadException>(() => inMemory.GetTypes());

            var ex = Assert.Throws<InvalidOperationException>(() =>
                ObjectFactoryV2.Create(typeof(IOnlyBrokenProbe), new Random(31), 0, false));

            Assert.Contains(typeof(IOnlyBrokenProbe).FullName!, ex.Message, StringComparison.Ordinal);
            // Both, sorted, and the nested one under the name reflection gives it.
            Assert.Contains("OnlyBroken.Broken, OnlyBroken.Outer+Inner in OnlyBrokenProbe", ex.Message, StringComparison.Ordinal);
            // The loader's own reason, in whatever language the runtime speaks, names the dependency it could not find.
            Assert.Contains("OnlyBrokenBase", ex.Message, StringComparison.Ordinal);
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
