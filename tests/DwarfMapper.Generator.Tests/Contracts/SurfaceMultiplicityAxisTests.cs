// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;

namespace DwarfMapper.Generator.Tests.Contracts
{
    /// <summary>
    ///     B37 — the ×2 multiplicity axis has to render two DIFFERENT applications, or it asks nothing the
    ///     single-application axis did not already ask.
    ///     <para>
    ///         <c>SampleArgument</c>'s own doc comment says so and was fixed for it; <c>ArgumentsFor</c> was not,
    ///         so every element with a declared <c>Arguments</c> list rendered its second application
    ///         byte-identical to its first. Two tests, because the fix has two halves that fail in opposite
    ///         directions: the axis must now VARY, and variant 1 must NOT have moved — axes 1 and 2 render at
    ///         variant 1, so a rotation that reached them would move every constructor and property cell of
    ///         every declared-arguments element while this test still passed.
    ///     </para>
    /// </summary>
    public class SurfaceMultiplicityAxisTests
    {
        /// <summary>
        ///     The ×2 cases whose two applications are still identical, exactly pinned with the reason each one
        ///     is out of the rotation's reach. Every entry is a constructor whose whole argument list is
        ///     <c>typeof</c>s or empty: a type reference is not a member name, so it has no fixture axis to
        ///     rotate along, and an arity-0 element has nothing to vary at all. Closing these means giving the
        ///     declaration a second stated argument list (a change to the declaration surface, and for the
        ///     hierarchy fixtures a second derived pair to point at) — recorded here rather than left to be
        ///     rediscovered.
        /// </summary>
        private static readonly Dictionary<string, string> IdenticalByConstruction = new(StringComparer.Ordinal)
        {
            ["DwarfProvidesMap/0"] = "ctor takes two typeofs; a type reference has no member axis to rotate",
            ["DwarfRequiresMap/0"] = "ctor takes two typeofs; a type reference has no member axis to rotate",
            ["GenerateMap/2"] = "arity-2 generic form, no constructor arguments to vary",
            ["GenerateWrapperMap/0"] = "ctor takes one typeof",
            ["MapDerivedType/0"] = "declared Arguments are two typeofs (the polymorphic-hierarchy fixture's " + "derived pair); rotating would need a SECOND derived pair in the fixture",
            ["MapDerivedType/2"] = "arity-2 generic form, no constructor arguments to vary",
            ["MapIgnore/0"] = "unscoped form takes no arguments — the DWARF095 shape",
            ["RestatesBase/2"] = "arity-2 generic form, no constructor arguments to vary",
            ["UsesMap/0"] = "ctor takes two typeofs",
            ["UsesMap/2"] = "arity-2 generic form, no constructor arguments to vary"
        };

        [Fact]
        public void The_multiplicity_axis_renders_two_different_applications()
        {
            var identical = new List<string>();
            foreach (var element in SurfaceCatalog.Elements)
            foreach (var c in SurfaceCatalog.CasesFor(element))
            {
                if (!string.Equals(c.Axis, "×2", StringComparison.Ordinal))
                {
                    continue;
                }

                var applications = c.Rendered.Split('\n');
                Assert.True(applications.Length == 2,
                    $"{Key(element)}: the ×2 case rendered {applications.Length} application(s), not 2 — the " + "axis stopped being the multiplicity axis.");

                if (string.Equals(applications[0], applications[1], StringComparison.Ordinal))
                {
                    identical.Add($"{Key(element)}  {applications[0]}");
                }
            }

            var unexpected = identical
                .Where(line => !IdenticalByConstruction.ContainsKey(line.Split("  ")[0]))
                .ToList();
            Assert.True(unexpected.Count == 0,
                "These ×2 cases render two BYTE-IDENTICAL applications, so the multiplicity axis asks nothing " + "the single-application axis did not already ask (B37):\n  " + string.Join("\n  ", unexpected) + "\nRotate the argument by variant, or pin it in IdenticalByConstruction with the reason it " + "cannot be.");

            // Exact pin, both directions: a residual that quietly disappears is an entry nobody deleted, and a
            // population this small cannot be ratcheted (AssertRatchet refuses a ceiling of ten or less).
            // Distinct, because an element renders one ×2 case per declaration SITE and the residual is a
            // property of its constructor, not of where it is written.
            var pinnedKeys = identical.Select(line => line.Split("  ")[0])
                .Distinct(StringComparer.Ordinal).ToList();
            Assert.Equal(
                IdenticalByConstruction.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                pinnedKeys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        }

        [Fact]
        public void Variant_one_still_renders_the_declaration_exactly_as_written()
        {
            // The rotation must be confined to the SECOND application. Axis 1 (each constructor overload) and
            // axis 2 (each property, on the shortest constructor) both render at variant 1, so this is what
            // keeps the churn where B37 said it belongs. Checked against the declaration itself rather than a
            // golden string: every {Member} the claim declares must appear, quoted, in the variant-1 rendering.
            var offenders = new List<string>();
            foreach (var element in SurfaceCatalog.Elements)
            foreach (var claim in ClaimsWithArguments(element))
            foreach (var member in SurfaceCatalog.MemberPlaceholders(claim.Arguments!))
            {
                var expected = '"' + member + '"';
                var singleApplicationCases = SurfaceCatalog.CasesFor(element)
                    .Where(c => c.Axis.StartsWith("ctor(", StringComparison.Ordinal) && c.Axis == $"ctor({claim.ConstructorArity})")
                    .ToList();

                foreach (var c in singleApplicationCases.Where(c =>
                             !c.Rendered.Contains(expected, StringComparison.Ordinal)))
                    offenders.Add($"{Key(element)} {c.Axis} @{c.Site}: declared {expected}, rendered " + c.Rendered);

                // …and the FIRST application of the ×2 case is variant 1 too.
                foreach (var c in SurfaceCatalog.CasesFor(element)
                             .Where(c => string.Equals(c.Axis, "×2", StringComparison.Ordinal) && claim.ConstructorArity == FirstCtorArity(element)))
                {
                    var first = c.Rendered.Split('\n')[0];
                    if (!first.Contains(expected, StringComparison.Ordinal))
                    {
                        offenders.Add($"{Key(element)} ×2 @{c.Site}: first application lost {expected}: {first}");
                    }
                }
            }

            Assert.True(offenders.Count == 0,
                "A declared argument list no longer renders as written at variant 1. The rotation belongs to " + "the SECOND application only — axes 1 and 2 render at variant 1, so this moves every " + "constructor and property cell of the element:\n  " + string.Join("\n  ", offenders));
        }

        private static string Key(SurfaceElement element)
        {
            return element.UsageName + "/" + element.Type.GetGenericArguments().Length;
        }

        private static int FirstCtorArity(SurfaceElement element)
        {
            return element.Type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Select(c => c.GetParameters().Length).FirstOrDefault(-1);
        }

        private static IEnumerable<SurfaceProbeClaim> ClaimsWithArguments(SurfaceElement element)
        {
            return element.ProbeClaims.Where(c => c.Arguments is not null);
        }
    }
}
