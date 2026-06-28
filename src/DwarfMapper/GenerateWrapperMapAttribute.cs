// SPDX-License-Identifier: GPL-2.0-only
using System;

namespace DwarfMapper;

/// <summary>
/// Opt-in: for every <c>[GenerateMap&lt;A, B&gt;]</c> declared on the same <c>[DwarfMapper]</c> class, also
/// synthesize a map for the closed wrapper instantiation <c>W&lt;A&gt; -&gt; W&lt;B&gt;</c> (where <c>W</c> is
/// the supplied open generic wrapper). This removes the boilerplate of declaring a wrapper map per payload
/// pair for single-payload generic envelope families (<c>Result&lt;T&gt;</c>, <c>Page&lt;T&gt;</c>,
/// <c>Envelope&lt;T&gt;</c>, …).
/// <para>
/// <b>Closed instantiations only.</b> One concrete mapper is emitted per <i>used</i> (A, B) pair — open
/// generics are never emitted, so the result stays NativeAOT- and trim-safe. The wrapper must be a generic
/// type with exactly one type parameter and a single payload member of that parameter's type (other members
/// are copied/converted as usual). A wrapper that does not qualify is reported as <c>DWARF067</c>.
/// </para>
/// <code>
/// public sealed class Envelope&lt;T&gt; { public T Payload { get; set; } = default!; public int Status { get; set; } }
///
/// [DwarfMapper]
/// [GenerateMap&lt;User, UserDto&gt;]
/// [GenerateWrapperMap(typeof(Envelope&lt;&gt;))]   // also synthesizes Envelope&lt;User&gt; -&gt; Envelope&lt;UserDto&gt;
/// public partial class Mappers { }
///
/// Envelope&lt;UserDto&gt; dto = new Mappers().Map(envelopeOfUser);
/// </code>
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class GenerateWrapperMapAttribute : Attribute
{
    /// <summary>Creates the opt-in for an open generic wrapper, e.g. <c>typeof(Envelope&lt;&gt;)</c>.</summary>
    /// <param name="wrapper">The open generic wrapper type (a single-type-parameter generic).</param>
    public GenerateWrapperMapAttribute(Type wrapper) => Wrapper = wrapper;

    /// <summary>The open generic wrapper type the family is generated for.</summary>
    public Type Wrapper { get; }
}
