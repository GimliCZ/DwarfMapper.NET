// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper
{
    /// <summary>
    ///     Declares a ZERO-COPY view from <typeparamref name="TSource" /> shaped as <typeparamref name="TTarget" />:
    ///     the generator emits a nested <c>readonly ref struct &lt;TTarget&gt;View</c> on the mapper class whose
    ///     properties evaluate the same member resolution a <c>Map</c> would — lazily, on access, against the
    ///     source instance. Nothing is allocated and nothing is copied; the view cannot outlive its source (it is a
    ///     <c>ref struct</c>), which is the contract that makes it free. Use it where a mapped DTO is consumed at
    ///     once — serialized, rendered, compared — and <c>Map</c> where it is stored or returned.
    /// </summary>
    [DwarfSurface(SurfaceCategory.ConsumerDirective)]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class GenerateViewAttribute<TSource, TTarget> : Attribute
    {
        /// <summary>Optional view type name; default is the target type's name followed by <c>View</c>.</summary>
        public string? Name { get; set; }
    }
}
