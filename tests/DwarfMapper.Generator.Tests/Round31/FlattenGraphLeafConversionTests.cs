// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Found while mapping T08's conversion family (2026-09-27): a <c>[FlattenGraph]</c> node's leaf members were
    ///     resolved WITHOUT the mapper's <c>ImplicitConversions</c> setting (the two leaf calls left it at its default of
    ///     true), and a leaf that resolved had its diagnostics thrown away. So under
    ///     <c>[DwarfMapper(ImplicitConversions = false)]</c> a lossy <c>long</c> → <c>double</c> leaf, refused as DWARF038
    ///     in a plain map, was converted SILENTLY inside a flattened graph — and without strict mode its DWARF038
    ///     suggestion vanished too. The I20 shape again: a setting read at every endpoint but one.
    /// </summary>
    public sealed class FlattenGraphLeafConversionTests
    {
        private const string Linear = """
            #nullable enable
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public class Node    { public long Big { get; set; } public Node? Next { get; set; } }
            public class NodeDto { public double Big { get; set; } public NodeDto? Next { get; set; } }
            public class Root    { public Node? Entry { get; set; } }
            public class RootDto { public List<NodeDto> Nodes { get; set; } = new(); }

            """;

        private const string Hetero = """
            #nullable enable
            using DwarfMapper;
            using System.Collections.Generic;
            namespace Demo;
            public abstract class FsNode { public string Name { get; set; } = ""; }
            public class Folder : FsNode { public List<FsNode> Children { get; set; } = new(); }
            public class File   : FsNode { public long Size { get; set; } }
            public abstract class FsNodeDto { public string Name { get; set; } = ""; }
            public class FolderDto : FsNodeDto { public List<FsNodeDto>? Children { get; set; } }
            public class FileDto   : FsNodeDto { public double Size { get; set; } }
            public class Tree    { public FsNode? Root { get; set; } }
            public class TreeDto { public List<FsNodeDto> Nodes { get; set; } = new(); }

            """;

        private const string LinearMapper = "public partial class M { [FlattenGraph(\"Entry\", \"Nodes\")] public partial RootDto Map(Root r); }";

        private const string HeteroMapper = "public partial class M { [FlattenGraph(nameof(Tree.Root), nameof(TreeDto.Nodes))]" +
                                            " [MapDerivedType<Folder, FolderDto>] [MapDerivedType<File, FileDto>] public partial TreeDto Map(Tree t); }";

        [Theory]
        [InlineData("linear")]
        [InlineData("hetero")]
        public void A_lossy_leaf_is_refused_under_ImplicitConversions_false(string shape)
        {
            var src = (shape == "linear" ? Linear : Hetero) + "[DwarfMapper(ImplicitConversions = false)]\n" +
                      (shape == "linear" ? LinearMapper : HeteroMapper);
            var dwarf038 = GeneratorTestHarness.Run(src).Diagnostics.Where(d => string.Equals(d.Id, "DWARF038", StringComparison.Ordinal)).ToList();
            Assert.Contains(dwarf038, d => d.Severity == DiagnosticSeverity.Error);
        }

        [Theory]
        [InlineData("linear")]
        [InlineData("hetero")]
        public void A_lossy_leaf_keeps_its_suggestion_when_implicit_conversions_are_allowed(string shape)
        {
            var src = (shape == "linear" ? Linear : Hetero) + "[DwarfMapper]\n" + (shape == "linear" ? LinearMapper : HeteroMapper);
            var diagnostics = GeneratorTestHarness.Run(src).Diagnostics;
            Assert.Contains(diagnostics, d => string.Equals(d.Id, "DWARF038", StringComparison.Ordinal));
            Assert.DoesNotContain(diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        }
    }
}
