// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>An after-map hook invocation: <c>Name(target)</c> or <c>Name(source, target)</c>.</summary>
public sealed record HookCall(string Name, bool TakesSource) : System.IEquatable<HookCall>;
