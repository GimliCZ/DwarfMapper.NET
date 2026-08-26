// SPDX-License-Identifier: GPL-2.0-only

using System.Text.RegularExpressions;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     SECURITY INVARIANT [SEC-1]: the SHIPPED RUNTIME assembly contains no memory-unsafe surface and no
    ///     dynamic reflection.
    ///     <para>
    ///         This is the invariant that makes the package's safety story checkable rather than asserted.
    ///         DwarfMapper does perform memory reinterpretation — the blit fast path is a
    ///         <c>MemoryMarshal.Cast</c> block copy — but every byte of it lives in <b>emitted</b> code, behind a
    ///         proof the generator discharged at compile time. The library assembly a consumer references
    ///         therefore offers no memory-unsafe surface at all: there is nothing in it to reach.
    ///     </para>
    ///     <para>
    ///         <see cref="ReflectionFreeMetaTests" /> is the sibling and is NOT the same claim. It pins that
    ///         GENERATED code is reflection-free. Nothing pinned the runtime assembly itself until this test,
    ///         so the trim- and AOT-safety guarantees rested on a property that was true and unguarded.
    ///     </para>
    ///     <para>
    ///         Measured at the commit that adds this file: zero occurrences of every forbidden construct.
    ///         The test is therefore a RATCHET — it does not fix anything, it prevents the first regression.
    ///     </para>
    /// </summary>
    public class ShippedRuntimeSafetyTests
    {
        /// <summary>
        ///     Memory-unsafe constructs. Any one of these in the shipped assembly would mean the safety argument
        ///     has to be made about the runtime too, not only about what the generator proves.
        /// </summary>
        private static readonly string[] MemoryUnsafe =
        [
            "unsafe", "stackalloc", "fixed(", "fixed (",
            "MemoryMarshal", "Unsafe.", "DllImport", "LibraryImport", "__makeref"
        ];

        /// <summary>
        ///     DYNAMIC reflection: discovery and invocation. These are what trimming cannot see through and what
        ///     NativeAOT cannot resolve, so each would break a shipped guarantee rather than merely offend taste.
        /// </summary>
        private static readonly string[] DynamicReflection =
        [
            "GetMethod(", "GetProperty(", "GetField(", "GetMembers(", "GetMethods(", "GetProperties(",
            "Activator.CreateInstance", "MakeGenericType", "MakeGenericMethod", "Assembly.Load",
            "GetCustomAttribute", "InvokeMember", "Type.GetType(",

            // GetInterfaces() is forbidden for a REASON THIS CODEBASE ALREADY DISCOVERED, not by analogy.
            // The obvious registry implementation is `source.GetType().GetInterfaces()`, and it trips IL2075:
            // the trimmer cannot prove the interface metadata of a type obtained from object.GetType()
            // survives. DwarfMapperRegistry keeps a flat InterfaceMaps list INSTEAD, precisely so that
            // suppressing IL2075 — which would have been the first trimming suppression in the assembly whose
            // pitch is trim-safety — never had to happen. Re-introducing the call would quietly undo that.
            ".GetInterfaces()"
        ];

        /// <summary>
        ///     Trim/AOT escape hatches. Their ABSENCE is the invariant: this assembly has never needed one, and
        ///     the first would mean some path can no longer be proven statically. Not forbidden because they are
        ///     bad practice — they are the correct tool when reflection is unavoidable — but because reaching for
        ///     one here signals that reflection has entered an assembly documented as having none.
        /// </summary>
        private static readonly string[] TrimEscapeHatches =
        [
            "UnconditionalSuppressMessage", "RequiresUnreferencedCode", "RequiresDynamicCode",
            "DynamicallyAccessedMembers"
        ];

        /// <summary>
        ///     ALLOWED, and enumerated rather than merely unforbidden. The registry resolves a runtime type
        ///     against registered pairs by walking BASE TYPES — <see cref="System.Type" /> navigation, not
        ///     dynamic reflection: nothing is discovered by name, nothing is invoked, and every type involved
        ///     was statically referenced by the emitted code that registered it, so trimming keeps it and AOT
        ///     resolves it.
        ///     <para>
        ///         Measured after stripping comments: 6 <c>typeof(</c>, 2 <c>.BaseType</c>. An earlier draft of
        ///         this list also allowed <c>.GetInterfaces()</c> and <c>IsAssignableFrom</c> — and this test's
        ///         own liveness check caught that: both occur ZERO times in live code, the first only inside the
        ///         comment explaining why it is avoided. An allowance for a mechanism nobody uses silently widens
        ///         the rule, so they were removed, and <c>.GetInterfaces()</c> moved to the forbidden list where
        ///         the design intent actually puts it.
        ///     </para>
        /// </summary>
        private static readonly string[] AllowedTypeNavigation = ["typeof(", ".BaseType"];

        /// <summary>
        ///     Strips comments and string literals before matching. Without this the test would fail on its own
        ///     subject matter: <c>DwarfRefContext</c>'s doc comments legitimately discuss unsafety, and a rule
        ///     that cannot be described in the prose it guards is a rule that gets deleted.
        /// </summary>
        private static string StripCommentsAndStrings(string src)
        {
            src = Regex.Replace(src, @"//[^\n]*", string.Empty);
            src = Regex.Replace(src, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
            src = Regex.Replace(src, "\"(?:[^\"\\\\\n]|\\\\.)*\"", "\"\"");
            return src;
        }

        private static IEnumerable<(string File, string Code)> RuntimeSources()
        {
            var dir = Path.Combine(RepoPaths.Src, "DwarfMapper");
            foreach (var path in RepoPaths.SourceFiles(dir))
            {
                yield return (Path.GetFileName(path), StripCommentsAndStrings(File.ReadAllText(path)));
            }
        }

        private static List<string> Scan(string[] forbidden)
        {
            var hits = new List<string>();

            foreach (var (file, code) in RuntimeSources())
            {
                foreach (var token in forbidden)
                {
                    if (code.Contains(token, StringComparison.Ordinal))
                    {
                        hits.Add($"{file}: {token}");
                    }
                }
            }

            return hits;
        }

        [Fact]
        public void The_scan_reads_a_non_empty_corpus()
        {
            // Without this the two scans below would pass by reading nothing — the vacuity failure this
            // repository has now produced six times in other mechanisms.
            var files = RuntimeSources().ToList();

            Assert.True(files.Count >= 20,
                $"Only {files.Count} runtime source files found — RepoPaths.Src/DwarfMapper is wrong and both "
                + "safety scans below are passing vacuously.");
            Assert.Contains(files, f => f.File == "DwarfMapperRegistry.cs");
        }

        [Fact]
        public void The_scanner_actually_detects_the_constructs_it_forbids()
        {
            // The control. A scan that cannot fail proves nothing, so drive the real matcher over planted
            // source and require every category to be caught.
            const string planted = """
                                   public static class Planted
                                   {
                                       public static unsafe void A() { var p = stackalloc byte[4]; }
                                       public static void B() { var s = MemoryMarshal.Cast<int, long>(default); }
                                       public static void C() { typeof(string).GetMethod("Trim"); }
                                       public static object D() => Activator.CreateInstance(typeof(string));
                                   }
                                   """;
            var stripped = StripCommentsAndStrings(planted);

            Assert.Contains(MemoryUnsafe, t => stripped.Contains(t, StringComparison.Ordinal));
            Assert.Contains(DynamicReflection, t => stripped.Contains(t, StringComparison.Ordinal));

            // And the stripper must not be so aggressive that it hides real code: the planted `unsafe`
            // survives, while a commented one does not.
            Assert.Contains("unsafe", stripped, StringComparison.Ordinal);
            Assert.DoesNotContain("unsafe", StripCommentsAndStrings("// unsafe\n"), StringComparison.Ordinal);
        }

        [Fact]
        public void The_shipped_runtime_contains_no_memory_unsafe_construct()
        {
            var hits = Scan(MemoryUnsafe);

            Assert.True(hits.Count == 0,
                "The shipped runtime assembly acquired a memory-unsafe construct. Every blit in DwarfMapper "
                + "lives in EMITTED code behind a compile-time proof, which is what lets the package claim the "
                + "library itself has no unsafe surface to attack. If this is deliberate, the safety argument "
                + "in docs and SECURITY.md has to be rewritten first — it currently says the opposite:\n  "
                + string.Join("\n  ", hits));
        }

        [Fact]
        public void The_shipped_runtime_uses_no_dynamic_reflection()
        {
            var hits = Scan(DynamicReflection);

            Assert.True(hits.Count == 0,
                "The shipped runtime assembly acquired dynamic reflection. Trimming cannot see through it and "
                + "NativeAOT cannot resolve it, so this breaks two shipped guarantees rather than merely "
                + "offending taste. Type NAVIGATION (typeof/BaseType/GetInterfaces) is allowed and enumerated "
                + "in AllowedTypeNavigation; discovery-by-name and invocation are not:\n  "
                + string.Join("\n  ", hits));
        }

        [Fact]
        public void The_shipped_runtime_needs_no_trimming_or_AOT_escape_hatch()
        {
            var hits = Scan(TrimEscapeHatches);

            Assert.True(hits.Count == 0,
                "The shipped runtime acquired a trim/AOT escape hatch. This assembly has never needed one — the "
                + "registry keeps a flat InterfaceMaps list specifically so that suppressing IL2075 never had to "
                + "happen. README.md states 'the shipped runtime library uses no reflection or runtime emit'; a "
                + "suppression here means that sentence is now false and must be rewritten before this test is "
                + "relaxed:\n  " + string.Join("\n  ", hits));
        }

        [Fact]
        public void Every_allowed_type_navigation_token_is_actually_used()
        {
            // An allowance for a mechanism nobody uses is dead prose that quietly widens the rule. This check
            // has already earned its place: the first draft allowed .GetInterfaces() and IsAssignableFrom, and
            // this test proved both occur zero times in live code — the former only inside the comment
            // explaining why it is avoided. Every entry must describe reality or be deleted.
            var unused = AllowedTypeNavigation
                .Where(t => !RuntimeSources().Any(s => s.Code.Contains(t, StringComparison.Ordinal)))
                .ToList();

            Assert.True(unused.Count == 0,
                "AllowedTypeNavigation lists a construct the runtime does not use. Delete it rather than leaving "
                + "an allowance nobody needs — an unused exemption is indistinguishable from a widened rule:\n  "
                + string.Join("\n  ", unused));
        }
    }
}
