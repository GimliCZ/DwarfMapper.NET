// SPDX-License-Identifier: GPL-2.0-only

using System.Globalization;

namespace DwarfMapper.Testing.Tests
{
    public class Person
    {
        public int Age { get; set; }

        public string Name { get; set; } = "";
    }

    public class PersonDto
    {
        public int Age { get; set; }

        public string Name { get; set; } = "";
    }

    [DwarfMapper]
    public partial class PersonRoundTripMapper
    {
        public partial PersonDto ToDto(Person p);
        public partial Person FromDto(PersonDto d);
    }

// A deliberately lossy mapper to prove failures are caught.
    public class LossyDto
    {
        public int Age { get; set; }

        public string Name { get; set; } = "";
    }

    public class RoundTripTests
    {
        [Fact]
        public void Fuzzer_yields_requested_count()
        {
            var items = new List<Person>(Fuzzer.Generate<Person>(5, 3));
            Assert.Equal(5, items.Count);
        }

        [Fact]
        public void Lossless_roundtrip_passes()
        {
            var m = new PersonRoundTripMapper();
            // Assert.Null(exception) makes the assertion explicit: the verifier must not throw.
            var ex = Record.Exception(() =>
                RoundTrip.Verify<Person, PersonDto>(m.ToDto, m.FromDto, 7, 50));
            Assert.Null(ex);
        }

        [Fact]
        public void Lossy_roundtrip_throws_with_informed_dump()
        {
            // forward drops Name; backward cannot restore it -> round-trip mismatch.
            Func<Person, LossyDto> forward = p => new LossyDto
            {
                Age = p.Age,
                Name = ""
            };
            Func<LossyDto, Person> backward = d => new Person
            {
                Age = d.Age,
                Name = d.Name
            };
            var ex = Assert.Throws<RoundTripException>(() =>
                RoundTrip.Verify(forward, backward, 1, 20));
            Assert.Contains("Name", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Verify_rejects_a_null_mapper_by_name()
        {
            var m = new PersonRoundTripMapper();

            Assert.Equal("forward",
                Assert.Throws<ArgumentNullException>(
                    () => RoundTrip.Verify<Person, PersonDto>(null!, m.FromDto, 7, 1)).ParamName);
            Assert.Equal("backward",
                Assert.Throws<ArgumentNullException>(
                    () => RoundTrip.Verify<Person, PersonDto>(m.ToDto, null!, 7, 1)).ParamName);
        }

        [Fact]
        public void A_failure_carries_the_seed_that_replays_it_and_the_diffs_the_replay_produces()
        {
            // The exception's Seed is the ITEM seed (not the run seed): ObjectFactoryV2.Create<T>(ex.Seed) must
            // rebuild the exact instance that failed, and comparing that instance's round-trip must reproduce
            // ex.Diffs. Otherwise the "informed dump" points at a case nobody can re-run.
            Func<Person, LossyDto> forward = p => new LossyDto
            {
                Age = p.Age,
                Name = ""
            };
            Func<LossyDto, Person> backward = d => new Person
            {
                Age = d.Age,
                Name = d.Name
            };
            var ex = Assert.Throws<RoundTripException>(() =>
                RoundTrip.Verify(forward, backward, 1, 20));

            Assert.InRange(ex.Iteration, 0, 19);
            Assert.NotEmpty(ex.Diffs);
            Assert.Contains(ex.Diffs, d => d.Path.EndsWith(".Name", StringComparison.Ordinal));

            var replay = ObjectFactoryV2.Create<Person>(ex.Seed);
            var replayed = StructuralComparer.Diff(replay, backward(forward(replay)));

            Assert.Equal(
                ex.Diffs.Select(d => (d.Path, d.Expected, d.Actual)),
                replayed.Select(d => (d.Path, d.Expected, d.Actual)));
            Assert.Contains("[seed: " + ex.Seed.ToString(CultureInfo.InvariantCulture) + ", iteration: " +
                            ex.Iteration.ToString(CultureInfo.InvariantCulture) + "]",
                ex.Message,
                StringComparison.Ordinal);
        }
    }
}
