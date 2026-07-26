// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;
using System.Reflection;

namespace DwarfMapper.DocTooling;

/// <summary>
///     Renders an options table's mechanical columns — name, type, default — from reflection, while carrying
///     the human "What it does" column over from the committed file.
///     <para>
///         That split is the whole point. A new option appears in the table automatically, with an EMPTY
///         prose cell, and the currency test fails until someone writes it. Undocumented public surface
///         becomes a build failure — the same trick the library plays on unmapped members, aimed at the docs.
///     </para>
///     <para>
///         Prose is keyed by the row's first cell, never by position: keying on position would re-associate
///         every description the moment a row is inserted, quietly attributing one option's meaning to
///         another.
///     </para>
/// </summary>
public static class OptionTableRenderer
{
    /// <summary>The rendered rows, including the header and separator.</summary>
    public static IReadOnlyList<string> RenderRows(Type attributeType, string committedMarkdown, string tableName)
    {
        ArgumentNullException.ThrowIfNull(attributeType);

        var prose = ExistingProse(committedMarkdown, tableName);
        var order = ExistingOrder(committedMarkdown, tableName);
        var defaults = TryCreate(attributeType);

        var rows = new List<string> { "| Option | Type | Default | What it does |", "|---|---|---|---|" };

        // The committed order is preserved and only genuinely new options are appended. Sorting instead would
        // be deterministic but would discard human curation — options.md deliberately places CaseInsensitive
        // next to NameConvention because they interact, and says so in a note directly under the table.
        foreach (var p in attributeType
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p is { CanWrite: true, CanRead: true })
                     .OrderBy(p => order.TryGetValue(p.Name, out var at) ? at : int.MaxValue)
                     .ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            var key = $"`{p.Name}`";
            var value = defaults is null ? "—" : Format(p.GetValue(defaults));
            var text = prose.TryGetValue(p.Name, out var t) ? t : "";
            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"| {key} | `{Display(p.PropertyType)}` | {value} | {text} |"));
        }

        return rows;
    }

    /// <summary>Option names whose prose cell is blank — the reason the currency test fails.</summary>
    public static IReadOnlyList<string> UndocumentedOptions(IReadOnlyList<string> renderedRows)
    {
        ArgumentNullException.ThrowIfNull(renderedRows);

        return renderedRows
            .Where(r => r.StartsWith("| `", StringComparison.Ordinal))
            .Select(r => r.Split('|'))
            .Where(cells => cells.Length >= 5 && string.IsNullOrWhiteSpace(cells[4]))
            .Select(cells => cells[1].Trim().Trim('`'))
            .ToList();
    }

    /// <summary>The committed row order, so curation survives regeneration and new options land at the end.</summary>
    private static Dictionary<string, int> ExistingOrder(string markdown, string tableName)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var name in ExistingProse(markdown, tableName).Keys) order[name] = order.Count;
        return order;
    }

    /// <summary>Reads the committed table body, mapping option name to its existing prose cell.</summary>
    private static Dictionary<string, string> ExistingProse(string markdown, string tableName)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (markdown is null) return result;

        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var start = Array.FindIndex(lines,
            l => string.Equals(l.Trim(), $"<!-- table: {tableName} -->", StringComparison.Ordinal));
        if (start < 0) return result;

        for (var i = start + 1; i < lines.Length; i++)
        {
            if (string.Equals(lines[i].Trim(), "<!-- endtable -->", StringComparison.Ordinal)) break;

            var cells = lines[i].Split('|');
            if (cells.Length < 5) continue;

            var name = cells[1].Trim().Trim('`');
            if (name.Length == 0 || name is "Option" or "---") continue;
            result[name] = cells[4].Trim();
        }

        return result;
    }

    private static object? TryCreate(Type type)
    {
        if (type.GetConstructor(Type.EmptyTypes) is null) return null;
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (TargetInvocationException)
        {
            return null;
        }
    }

    private static string Format(object? value) => value switch
    {
        null => "`null`",
        bool b => b ? "`true`" : "`false`",
        string s => s.Length == 0 ? "`\"\"`" : $"`\"{s}\"`",
        Enum e => $"`{e}`",
        _ => $"`{Convert.ToString(value, CultureInfo.InvariantCulture)}`"
    };

    private static string Display(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        if (underlying is not null) return Display(underlying) + "?";
        return type == typeof(int) ? "int"
            : type == typeof(bool) ? "bool"
            : type == typeof(string) ? "string"
            : type.Name;
    }
}
