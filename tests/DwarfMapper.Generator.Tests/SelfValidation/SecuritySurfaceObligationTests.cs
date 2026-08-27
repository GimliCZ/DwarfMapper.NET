// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using DwarfMapper.Generator.Tests.Contracts;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     Every element declaring a <see cref="SecuritySurface" /> carries that value's obligation, and no
    ///     element that SHOULD declare one gets away with <see cref="SecuritySurface.None" />.
    ///     <para>
    ///         This is the security axis of the surface architecture, added round 27. It follows the rule
    ///         <see cref="SurfaceCategory" /> states in its own docstring — "classifying an element redirects
    ///         its proof rather than waiving it" — so each value carries a different obligation and none is an
    ///         exemption.
    ///     </para>
    ///     <para>
    ///         THE VACUITY PROBLEM AND ITS ANSWER. A default of <c>None</c> is exactly the shape that passes
    ///         while proving nothing: a genuinely security-relevant element that simply never declares anything
    ///         would sail through. So the value is DETECTED as well as declared. The detector reads the
    ///         element's own shape — an attribute exposing <c>AllowNonPublic</c> widens binding and must
    ///         declare <see cref="SecuritySurface.TrustBoundary" />; one exposing <c>MaxDepth</c> sets a
    ///         resource limit and must declare <see cref="SecuritySurface.ResourceBound" />; the blit override
    ///         must declare <see cref="SecuritySurface.MemorySafety" /> — and the test asserts
    ///         <b>declared ⊇ detected</b>.
    ///     </para>
    ///     <para>
    ///         The limit of that guarantee, stated here rather than discovered later: it proves no KNOWN
    ///         mechanism is undeclared. A novel security mechanism goes undetected until a detector is written
    ///         for it. That is a real bound, and the detector list is the place to widen it.
    ///     </para>
    /// </summary>
    public class SecuritySurfaceObligationTests
    {
        private static Type SurfaceAttr =>
            typeof(DwarfMapperAttribute).Assembly.GetType("DwarfMapper.DwarfSurfaceAttribute", throwOnError: true)!;

        /// <summary>Every public attribute in the shipped runtime, with its declared security flags.</summary>
        private static List<(Type Type, int Declared)> Declarations()
        {
            var rows = new List<(Type, int)>();

            foreach (var t in typeof(DwarfMapperAttribute).Assembly.GetTypes())
            {
                if (!t.IsPublic || !typeof(Attribute).IsAssignableFrom(t)) { continue; }

                var data = t.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType == SurfaceAttr);

                if (data is null) { continue; }

                var named = data.NamedArguments.FirstOrDefault(n => n.MemberName == "Security");
                rows.Add((t, named.TypedValue.Value is null ? 0 : (int)named.TypedValue.Value!));
            }

            return rows;
        }

        /// <summary>
        ///     What the element's own shape says it must declare. Widening the detector list is how this rule's
        ///     coverage grows; each entry names a mechanism with a security consequence.
        /// </summary>
        private static int Detect(Type attr)
        {
            var flags = 0;
            var props = attr.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => p.Name)
                .ToHashSet(StringComparer.Ordinal);

            // Widens what the generator will bind to, beyond what a consumer wrote publicly.
            if (props.Contains("AllowNonPublic")) { flags |= 1; }

            // Sets the recursion bound — the guard against a cyclic graph exhausting the stack.
            if (props.Contains("MaxDepth")) { flags |= 4; }

            // The one consumer-facing override of a compile-time memory-layout proof.
            if (string.Equals(attr.Name, "ReinterpretAttribute", StringComparison.Ordinal)) { flags |= 2; }

            return flags;
        }

        [Fact]
        public void The_scan_reads_a_real_and_fully_classified_surface()
        {
            // Non-vacuity: the reflection must actually find the attributes, or every rule below guards an
            // empty set — the failure this repository has produced six times in its own instruments.
            var rows = Declarations();

            Assert.True(rows.Count >= 25,
                $"Only {rows.Count} classified public attributes found. The reflection is pointed at the wrong "
                + "assembly, or [DwarfSurface] stopped being applied — either way the rules below prove nothing.");
        }

        [Fact]
        public void Every_element_declares_at_least_what_its_own_shape_implies()
        {
            var offenders = new List<string>();

            foreach (var (type, declared) in Declarations())
            {
                var detected = Detect(type);
                var missing = detected & ~declared;

                if (missing != 0)
                {
                    offenders.Add($"{type.Name}: declared {declared}, but its shape implies {detected} "
                        + $"(missing {missing})");
                }
            }

            Assert.True(offenders.Count == 0,
                "An element carries a security mechanism it does not declare. Security = None is only honest "
                + "when the element has no such mechanism; here the element's own shape says otherwise. Declare "
                + "the flag and satisfy its obligation:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void Every_declared_security_surface_is_named_in_SECURITY_md()
        {
            // The TrustBoundary/MemorySafety/ResourceBound obligations all begin with "it is written down".
            // An element can be security-relevant and undocumented only by accident, so this catches it.
            var doc = File.ReadAllText(Path.Combine(RepoPaths.Root, "SECURITY.md"));
            var offenders = new List<string>();

            foreach (var (type, declared) in Declarations())
            {
                if (declared == 0) { continue; }

                // The attribute is named either bare or with its Attribute suffix trimmed, as prose does.
                var shortName = type.Name.EndsWith("Attribute", StringComparison.Ordinal)
                    ? type.Name[..^"Attribute".Length]
                    : type.Name;

                if (!doc.Contains(shortName, StringComparison.Ordinal))
                {
                    offenders.Add($"{type.Name} declares Security = {declared} but SECURITY.md never mentions it");
                }
            }

            Assert.True(offenders.Count == 0,
                "A security-relevant element is absent from SECURITY.md's trust model. Every non-None value's "
                + "obligation starts with naming the boundary in the document a reader auditing this library "
                + "will actually open:\n  " + string.Join("\n  ", offenders));
        }

        [Fact]
        public void The_detector_would_catch_an_undeclared_mechanism()
        {
            // Control, both directions. If Detect() returned 0 for everything the second rule would pass
            // whatever anyone declared — and "passes regardless" is the failure mode, not a false alarm.
            Assert.NotEqual(0, Detect(typeof(DwarfMapperAttribute)));
            Assert.NotEqual(0, Detect(typeof(ReinterpretAttribute)));
            Assert.Equal(0, Detect(typeof(MapIgnoreAttribute)));

            // And the real declarations must satisfy the real detector, not a weakened one.
            var mapper = Declarations().Single(r => r.Type == typeof(DwarfMapperAttribute));
            Assert.Equal(Detect(typeof(DwarfMapperAttribute)), Detect(typeof(DwarfMapperAttribute)) & mapper.Declared);
        }
    }
}
