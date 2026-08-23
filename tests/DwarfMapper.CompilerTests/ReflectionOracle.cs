// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.CompilerTests
{
    /// <summary>
    ///     K1's reference implementation: a DELIBERATELY NAIVE test-side copier plus a name-keyed
    ///     deterministic populator. Its only virtue is being obviously correct — same-name public members,
    ///     recursive on nested graph types, element-wise on collections; no caching, no planning, no codegen.
    ///     McKeeman's differential principle needs nothing smarter: any disagreement with DwarfMapper's
    ///     generated map is a finding for one side or the other.
    ///     <para>
    ///         Reflection here is test-side only — the audit's stated boundary: the house no-reflection stance
    ///         governs the shipped product's accessibility behaviour, not test oracles. Public members only.
    ///     </para>
    ///     <para>
    ///         <b>Option switches.</b> Where DwarfMapper's DOCUMENTED semantics diverge from a naive copy, the
    ///         oracle consults the same documented option — exactly one switch per documented option, each
    ///         quoting its doc anchor (see <see cref="OracleOptions" />). An UNDOCUMENTED divergence found by
    ///         sampling is a finding (pinned corpus row; I-row if product-shaped) and is NEVER ratified by
    ///         silently teaching the oracle.
    ///     </para>
    ///     <para>
    ///         <b>Declared population bias</b> (the K0 enum-ratchet lesson applied to values: a bias is
    ///         declared, never silent): (a) string members are always populated, never null — a null string is
    ///         copied identically by both sides, so it buys no discrimination; (b) reference-typed NESTED
    ///         members and collection ELEMENTS are always populated, never null — what a null nested value
    ///         becomes on a re-kinded (struct) destination is a semantics question the docs do not answer
    ///         member-by-member yet (K1's first deep run proved the point in the mirror direction: the
    ///         value→reference re-kind THROWS at runtime — filed as I7, pinned deterministically; this bias is
    ///         why the reference→value genre of the same I7 filing is unreachable in sampling and carries its
    ///         own pin), so nulls there would force the oracle to invent an answer, which is exactly what it
    ///         must never do. Null coverage that IS documented — null COLLECTION members
    ///         (<c>NullCollections</c>) and null <c>Nullable&lt;T&gt;</c> scalars — is generated. Widening
    ///         either bias is future pressure, gated on a documented sentence to encode.
    ///     </para>
    ///     <para>
    ///         H7: every recursion below carries an explicit depth bound with a LOUD failure. The bound can
    ///         only trip if <c>GraphSpec</c>'s V2 DAG rule is violated upstream — it is the belt, not the
    ///         termination argument.
    ///     </para>
    /// </summary>
    internal static class ReflectionOracle
    {
        /// <summary>
        ///     H7 belt. Sampled graphs are DAGs of at most 8 nodes (V2), so real recursion depth is far below
        ///     this; reaching the bound means an upstream invariant broke and the oracle says so loudly.
        /// </summary>
        private const int MaxDepth = 32;

        // ─── population ──────────────────────────────────────────────────────────────

        /// <summary>
        ///     Builds a fully deterministic instance of <paramref name="type" />. NAME-KEYED per the audit's
        ///     binding correction: every member's value derives from <paramref name="seed" /> mixed with the
        ///     member's own NAME PATH (never its declaration index), so the same logical graph reordered — or
        ///     re-kinded — populates identically, which K2's MR relations will rely on. The seed comes from the
        ///     CsCheck sample, so a shrunk failure replays exactly.
        /// </summary>
        /// <param name="type">The type to build.</param>
        /// <param name="seed">The CsCheck-sampled population seed.</param>
        public static object Populate(Type type, int seed)
        {
            return BuildValue(type, seed, 0) ?? throw new InvalidOperationException($"population produced null for root type {type}");
        }

        private static object? BuildValue(Type type, int key, int depth)
        {
            DemandDepth(depth, type);

            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable is not null)
            {
                // Documented-coverage null: a Nullable<T> member is null in ~25% of populations.
                return Positive(key, "null?") % 4 == 0 ? null : BuildValue(nullable, key, depth + 1);
            }

            if (IsScalar(type))
            {
                return ScalarFor(type, key);
            }

            if (TryGetCollectionShape(type, out var shape, out var elementType, out var valueType))
            {
                // Documented-coverage null: a collection member is null in ~20% of populations — this is the
                // NullCollections switch's food; its deterministic regression pin lives in
                // DifferentialOracleTests so the coverage here cannot silently go vacuous.
                if (Positive(key, "nullcoll?") % 5 == 0)
                {
                    return null;
                }

                return BuildCollection(shape, type, elementType, valueType, key, depth);
            }

            // Graph node type: construct it, every member keyed by name.
            return Instantiate(type, (name, memberType) => BuildValue(memberType, Mix(key, name), depth + 1));
        }

        private static object BuildCollection(
            CollectionShapeKind shape,
            Type type,
            Type elementType,
            Type? valueType,
            int key,
            int depth)
        {
            var count = Positive(key, "count") % 3; // 0..2 — empty collections stay reachable

            if (shape == CollectionShapeKind.Dictionary)
            {
                var dict = (IDictionary)Activator.CreateInstance(type)!;
                for (var i = 0; i < count; i++)
                    dict.Add(
                        "K" + i.ToString(CultureInfo.InvariantCulture),
                        BuildValue(valueType!, Mix(key, "[" + i.ToString(CultureInfo.InvariantCulture) + "]"), depth + 1));

                return dict;
            }

            var items = new object?[count];
            for (var i = 0; i < count; i++)
                items[i] = BuildValue(elementType, Mix(key, "[" + i.ToString(CultureInfo.InvariantCulture) + "]"), depth + 1);
            return MaterializeSequence(shape, type, elementType, items);
        }

        // ─── the naive copier ────────────────────────────────────────────────────────

        /// <summary>
        ///     The oracle map: copy <paramref name="source" /> onto a new instance of
        ///     <paramref name="destType" /> by same-name public members, recursively for nested graph types,
        ///     element-wise for collections. Keep it stupid — every branch is either the naive copy or a
        ///     documented-option switch with its doc anchor on <see cref="OracleOptions" />.
        /// </summary>
        public static object? NaiveMap(object? source, Type destType, OracleOptions options)
        {
            ArgumentNullException.ThrowIfNull(destType);
            ArgumentNullException.ThrowIfNull(options);
            return CopyValue(source, destType, options, 0);
        }

        private static object? CopyValue(object? source, Type destType, OracleOptions options, int depth)
        {
            DemandDepth(depth, destType);

            var nullableDest = Nullable.GetUnderlyingType(destType);
            if (nullableDest is not null)
            {
                return source is null ? null : CopyValue(source, nullableDest, options, depth + 1);
            }

            if (source is null)
            {
                if (TryGetCollectionShape(destType, out var nullShape, out var nullElement, out var nullValue))
                {
                    // OPTION SWITCH — NullCollections (docs/options.md class-options row; default AsEmpty):
                    // the documented default turns a null source collection into an EMPTY destination
                    // collection; AsNull propagates the null. The ONLY non-naive branch in this copier.
                    return options.NullCollections == NullCollectionStrategy.AsEmpty
                        ? EmptyCollection(nullShape, destType, nullElement, nullValue)
                        : null;
                }

                return null;
            }

            if (IsScalar(destType))
            {
                return source; // mirrored scalar vocabulary — no conversions to model
            }

            if (TryGetCollectionShape(destType, out var shape, out var elementType, out var valueType))
            {
                if (shape == CollectionShapeKind.Dictionary)
                {
                    var dest = (IDictionary)Activator.CreateInstance(destType)!;
                    foreach (DictionaryEntry entry in (IDictionary)source)
                        dest.Add(entry.Key, CopyValue(entry.Value, valueType!, options, depth + 1));
                    return dest;
                }

                var items = new List<object?>();
                foreach (var item in (IEnumerable)source)
                    items.Add(CopyValue(item, elementType, options, depth + 1));
                return MaterializeSequence(shape, destType, elementType, [.. items]);
            }

            // Graph node: same-name public member copy, dest-driven (completeness is the generator's own
            // documented contract, so a dest member with no source counterpart is a refusal upstream and
            // this lookup failing LOUDLY is correct).
            return Instantiate(destType,
                (name, memberType) =>
                    CopyValue(ReadSourceMember(source, name), memberType, options, depth + 1));
        }

        private static object? ReadSourceMember(object source, string name)
        {
            var type = source.GetType();
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null && property.CanRead)
            {
                return property.GetValue(source);
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null)
            {
                return field.GetValue(source);
            }

            throw new InvalidOperationException(
                $"oracle precondition violated: source type {type} has no public member '{name}'");
        }

        // ─── shared construction ─────────────────────────────────────────────────────

        /// <summary>
        ///     The one construction path both the populator and the copier use: values come from
        ///     <paramref name="valueFor" /> keyed by MEMBER NAME. Constructor parameters are matched to members
        ///     case-insensitively (the renderer lower-cases the first letter, mirroring the product's own
        ///     ctor-parameter matching); everything else goes through writable properties (init included —
        ///     runtime reflection can set init-only setters) and public fields.
        /// </summary>
        private static object Instantiate(Type type, Func<string, Type, object?> valueFor)
        {
            var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            object instance;
            if (ctor is null || ctor.GetParameters().Length == 0)
            {
                instance = Activator.CreateInstance(type) ?? throw new InvalidOperationException($"could not construct {type}");
            }
            else
            {
                var args = ctor.GetParameters()
                    .Select(p =>
                    {
                        var member = FindMemberIgnoreCase(type, p.Name!);
                        return valueFor(member, p.ParameterType);
                    })
                    .ToArray();
                instance = ctor.Invoke(args);
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!property.CanWrite || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                property.SetValue(instance, valueFor(property.Name, property.PropertyType));
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                field.SetValue(instance, valueFor(field.Name, field.FieldType));

            return instance;
        }

        private static string FindMemberIgnoreCase(Type type, string parameterName)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (string.Equals(property.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return property.Name;
                }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (string.Equals(field.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return field.Name;
                }

            throw new InvalidOperationException(
                $"oracle precondition violated: {type} ctor parameter '{parameterName}' matches no public member");
        }

        /// <summary>The K0 grammar's CollShape vocabulary, recognized from the runtime type.</summary>
        private static bool TryGetCollectionShape(
            Type type,
            out CollectionShapeKind shape,
            out Type elementType,
            out Type? valueType)
        {
            shape = default;
            elementType = typeof(object);
            valueType = null;

            if (type.IsArray)
            {
                shape = CollectionShapeKind.Array;
                elementType = type.GetElementType()!;
                return true;
            }

            if (!type.IsGenericType)
            {
                return false;
            }

            var definition = type.GetGenericTypeDefinition();
            var args = type.GetGenericArguments();

            if (definition == typeof(List<>))
            {
                shape = CollectionShapeKind.List;
                elementType = args[0];
                return true;
            }

            if (definition == typeof(IReadOnlyList<>))
            {
                shape = CollectionShapeKind.ReadOnlyList;
                elementType = args[0];
                return true;
            }

            if (definition == typeof(HashSet<>))
            {
                shape = CollectionShapeKind.Set;
                elementType = args[0];
                return true;
            }

            if (definition == typeof(Dictionary<,>))
            {
                shape = CollectionShapeKind.Dictionary;
                elementType = args[0];
                valueType = args[1];
                return true;
            }

            return false;
        }

        private static object MaterializeSequence(
            CollectionShapeKind shape,
            Type declaredType,
            Type elementType,
            object?[] items)
        {
            if (shape == CollectionShapeKind.Array)
            {
                var array = Array.CreateInstance(elementType, items.Length);
                for (var i = 0; i < items.Length; i++) array.SetValue(items[i], i);
                return array;
            }

            var concrete = shape switch
            {
                CollectionShapeKind.List => typeof(List<>).MakeGenericType(elementType),
                CollectionShapeKind.ReadOnlyList => typeof(List<>).MakeGenericType(elementType),
                CollectionShapeKind.Set => typeof(HashSet<>).MakeGenericType(elementType),
                _ => throw new InvalidOperationException($"unexpected sequence shape {shape} for {declaredType}")
            };
            var instance = Activator.CreateInstance(concrete)!;
            var add = concrete.GetMethod("Add")!;
            foreach (var item in items) add.Invoke(instance, [item]);
            return instance;
        }

        private static object EmptyCollection(
            CollectionShapeKind shape,
            Type declaredType,
            Type elementType,
            Type? valueType)
        {
            return shape switch
            {
                CollectionShapeKind.Array => Array.CreateInstance(elementType, 0),
                CollectionShapeKind.Dictionary =>
                    Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(elementType, valueType!))!,
                _ => MaterializeSequence(shape, declaredType, elementType, [])
            };
        }

        // ─── deterministic values ────────────────────────────────────────────────────

        /// <summary>The K0 scalar vocabulary (TypeGraphGen.Scalars), one deterministic value per (type, key).</summary>
        private static object ScalarFor(Type type, int key)
        {
            var h = Positive(key, type.Name);
            if (type == typeof(int))
            {
                return h;
            }

            if (type == typeof(long))
            {
                return h * 2654435761L;
            }

            if (type == typeof(string))
            {
                return "s" + h.ToString(CultureInfo.InvariantCulture);
            }

            if (type == typeof(decimal))
            {
                return new decimal(h) / 100m;
            }

            if (type == typeof(bool))
            {
                return (h & 1) == 0;
            }

            if (type == typeof(byte))
            {
                return (byte)h;
            }

            if (type == typeof(double))
            {
                return h * 0.5;
            }

            if (type == typeof(Guid))
            {
                var bytes = new byte[16];
                var v = (uint)h;
                for (var i = 0; i < 16; i++)
                {
                    v = unchecked(v * 16777619u + 2166136261u);
                    bytes[i] = (byte)(v >> 24);
                }

                return new Guid(bytes);
            }

            if (type == typeof(DateTimeOffset))
            {
                return new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(h % 1_000_000);
            }

            throw new InvalidOperationException(
                $"scalar type {type} is outside the K0 vocabulary — extend ScalarFor deliberately, not by default");
        }

        /// <summary>FNV-1a over the name, mixed with the seed — stable across processes (never string.GetHashCode).</summary>
        private static int Mix(int seed, string name)
        {
            unchecked
            {
                var h = 2166136261u ^ (uint)seed;
                foreach (var c in name) h = (h ^ c) * 16777619u;
                return (int)h;
            }
        }

        private static int Positive(int key, string salt)
        {
            return (int)((uint)Mix(key, salt) % int.MaxValue);
        }

        private static void DemandDepth(int depth, Type at)
        {
            if (depth > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"oracle recursion exceeded depth {MaxDepth} at {at} — the sampled graph is not the DAG " + "GraphSpec rule V2 guarantees; fix the upstream invariant, do not raise this bound");
            }
        }

        private static bool IsScalar(Type type)
        {
            return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) || type == typeof(DateTimeOffset);
        }

        /// <summary>
        ///     One switch per DOCUMENTED option whose semantics diverge from a naive copy, each anchored to its
        ///     documentation row. Defaults mirror the product's documented defaults, because the sampled graphs
        ///     declare no options at all.
        /// </summary>
        /// <param name="NullCollections">
        ///     <c>docs/options.md</c>, class-options table, row <c>NullCollections</c> (default
        ///     <c>AsEmpty</c>): "Null source collection → <c>AsEmpty</c> (never throws) or <c>AsNull</c>…".
        ///     A naive copy would propagate the null; the documented default materializes an empty collection,
        ///     confirmed live against the emitted code (K1 probe, 2026-08-22:
        ///     <c>if (src is null) return new List&lt;…&gt;();</c>).
        /// </param>
        internal sealed record OracleOptions(NullCollectionStrategy NullCollections)
        {
            /// <summary>The documented defaults — what an attribute-less mapper declaration gets.</summary>
            public static OracleOptions DocumentedDefaults { get; } = new(NullCollectionStrategy.AsEmpty);
        }

        // ─── collection shape plumbing ───────────────────────────────────────────────

        private enum CollectionShapeKind
        {
            Array,
            List,
            ReadOnlyList,
            Set,
            Dictionary
        }
    }
}
