// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Roslyn512.Tests
{
    /// <summary>The harness itself: it runs the Roslyn 5.12 build, and a generated map runs.</summary>
    public sealed class HarnessTests
    {
        [Fact]
        public void This_project_tests_the_generator_built_against_Roslyn_5_12()
        {
            // The vacuity guard for everything in this project: a reference that slid back to the Roslyn 5.0 build
            // would still compile and pass every test that does not need C# 15.
            var roslyn = typeof(DwarfGenerator).Assembly.GetReferencedAssemblies()
                .Single(a => string.Equals(a.Name, "Microsoft.CodeAnalysis", StringComparison.Ordinal));
            Assert.Equal(new Version(5, 12), new Version(roslyn.Version!.Major, roslyn.Version.Minor));
        }

        [Fact]
        public void Harness_runs_the_512_build()
        {
            var run = Roslyn512Harness.CompilesClean("""
                using DwarfMapper;
                namespace Demo;
                public class A { public int X { get; set; } }
                public class B { public int X { get; set; } }
                [DwarfMapper] public partial class M { public partial B Map(A a); }
                public static class Probe { public static string Run() => new M().Map(new A { X = 7 }).X.ToString(System.Globalization.CultureInfo.InvariantCulture); }
                """);

            Assert.Equal("7", Roslyn512Harness.Invoke(run, "Demo.Probe", "Run"));
        }
    }
}
