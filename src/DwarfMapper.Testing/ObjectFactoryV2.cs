// SPDX-License-Identifier: GPL-2.0-only

using System.Collections;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;

namespace DwarfMapper.Testing
{
    /// <summary>
    ///     Extended deterministic object factory (Plan 19 Part E self-materializer).
    ///     Constructs instances of ANY supported DwarfMapper type from a seed:
    ///     - every basic scalar type
    ///     - every supported collection / dictionary / immutable / interface target (populated up to a bounded
    ///     depth; empty at the depth cap to terminate recursion)
    ///     - nested objects and records (bounded depth)
    ///     - reference-graph fixtures: self-loop, 2-node cycle, diamond, owner graph A→B/B⇄C/C⇄D/B⇄D.
    ///     Deterministic and replayable: same seed always produces the same value.
    /// </summary>
    public static class ObjectFactoryV2
    {
        private const int DefaultMaxDepth = 6;

        /// <summary>
        ///     Probability that a nullable member comes back as <c>null</c>.
        ///     <para>
        ///         The factory used to unwrap <c>Nullable&lt;T&gt;</c> to <c>T</c> and always return a value, and never
        ///         returned a null reference either — so the fuzz suites drove the mapper exclusively with fully
        ///         populated graphs, and NONE of the null machinery (NullStrategy, NullSubstitute/DWARF049,
        ///         nullable→non-nullable/DWARF070, SkipNullSourceMembers, nullAsNull, null-propagation in synthesized
        ///         nested helpers) was ever exercised by a fuzzer. Null handling is exactly where subtle mapping bugs
        ///         live, so that was the single largest blind spot in the suite.
        ///     </para>
        /// </summary>
        public const double NullProbability = 0.15;

        /// <summary>
        ///     Probability that a numeric/char/string draw returns a boundary value (0, MinValue, MaxValue, -1, 1,
        ///     empty/whitespace) instead of a "comfortable" one. Every integral draw used to be
        ///     <c>rng.Next(1, MaxValue)</c> — never zero, never negative, never at a limit — so the narrowing and
        ///     sign-conversion machinery (CreateChecked/H4) was only ever probed by <c>long</c>'s 1-in-4 escape.
        /// </summary>
        public const double EdgeProbability = 0.25;

        /// <summary>Picks one of <paramref name="edges" /> with <see cref="EdgeProbability" />, else <paramref name="normal" />.</summary>
        private static object EdgeOr(Random rng, Func<object> normal, params object[] edges)
        {
            return rng.NextDouble() < EdgeProbability ? edges[rng.Next(edges.Length)] : normal();
        }

        /// <summary>
        ///     Element/member count for a generated collection. Empty (0) is drawn at any depth so the
        ///     empty-collection paths (nullCollections AsEmpty/AsNull, the unknown-count buffer) get exercised; the
        ///     LARGE draw is restricted to depth 0 because a large fan-out repeated at every nesting level would
        ///     explode combinatorially (16^6) and make the fuzz suite unusable.
        /// </summary>
        private static int NextCollectionSize(Random rng, int depth)
        {
            if (depth >= DefaultMaxDepth)
            {
                return 0;
            }

            var roll = rng.NextDouble();
            if (roll < 0.15)
            {
                return 0;
            }

            if (roll < 0.25 && depth == 0)
            {
                return rng.Next(8, 17);
            }

            return rng.Next(1, 4);
        }

        // ── Public entry points ──────────────────────────────────────────────────────

        /// <summary>Create a populated instance of <typeparamref name="T" /> for seed 0.</summary>
        public static T Create<T>()
        {
            return Create<T>(0);
        }

        /// <summary>Create a populated instance of <typeparamref name="T" /> for the given seed.</summary>
        public static T Create<T>(int seed)
        {
            return (T)Create(typeof(T), new Random(seed), 0)!;
        }

        /// <summary>
        ///     Create a populated instance of <paramref name="type" /> using <paramref name="rng" />.
        ///     Supports every scalar, T?, array, List, HashSet, IReadOnlyList, IReadOnlyCollection,
        ///     ICollection, IList, ISet, IReadOnlySet, IEnumerable, ImmutableArray, ImmutableList,
        ///     IImmutableList, ImmutableHashSet, IImmutableSet, Dictionary, IDictionary,
        ///     IReadOnlyDictionary, ImmutableDictionary, IImmutableDictionary,
        ///     and arbitrary class/struct/record types.
        /// </summary>
        public static object? Create(Type type, Random rng, int depth)
        {
            return Create(type, rng, depth, true);
        }

        /// <summary>
        ///     Create a populated instance of <paramref name="type" />, optionally allowing <c>null</c>.
        /// </summary>
        /// <param name="type">The type to materialise.</param>
        /// <param name="rng">Seeded random source; the same seed always produces the same value.</param>
        /// <param name="depth">Current nesting depth; recursion stops at the depth cap.</param>
        /// <param name="allowNull">
        ///     When false, never returns null. Used for positions where null is not a legal value regardless of the
        ///     mapping contract — dictionary KEYS (a null key throws) and graph-fixture roots.
        /// </param>
        /// <returns>A populated instance, or <c>null</c> when a nullable position was drawn as null.</returns>
        public static object? Create(Type type, Random rng, int depth, bool allowNull)
        {
            if (rng is null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            if (type is null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            // Nullable<T> → sometimes null, else unwrap and recurse. Nullable<T> may be null at ANY depth
            // (including a root `Create(typeof(int?), …)`), since that is the whole point of the type.
            var underlying = Nullable.GetUnderlyingType(type);
            if (underlying is not null)
            {
                return allowNull && rng.NextDouble() < NullProbability ? null : Create(underlying, rng, depth);
            }

            // Reference types: sometimes null, but only BELOW the root — callers materialise roots with `!` and a
            // null root would just be a broken fixture rather than an interesting input.
            if (!type.IsValueType && depth > 0 && allowNull && rng.NextDouble() < NullProbability)
            {
                return null;
            }

            // ── Scalars ──────────────────────────────────────────────────────────
            if (type == typeof(string))
            {
                return EdgeOr(rng,
                    () => "s" + rng.Next(0, 1_000_000).ToString(CultureInfo.InvariantCulture),
                    "",
                    " ",
                    "\t");
            }

            if (type == typeof(bool))
            {
                return rng.Next(0, 2) == 1;
            }

            if (type == typeof(byte))
            {
                return EdgeOr(rng, () => (byte)rng.Next(1, 256), (byte)0, byte.MinValue, byte.MaxValue, (byte)1);
            }

            if (type == typeof(sbyte))
            {
                return EdgeOr(rng,
                    () => (sbyte)rng.Next(1, 127),
                    (sbyte)0,
                    sbyte.MinValue,
                    sbyte.MaxValue,
                    (sbyte)-1,
                    (sbyte)1);
            }

            if (type == typeof(short))
            {
                return EdgeOr(rng,
                    () => (short)rng.Next(1, short.MaxValue),
                    (short)0,
                    short.MinValue,
                    short.MaxValue,
                    (short)-1,
                    (short)1);
            }

            if (type == typeof(ushort))
            {
                return EdgeOr(rng,
                    () => (ushort)rng.Next(1, ushort.MaxValue),
                    (ushort)0,
                    ushort.MinValue,
                    ushort.MaxValue,
                    (ushort)1);
            }

            if (type == typeof(int))
            {
                return EdgeOr(rng, () => rng.Next(1, int.MaxValue), 0, int.MinValue, int.MaxValue, -1, 1);
            }

            if (type == typeof(uint))
            {
                return EdgeOr(rng, () => (uint)rng.Next(1, int.MaxValue), 0u, uint.MinValue, uint.MaxValue, 1u);
            }

            // C9: occasionally emit out-of-int-range long values so the long→int CreateChecked
            // overflow path is exercised.
            if (type == typeof(long))
            {
                return EdgeOr(rng,
                    () => rng.Next(0, 4) == 0 ? rng.NextInt64(long.MinValue, long.MaxValue) : rng.Next(1, int.MaxValue),
                    0L,
                    long.MinValue,
                    long.MaxValue,
                    -1L,
                    1L);
            }

            if (type == typeof(ulong))
            {
                return EdgeOr(rng, () => (ulong)rng.Next(1, int.MaxValue), 0ul, ulong.MinValue, ulong.MaxValue, 1ul);
            }

            if (type == typeof(float))
            {
                return EdgeOr(rng, () => (float)(rng.NextDouble() * 1000), 0f, -1f, 1f, float.MinValue, float.MaxValue);
            }

            if (type == typeof(double))
            {
                return EdgeOr(rng, () => rng.NextDouble() * 1000, 0d, -1d, 1d, double.MinValue, double.MaxValue);
            }

            if (type == typeof(decimal))
            {
                return EdgeOr(rng,
                    () => (decimal)(rng.NextDouble() * 1000),
                    0m,
                    -1m,
                    1m,
                    decimal.MinValue,
                    decimal.MaxValue);
            }

            if (type == typeof(char))
            {
                return EdgeOr(rng, () => (char)rng.Next('A', 'Z' + 1), '\0', char.MinValue, char.MaxValue);
            }

            if (type == typeof(Guid))
            {
                var b = new byte[16];
                rng.NextBytes(b);
                return new Guid(b);
            }

            if (type == typeof(DateTime))
            {
                return new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(rng.Next(0, 5_000_000));
            }

            if (type == typeof(DateTimeOffset))
            {
                return new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(rng.Next(0, 5_000_000));
            }

            if (type == typeof(TimeSpan))
            {
                return TimeSpan.FromSeconds(rng.Next(1, 5_000_000));
            }

            // ── Enum ─────────────────────────────────────────────────────────────
            if (type.IsEnum)
            {
                var values = Enum.GetValues(type);
                if (values.Length == 0)
                {
                    return Activator.CreateInstance(type);
                }

                // MERGED FROM V1, 2026-08-26. A [Flags] enum's whole point is that COMBINED values
                // (Read | Write) are legal, and picking a single declared member can never produce one. So
                // the fuzzers only ever fed enums values that happened to have a NAME, and a by-name
                // converter that threw on every combination looked perfectly healthy.
                if (type.IsDefined(typeof(FlagsAttribute), false))
                {
                    var picks = rng.Next(1, Math.Min(values.Length, 4) + 1);

                    // Accumulate in the enum's own underlying type: an unsigned enum can hold values above
                    // long.MaxValue, which Convert.ToInt64 would throw on.
                    if (Enum.GetUnderlyingType(type) == typeof(ulong))
                    {
                        ulong acc = 0;
                        for (var i = 0; i < picks; i++)
                            acc |= Convert.ToUInt64(values.GetValue(rng.Next(values.Length)), CultureInfo.InvariantCulture);
                        return Enum.ToObject(type, acc);
                    }

                    long signed = 0;
                    for (var i = 0; i < picks; i++)
                        signed |= Convert.ToInt64(values.GetValue(rng.Next(values.Length)), CultureInfo.InvariantCulture);
                    return Enum.ToObject(type, signed);
                }

                return values.GetValue(rng.Next(values.Length));
            }

            // ── Array ─────────────────────────────────────────────────────────────
            if (type.IsArray)
            {
                var elemType = type.GetElementType()!;
                var n = NextCollectionSize(rng, depth);
                var array = Array.CreateInstance(elemType, n);
                for (var i = 0; i < n; i++)
                    array.SetValue(Create(elemType, rng, depth + 1), i);
                return array;
            }

            if (type.IsGenericType)
            {
                var gtd = type.GetGenericTypeDefinition();
                var args = type.GetGenericArguments();

                // ── List<T> ───────────────────────────────────────────────────────
                if (gtd == typeof(List<>))
                {
                    return MakeList(args[0], rng, depth);
                }

                // ── HashSet<T> ────────────────────────────────────────────────────
                if (gtd == typeof(HashSet<>))
                {
                    return MakeHashSet(args[0], rng, depth);
                }

                // ── IEnumerable<T>, ICollection<T>, IList<T>, IReadOnlyList<T>, IReadOnlyCollection<T>
                //    → materialise as List<T>
                if (gtd == typeof(IEnumerable<>) ||
                    gtd == typeof(ICollection<>) ||
                    gtd == typeof(IList<>) ||
                    gtd == typeof(IReadOnlyList<>) ||
                    gtd == typeof(IReadOnlyCollection<>))
                {
                    return MakeList(args[0], rng, depth);
                }

                // ── ISet<T>, IReadOnlySet<T> → HashSet<T>
                if (gtd == typeof(ISet<>) || gtd == typeof(IReadOnlySet<>))
                {
                    return MakeHashSet(args[0], rng, depth);
                }

                // ── Queue<T>, Stack<T> → built from a generated List<T> through their IEnumerable<T> constructor.
                // They used to fall through to the class path, where the parameterless constructor built them EMPTY
                // for every seed (53 construction calls in Generator.Tests, 2026-09-15 probe).
                if (gtd == typeof(Queue<>) || gtd == typeof(Stack<>))
                {
                    return Activator.CreateInstance(type, MakeList(args[0], rng, depth));
                }

                // ── ImmutableArray<T>
                if (gtd == typeof(ImmutableArray<>))
                {
                    var elemType = args[0];
                    var list = (IList)MakeList(elemType, rng, depth);
                    // ImmutableArray.CreateRange<T>(IEnumerable<T>) — pick the correct single-arg overload
                    var createRange = FindImmutableCreateRange(typeof(ImmutableArray), elemType);
                    if (createRange is null)
                    {
                        return ImmutableArray<int>.Empty; // fallback (shouldn't happen)
                    }

                    return createRange.Invoke(null,
                        new object[]
                        {
                            list
                        });
                }

                // ── ImmutableList<T>, IImmutableList<T>
                if (gtd == typeof(ImmutableList<>) ||
                    (type.IsInterface &&
                     args.Length == 1 &&
                     typeof(IImmutableList<>).MakeGenericType(args[0]).IsAssignableFrom(type)))
                {
                    var elemType = args[0];
                    var list = (IList)MakeList(elemType, rng, depth);
                    var createRange = FindImmutableCreateRange(typeof(ImmutableList), elemType);
                    if (createRange is null)
                    {
                        return ImmutableList<int>.Empty;
                    }

                    return createRange.Invoke(null,
                        new object[]
                        {
                            list
                        });
                }

                // ── ImmutableHashSet<T>, IImmutableSet<T>
                if (gtd == typeof(ImmutableHashSet<>) ||
                    (type.IsInterface &&
                     args.Length == 1 &&
                     typeof(IImmutableSet<>).MakeGenericType(args[0]).IsAssignableFrom(type)))
                {
                    var elemType = args[0];
                    var list = (IList)MakeList(elemType, rng, depth);
                    var createRange = FindImmutableCreateRange(typeof(ImmutableHashSet), elemType);
                    if (createRange is null)
                    {
                        return ImmutableHashSet<int>.Empty;
                    }

                    return createRange.Invoke(null,
                        new object[]
                        {
                            list
                        });
                }

                // ── Dictionary<K,V>
                if (gtd == typeof(Dictionary<,>))
                {
                    return MakeDictionary(args[0], args[1], rng, depth);
                }

                // ── IDictionary<K,V>, IReadOnlyDictionary<K,V>
                if (gtd == typeof(IDictionary<,>) || gtd == typeof(IReadOnlyDictionary<,>))
                {
                    return MakeDictionary(args[0], args[1], rng, depth);
                }

                // ── ImmutableDictionary<K,V>, IImmutableDictionary<K,V>
                if (gtd == typeof(ImmutableDictionary<,>) ||
                    (type.IsInterface && args.Length == 2))
                {
                    // Check for IImmutableDictionary interface
                    var iimmutDictType = typeof(IImmutableDictionary<,>).MakeGenericType(args[0], args[1]);
                    if (iimmutDictType.IsAssignableFrom(type) || gtd == typeof(ImmutableDictionary<,>))
                    {
                        var plainDict = (IDictionary)MakeDictionary(args[0], args[1], rng, depth);
                        var builderMethod =
                            typeof(ImmutableDictionary).GetMethods(BindingFlags.Public | BindingFlags.Static);
                        // Use ImmutableDictionary.CreateRange(IEnumerable<KVP>)
                        var kvpType = typeof(KeyValuePair<,>).MakeGenericType(args[0], args[1]);
                        foreach (var m in builderMethod)
                            if (m.Name == "CreateRange" &&
                                m.IsGenericMethodDefinition &&
                                m.GetGenericArguments().Length == 2)
                            {
                                try
                                {
                                    var concrete = m.MakeGenericMethod(args[0], args[1]);
                                    // Build a list of KVPs
                                    var kvpList = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(kvpType))!;
                                    foreach (DictionaryEntry entry in plainDict)
                                        kvpList.Add(Activator.CreateInstance(kvpType, entry.Key, entry.Value)!);
                                    return concrete.Invoke(null,
                                        new object[]
                                        {
                                            kvpList
                                        });
                                }
                                catch (TargetInvocationException)
                                {
                                    /* try next overload */
                                }
                                catch (ArgumentException)
                                {
                                    /* try next overload */
                                }
                                catch (InvalidOperationException)
                                {
                                    /* try next overload */
                                }
                            }

                        // Fallback: return the plain Dictionary
                        return plainDict;
                    }
                }
            }

            // ── Interface or abstract → substitute a concrete implementation ────
            //
            // MERGED FROM V1, 2026-08-26. This branch used to carry exactly this comment and then
            // `return null`, which is the bug V1 was fixed for: found migrating a ~300-map codebase off
            // AutoMapper, where a Dictionary<K, AbstractValue> came out with null VALUES, so every fixture
            // built from it exercised the null path rather than the dispatch path — the shape
            // [MapDerivedType] exists to map — and then looked like a real behavioural difference when
            // replayed against a mapper that correctly refuses nulls.
            if (type.IsInterface || type.IsAbstract)
            {
                var concrete = depth < DefaultMaxDepth ? PickConcrete(type, rng) : null;
                if (concrete is not null)
                {
                    return Create(concrete, rng, depth + 1, allowNull);
                }

                // No value-type arm: an interface or an abstract type is never a value type.
                return null;
            }

            if (depth >= DefaultMaxDepth)
            {
                return type.IsValueType ? Activator.CreateInstance(type) : null;
            }

            // ── Class / struct / record ──────────────────────────────────────────
            var ctor = type.GetConstructor(Type.EmptyTypes);
            object? instance;
            ParameterInfo[] bound = [];
            if (ctor is null)
            {
                // MERGED FROM V1, 2026-08-26. This took ctors[0] — whichever constructor reflection
                // happened to return first. Reflection member order is not contractually stable, so the
                // factory's output was not a pure function of the seed, and seed-determinism is the
                // property the entire fuzz corpus rests on. Order by parameter count, then by signature.
                var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
                if (ctors.Length == 0)
                {
                    if (!type.IsValueType)
                    {
                        return null;
                    }

                    // A struct with no declared constructor has no PUBLIC constructor at all (its parameterless one
                    // is implicit), so it is default-constructed here and then populated below like any other type.
                    // Returning the default instead left every such struct all zeros in every fuzz fixture, so struct
                    // mapping was only ever fuzzed with default values (found 2026-09-15, fixed by owner ruling).
                    instance = Activator.CreateInstance(type);
                }
                else
                {
                    var pc = ctors
                        .OrderBy(c => c.GetParameters().Length)
                        .ThenBy(c => string.Join(",", c.GetParameters().Select(x => x.ParameterType.FullName)),
                            StringComparer.Ordinal)
                        .First();
                    var parms = pc.GetParameters();
                    var pvals = new object?[parms.Length];
                    for (var i = 0; i < parms.Length; i++)
                        pvals[i] = Create(parms[i].ParameterType, rng, depth + 1);

                    // Falls through to the member loops below. This used to return here, so every member the
                    // constructor does not set stayed at its default for every seed (owner ruling 2026-09-15).
                    instance = pc.Invoke(pvals);
                    bound = parms;
                }
            }
            else
            {
                instance = ctor.Invoke(null);
            }

            // A member named like a constructor parameter was already set from its seeded argument, so it is not
            // drawn again. That also keeps a positional record's rng sequence, and so its fixtures, unchanged.
            foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                if (p.CanWrite && p.GetSetMethod() is not null && p.GetIndexParameters().Length == 0 && !IsBoundByConstructor(bound, p.Name))
                {
                    p.SetValue(instance, Create(p.PropertyType, rng, depth + 1));
                }

            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                if (!f.IsInitOnly && !IsBoundByConstructor(bound, f.Name))
                {
                    f.SetValue(instance, Create(f.FieldType, rng, depth + 1));
                }

            return instance;
        }

        /// <summary>Whether <paramref name="memberName" /> names one of the chosen constructor's parameters.</summary>
        private static bool IsBoundByConstructor(ParameterInfo[] parameters, string memberName)
        {
            return Array.Exists(parameters, x => string.Equals(x.Name, memberName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Concrete candidates per abstract type, resolved once per process.</summary>
        private static readonly Dictionary<Type, Type[]> ConcreteCandidates = [];

        /// <summary>
        ///     A concrete, parameterless-constructible type assignable to <paramref name="abstractType" />, or
        ///     <see langword="null" /> when the loaded assemblies offer none.
        /// </summary>
        /// <remarks>
        ///     Ordered by full name before the draw so the choice is a pure function of the seed — fixtures
        ///     must not shift because the runtime happened to enumerate assemblies differently.
        /// </remarks>
        private static Type? PickConcrete(Type abstractType, Random rng)
        {
            Type[] candidates;
            lock (ConcreteCandidates)
            {
                if (!ConcreteCandidates.TryGetValue(abstractType, out candidates!))
                {
                    candidates = AppDomain.CurrentDomain.GetAssemblies()
                        .SelectMany(SafeTypes)
                        .Where(c => !c.IsAbstract && !c.IsInterface && !c.IsGenericTypeDefinition && abstractType.IsAssignableFrom(c) && c.GetConstructor(Type.EmptyTypes) is not null)
                        .OrderBy(c => c.FullName, StringComparer.Ordinal)
                        .ToArray();
                    ConcreteCandidates[abstractType] = candidates;
                }
            }

            return candidates.Length == 0 ? null : candidates[rng.Next(candidates.Length)];
        }

        /// <summary>A half-loadable assembly must not take the whole scan down.</summary>
        private static IEnumerable<Type> SafeTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t is not null)!;
            }
        }

        // ── Graph fixture builders ───────────────────────────────────────────────────

        /// <summary>
        ///     Build a self-loop graph fixture using reflection.
        ///     The returned type must have a settable property named <paramref name="selfPropName" />
        ///     of the same type.
        /// </summary>
        public static object MakeSelfLoop(Type nodeType, string selfPropName, Random rng)
        {
            if (nodeType is null)
            {
                throw new ArgumentNullException(nameof(nodeType));
            }

            var node = Create(nodeType, rng, 0)!;
            var prop = nodeType.GetProperty(selfPropName, BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException($"Property '{selfPropName}' not found on {nodeType.Name}", nameof(selfPropName));
            prop.SetValue(node, node);
            return node;
        }

        /// <summary>
        ///     Build a 2-node mutual cycle: a.Next=b, b.Next=a.
        ///     Returns (a, b).
        /// </summary>
        public static (object A, object B) MakeTwoNodeCycle(Type nodeType, string nextPropName, Random rng)
        {
            if (nodeType is null)
            {
                throw new ArgumentNullException(nameof(nodeType));
            }

            var a = Create(nodeType, rng, 0)!;
            var b = Create(nodeType, rng, 0)!;
            var prop = nodeType.GetProperty(nextPropName, BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException($"Property '{nextPropName}' not found on {nodeType.Name}", nameof(nextPropName));
            prop.SetValue(a, b);
            prop.SetValue(b, a);
            return (a, b);
        }

        /// <summary>
        ///     Build the owner graph fixture A→B, B⇄C, C⇄D, B⇄D using four types.
        ///     Each type is constructed via <see cref="Create(Type, Random, int)" />; then back-edges are wired.
        ///     Returns (a, b, c, d).
        /// </summary>
        public static (object A, object B, object C, object D) MakeOwnerGraph(
            Type typeA,
            Type typeB,
            Type typeC,
            Type typeD,
            string bPropOnA,
            string cPropOnB,
            string dPropOnB,
            string bPropOnC,
            string dPropOnC,
            string bPropOnD,
            string cPropOnD,
            Random rng)
        {
            var a = Create(typeA, rng, 0)!;
            var b = Create(typeB, rng, 0)!;
            var c = Create(typeC, rng, 0)!;
            var d = Create(typeD, rng, 0)!;

            static void Set(object target, string propName, object value)
            {
                var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException($"Property '{propName}' not found on {target.GetType().Name}", nameof(propName));
                p.SetValue(target, value);
            }

            Set(a, bPropOnA, b);
            Set(b, cPropOnB, c);
            Set(b, dPropOnB, d);
            Set(c, bPropOnC, b);
            Set(c, dPropOnC, d);
            Set(d, bPropOnD, b);
            Set(d, cPropOnD, c);
            return (a, b, c, d);
        }

        /// <summary>
        ///     Build a diamond graph: root.Left = root.Right = sharedChild.
        /// </summary>
        public static (object Root, object SharedChild) MakeDiamond(
            Type rootType,
            Type childType,
            string leftProp,
            string rightProp,
            Random rng)
        {
            var root = Create(rootType, rng, 0)!;
            var child = Create(childType, rng, 0)!;

            static void Set(object target, string propName, object? value)
            {
                var p = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.Instance) ?? throw new ArgumentException($"Property '{propName}' not found on {target.GetType().Name}", nameof(propName));
                p.SetValue(target, value);
            }

            Set(root, leftProp, child);
            Set(root, rightProp, child);
            return (root, child);
        }

        // ── Private helpers ──────────────────────────────────────────────────────────

        private static object MakeList(Type elemType, Random rng, int depth)
        {
            var listType = typeof(List<>).MakeGenericType(elemType);
            var list = (IList)Activator.CreateInstance(listType)!;
            var n = NextCollectionSize(rng, depth);
            for (var i = 0; i < n; i++)
                list.Add(Create(elemType, rng, depth + 1));
            return list;
        }

        private static object MakeHashSet(Type elemType, Random rng, int depth)
        {
            var setType = typeof(HashSet<>).MakeGenericType(elemType);
            var add = setType.GetMethod("Add")!;
            var set = Activator.CreateInstance(setType)!;
            var n = NextCollectionSize(rng, depth);
            for (var i = 0; i < n; i++)
                add.Invoke(set,
                    new[]
                    {
                        Create(elemType, rng, depth + 1)
                    });
            return set;
        }

        private static object MakeDictionary(Type keyType, Type valType, Random rng, int depth)
        {
            var dictType = typeof(Dictionary<,>).MakeGenericType(keyType, valType);
            var dict = (IDictionary)Activator.CreateInstance(dictType)!;
            var n = depth >= DefaultMaxDepth ? 0 : rng.Next(1, 3); // smaller to avoid duplicate key conflicts
            var seen = new HashSet<object>();
            for (var i = 0; i < n; i++)
            {
                var key = Create(keyType, rng, depth + 1, false); // a null dictionary key throws
                if (key is null || seen.Contains(key))
                {
                    continue;
                }

                seen.Add(key);
                var val = Create(valType, rng, depth + 1);
                dict[key] = val;
            }

            return dict;
        }

        /// <summary>
        ///     Locate the single-arg <c>CreateRange&lt;T&gt;(IEnumerable&lt;T&gt;)</c> overload on
        ///     the given immutable factory class (ImmutableArray / ImmutableList / ImmutableHashSet).
        ///     Returns null if not found.
        /// </summary>
        private static MethodInfo? FindImmutableCreateRange(Type factoryType, Type elemType)
        {
            foreach (var m in factoryType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (m.Name != "CreateRange" || !m.IsGenericMethodDefinition)
                {
                    continue;
                }

                var gargs = m.GetGenericArguments();
                if (gargs.Length != 1)
                {
                    continue;
                }

                var parms = m.GetParameters();
                if (parms.Length != 1)
                {
                    continue;
                }

                // Verify the single parameter is IEnumerable<T>
                var paramType = parms[0].ParameterType;
                if (!paramType.IsGenericType)
                {
                    continue;
                }

                if (paramType.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                {
                    continue;
                }

                return m.MakeGenericMethod(elemType);
            }

            return null;
        }
    }
}
