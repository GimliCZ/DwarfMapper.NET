// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using DwarfMapper;

namespace DwarfMapper.Generator.Tests.SelfValidation;

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
            .Where(t => !t.IsNested || t.IsPublic)
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

        foreach (var group in types.GroupBy(t => t.Namespace).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append(CultureInfo.InvariantCulture, $"## `{group.Key}`\n\n");

            foreach (var type in group)
            {
                sb.Append(CultureInfo.InvariantCulture, $"### {Kind(type)} `{Display(type)}`\n\n");

                var summary = Lookup(summaries, "T:" + type.FullName);
                if (summary is not null) sb.Append(summary).Append("\n\n");

                if (type.IsEnum) RenderEnum(sb, type, summaries);
                else RenderMembers(sb, type, summaries);
            }
        }

        return sb.ToString();
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
    private static object? TryCreateDefaults(Type type)
    {
        if (type.IsAbstract || type.IsInterface) return null;
        // An open generic ([GenerateMap<TSource, TTarget>]) has no instance to read defaults from — the type
        // arguments are the caller's. Reported as "—" rather than crashing the whole page.
        if (type.ContainsGenericParameters) return null;
        if (type.GetConstructor(Type.EmptyTypes) is null) return null;
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (MissingMethodException)
        {
            return null;
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static object? SafeGet(object instance, PropertyInfo p)
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

    private static string FormatValue(object? value) => value switch
    {
        null => "`null`",
        bool b => b ? "`true`" : "`false`",
        string s => s.Length == 0 ? "`\"\"`" : $"`\"{s}\"`",
        Enum e => $"`{e}`",
        _ => $"`{Convert.ToString(value, CultureInfo.InvariantCulture)}`"
    };

    private static string Kind(Type type) =>
        type.IsEnum ? "enum"
        : type.IsInterface ? "interface"
        : typeof(Attribute).IsAssignableFrom(type) ? "attribute"
        : type.IsValueType ? "struct"
        : "class";

    private static string Display(Type type)
    {
        if (!type.IsGenericType) return type.Name;
        var name = type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)];
        var args = string.Join(", ", type.GetGenericArguments().Select(Display));
        return $"{name}<{args}>";
    }

    private static string? Lookup(Dictionary<string, string> summaries, string key) =>
        summaries.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    ///     Parses the compiler-produced XML doc file sitting next to the assembly. Its absence is a hard
    ///     failure rather than an empty page — silently rendering a reference with no summaries would look
    ///     like the code is undocumented.
    /// </summary>
    private static Dictionary<string, string> LoadSummaries(Assembly assembly)
    {
        var xmlPath = Path.ChangeExtension(assembly.Location, ".xml");
        Assert.True(File.Exists(xmlPath),
            $"No XML documentation beside {assembly.GetName().Name} at {xmlPath}. The API reference is "
            + "rendered from it, so an empty page would misrepresent documented code as undocumented. "
            + "Check GenerateDocumentationFile is still true for that project.");

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in XDocument.Load(xmlPath).Descendants("member"))
        {
            var name = member.Attribute("name")?.Value;
            var summary = member.Element("summary");
            if (name is null || summary is null) continue;
            result[name] = Flatten(summary);
        }

        return result;
    }

    /// <summary>Collapses doc XML to one markdown table cell: inline tags become text, whitespace collapses,
    /// and pipes are escaped so a summary containing one cannot break the table it sits in.</summary>
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
                    var cref = e.Attribute("cref")?.Value ?? e.Attribute("langword")?.Value ?? "";
                    var idx = cref.LastIndexOf('.');
                    sb.Append(idx >= 0 ? cref[(idx + 1)..] : cref.Length > 2 ? cref[2..] : cref);
                    break;
            }

        var flat = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return flat.Replace("|", @"\|", StringComparison.Ordinal);
    }
}
