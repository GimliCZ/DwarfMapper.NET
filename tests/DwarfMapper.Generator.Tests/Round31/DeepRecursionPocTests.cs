// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     AutoMapper CVE-2026-32933 (GHSA-rvv3-g6hj-g44x): ~25-30k nesting levels exhaust the stack and kill the
    ///     process. DwarfMapper's answer is a default MaxDepth with a catchable exception. This pins it for the
    ///     advisory's own shape and for collection- and dictionary-routed recursion, in both reference modes.
    /// </summary>
    /// <remarks>
    ///     A GUARD, expected green before any code change: the behaviour already exists, and the point of the test is
    ///     that it cannot be lost. The claim it backs is the one a team leaving AutoMapper is told in SECURITY.md and
    ///     docs/MIGRATION.md, so it must be evidence rather than an implication.
    /// </remarks>
    public sealed class DeepRecursionPocTests
    {
        private const int Levels = 30_000; // the advisory's PoC depth

        public static TheoryData<string, string> Shapes()
        {
            return new TheoryData<string, string>
            {
                { "Self", "None" }, { "Self", "Preserve" },
                { "List", "None" }, { "List", "Preserve" },
                { "Dict", "None" }, { "Dict", "Preserve" }
            };
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void A_30000_level_graph_ends_in_a_catchable_depth_exception_not_a_dead_process(string shape, string mode)
        {
            var ns = "T03" + shape + mode;
            var member = shape switch
            {
                "Self" => "public Node? Next { get; set; }",
                "List" => "public System.Collections.Generic.List<Node> Next { get; set; } = new();",
                _ => "public System.Collections.Generic.Dictionary<string, Node> Next { get; set; } = new();"
            };
            var dtoMember = member.Replace("Node", "NodeDto", StringComparison.Ordinal);
            var src = $$"""
                #nullable enable
                using DwarfMapper;
                namespace {{ns}};
                public class Node { public int V { get; set; } {{member}} }
                public class NodeDto { public int V { get; set; } {{dtoMember}} }
                [DwarfMapper(ReferenceHandling = ReferenceHandlingStrategy.{{mode}})]
                public partial class M { public partial NodeDto Map(Node n); }
                """;
            var (asm, errors) = GeneratorTestHarness.EmitAssembly(src);
            Assert.True(asm is not null, "emit failed: " + string.Join(",", errors.Select(e => e.Id)));

            var nodeT = asm!.GetType(ns + ".Node", true)!;
            var next = nodeT.GetProperty("Next")!;
            object root = Activator.CreateInstance(nodeT)!, cur = root;
            for (var i = 0; i < Levels; i++)
            {
                var child = Activator.CreateInstance(nodeT)!;
                switch (shape)
                {
                    case "Self":
                        next.SetValue(cur, child);
                        break;
                    case "List":
                        ((IList)next.GetValue(cur)!).Add(child);
                        break;
                    default:
                        ((IDictionary)next.GetValue(cur)!).Add("k", child);
                        break;
                }

                cur = child;
            }

            var mapperT = asm.GetType(ns + ".M", true)!;
            var map = mapperT.GetMethod("Map")!;
            Exception? thrown = null;

            // Default 1 MB stack, like a request thread. A stack overflow here kills the test host: that is the red.
            var t = new Thread(
                () =>
                {
                    try
                    {
                        map.Invoke(map.IsStatic ? null : Activator.CreateInstance(mapperT), [root]);
                    }
#pragma warning disable CA1031 // the assertion is on WHICH exception escaped
                    catch (TargetInvocationException e)
                    {
                        thrown = e.InnerException;
                    }
                    catch (Exception e)
                    {
                        thrown = e;
                    }
#pragma warning restore CA1031
                },
                1024 * 1024);
            t.Start();
            t.Join();

            Assert.NotNull(thrown);
            Assert.Equal("DwarfMapper.DwarfMappingDepthException", thrown!.GetType().FullName);
        }
    }
}
