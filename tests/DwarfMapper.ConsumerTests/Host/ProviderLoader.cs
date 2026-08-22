// SPDX-License-Identifier: GPL-2.0-only

using System.Reflection;
using System.Runtime.CompilerServices;

namespace ConsumerTests.Host;

/// <summary>
///     Loads the provider assemblies BY PATH and runs their module initializers.
/// </summary>
/// <remarks>
///     <para>
///         The host does not reference <c>ProviderA</c>/<c>ProviderB</c> at compile time — that is the whole
///         point of this project — so it cannot name their types to force registration the way an in-assembly
///         test would. Loading by path is what a plugin host, or a service whose providers live in sibling
///         assemblies, actually does.
///     </para>
///     <para>
///         Module initializers run lazily, so merely loading the assembly is not enough: nothing has touched a
///         type in it yet. <see cref="RuntimeHelpers.RunModuleConstructor" /> forces the registration that a
///         real app gets when its composition root first touches the provider.
///     </para>
/// </remarks>
internal static class ProviderLoader
{
    private static readonly Lock Gate = new();
    private static bool _loaded;

    /// <summary>Idempotent: the registry is process-wide and re-registering would read as ambiguity.</summary>
    internal static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded) return;

            foreach (var name in new[] { "ConsumerTests.ProviderA", "ConsumerTests.ProviderB" })
            {
                var path = Path.Combine(AppContext.BaseDirectory, name + ".dll");

                Assert.True(File.Exists(path),
                    $"Provider assembly '{name}.dll' was not copied next to the host. The ProjectReference "
                    + "must stay (with ReferenceOutputAssembly=\"false\") so it is BUILT and copied without "
                    + "being visible to the compiler.");

                var assembly = Assembly.LoadFrom(path);
                RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            }

            _loaded = true;
        }
    }

    /// <summary>
    ///     Proves the host really cannot see the provider types at compile time — so a future refactor that
    ///     quietly adds a reference cannot make these tests pass for the wrong reason.
    /// </summary>
    internal static bool HostReferencesProviders()
    {
        return typeof(ProviderLoader).Assembly
            .GetReferencedAssemblies()
            .Any(a => a.Name?.StartsWith("ConsumerTests.Provider", StringComparison.Ordinal) == true);
    }
}
