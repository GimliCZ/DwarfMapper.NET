// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using System.Runtime.CompilerServices;

namespace DwarfMapper.Generator.Tests.SelfValidation;

/// <summary>
///     STRUCTURAL incremental-cache-safety invariant. The pipeline models flow through Roslyn's incremental
///     cache, which compares them by VALUE. <see cref="DwarfMapper.Generator.Tests.IncrementalCachingTests" />
///     proves caching works for the models as they are today; this proves it can't silently regress: every
///     type in the Model/Diagnostics namespaces must be a record (value equality) whose every member is a
///     cache-safe type. The classic traps this catches the instant they're introduced:
///     • a raw <c>ImmutableArray&lt;T&gt;</c> (a struct, but Equals compares the backing array by REFERENCE),
///     an array, or a <c>List&lt;T&gt;</c> — use <c>EquatableArray&lt;T&gt;</c> instead;
///     • a leaked <c>ISymbol</c>/<c>SyntaxNode</c>/<c>Compilation</c>/<c>Location</c> (reference equality →
///     never equal across compilations → caching disabled + compilations rooted in memory).
///     An unrecognised type fails loudly so a human must classify it rather than silently weakening caching.
/// </summary>
public class ModelCacheSafetyTests
{
    private const string ModelNs = "DwarfMapper.Generator.Model";
    private const string DiagNs = "DwarfMapper.Generator.Diagnostics";
    private const string EquatableArrayDef = "DwarfMapper.Generator.Collections.EquatableArray`1";

    // Roslyn types that ARE value-equatable and therefore cache-safe, by full name:
    //  • TextSpan / LinePositionSpan: structs with value equality (held by LocationInfo).
    //  • DiagnosticDescriptor: a class with reference equality, but callers MUST pass the static singletons
    //    from DiagnosticDescriptors (documented contract in DiagnosticInfo), so identity == value here.
    private static readonly HashSet<string> AllowedRoslynValueTypes = new(StringComparer.Ordinal)
    {
        "Microsoft.CodeAnalysis.Text.TextSpan",
        "Microsoft.CodeAnalysis.Text.LinePositionSpan",
        "Microsoft.CodeAnalysis.DiagnosticDescriptor",
        "Microsoft.CodeAnalysis.DiagnosticSeverity" // enum, but be explicit
    };

    private static IEnumerable<Type> ModelTypes()
    {
        return typeof(DwarfGenerator).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && (t.Namespace == ModelNs || t.Namespace == DiagNs))
            .Where(t => !t.IsDefined(typeof(CompilerGeneratedAttribute), false));
    }

    [Fact]
    public void Every_model_type_is_a_record()
    {
        var nonRecords = ModelTypes().Where(t => !IsRecord(t)).Select(t => t.FullName).ToList();
        Assert.True(nonRecords.Count == 0,
            "These Model/Diagnostics types are not records (records give value equality, required for " +
            "incremental caching):\n  " + string.Join("\n  ", nonRecords));
    }

    [Fact]
    public void Every_model_member_type_is_cache_safe()
    {
        var violations = new List<string>();

        foreach (var type in ModelTypes())
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.Name == "EqualityContract") continue; // compiler-generated record member
            if (!IsCacheSafe(prop.PropertyType, out var why))
                violations.Add($"{type.Name}.{prop.Name} : {Pretty(prop.PropertyType)} — {why}");
        }

        Assert.True(violations.Count == 0,
            "Incremental-cache-UNSAFE members found in pipeline models. Each breaks value equality and " +
            "silently disables caching (Roslyn re-runs the generator on every keystroke). Use a cache-safe " +
            "type (primitive/enum/string, EquatableArray<T>, another Model record, or a documented value " +
            "type):\n  " + string.Join("\n  ", violations));
    }

    private static bool IsCacheSafe(Type type, out string why)
    {
        why = "";
        var t = Nullable.GetUnderlyingType(type) ?? type;

        if (t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal))
            return true;

        if (t.IsArray)
        {
            why = "arrays use reference equality — use EquatableArray<T>";
            return false;
        }

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition().FullName;
            if (def == EquatableArrayDef)
                return IsCacheSafe(t.GetGenericArguments()[0], out why); // recurse into element type
            why = $"generic collection '{Pretty(t)}' is not value-equatable — use EquatableArray<T>";
            return false;
        }

        if (AllowedRoslynValueTypes.Contains(t.FullName ?? ""))
            return true;

        if (t.Namespace == ModelNs || t.Namespace == DiagNs)
        {
            if (IsRecord(t)) return true;
            why = "Model type that is not a record (no value equality)";
            return false;
        }

        if ((t.Namespace ?? "").StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
        {
            why = "Roslyn type with reference equality — leaking it disables caching and roots the " +
                  "compilation in memory. Extract the data you need into a value-equatable field.";
            return false;
        }

        why = "unrecognised type — classify it as cache-safe (add to the allow-list) or replace it";
        return false;
    }

    // A C# record has a compiler-generated `<Clone>$` method (and a protected EqualityContract property).
    private static bool IsRecord(Type t)
    {
        return t.GetMethod("<Clone>$",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null;
    }

    private static string Pretty(Type t)
    {
        return t.IsGenericType
            ? t.Name.Split('`')[0] + "<" + string.Join(", ", t.GetGenericArguments().Select(Pretty)) + ">"
            : t.Name;
    }
}
