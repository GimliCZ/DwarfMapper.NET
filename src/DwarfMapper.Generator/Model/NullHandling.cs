// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Model
{
    /// <summary>How the emitter handles a possibly-null source value on its way to the destination.</summary>
    public enum NullHandling
    {
        /// <summary>Assign directly.</summary>
        None = 0,

        /// <summary>Emit <c>x ?? throw</c>.</summary>
        ThrowIfNull = 1,

        /// <summary>Emit <c>x.GetValueOrDefault()</c>.</summary>
        ValueOrDefault = 2,

        /// <summary>
        ///     Source is <c>Nullable&lt;T&gt;</c> and the target can hold null (it is <c>Nullable&lt;U&gt;</c> or a
        ///     nullable-annotated reference); emit <c>x.HasValue ? Conv(x.Value) : null</c> (null-preserving).
        /// </summary>
        NullableProject = 3,

        /// <summary>
        ///     The reference-source mirror of <see cref="NullableProject" />: the source is a possibly-null
        ///     REFERENCE and the target is <c>Nullable&lt;U&gt;</c>; emit <c>x is null ? null : Conv(x)</c>
        ///     (null-preserving). Without it the converter — a synthesized nested mapper whose return type is
        ///     a value type, so it cannot answer null — throws on a null the destination could have held.
        /// </summary>
        NullableProjectRef = 4,

        /// <summary>
        ///     <see cref="NullableProjectRef" /> where the DESTINATION'S ANNOTATION forbids the null being
        ///     preserved: emit <c>x is null ? null! : Conv(x)</c>. Same lift, same runtime semantics — the one
        ///     difference is the null-forgiving operator on the null arm, without which the C# compiler raises
        ///     CS8601 <b>inside the generated file</b>, where no consumer <c>#pragma</c>, <c>NoWarn</c> or
        ///     <c>.editorconfig</c> can reach it.
        ///     <para>
        ///         Reached when BOTH ends are non-nullable-annotated references and the converter is a
        ///         user-declared map method that refuses null — the shape a <c>Result&lt;T&gt;</c> whose failure
        ///         arm stores <c>default!</c> produces. The annotation says the payload cannot be null and the
        ///         value is null anyway, so the emitter mirrors what the synthesized object helper it stands in
        ///         for has always done (<c>if (s is null) return null!;</c>) rather than letting the callee's
        ///         <c>ArgumentNullException.ThrowIfNull</c> fire. It is a SEPARATE enum value rather than a flag
        ///         on <see cref="NullableProjectRef" /> so that every already-emitted lift — all of which have a
        ///         null-capable destination — keeps its exact text: a <c>!</c> where none is needed is noise in
        ///         a file the reader cannot edit.
        ///     </para>
        /// </summary>
        NullableProjectRefForgiving = 5
    }
}
