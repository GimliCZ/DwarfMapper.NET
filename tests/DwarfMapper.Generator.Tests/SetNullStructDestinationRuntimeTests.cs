// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Reflection;

namespace DwarfMapper.Generator.Tests
{
    /// <summary>
    ///     Runtime proof for DWARF108's fallback: a struct destination reached through a
    ///     recursion-capable pair under <c>OnCycle = SetNull</c> falls back to the plain
    ///     depth-guarded body, and that fallback is actually SAFE — not merely that it compiles.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Runtime proof lives here AND (as of round 30's DWARF108 blocking-finding fix) as a
    ///         suppressed fixture in <c>DwarfMapper.IntegrationTests</c>
    ///         (<c>SetNullAdversarialRuntimeTests.SnStructMapper</c>). DWARF108 now reports at the
    ///         <c>[DwarfMapper]</c> class identifier rather than a null location, but that alone does
    ///         NOT make it suppressible: proven empirically (in that order) that a <c>#pragma</c>, a
    ///         <c>[SuppressMessage]</c>, AND a file-scoped <c>.editorconfig</c> severity override all
    ///         still fail against it even WITH a real location. The real cause is architectural, not
    ///         positional — a source generator (<c>IIncrementalGenerator</c>) carries no
    ///         <c>SupportedDiagnostics</c> contract the way a <c>DiagnosticAnalyzer</c> does, so
    ///         Roslyn's pragma/SuppressMessage/editorconfig suppression pipeline never runs against
    ///         its diagnostics at all. Only <c>&lt;NoWarn&gt;</c> — an MSBuild-level filter applied by
    ///         ID string after generation completes — actually reaches it; see that project's
    ///         fixture and its <c>.csproj</c> comment. This file keeps the in-process reflection proof
    ///         (no MSBuild round-trip needed) via
    ///         <see cref="GeneratorTestHarness.EmitAssembly(string)" />, which compiles through the
    ///         real generator with its own (non-warnings-as-errors) options, so the Warning is just
    ///         data in the result here, exactly as it is for every other diagnostic-observing test in
    ///         this project.
    ///     </para>
    /// </remarks>
    public class SetNullStructDestinationRuntimeTests
    {
        private const string Source = """
                                      using System.Collections.Generic;
                                      using DwarfMapper;
                                      namespace Demo;
                                      public class Node    { public int V { get; set; } public List<Node>? Children { get; set; } = new(); }
                                      public struct NodeDto { public int V { get; set; } public List<NodeDto>? Children { get; set; } }
                                      [DwarfMapper(OnCycle = OnCycleStrategy.SetNull, MaxDepth = 20)]
                                      public partial class M { public partial NodeDto Map(Node n); }
                                      """;

        private static (Type NodeType, Type DtoType, object Mapper, MethodInfo MapMethod) Load()
        {
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(Source);
            Assert.True(asm is not null, "expected the fallback to compile clean; errors: " + string.Join("; ", errors));

            var nodeType = asm!.GetType("Demo.Node")!;
            var dtoType = asm.GetType("Demo.NodeDto")!;
            var mapperType = asm.GetType("Demo.M")!;
            var mapper = Activator.CreateInstance(mapperType)!;
            var mapMethod = mapperType.GetMethod("Map")!;
            return (nodeType, dtoType, mapper, mapMethod);
        }

        [Fact]
        public void Acyclic_tree_maps_every_value_through_the_fallback()
        {
            var (nodeType, dtoType, mapper, mapMethod) = Load();

            var root = Activator.CreateInstance(nodeType)!;
            nodeType.GetProperty("V")!.SetValue(root, 1);
            var child2 = Activator.CreateInstance(nodeType)!;
            nodeType.GetProperty("V")!.SetValue(child2, 2);
            var child3 = Activator.CreateInstance(nodeType)!;
            nodeType.GetProperty("V")!.SetValue(child3, 3);
            var children = (IList)nodeType.GetProperty("Children")!.GetValue(root)!;
            children.Add(child2);
            children.Add(child3);

            var dto = mapMethod.Invoke(mapper, [root])!;

            Assert.Equal(1, dtoType.GetProperty("V")!.GetValue(dto));
            var dtoChildren = (IList)dtoType.GetProperty("Children")!.GetValue(dto)!;
            Assert.Equal(2, dtoChildren.Count);
            Assert.Equal(2, dtoType.GetProperty("V")!.GetValue(dtoChildren[0]));
            Assert.Equal(3, dtoType.GetProperty("V")!.GetValue(dtoChildren[1]));
        }

        /// <summary>
        ///     Proves the fallback is SAFE, not just compiling: a struct destination cannot represent
        ///     "null" for a back-edge, so SetNull's early termination is unavailable for this pair —
        ///     the plain depth guard is what actually protects the call from an unbounded walk.
        /// </summary>
        [Fact]
        public void Cyclic_source_throws_DwarfMappingDepthException_instead_of_hanging_or_overflowing()
        {
            var (nodeType, _, mapper, mapMethod) = Load();

            var a = Activator.CreateInstance(nodeType)!;
            nodeType.GetProperty("V")!.SetValue(a, 1);
            var children = (IList)nodeType.GetProperty("Children")!.GetValue(a)!;
            children.Add(a); // self-cycle: an unguarded depth-first walk never terminates.

            var thrown = Assert.Throws<TargetInvocationException>(() => mapMethod.Invoke(mapper, [a]));
            Assert.IsType<DwarfMappingDepthException>(thrown.InnerException);
        }
    }
}
