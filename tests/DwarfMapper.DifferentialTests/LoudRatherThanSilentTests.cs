// SPDX-License-Identifier: GPL-2.0-only

using AutoMapper;

namespace DwarfMapper.DifferentialTests
{
    /// <summary>
    ///     The axis on which DwarfMapper deliberately differs from both oracles: what happens when a value has no
    ///     mapping, and nobody said what to do about it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="ShapeAgreementTests" /> asks where the three mappers AGREE. This asks what each does
    ///         with a value nobody declared an answer for, on the two axes where that question arises: an enum
    ///         value matching no destination member, and a runtime type matching no dispatch arm.
    ///     </para>
    ///     <para>
    ///         The result is not the one the inventory that prompted these tests predicted. DwarfMapper and
    ///         MAPPERLY both refuse, by default, on both axes — two independently designed source generators
    ///         reaching the same answer, which is a stronger argument for that answer than either makes alone.
    ///         AutoMapper substitutes and carries on. What Mapperly has and DwarfMapper does not is the OPT-OUT:
    ///         <c>FallbackValue</c> names what an unmatched enum value should become.
    ///     </para>
    ///     <para>
    ///         Neither stance is universally right — a fallback is what you want in a display path and a refusal
    ///         is what you want in a persistence path — which is exactly why this belongs in
    ///         <c>docs/COMPARISON.md</c> as a stated difference rather than in a changelog as a fix. This file is
    ///         what makes those rows executable: they fail here the day they stop being true, in either
    ///         direction.
    ///     </para>
    ///     <para>
    ///         These cannot be <see cref="ShapeCatalog" /> entries. That method is enumerated eagerly and its
    ///         results compared member by member; a shape that throws would take every comparison in the class
    ///         down with it, and "they differ by an exception" is not a member-by-member difference anyway.
    ///     </para>
    /// </remarks>
    public class LoudRatherThanSilentTests
    {
        private static readonly DwarfShapes Dwarf = new();

        private static readonly MapperlyShapes Mapperly = new();

        private static readonly DwarfEnumByName DwarfByName = new();

        private static readonly IMapper Auto = new MapperConfiguration(c =>
        {
            c.CreateMap<Command, CommandDto>().Include<AliasCommand, AliasCommandDto>();
            c.CreateMap<AliasCommand, AliasCommandDto>();
            c.CreateMap<Level, LevelDto>();
        }).CreateMapper();

        [Fact]
        public void A_base_instance_nobody_declared_is_a_build_error_here_a_throw_in_Mapperly_and_silent_in_AutoMapper()
        {
            // Mapperly's mapper registers only AliasCommand. A plain Command reaching it is a case the author did
            // not declare, and both source generators refuse it - at different times.
            var plain = new Command
            {
                Name = "pwd"
            };

            // DwarfMapper refuses it in the BUILD since round 35: Command can be instantiated, so a dispatch over it
            // with no arm for Command itself is DWARF119 (held by NegativeCases and DispatchBaseArmTests - a
            // declaration that does not compile cannot sit in this assembly). DwarfShapes therefore declares the
            // arm, and what it returns is what its author wrote down, not a fallback nobody chose. Until then it
            // threw ArgumentException here, as Mapperly does below.
            var dwarf = Dwarf.ToCommand(plain);
            Assert.Equal(typeof(CommandDto), dwarf.GetType());
            Assert.Equal("pwd", dwarf.Name);

            // Mapperly refuses at run time — worth asserting rather than assuming, since the first draft of this
            // test claimed it fell back and was wrong.
            var mapperly = Assert.Throws<ArgumentException>(() => Mapperly.ToCommand(plain));
            Assert.Contains("derived type", mapperly.Message, StringComparison.OrdinalIgnoreCase);

            // AutoMapper maps the base as itself and says nothing. In THIS shape that is even the answer most
            // people want; the point is that it happens whether or not anybody considered the case, and the
            // shape where it is wrong looks identical from the call site.
            Assert.Equal(typeof(CommandDto), Auto.Map<CommandDto>(plain).GetType());
            Assert.Equal("pwd", Auto.Map<CommandDto>(plain).Name);
        }

        [Fact]
        public void An_undefined_enum_value_throws_here_and_Mapperly_can_be_told_not_to()
        {
            // The names are complete on both sides, so no build-time check fires on either generator. This is
            // the value that CANNOT be checked at build time — a cast, a database column, a wire format — and it
            // is where enums actually go wrong.
            const Level undefined = (Level)99;

            Assert.Throws<ArgumentOutOfRangeException>(() => DwarfByName.Map(undefined));

            // Mapperly's default agrees. Its FallbackValue is the capability DwarfMapper has no equivalent of:
            // the same by-name mapping, told what to do with a value it cannot match.
            Assert.Throws<ArgumentOutOfRangeException>(() => Mapperly.ToLevel(undefined));
            Assert.Equal(LevelDto.Low, Mapperly.ToLevelWithFallback(undefined));

            // AutoMapper maps enums by VALUE, so an undefined 99 arrives as an undefined 99 — no exception, no
            // fallback, and a destination variable holding a value its own type never declared.
            Assert.Equal((LevelDto)99, Auto.Map<LevelDto>(undefined));

            // And the defined values are unremarkable on all three, which is what makes the difference above a
            // difference about UNMATCHED values rather than about enum mapping in general.
            Assert.Equal(LevelDto.High, DwarfByName.Map(Level.High));
            Assert.Equal(LevelDto.High, Mapperly.ToLevel(Level.High));
            Assert.Equal(LevelDto.High, Auto.Map<LevelDto>(Level.High));
        }

        [Fact]
        public void The_arm_that_IS_registered_agrees_with_both_oracles()
        {
            // The negative half: the difference above is about the UNDECLARED case only. Where the author did
            // declare the arm, all three dispatch identically — asserted so "DwarfMapper is stricter" cannot be
            // quietly read as "DwarfMapper dispatches differently".
            var alias = new AliasCommand
            {
                Name = "ll",
                Alias = "ls -l"
            };

            Assert.Equal(typeof(AliasCommandDto), Dwarf.ToCommand(alias).GetType());
            Assert.Equal(typeof(AliasCommandDto), Mapperly.ToCommand(alias).GetType());
            Assert.Equal(typeof(AliasCommandDto), Auto.Map<CommandDto>(alias).GetType());
            Assert.Equal("ls -l", ((AliasCommandDto)Dwarf.ToCommand(alias)).Alias);
        }
    }
}
