// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.DocTooling;

/// <summary>
///     The one failure type the doc pipeline raises. Every message must name the offending
///     <c>file:line</c>, because these fire during a test run where the only diagnostic anyone sees is the
///     exception text.
/// </summary>
/// <remarks>
///     Derives from <see cref="InvalidOperationException" /> rather than <see cref="Exception" /> so that
///     <see cref="RepoLayout.Root" /> can report an unusable working tree from a property getter without
///     tripping CA1065. That is also the accurate base: every one of these means the pipeline was asked to
///     run against a repository state it cannot work in.
/// </remarks>
public sealed class DocToolingException : InvalidOperationException
{
    public DocToolingException(string message) : base(message)
    {
    }

    public DocToolingException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public DocToolingException()
    {
    }
}
