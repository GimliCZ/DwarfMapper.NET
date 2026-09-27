// SPDX-License-Identifier: GPL-2.0-only

using DwarfMapper.Generator.Collections;
using DwarfMapper.Generator.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DwarfMapper.Generator.Pipeline
{
    /// <summary>One declared projection method, where DWARF115 would point.</summary>
    internal sealed record ProjectionSite(string Mapper, string Method, LocationInfo? Location);

    /// <summary>
    ///     <c>DWARF115</c> — a projection method (<c>IQueryable&lt;D&gt; Project(IQueryable&lt;S&gt;)</c>) in a project that
    ///     publishes NativeAOT and references EF Core.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Measured on EF Core 10.0.9 + SQLite (round 31 T17, <c>Issues/round31/FINDING-T17-ef-precompile.md</c>): EF's
    ///         query precompiler analyses a query only where its whole operator chain is written at the call site. A call
    ///         to a generated <c>Project(db.Orders)</c> hides the <c>Select</c> inside another method, so EF reports
    ///         "Dynamic LINQ queries are not supported when precompiling queries" and emits no interceptor for it — in
    ///         every form tried (inline, via a local, with <c>.AsEnumerable()</c>). The same tree composed at the call
    ///         site, <c>db.Orders.Where(…).Select(OrderMapper.ProjectExpression)</c> (round 31 T16), IS precompiled. Under
    ///         NativeAOT a query that was not precompiled cannot be compiled at run time, so the first shape is a
    ///         run-time failure waiting for its first execution; this moves it to the build, with the shape that works.
    ///     </para>
    ///     <para>
    ///         Gated on BOTH facts, read without reflection: <c>PublishAot</c> through the <c>CompilerVisibleProperty</c>
    ///         the package's <c>build/DwarfMapper.props</c> declares, and a reference to the
    ///         <c>Microsoft.EntityFrameworkCore</c> assembly. A projection used with another provider, or in a
    ///         JIT-compiled app, is untouched. Everything this node outputs is a bool or an equatable array, so it caches
    ///         across keystrokes like the other assembly-wide nodes.
    ///     </para>
    /// </remarks>
    internal static class EfAotProjectionCheck
    {
        private const string EfCoreAssembly = "Microsoft.EntityFrameworkCore";

        public static bool IsEfAot(Compilation compilation, AnalyzerConfigOptions globalOptions)
        {
            if (!globalOptions.TryGetValue("build_property.PublishAot", out var publishAot) ||
                !string.Equals(publishAot.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            foreach (var reference in compilation.ReferencedAssemblyNames)
                if (string.Equals(reference.Name, EfCoreAssembly, StringComparison.Ordinal))
                {
                    return true;
                }

            return false;
        }

        public static EquatableArray<ProjectionSite> ProjectionsOf(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol mapper)
            {
                return EquatableArray.From(Array.Empty<ProjectionSite>());
            }

            var sites = new List<ProjectionSite>();
            foreach (var member in mapper.GetMembers())
                if (member is IMethodSymbol { IsPartialDefinition: true, Parameters.Length: 1 } method &&
                    IsQueryable(method.ReturnType) && IsQueryable(method.Parameters[0].Type))
                {
                    sites.Add(new ProjectionSite(mapper.Name, method.Name, LocationInfo.FromFirst(method.Locations)));
                }

            return EquatableArray.From(sites.ToArray());
        }

        public static void Report(SourceProductionContext spc, System.Collections.Immutable.ImmutableArray<EquatableArray<ProjectionSite>> perMapper, bool efAot)
        {
            if (!efAot)
            {
                return;
            }

            foreach (var sites in perMapper)
            foreach (var site in sites)
                spc.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.ProjectionNotPrecompilable,
                    LocationInfo.ToLocationOrNone(site.Location),
                    $"Projection '{site.Method}' on '{site.Mapper}' cannot be precompiled by EF Core: its Select is built " +
                    "inside the generated method, which EF's query precompiler treats as a dynamic query, and this project " +
                    "publishes NativeAOT, where a query that was not precompiled fails when it first runs. Compose the tree " +
                    $"at the call site instead - db.Set<...>().Where(...).Select({site.Mapper}.{site.Method}Expression) - " +
                    "which EF precompiles"));
        }

        private static bool IsQueryable(ITypeSymbol type)
        {
            return type is INamedTypeSymbol { Name: "IQueryable", TypeArguments.Length: 1 } named &&
                   KnownNames.IsNamespace(named.ContainingNamespace, "System.Linq");
        }
    }
}
