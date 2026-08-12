// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper;

/// <summary>
///     Declares that the <c>(TSource → TTarget)</c> pair restates the configuration of the pair for their base
///     types, so the two can be checked against each other and drift is reported instead of discovered.
/// </summary>
/// <remarks>
///     <para>
///         DwarfMapper has no inheritance primitive — no <c>IncludeBase</c>. Every pair's configuration stays
///         literally visible at its own declaration, which is a deliberate choice: a reader of one pair never
///         has to go and find what some other pair decided on its behalf. The cost is restatement, and that
///         cost splits in two. <b>Typing it</b> is mechanical, annoying, and over once. <b>Drifting from the
///         base later</b> is silent, and it only ever drifts toward wrong data — a base pair gains a
///         <c>[MapProperty(Use = …)]</c> and the derived one quietly keeps mapping the raw value.
///     </para>
///     <para>
///         This attribute kills the second half. It changes no emitted code whatsoever; it exists so the
///         generator has something to check. Applied, the two pairs' resolved mappings are compared member by
///         member, and a member the base configures explicitly but the derived pair treats differently is
///         <c>DWARF085</c>.
///     </para>
///     <para>
///         The base pair is <b>inferred</b> from the type hierarchy: the most-derived pair declared on the same
///         mapper whose source is a base of <typeparamref name="TSource" /> and whose target is a base of
///         <typeparamref name="TTarget" />. Naming it would be a fourth type parameter that could only ever
///         disagree with the hierarchy. If no such pair is declared, or two are equally close, that is
///         <c>DWARF084</c> — refused rather than skipped, because a check the author asked for and did not get
///         is exactly the risk being guarded against.
///     </para>
///     <code>
/// [DwarfMapper]
/// [GenerateMap&lt;Command, CommandDto&gt;]
/// [MapProperty&lt;Command, CommandDto&gt;(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
///
/// [GenerateMap&lt;AliasCommand, AliasCommandDto&gt;]
/// [RestatesBase&lt;AliasCommand, AliasCommandDto&gt;]
/// [MapProperty&lt;AliasCommand, AliasCommandDto&gt;(nameof(Command.Raw), nameof(CommandDto.Text), Use = nameof(Clean))]
/// public partial class Mappers;   // drop the second [MapProperty] and DWARF085 says so
/// </code>
/// </remarks>
/// <typeparam name="TSource">The source type of the DERIVED pair — the one doing the restating.</typeparam>
/// <typeparam name="TTarget">The destination type of the DERIVED pair.</typeparam>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RestatesBaseAttribute<TSource, TTarget> : Attribute
{
    /// <summary>
    ///     Destination member names this pair deliberately configures differently from the base.
    /// </summary>
    /// <remarks>
    ///     An override is a decision, and a decision should be visible at the point it is made. Listing the
    ///     member here is that statement — it exempts exactly that member from the drift check and leaves
    ///     every other one guarded, which is what separates "we meant this" from "we forgot".
    /// </remarks>
    public string[] Overrides { get; set; } = [];
}
