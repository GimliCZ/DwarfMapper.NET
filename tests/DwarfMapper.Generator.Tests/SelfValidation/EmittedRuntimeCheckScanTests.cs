// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Type and layout safety is the ANALYZER's job. Nothing that the generator can prove at compile time
    ///     may be re-checked in the code it emits.
    ///     <para>
    ///         The blit carried an emitted element-size guard for several rounds:
    ///         <c>if (Unsafe.SizeOf&lt;TSrc&gt;() != Unsafe.SizeOf&lt;TDst&gt;()) throw …</c>. It was
    ///         unreachable — equal size is a CONSEQUENCE of the layout proof, so the branch could only be taken
    ///         if the generator itself were broken. Three things were wrong with it: a generator bug would
    ///         surface as a runtime exception in a consumer's production code, which is the worst possible
    ///         place to discover it; the dead branch still counted against the method's IL size and therefore
    ///         against the JIT's inlining budget; and it advertised doubt about a proof the whole design rests
    ///         on.
    ///     </para>
    ///     <para>
    ///         Null checks are NOT in scope and must stay. Null is a genuine runtime value that no compile-time
    ///         proof can exclude — nullable annotations are advisory and the data comes from outside. So does
    ///         <c>GetType()</c> dispatch for <c>[MapDerivedType]</c>, which is runtime by design. The ban here
    ///         is narrow and specific: SIZE and TYPE-IDENTITY checks, which the proof already settles.
    ///     </para>
    /// </summary>
    public class EmittedRuntimeCheckScanTests
    {
        private static readonly Regex EmittedSizeCheck = new(
            @"""[^""]*(?:Unsafe\.SizeOf|\bsizeof\s*\()", RegexOptions.Compiled);

        private static List<string> GeneratorSources()
        {
            var root = Path.Combine(RepoPaths.Root, "src", "DwarfMapper.Generator");
            return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToList();
        }

        [Fact]
        public void No_emitter_writes_a_runtime_SIZE_check_into_generated_code()
        {
            var offenders = new List<string>();

            foreach (var file in GeneratorSources())
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                    if (EmittedSizeCheck.IsMatch(lines[i]))
                    {
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
                    }
            }

            Assert.True(offenders.Count == 0,
                "The generator is emitting a runtime SIZE check. Size equality follows from the layout proof, "
                + "so this can only fire when the generator is broken — and a consumer's production runtime is "
                + "the wrong place to learn that. Refuse the pair at compile time instead:\n  "
                + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_scan_reads_a_real_corpus_and_null_guards_are_deliberately_untouched()
        {
            // Anti-vacuity, both directions. The scan must be reading real files, AND it must not have been
            // written so broadly that it would also ban the null guards — which are legitimate, because null
            // is a runtime value no proof excludes.
            var sources = GeneratorSources();
            Assert.True(sources.Count > 20, $"only {sources.Count} generator sources found — scan is misdirected");

            var text = string.Join("\n", sources.Select(File.ReadAllText));
            Assert.Contains("Collection element was null", text, StringComparison.Ordinal);
            Assert.Contains("Dictionary entry was null", text, StringComparison.Ordinal);
        }

        [Fact]
        public void Element_pairs_of_DIFFERENT_size_are_refused_at_COMPILE_time()
        {
            // The property the deleted runtime guard was pretending to check. It is settled by the proof: a
            // pair whose elements differ in size is never handed to the blit at all, so no emitted code needs
            // to ask. This is the test that makes deleting the guard safe.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public int X; public int Y; }
                             public struct DstV { public long X; public long Y; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var (_, gen) = GeneratorTestHarness.Run(s);

            Assert.DoesNotContain("MemoryMarshal.Cast<", gen, StringComparison.Ordinal);

            // Anti-vacuity: the pair must still MAP — it is a widening conversion, not a refusal. If the
            // mapper produced nothing, the assertion above would pass for the wrong reason.
            Assert.Contains("__DwarfMapColl_", gen, StringComparison.Ordinal);
        }

        [Fact]
        public void A_blittable_pair_emits_a_copy_helper_with_no_conditional_at_all_beyond_the_null_check()
        {
            // The positive shape: after the guard's removal the whole helper is allocate-and-copy. One `if`,
            // and it is the null check. Anything else creeping back in is a runtime check that should have
            // been a compile-time refusal.
            const string s = """
                             using DwarfMapper;
                             namespace Demo;
                             public struct SrcV { public float X; public float Y; public float Z; }
                             public struct DstV { public float X; public float Y; public float Z; }
                             public class C { public SrcV[] V { get; set; } = System.Array.Empty<SrcV>(); }
                             public class D { public DstV[] V { get; set; } = System.Array.Empty<DstV>(); }
                             [DwarfMapper] public partial class M { public partial D Map(C c); }
                             """;
            var gen = GeneratorAssert.CompilesClean(s);

            var start = gen.IndexOf("__DwarfBlit_", StringComparison.Ordinal);
            Assert.True(start >= 0, "expected a blit helper");
            var bodyStart = gen.IndexOf('{', gen.IndexOf("private static", start, StringComparison.Ordinal));
            var end = gen.IndexOf("return __r;", bodyStart, StringComparison.Ordinal);
            Assert.True(end > bodyStart, "could not bound the helper body");

            var body = gen.Substring(bodyStart, end - bodyStart);
            Assert.Contains("MemoryMarshal.Cast<", body, StringComparison.Ordinal); // anti-vacuity
            Assert.Single(Regex.Matches(body, @"\bif\s*\("));
            Assert.Contains("is null", body, StringComparison.Ordinal);
            Assert.DoesNotContain("throw", body, StringComparison.Ordinal);
        }
    }
}
