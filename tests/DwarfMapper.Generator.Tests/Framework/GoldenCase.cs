// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.Generator.Tests.Framework;

/// <summary>One pinned corpus case: a stable id, the source to run, and which generator runs it.</summary>
internal sealed record GoldenCase(string Id, string Source, string GeneratorName);
