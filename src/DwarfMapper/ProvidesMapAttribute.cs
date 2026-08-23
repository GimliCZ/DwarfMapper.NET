// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper
{
    /// <summary>
    ///     Registers a <b>hand-written</b> mapping method into the ambient registry, so a shape the generator
    ///     cannot express is still reachable through <see cref="IDwarfMapper" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Some conversions are legitimately not DwarfMapper mapping shapes — an object that <i>holds</i> a
    ///         collection mapped to the collection itself is the common one
    ///         (<c>UserQuotesDocument</c> ↔ <c>ICollection&lt;QuoteData&gt;</c>). Those must be written by hand,
    ///         and once written they are ordinary methods: nothing self-registers them, so every facade call site
    ///         for them throws and the parity harness reports them as "not registered".
    ///     </para>
    ///     <para>
    ///         Round 18 hit this on five pairs and recorded the conclusion plainly:
    ///         <i>
    ///             "The code is fine; the
    ///             harness cannot see it."
    ///         </i>
    ///         This attribute is how a consumer says so.
    ///     </para>
    ///     <para>
    ///         The method must be <c>public</c>, take exactly one parameter, and return a value — the same shape
    ///         the generator's own maps register under. It may be static or an instance method on a mapper with a
    ///         parameterless constructor.
    ///     </para>
    ///     <example>
    ///         <code>
    /// [DwarfMapper]
    /// public partial class QuoteMappers
    /// {
    ///     // Not a mapping SHAPE — a document that holds a collection, mapped to the collection.
    ///     [ProvidesMap]
    ///     public ICollection&lt;QuoteData&gt; ToQuotes(UserQuotesDocument document) =&gt;
    ///         document.Quotes.Select(ToQuote).ToList();
    /// }
    /// 
    /// // …now resolvable from any assembly, with no reference to QuoteMappers:
    /// var quotes = mapper.Map&lt;ICollection&lt;QuoteData&gt;&gt;(document);
    /// </code>
    ///     </example>
    ///     <para>
    ///         This is a deliberate, compile-time-checked declaration — not reflection. The generator emits the
    ///         same module-initializer registration it emits for its own maps, so the AOT and trimming guarantees
    ///         are unchanged.
    ///     </para>
    /// </remarks>
    [DwarfSurface(SurfaceCategory.ConsumerDirective)]
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class ProvidesMapAttribute : Attribute
    {
    }
}
