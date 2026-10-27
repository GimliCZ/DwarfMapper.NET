// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using Xunit.Abstractions;

namespace DwarfMapper.Generator.Tests.SelfValidation
{
    /// <summary>
    ///     <b>Every theory row is a test case of its own.</b> xunit runs a row as its own test case only when it can
    ///     serialize the row's data. When it cannot, the whole theory is ONE test case that reports a result per row.
    ///     <para>
    ///         Round 34 measured what that costs in the mutation legs. Stryker's coverage collector closes a test's
    ///         window at its first result, so rows two to N of such a theory ran the generator outside every window,
    ///         and the collector reported what they reached as covered outside any test. Stryker tests such a mutant
    ///         against the WHOLE suite. Five theories over records (four over <c>MatrixCell</c>, one over
    ///         <c>FimMatrixCase</c>, 1,919 extra results in all) made 160 of the generator leg's 415 mutants and 178 of
    ///         the pipeline leg's 314 static: the hot helpers every mapping runs, each run against all ~9,700 tests
    ///         instead of the handful that cover it. Their rows now carry a key the test resolves.
    ///     </para>
    ///     <para>
    ///         So a theory parameter must be a type xunit serializes: a primitive, <c>string</c>, <c>decimal</c>, an
    ///         enum, <c>Type</c>, an <see cref="IXunitSerializable" />, or a nullable or array of one of those.
    ///     </para>
    /// </summary>
    public sealed class TheoryRowsAreTestCasesScanTests
    {
        [Fact]
        public void Every_theory_parameter_is_a_type_xunit_serializes()
        {
            var theories = typeof(TheoryRowsAreTestCasesScanTests).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttributes<TheoryAttribute>(inherit: false).Any())
                .ToList();

            // Non-vacuity: the suite has hundreds of theories, and a scan that found none read the wrong assembly.
            Assert.True(theories.Count >= 100, $"found only {theories.Count} theory method(s); this scan is reading the wrong thing.");

            var offenders = theories
                .SelectMany(m => m.GetParameters().Where(p => !Serializable(p.ParameterType))
                    .Select(p => $"{m.DeclaringType?.Name}.{m.Name}({p.ParameterType.Name} {p.Name})"))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();

            Assert.True(offenders.Count == 0,
                "theory parameter(s) xunit cannot serialize, so each such theory runs as ONE test case and its later rows " +
                "run outside every coverage window: [" + string.Join(", ", offenders) + "]. Pass a key the test resolves " +
                "(see MatrixCell.Key), or make the type IXunitSerializable.");
        }

        [Theory]
        [InlineData(typeof(int), true)]
        [InlineData(typeof(string), true)]
        [InlineData(typeof(Fuzzing.MatrixVariant), true)]
        [InlineData(typeof(int?[]), true)]
        [InlineData(typeof(Fuzzing.MatrixCell), false)]
        [InlineData(typeof(object), false)]
        public void The_rule_tells_a_serializable_parameter_from_one_that_folds_the_theory(Type type, bool serializable)
        {
            ArgumentNullException.ThrowIfNull(type);

            Assert.Equal(serializable, Serializable(type));
        }

        private static bool Serializable(Type type)
        {
            if (type.IsArray && type.GetElementType() is { } element)
            {
                return Serializable(element);
            }

            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            return underlying.IsPrimitive || underlying.IsEnum || underlying == typeof(string) || underlying == typeof(decimal) ||
                   underlying == typeof(Type) || typeof(IXunitSerializable).IsAssignableFrom(underlying);
        }
    }
}
