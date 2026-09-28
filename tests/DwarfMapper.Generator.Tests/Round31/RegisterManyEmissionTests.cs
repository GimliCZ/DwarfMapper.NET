// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Text.RegularExpressions;
using Xunit;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     Round 31 T11 (research P6): a module initializer registers its assembly's create-maps in ONE
    ///     <c>RegisterMany</c> call, so the registry's interface list is copied once per assembly rather than once per
    ///     entry. The runtime equivalence to sequential <c>Register</c> calls is pinned in the integration tests; this
    ///     pins that the generator actually uses it — including for the six collection shapes, which are the
    ///     interface-keyed entries the change is for.
    /// </summary>
    public sealed class RegisterManyEmissionTests
    {
        private const string Src = """
            #nullable enable
            using DwarfMapper;
            namespace T11;
            public class S { public int V { get; set; } }
            public class D { public int V { get; set; } }
            public class S2 { public int V { get; set; } }
            public class D2 { public int V { get; set; } }
            [DwarfMapper] public partial class M { public partial D Map(S s); public partial D2 Map(S2 s); }
            """;

        [Fact]
        public void The_module_initializer_registers_in_one_batch()
        {
            var generated = GeneratorTestHarness.RunAll(Src).GeneratedSource;
            Assert.Single(Regex.Matches(generated, @"DwarfMapperRegistry\.RegisterMany\(", RegexOptions.CultureInvariant));
            Assert.DoesNotContain("DwarfMapperRegistry.Register(", generated, StringComparison.Ordinal);
        }

        [Fact]
        public void The_batch_carries_the_pairs_and_their_collection_shapes()
        {
            var generated = GeneratorTestHarness.RunAll(Src).GeneratedSource;
            // Two pairs, each with itself plus six collection shapes.
            Assert.Equal(14, Regex.Matches(generated, @"^\s+\(typeof\(", RegexOptions.Multiline | RegexOptions.CultureInvariant).Count);
            Assert.Contains("(typeof(global::System.Collections.Generic.IEnumerable<global::T11.S>)", generated, StringComparison.Ordinal);
        }
    }
}
