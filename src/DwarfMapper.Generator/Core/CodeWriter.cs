// SPDX-License-Identifier: GPL-2.0-only

using System.Text;

namespace DwarfMapper.Generator.Core;

/// <summary>
///     Builds generated source with indentation and line endings handled once, instead of hand-counted in
///     string literals. The codebase had 439 hard-coded indent runs and 247 literal <c>\n</c> escapes inside
///     emitted strings; that layering is what makes emitters hard to edit correctly.
///     <para>
///     Line endings are always LF, never <c>Environment.NewLine</c>: generated output is LF-normalised
///     repo-wide (audit ISSUE-024) so fingerprints stay identical across Windows and Linux. Using the platform
///     newline here would reintroduce exactly the divergence that fix removed.
///     </para>
/// </summary>
internal sealed class CodeWriter
{
    private const string IndentUnit = "    ";

    private readonly StringBuilder _sb = new();
    private int _level;

    public CodeWriter(int initialIndent = 0)
    {
        _level = initialIndent;
    }

    /// <summary>Writes an indented line terminated by LF.</summary>
    public CodeWriter Line(string text)
    {
        for (var i = 0; i < _level; i++) _sb.Append(IndentUnit);
        _sb.Append(text).Append('\n');
        return this;
    }

    /// <summary>
    ///     Writes a blank line. Deliberately emits no indentation — current generated output carries no
    ///     trailing whitespace, and adding it would change bytes on nearly every generated file.
    /// </summary>
    public CodeWriter Line()
    {
        _sb.Append('\n');
        return this;
    }

    /// <summary>
    ///     Appends text verbatim: no indentation, no terminator. For splicing blocks that already carry their
    ///     own formatting, such as the synthesized helper bodies appended at <c>MapEmitter.cs:44</c>.
    /// </summary>
    public CodeWriter Raw(string text)
    {
        _sb.Append(text);
        return this;
    }

    /// <summary>Indents one level until the returned scope is disposed.</summary>
    public IDisposable Indent()
    {
        _level++;
        return new Scope(this, closeBrace: false);
    }

    /// <summary>Writes <paramref name="header" />, then <c>{</c>, indents, and closes with <c>}</c> on dispose.</summary>
    public IDisposable Block(string header)
    {
        Line(header);
        Line("{");
        _level++;
        return new Scope(this, closeBrace: true);
    }

    public override string ToString()
    {
        return _sb.ToString();
    }

    private sealed class Scope : IDisposable
    {
        private readonly bool _closeBrace;
        private readonly CodeWriter _writer;
        private bool _disposed;

        public Scope(CodeWriter writer, bool closeBrace)
        {
            _writer = writer;
            _closeBrace = closeBrace;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _writer._level--;
            if (_closeBrace) _writer.Line("}");
        }
    }
}
