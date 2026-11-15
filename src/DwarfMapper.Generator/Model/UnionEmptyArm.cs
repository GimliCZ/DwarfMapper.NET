// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Model
{
    /// <summary>
    ///     How a dispatch over <see cref="MapMethodModel.DerivedTypeArms" /> ends after its arms (round 35 T23).
    /// </summary>
    public enum UnionEmptyArm
    {
        /// <summary>
        ///     A <c>[MapDerivedType]</c> dispatch over a class hierarchy: a wildcard arm that throws
        ///     <c>ArgumentException</c> for a runtime type no arm names.
        /// </summary>
        NotUnion,

        /// <summary>
        ///     A union dispatch with no arm after the case arms: the switch is exhaustive over the source union's case
        ///     types, and an empty source cannot be represented in the target, so it throws as the same switch written by
        ///     hand does. (A wildcard after the case arms would be CS8510 once a null arm is there, and is not needed
        ///     before.)
        /// </summary>
        None,

        /// <summary>
        ///     A union dispatch whose empty source - <c>Value</c> null - maps to <c>default</c> of the target: an empty struct
        ///     union, or <c>null</c> for a nullable class union. Emitted as a TYPED <c>null =&gt; default(T)</c> arm,
        ///     because an untyped <c>default</c> takes the arms' common type when they share one (CS8604, measured).
        /// </summary>
        Default,
    }
}
