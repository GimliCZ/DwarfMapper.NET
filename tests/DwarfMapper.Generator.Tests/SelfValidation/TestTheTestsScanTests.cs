// SPDX-License-Identifier: GPL-2.0-only

// Test-the-tests meta-suite.
// Purpose: catch hollowness and coverage gaps in the test suite itself, so that
// adding a new feature / attribute / enum value / collection target automatically
// fails if the test suite hasn't been updated.
//
//  Scan T1 — Hollow-test detector: every [Fact]/[Theory] method must contain a
//             real assertion (Assert.*, Verifier.Verify, Record.Exception, .ShouldBe).
//             Delegation to an in-file helper that contains assertions is also accepted
//             (Roslyn intra-file method resolution).  Methods with no assertion of any
//             kind, even indirectly, are HOLLOW.
//
//  Scan T2 — Feature-attribute in FeatureInteractionCompileMatrix: every public
//             Attribute type from DwarfMapper.dll must appear (by usage name) in
//             FeatureInteractionCompileMatrixTests.cs, or be in MatrixExemptAttributes.
//
//  Scan T3 — Enum-value in matrix or tests: every public enum VALUE from
//             DwarfMapper.dll must appear in the FIM source OR in a test source file.
//             Every TargetKind value must appear in the FIM source OR a test source.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DwarfMapper.Generator.Pipeline;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DwarfMapper.Generator.Tests.SelfValidation;

// ── Exemption lists ───────────────────────────────────────────────────────────

/// <summary>
/// Attribute types exempt from the FeatureInteractionCompileMatrix coverage requirement.
/// Keep this list tiny and justified; it must only SHRINK — never grow without explicit
/// justification added as a comment.
///
/// RoundTrip         — pairing/verification feature, orthogonal to all others; the matrix
///                     would need a *pair* of methods and an emit step, making it a full
///                     integration scenario, not a compile-matrix case.  Covered by
///                     RoundTripGenTests + integration tests.
///
/// DwarfMapperConstructor — constructor-selection marker; must live on the constructor,
///                     not on the mapping method, so it's structurally incompatible with
///                     the method-level matrix format.  Covered by ConstructorMappingTests.
///
/// DwarfMapperOptions — an ASSEMBLY-level compile-time option (controls generated-extension
///                     visibility); it is not a per-mapping feature and has no method-level
///                     matrix form.  Covered by FacadeExtensionsGeneratorTests.
///
/// DwarfProvidesMap / DwarfRequiresMap / UsesMap / DwarfMapperValidationRoot — the ambient
///                     cross-assembly registry infrastructure (generator-emitted manifests, the
///                     consumption marker, and the validation-root marker). None affect a single
///                     mapping's generated output, so they have no per-mapping compile-matrix form.
///                     Covered by AmbientManifestAttributesTests + AmbientRegistryTests (and the
///                     ambient-registry generator/validation tests).
/// </summary>
file static class MatrixExemptAttributes
{
    public static readonly IReadOnlySet<string> UsageNames =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "RoundTrip",
            "DwarfMapperConstructor",
            "DwarfMapperOptions",
            "DwarfProvidesMap",
            "DwarfRequiresMap",
            "UsesMap",
            "DwarfMapperValidationRoot",
        };
}

/// <summary>
/// Test class-name prefixes whose [Fact]/[Theory] methods are legitimately
/// assertion-free in their OWN body — because they return a Task&lt;VerifyResult&gt;
/// from Verifier.Verify, which is the assertion mechanism at the runner level.
/// Every entry must be justified.  This list MUST only SHRINK.
///
/// SnapshotSuite — partial class; all methods are of the form
///     return Verifier.Verify(generated);
///     The Verify infrastructure throws on mismatch — the return-value IS the assertion.
/// </summary>
file static class HollowAllowlist
{
    public static readonly IReadOnlySet<string> ClassNamePrefixes =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "SnapshotSuite",
        };
}

// ─────────────────────────────────────────────────────────────────────────────
// The meta-test class
// ─────────────────────────────────────────────────────────────────────────────

public sealed class TestTheTestsScanTests
{
    // ── Assembly & path resolution (mirrors AssemblyScanTests) ───────────────

    private static readonly Assembly DwarfMapperAssembly =
        typeof(DwarfMapperAttribute).Assembly;

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(TestTheTestsScanTests).Assembly.Location)!);
        while (dir != null)
        {
            if (dir.GetFiles("DwarfMapper.NET.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Cannot locate repository root: no DwarfMapper.NET.sln found walking upward from " +
            typeof(TestTheTestsScanTests).Assembly.Location);
    }

    private static string RepoRoot { get; } = FindRepoRoot();

    private static IEnumerable<string> EnumerateSources(string subPath)
        => Directory.EnumerateFiles(
                Path.Combine(RepoRoot, subPath), "*.cs",
                SearchOption.AllDirectories)
            .Where(f => !f.Contains(
                Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal));

    private static IEnumerable<string> TestSources()
        => EnumerateSources("tests");

    private static readonly Lazy<string> AllTestSourceText = new(() =>
        string.Concat(TestSources().Select(File.ReadAllText)));

    /// <summary>Path to the FeatureInteractionCompileMatrix source file.</summary>
    private static string FimFile =>
        Path.Combine(RepoRoot, "tests", "DwarfMapper.Generator.Tests",
                     "FeatureInteractionCompileMatrixTests.cs");

    private static readonly Lazy<string> FimSourceText = new(() => File.ReadAllText(FimFile));

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN T1 — Hollow-test detector
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parse every test .cs file with Roslyn.  Every [Fact]/[Theory] method body
    /// must contain at least one ASSERTION — either directly or by calling a
    /// method declared in the SAME file whose body contains an assertion.
    ///
    /// Recognised direct assertion patterns:
    ///   Assert.            xunit Assert.* (True, False, Equal, Contains, etc.)
    ///   Verifier.Verify    Verify.Xunit snapshot assertion (return value = assertion)
    ///   await Verifier.    async snapshot
    ///   Record.Exception   xunit exception recorder
    ///   .ShouldBe          Shouldly (future-proof)
    ///
    /// Intra-file delegation: if the method body calls a method (by simple name or
    /// qualified with a file-local class) that is declared in the same source file
    /// AND that callee's body contains a direct assertion, the caller is non-hollow.
    ///
    /// Methods in HollowAllowlist.ClassNamePrefixes are skipped ONLY IF their body
    /// contains a Verifier.Verify call (confirming the exemption is earned).
    /// </summary>
    [Fact]
    public void T1_No_Fact_or_Theory_method_is_hollow()
    {
        var hollow = new List<string>();

        foreach (var filePath in TestSources())
        {
            var text = File.ReadAllText(filePath);
            var tree = CSharpSyntaxTree.ParseText(text);
            var root = tree.GetRoot();

            // Build a map of all named method bodies in the file for intra-file resolution.
            // Multiple overloads of the same name are merged: if ANY overload contains an
            // assertion, the name is considered assertive.
            var fileMethodBodies = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                var mName = m.Identifier.Text;
                var mBody = GetMethodBodyText(m);
                if (!fileMethodBodies.TryGetValue(mName, out var existing))
                    fileMethodBodies[mName] = mBody;
                else
                    fileMethodBodies[mName] = existing + mBody; // merge: if any overload asserts, all pass
            }

            var factMethods = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .Where(m => IsFactOrTheory(m));

            foreach (var method in factMethods)
            {
                var className = GetEnclosingClassName(method);
                var methodName = method.Identifier.Text;

                // ── Allowlist ──────────────────────────────────────────────
                if (HollowAllowlist.ClassNamePrefixes
                        .Any(p => className.StartsWith(p, StringComparison.Ordinal)))
                {
                    // Verify the allowlisted method genuinely uses Verifier.
                    var bodyText = GetMethodBodyText(method);
                    if (!ContainsVerifierCall(bodyText))
                    {
                        hollow.Add(
                            $"{Path.GetFileName(filePath)}::{className}::{methodName} " +
                            $"[in HollowAllowlist but has no Verifier.Verify call — not earned]");
                    }
                    continue;
                }

                // ── Direct assertion check ─────────────────────────────────
                var body = GetMethodBodyText(method);
                if (ContainsAssertion(body))
                    continue;

                // ── Intra-file delegation check ────────────────────────────
                // Extract all simple method-call names from the body and look them up
                // in the file-local helper dictionary.
                var calledNames = ExtractCalledMethodNames(method);
                var delegatesAssertion = calledNames
                    .Any(name =>
                        fileMethodBodies.TryGetValue(name, out var calleeBody) &&
                        ContainsAssertion(calleeBody));

                if (!delegatesAssertion)
                {
                    hollow.Add($"{Path.GetFileName(filePath)}::{className}::{methodName}");
                }
            }
        }

        Assert.True(hollow.Count == 0,
            $"HOLLOW test(s) — [Fact]/[Theory] method(s) with no assertion (direct or via in-file helper):\n" +
            string.Join("\n", hollow.Select(h => "  " + h)) +
            "\nFix: add an Assert.* / Verifier.Verify / Record.Exception call (directly or via a helper), " +
            "or justify an exemption in HollowAllowlist.");
    }

    /// <summary>
    /// Assert that every entry in HollowAllowlist.ClassNamePrefixes corresponds to
    /// at least one class in the test sources (no stale entries).
    /// </summary>
    [Fact]
    public void T1b_HollowAllowlist_has_no_stale_entries()
    {
        var testText = AllTestSourceText.Value;

        var stale = HollowAllowlist.ClassNamePrefixes
            .Where(prefix => !testText.Contains(prefix, StringComparison.Ordinal))
            .ToList();

        Assert.True(stale.Count == 0,
            "HollowAllowlist.ClassNamePrefixes contains stale entries (class/prefix not found in test sources): " +
            string.Join(", ", stale));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN T2 — Feature attribute in FeatureInteractionCompileMatrix
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every public, non-abstract Attribute subclass in DwarfMapper.dll must have
    /// its usage name appear in FeatureInteractionCompileMatrixTests.cs, UNLESS it
    /// is in MatrixExemptAttributes.
    /// Forces: new feature → must add a matrix case.
    ///
    /// Usage-name derivation: strip backtick generic suffix first (e.g.
    /// "MapDerivedTypeAttribute`2" → "MapDerivedTypeAttribute"), then strip
    /// "Attribute" suffix (→ "MapDerivedType"), then deduplicate.  Both
    /// MapDerivedTypeAttribute and MapDerivedTypeAttribute`2 resolve to "MapDerivedType".
    /// </summary>
    [Fact]
    public void T2_Every_feature_attribute_is_represented_in_FeatureInteractionCompileMatrix()
    {
        var fimText = FimSourceText.Value;

        var usageNames = GetAttributeUsageNames();

        var missing = usageNames
            .Where(name => !MatrixExemptAttributes.UsageNames.Contains(name))
            .Where(name => !fimText.Contains(name, StringComparison.Ordinal))
            .Select(name => $"[{name}]")
            .OrderBy(n => n)
            .ToList();

        Assert.True(missing.Count == 0,
            "Feature attribute(s) not represented in FeatureInteractionCompileMatrixTests.cs:\n" +
            string.Join("\n", missing.Select(m => "  " + m)) +
            "\nFix: add a FimMatrixCase that exercises each missing attribute, " +
            "or add it to MatrixExemptAttributes with a justification.");
    }

    /// <summary>
    /// Assert that every entry in MatrixExemptAttributes still exists as a real
    /// attribute in the DwarfMapper assembly (stale exemption detection).
    /// </summary>
    [Fact]
    public void T2b_MatrixExemptAttributes_has_no_stale_entries()
    {
        var knownUsageNames = GetAttributeUsageNames();

        var stale = MatrixExemptAttributes.UsageNames
            .Where(name => !knownUsageNames.Contains(name))
            .ToList();

        Assert.True(stale.Count == 0,
            "MatrixExemptAttributes contains stale entries (usage name not found in DwarfMapper.dll): " +
            string.Join(", ", stale));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN T3 — Enum values in matrix or tests + TargetKind in matrix or tests
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Every public enum VALUE from DwarfMapper.dll must appear in the
    /// FeatureInteractionCompileMatrix OR in any test source file.
    /// </summary>
    [Fact]
    public void T3a_Every_public_enum_value_appears_in_matrix_or_tests()
    {
        var fimText = FimSourceText.Value;
        var testText = AllTestSourceText.Value;
        var combinedText = fimText + testText;

        var publicEnums = DwarfMapperAssembly
            .GetTypes()
            .Where(t => t.IsPublic && t.IsEnum)
            .ToList();

        var missing = new List<string>();
        foreach (var enumType in publicEnums)
        {
            foreach (var valueName in Enum.GetNames(enumType))
            {
                if (!combinedText.Contains(valueName, StringComparison.Ordinal))
                    missing.Add($"{enumType.Name}.{valueName}");
            }
        }

        Assert.True(missing.Count == 0,
            "Public enum value(s) with no coverage in the FIM or any test file:\n" +
            string.Join("\n", missing.Select(m => "  " + m)) +
            "\nFix: add a matrix case or test that uses the missing value.");
    }

    /// <summary>
    /// Every TargetKind value (internal enum, exposed via InternalsVisibleTo) must
    /// appear in FeatureInteractionCompileMatrixTests.cs OR in any test source file.
    /// </summary>
    [Fact]
    public void T3b_Every_TargetKind_value_appears_in_matrix_or_tests()
    {
        var fimText = FimSourceText.Value;
        var testText = AllTestSourceText.Value;
        var combinedText = fimText + testText;

        var missing = Enum.GetNames<CollectionConverter.TargetKind>()
            .Where(name => !combinedText.Contains(name, StringComparison.Ordinal))
            .Select(name => $"TargetKind.{name}")
            .ToList();

        Assert.True(missing.Count == 0,
            "TargetKind value(s) with no coverage in the FIM or any test file:\n" +
            string.Join("\n", missing.Select(m => "  " + m)) +
            "\nFix: add a FimMatrixCase with this collection target type, " +
            "or add it to a CollectionTaxonomyTests / ProjectionMatrixTests case.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SCAN T4 (self-check) — Allowlist size gates
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sanity gate: the HollowAllowlist prefix set is smaller than a hard limit,
    /// preventing the allowlist from growing unbounded to silence the hollow detector.
    /// </summary>
    [Fact]
    public void T4a_HollowAllowlist_is_small()
    {
        const int MaxAllowedEntries = 3;
        Assert.True(
            HollowAllowlist.ClassNamePrefixes.Count <= MaxAllowedEntries,
            $"HollowAllowlist.ClassNamePrefixes has {HollowAllowlist.ClassNamePrefixes.Count} entries " +
            $"(limit: {MaxAllowedEntries}). The allowlist must only SHRINK. " +
            "Fix hollow tests instead of exempting them.");
    }

    /// <summary>
    /// Sanity gate: the MatrixExemptAttributes set is smaller than a hard limit.
    /// </summary>
    [Fact]
    public void T4b_MatrixExemptAttributes_is_small()
    {
        // 3 historical (RoundTrip, DwarfMapperConstructor, DwarfMapperOptions) + 4 ambient cross-assembly
        // registry markers (DwarfProvidesMap/DwarfRequiresMap/UsesMap/DwarfMapperValidationRoot) — none of
        // which affect a single mapping's output, so none have a per-mapping compile-matrix form. Bumped
        // from 4 -> 7 with that justification. The set must still only SHRINK from here.
        const int MaxAllowedEntries = 7;
        Assert.True(
            MatrixExemptAttributes.UsageNames.Count <= MaxAllowedEntries,
            $"MatrixExemptAttributes has {MatrixExemptAttributes.UsageNames.Count} entries " +
            $"(limit: {MaxAllowedEntries}). The exempt set must only SHRINK. " +
            "Fix missing matrix coverage instead of exempting attributes.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers: attribute usage-name derivation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Derives the C# usage name for each public non-abstract Attribute subclass in
    /// DwarfMapper.dll, deduplicating (e.g. MapDerivedTypeAttribute and
    /// MapDerivedTypeAttribute`2 both yield "MapDerivedType").
    ///
    /// Algorithm:
    ///   1. Strip backtick-suffix (generic arity marker), e.g. "Foo`2" → "Foo".
    ///   2. Strip "Attribute" suffix, e.g. "FooAttribute" → "Foo".
    /// </summary>
    private static HashSet<string> GetAttributeUsageNames()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in DwarfMapperAssembly.GetTypes()
                     .Where(t => t.IsPublic && !t.IsAbstract &&
                                 typeof(Attribute).IsAssignableFrom(t)))
        {
            var name = t.Name;
            // Step 1: strip generic arity ("`2" etc.)
            var bt = name.IndexOf('`', StringComparison.Ordinal);
            if (bt >= 0) name = name[..bt];
            // Step 2: strip "Attribute" suffix
            if (name.EndsWith("Attribute", StringComparison.Ordinal))
                name = name[..^"Attribute".Length];
            if (!string.IsNullOrEmpty(name))
                result.Add(name);
        }
        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers: Roslyn-based method analysis
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsFactOrTheory(MethodDeclarationSyntax method)
    {
        foreach (var attrList in method.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name is "Fact" or "Theory" or
                    "Xunit.Fact" or "Xunit.Theory" or
                    "FactAttribute" or "TheoryAttribute")
                    return true;
            }
        }
        return false;
    }

    private static string GetEnclosingClassName(MethodDeclarationSyntax method)
    {
        var parent = method.Parent;
        while (parent != null)
        {
            if (parent is ClassDeclarationSyntax cls)
                return cls.Identifier.Text;
            parent = parent.Parent;
        }
        return "<unknown>";
    }

    private static string GetMethodBodyText(MethodDeclarationSyntax method)
    {
        if (method.Body != null)
            return method.Body.ToFullString();
        if (method.ExpressionBody != null)
            return method.ExpressionBody.ToFullString();
        return string.Empty;
    }

    /// <summary>
    /// Returns true when the body text contains at least one recognised assertion.
    /// </summary>
    private static bool ContainsAssertion(string bodyText)
        => bodyText.Contains("Assert.", StringComparison.Ordinal)
        || bodyText.Contains("Verifier.Verify", StringComparison.Ordinal)
        || bodyText.Contains("await Verifier.", StringComparison.Ordinal)
        || bodyText.Contains("Record.Exception", StringComparison.Ordinal)
        || bodyText.Contains(".ShouldBe", StringComparison.Ordinal)
        || bodyText.Contains("Assert.Fail(", StringComparison.Ordinal);

    private static bool ContainsVerifierCall(string bodyText)
        => bodyText.Contains("Verifier.Verify", StringComparison.Ordinal)
        || bodyText.Contains("await Verifier.", StringComparison.Ordinal);

    /// <summary>
    /// Extracts simple method-call names from a test method, so we can resolve
    /// intra-file helper delegation.  We collect:
    ///   - InvocationExpression where the expression is a simple IdentifierName
    ///     (e.g. "NoErrors(src)")
    ///   - InvocationExpression where the expression is a MemberAccessExpression
    ///     (e.g. "Col.NoErrors(src)") — we take the member name part.
    /// </summary>
    private static IEnumerable<string> ExtractCalledMethodNames(MethodDeclarationSyntax method)
    {
        var bodyNode = method.Body as SyntaxNode ?? method.ExpressionBody;
        if (bodyNode is null) yield break;

        foreach (var invocation in bodyNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            switch (invocation.Expression)
            {
                case IdentifierNameSyntax id:
                    yield return id.Identifier.Text;
                    break;
                case MemberAccessExpressionSyntax ma:
                    yield return ma.Name.Identifier.Text;
                    break;
            }
        }
    }
}
