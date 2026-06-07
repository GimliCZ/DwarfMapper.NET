// SPDX-License-Identifier: GPL-2.0-only
namespace DwarfMapper.Generator.Model;

/// <summary>A discovered round-trip pair to emit a verifier for.</summary>
public sealed record RoundTripPair(
    string ForwardName,
    string BackwardName,
    string SourceTypeFullName,
    string DtoTypeFullName) : System.IEquatable<RoundTripPair>;
