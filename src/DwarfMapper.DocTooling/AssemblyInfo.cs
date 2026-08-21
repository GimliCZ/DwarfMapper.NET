// SPDX-License-Identifier: GPL-2.0-only

using System.Runtime.CompilerServices;

// Exposes the internal seams (ExampleCatalogue.Build's file-list parameter, the IsNotBuildOutput
// path predicates) to the test project so their refusal paths can be exercised with synthetic
// inputs instead of the live Gallery corpus, which is well-formed by construction and can never
// reach them. Same pattern as the runtime and generator assemblies.
[assembly: InternalsVisibleTo("DwarfMapper.Generator.Tests")]
