// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DwarfMapper.DocTooling
{
    /// <summary>
    ///     Renders the public API reference from the two things that cannot lie about the API: the compiled
    ///     assembly (what actually exists) and its XML documentation file (what the author wrote next to it).
    ///     <para>
    ///         A hand-written API page decays the moment a property is added, renamed, or given a different
    ///         default — and nothing fails when it does, because prose has no compiler. Deriving it means a
    ///         renamed option shows up as a doc diff in the same commit that renamed it.
    ///     </para>
    ///     <para>
    ///         Deliberately NOT a replacement for the guides. This lists what exists and what its summary says;
    ///         the narrative docs explain when to reach for it. Generating the reference is what frees the prose
    ///         to stop enumerating members it will not keep up to date.
    ///     </para>
    /// </summary>
    public static class ApiReferenceRenderer
    {
        public static string Render(string doNotEditBanner)
        {
            var assembly = typeof(DwarfMapperAttribute).Assembly;
            var summaries = LoadSummaries(assembly);

            var types = assembly.GetExportedTypes()
                .Where(IsRenderableType)
                .OrderBy(t => t.Namespace, StringComparer.Ordinal)
                .ThenBy(t => t.Name, StringComparer.Ordinal)
                .ToList();

            var sb = new StringBuilder();
            sb.Append("<!-- SPDX-License-Identifier: GPL-2.0-only -->\n");
            sb.Append(doNotEditBanner).Append('\n');
            sb.Append("# API reference\n\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"The public surface of `{assembly.GetName().Name}`, rendered from the compiled assembly and its\n");
            sb.Append("XML documentation. The assembly decides what exists; the `<summary>` next to each member\n");
            sb.Append("decides what it says. Neither can drift from the code without this page changing.\n\n");
            sb.Append("Attribute properties list their **default**, read from a fresh instance — the value you get\n");
            sb.Append("when you do not set it, which is the question a reference page is usually opened to answer.\n\n");

            foreach (var group in types.GroupBy(t => t.Namespace, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
            {
                sb.Append(CultureInfo.InvariantCulture, $"## `{group.Key}`\n\n");

                foreach (var type in group)
                {
                    sb.Append(CultureInfo.InvariantCulture, $"### {Kind(type)} `{Display(type)}`\n\n");

                    var summary = Lookup(summaries, "T:" + type.FullName);
                    if (summary is not null)
                    {
                        sb.Append(summary).Append("\n\n");
                    }

                    if (type.IsEnum)
                    {
                        RenderEnum(sb, type, summaries);
                    }
                    else
                    {
                        RenderMembers(sb, type, summaries);
                    }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        ///     Whether a type that <see cref="Assembly.GetExportedTypes" /> returned belongs on the page.
        ///     <para>
        ///         The test is about NESTING, not visibility: GetExportedTypes already returns only publicly
        ///         visible types, so the only thing left to decide is whether a nested one is public in its own
        ///         right. <see cref="Type.IsPublic" /> answers that question WRONG for a nested type - it is
        ///         false for every nested type however visible, because the nested flavour is
        ///         <see cref="Type.IsNestedPublic" />. Reading IsPublic therefore dropped every public nested
        ///         type from the reference page (found 2026-09-20; latent, because the runtime assembly's only
        ///         nested types are private today).
        ///     </para>
        ///     <para>
        ///         Internal rather than private so the two arms can be pinned directly: Render reflects one
        ///         fixed assembly, so no test input can put a public nested type in front of the filter.
        ///     </para>
        /// </summary>
        internal static bool IsRenderableType(Type type)
        {
            return !type.IsNested || type.IsNestedPublic;
        }

        private static void RenderEnum(StringBuilder sb, Type type, Dictionary<string, string> summaries)
        {
            sb.Append("| Value | Numeric | Summary |\n|---|---|---|\n");
            foreach (var name in Enum.GetNames(type).OrderBy(n => n, StringComparer.Ordinal))
            {
                var value = Convert.ToInt64(Enum.Parse(type, name), CultureInfo.InvariantCulture);
                var doc = Lookup(summaries, $"F:{type.FullName}.{name}") ?? "";
                sb.Append(CultureInfo.InvariantCulture, $"| `{name}` | {value} | {doc} |\n");
            }

            sb.Append('\n');
        }

        private static void RenderMembers(StringBuilder sb, Type type, Dictionary<string, string> summaries)
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .ToList();

            if (properties.Count == 0)
            {
                sb.Append("_No public settable surface._\n\n");
                return;
            }

            var defaults = TryCreateDefaults(type);

            sb.Append("| Member | Type | Default | Summary |\n|---|---|---|---|\n");
            foreach (var p in properties)
            {
                var doc = Lookup(summaries, $"P:{type.FullName}.{p.Name}") ?? "";
                var def = defaults is null || !p.CanRead ? "—" : FormatValue(SafeGet(defaults, p));
                sb.Append(CultureInfo.InvariantCulture,
                    $"| `{p.Name}` | `{Display(p.PropertyType)}` | {def} | {doc} |\n");
            }

            sb.Append('\n');
        }

        /// <summary>
        ///     A default-constructed instance, purely to read property defaults. Types with no parameterless
        ///     constructor simply report "—": inventing constructor arguments would produce a "default" that no
        ///     caller ever sees, which is worse than admitting the page cannot say.
        /// </summary>
        /// <remarks>
        ///     Internal rather than private for the reason IsRenderableType is: Render reflects one fixed
        ///     assembly, so no test input can put an abstract type, an interface or a throwing constructor in
        ///     front of this method.
        /// </remarks>
        internal static object? TryCreateDefaults(Type type)
        {
            if (type.IsAbstract || type.IsInterface)
            {
                return null;
            }

            // An open generic ([GenerateMap<TSource, TTarget>]) has no instance to read defaults from — the type
            // arguments are the caller's. Reported as "—" rather than crashing the whole page.
            if (type.ContainsGenericParameters)
            {
                return null;
            }

            // This guard is what makes a MissingMethodException impossible below, which is why there is no catch
            // for one (owner ruling 2026-09-21, deleted with its proof): every shape that could raise it has
            // already returned - an abstract type or an interface above, an open generic above that, and here
            // anything with no PUBLIC parameterless constructor, including a struct with none declared and a
            // class whose own is private.
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(type);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Reads one property for the defaults column. A getter that throws costs that one cell, not the
        ///     page. Internal for the same reason as its neighbours: Render reflects one fixed assembly.
        /// </summary>
        internal static object? SafeGet(object instance, PropertyInfo p)
        {
            try
            {
                return p.GetValue(instance);
            }
            catch (TargetInvocationException)
            {
                return null;
            }
        }

        /// <summary>Renders one default value as a markdown code span. Internal so each arm can be stated directly.</summary>
        internal static string FormatValue(object? value)
        {
            return value switch
            {
                null => "`null`",
                bool b => b ? "`true`" : "`false`",
                string s => s.Length == 0 ? "`\"\"`" : $"`\"{s}\"`",
                Enum e => $"`{e}`",
                _ => $"`{Convert.ToString(value, CultureInfo.InvariantCulture)}`"
            };
        }

        /// <summary>The word the page uses for a type. Internal so every arm can be pinned, including the ones the reflected assembly has no example of.</summary>
        internal static string Kind(Type type)
        {
            return type.IsEnum ? "enum"
                : type.IsInterface ? "interface"
                : typeof(Attribute).IsAssignableFrom(type) ? "attribute"
                : type.IsValueType ? "struct"
                : "class";
        }

        private static string Display(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.Name;
            }

            var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
            var args = string.Join(", ", type.GetGenericArguments().Select(Display));
            return $"{name}<{args}>";
        }

        private static string? Lookup(Dictionary<string, string> summaries, string key)
        {
            return summaries.TryGetValue(key, out var v) ? v : null;
        }

        /// <summary>
        ///     Parses the compiler-produced XML doc file sitting next to the assembly. Its absence is a hard
        ///     failure rather than an empty page — silently rendering a reference with no summaries would look
        ///     like the code is undocumented.
        /// </summary>
        /// <remarks>
        ///     Internal for the reason its neighbours are: Render reflects one fixed assembly, whose XML the
        ///     build always produces, so the failure below cannot be reached through any public entry point.
        /// </remarks>
        internal static Dictionary<string, string> LoadSummaries(Assembly assembly)
        {
            var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
            if (!File.Exists(xmlPath))
            {
                throw new DocToolingException(
                    $"No XML documentation beside {assembly.GetName().Name} at {xmlPath}. The API reference is " + "rendered from it, so an empty page would misrepresent documented code as undocumented. " + "Check GenerateDocumentationFile is still true for that project.");
            }

            return ParseSummaries(xmlPath);
        }

        /// <summary>
        ///     Reads the member summaries out of a doc-XML file at <paramref name="xmlPath" />.
        ///     <para>
        ///         <see cref="LoadOptions.PreserveWhitespace" /> is load-bearing, not tidiness (B23). The default
        ///         <see cref="LoadOptions.None" /> discards whitespace-ONLY text nodes, so a doc comment that
        ///         separates two inline elements by nothing but a space — <c>&lt;/b&gt; &lt;c&gt;</c>,
        ///         <c>&lt;/c&gt; &lt;see/&gt;</c> — lost that space before <see cref="Flatten" /> ever ran, and the
        ///         rendered page welded the two words together. <see cref="Flatten" />'s own whitespace collapse
        ///         cannot restore what the loader already dropped, so the repair has to happen here.
        ///     </para>
        ///     <para>
        ///         Internal rather than private so the spacing can be pinned against a real file on the real load
        ///         path. A test that hand-built an <see cref="XElement" /> and called <see cref="Flatten" /> would
        ///         have passed before the fix as well: the defect lived in the LOAD, not in the flattening.
        ///     </para>
        /// </summary>
        internal static Dictionary<string, string> ParseSummaries(string xmlPath)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var member in XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace).Descendants("member"))
            {
                var name = member.Attribute("name")?.Value;
                var summary = member.Element("summary");
                if (name is null || summary is null)
                {
                    continue;
                }

                result[name] = Flatten(summary);
            }

            return result;
        }

        /// <summary>
        ///     Collapses doc XML to one markdown table cell: inline tags become text, whitespace collapses,
        ///     and pipes are escaped so a summary containing one cannot break the table it sits in.
        /// </summary>
        private static string Flatten(XElement summary)
        {
            var sb = new StringBuilder();
            foreach (var node in summary.DescendantNodes())
                switch (node)
                {
                    case XText text:
                        sb.Append(text.Value);
                        break;

                    case XElement { Name.LocalName: "see" or "seealso" } e:
                        // A langword is a C# keyword and is rendered as written. It used to go through the
                        // cref path below, which strips a doc-comment ID's two-character prefix, so
                        // `<see langword="null"/>` rendered as "ll" and `"false"` as "lse" (found 2026-09-20 by
                        // the first test to cover this arm). No page shows it today only because every langword
                        // in the reflected assembly sits in a <param>, which this renderer does not read.
                        var langword = e.Attribute("langword")?.Value;
                        if (langword is not null)
                        {
                            sb.Append(langword);
                            break;
                        }

                        // A cref is a doc-comment ID: "T:Namespace.Type", "M:Namespace.Type.Method". The page
                        // wants the last segment, and for an ID with no namespace the part after the prefix.
                        var cref = e.Attribute("cref")?.Value ?? "";
                        var idx = cref.LastIndexOf('.');
                        sb.Append(idx >= 0 ? cref[(idx + 1)..] :
                            cref.Length > 2 && cref[1] == ':' ? cref[2..] : cref);
                        break;
                }

            // The pattern cannot backtrack catastrophically, but MA0009 is right that an unbounded Regex over
            // XML-doc input deserves a ceiling; a second is orders of magnitude above any real summary.
            var flat = Regex.Replace(sb.ToString(), @"\s+", " ", RegexOptions.None, TimeSpan.FromSeconds(1)).Trim();
            return flat.Replace("|", @"\|", StringComparison.Ordinal);
        }
    }
}
