// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.CompilerTests
{
    /// <summary>
    ///     K2's stable member-path/value fingerprint over a mapped result: one sorted line per leaf
    ///     (<c>path=value</c>), so two results are equal exactly when every leaf value at every member path is
    ///     equal — independent of member declaration order, of representation kind, and of which emitted
    ///     assembly the instances came from.
    ///     <para>
    ///         <b>Why not the shipped <c>StructuralComparer</c></b> (the reuse the audit would otherwise
    ///         demand, rejected here for measured reasons, not taste): (1) it compares via
    ///         <c>expected.GetType().GetProperties()</c> and reads the SAME <see cref="PropertyInfo" /> off
    ///         both objects — the metamorphic variants live in DIFFERENT emitted assemblies whose same-named
    ///         types are distinct runtime types, so <c>GetValue(actual)</c> throws <c>TargetException</c>
    ///         instead of comparing; (2) past its <c>MaxDepth</c> = 12 it returns SILENTLY, which for a
    ///         relation gate is a vacuity hazard — this walker throws loudly instead; (3) it compares
    ///         sequences by enumeration index, but a <c>HashSet</c>'s enumeration order is bucket-layout
    ///         dependent (reference-kind elements hash by identity, value-kind by value), so MR-3's
    ///         re-kinding would diverge spuriously — sets are canonicalized here by sorting element blocks.
    ///         K1's differential oracle keeps using <c>StructuralComparer</c>, where both objects come from
    ///         ONE assembly and a diff (not a fingerprint) is the wanted output.
    ///     </para>
    ///     <para>
    ///         H7: recursion carries an explicit depth bound with a LOUD failure. Mapped-result depth is
    ///         bounded by the same arithmetic as K1's differ note (max path 10 at the generator default of
    ///         maxNodes = 4); reaching this bound means an upstream invariant broke.
    ///     </para>
    /// </summary>
    internal static class ResultFingerprint
    {
        /// <summary>H7 belt — see the class header; never raise this to make a failure go away.</summary>
        private const int MaxDepth = 32;

        /// <summary>Fingerprints one object graph. <c>null</c> roots fingerprint as a single null line.</summary>
        public static string Compute(object? root)
        {
            var lines = new List<string>();
            Walk(root, "r", lines, 0);

            // The global ordinal sort is what makes the fingerprint order-INDEPENDENT: reflection member
            // enumeration order (which follows declaration order, the very axis MR-1 varies) never survives
            // into the comparison — only paths and values do.
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines);
        }

        private static void Walk(object? value, string path, List<string> lines, int depth)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"fingerprint recursion exceeded depth {MaxDepth} at '{path}' — the graph is not the DAG " + "GraphSpec rule V2 guarantees; fix the upstream invariant, do not raise this bound");
            }

            if (value is null)
            {
                lines.Add(path + "=<null>");
                return;
            }

            var type = value.GetType();
            if (IsScalar(type))
            {
                lines.Add(path + "=" + Fmt(value));
                return;
            }

            if (value is IDictionary dictionary)
            {
                // Entries sorted by formatted key: dictionary enumeration order is insertion-order-typical
                // but not contractual, and the relation must not ride on it. Keys in the sampled space are
                // the populator's "K{i}" strings, so the ordinal sort is total.
                var entries = new List<(string Key, object? Value)>();
                foreach (DictionaryEntry entry in dictionary) entries.Add((Fmt(entry.Key), entry.Value));
                entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
                lines.Add(path + ".#=" + entries.Count.ToString(CultureInfo.InvariantCulture));
                foreach (var (key, entryValue) in entries)
                    Walk(entryValue, path + "{" + key + "}", lines, depth + 1);
                return;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(HashSet<>))
            {
                // Set canonicalization — reason (3) in the class header: fingerprint each element into its
                // own block, sort the blocks, then index by SORTED position, so bucket layout (which differs
                // between reference-kind and value-kind elements, and between assemblies) cannot leak in.
                var blocks = new List<string>();
                foreach (var element in (IEnumerable)value)
                {
                    var elementLines = new List<string>();
                    Walk(element, "e", elementLines, depth + 1);
                    elementLines.Sort(StringComparer.Ordinal);
                    blocks.Add(string.Join("; ", elementLines));
                }

                blocks.Sort(StringComparer.Ordinal);
                lines.Add(path + ".#=" + blocks.Count.ToString(CultureInfo.InvariantCulture));
                for (var i = 0; i < blocks.Count; i++)
                    lines.Add(path + "{" + i.ToString(CultureInfo.InvariantCulture) + "}=" + blocks[i]);
                return;
            }

            if (value is IEnumerable sequence)
            {
                // Ordered sequences (array / List / IReadOnlyList) keep their index: element ORDER is part
                // of list semantics and the mapper must preserve it — a reordering bug must stay visible.
                var i = 0;
                foreach (var element in sequence)
                {
                    Walk(element, path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]", lines, depth + 1);
                    i++;
                }

                lines.Add(path + ".#=" + i.ToString(CultureInfo.InvariantCulture));
                return;
            }

            // Graph node: public readable properties and public fields, path-keyed by member NAME. The
            // merged list needs no local sort — the global sort in Compute owns canonical order.
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                Walk(property.GetValue(value), path + "." + property.Name, lines, depth + 1);
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Walk(field.GetValue(value), path + "." + field.Name, lines, depth + 1);
        }

        /// <summary>The K0 scalar vocabulary plus primitives — mirrors <c>ReflectionOracle.IsScalar</c>.</summary>
        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTimeOffset);
        }

        private static string Fmt(object value)
        {
            return value switch
            {
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? "<null-tostring>"
            };
        }
    }
}
