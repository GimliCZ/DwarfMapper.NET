// SPDX-License-Identifier: GPL-2.0-only

namespace DwarfMapper.IntegrationTests
{
    // One distinct source/destination pair per test, at namespace scope: the registry is process-wide and has
// no reset hook, so isolation comes from distinct keys. Namespace scope rather than nested because this
// project treats CA1034 as an error.
    public sealed class IfSrcA
    {
        public int X { get; set; }
    }

    public sealed class IfDstA
    {
        public int X { get; set; }
    }

    public sealed class IfSrcB
    {
        public int X { get; set; }
    }

    public sealed class IfDstB
    {
        public int X { get; set; }
    }

    public sealed class IfSrcC
    {
        public int X { get; set; }
    }

    public sealed class IfDstC
    {
        public int X { get; set; }
    }

    public sealed class IfSrcD
    {
        public int X { get; set; }
    }

    public sealed class IfDstD
    {
        public int X { get; set; }
    }

    public sealed class IfSrcE
    {
        public int X { get; set; }
    }

    public sealed class IfDstE
    {
        public int X { get; set; }
    }

    public sealed class IfSrcF
    {
        public int X { get; set; }
    }

    public sealed class IfDstF
    {
        public int X { get; set; }
    }

    public sealed class IfSrcG
    {
        public int X { get; set; }
    }

    public sealed class IfDstG
    {
        public int X { get; set; }
    }

    public sealed class IfSrcH
    {
        public int X { get; set; }
    }

    public sealed class IfDstH
    {
        public int X { get; set; }
    }


    /// <summary>
    ///     The ambient registry resolves through the source's <b>interfaces</b>, not only its base chain.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Without this, two independent gates disagreed about which type to key on. The compile-time check
    ///         (<c>DWARF061</c>) validates the call site's <i>static</i> argument type, while the runtime lookup
    ///         used <c>source.GetType()</c> and its base chain only. A repository method declared
    ///         <c>ICollection&lt;T&gt;</c> that returns a <c>List&lt;T&gt;</c> therefore needed <b>both</b> pairs
    ///         declared — declaring only the static one built clean and threw at first use.
    ///     </para>
    ///     <para>
    ///         Round 18 lost a false start to exactly that: a single declared pair turned the DI graph green while
    ///         46 other call sites stayed broken, because the wrong thing had been proved. See
    ///         <c>Issues/Rount18/</c>.
    ///     </para>
    ///     <para>
    ///         Every test uses its own source/destination types. The registry is process-wide and offers no
    ///         reset hook, so isolation comes from distinct keys — the same convention
    ///         <c>AmbientRegistryTests</c> follows.
    ///     </para>
    /// </remarks>
    public sealed class RegistryInterfaceLookupTests
    {
        [Fact]
        public void A_map_registered_on_an_interface_serves_a_concrete_implementation()
        {
            // One registration keyed on IEnumerable<T> now serves List<T>, T[], HashSet<T> and a lazy LINQ
            // iterator alike. That reach is what makes declaring collection shapes affordable at all.
            DwarfMapperRegistry.Register(typeof(IEnumerable<IfSrcA>),
                typeof(List<IfDstA>),
                o => ((IEnumerable<IfSrcA>)o).Select(s => new IfDstA
                {
                    X = s.X
                }).ToList());

            var list = new List<IfSrcA>
            {
                new()
                {
                    X = 1
                },
                new()
                {
                    X = 2
                }
            };

            var mapped = (List<IfDstA>)DwarfMapperRegistry.Map(list, typeof(List<IfDstA>));

            Assert.Equal([1, 2], mapped.Select(d => d.X));
        }

        [Fact]
        public void An_array_is_served_by_the_same_interface_registration()
        {
            DwarfMapperRegistry.Register(typeof(IEnumerable<IfSrcB>),
                typeof(List<IfDstB>),
                o => ((IEnumerable<IfSrcB>)o).Select(s => new IfDstB
                {
                    X = s.X
                }).ToList());

            var mapped = (List<IfDstB>)DwarfMapperRegistry.Map(
                new[]
                {
                    new IfSrcB
                    {
                        X = 7
                    }
                },
                typeof(List<IfDstB>));

            Assert.Equal(7, Assert.Single(mapped).X);
        }

        [Fact]
        public void A_lazy_LINQ_iterator_is_served_too()
        {
            // The case no [GenerateMap] attribute can ever name: Where()/Select() return PRIVATE compiler-
            // generated iterator types. Interface lookup is the only way these can resolve; before it, the remedy
            // was a load-bearing .ToList() at every such call site, discovered one runtime failure at a time.
            DwarfMapperRegistry.Register(typeof(IEnumerable<IfSrcC>),
                typeof(List<IfDstC>),
                o => ((IEnumerable<IfSrcC>)o).Select(s => new IfDstC
                {
                    X = s.X
                }).ToList());

            var lazy = new List<IfSrcC>
            {
                new()
                {
                    X = 1
                },
                new()
                {
                    X = 9
                }
            }.Where(s => s.X > 5);

            var mapped = (List<IfDstC>)DwarfMapperRegistry.Map(lazy, typeof(List<IfDstC>));

            Assert.Equal(9, Assert.Single(mapped).X);
        }

        [Fact]
        public void An_exact_registration_still_wins_over_an_interface_one()
        {
            // Precedence must not change: exact type, then base chain, then interfaces. A more specific
            // registration silently losing to a broader one would be a regression, not a feature.
            DwarfMapperRegistry.Register(typeof(IEnumerable<IfSrcD>),
                typeof(List<IfDstD>),
                _ => new List<IfDstD>
                {
                    new()
                    {
                        X = -1
                    }
                });
            DwarfMapperRegistry.Register(typeof(List<IfSrcD>),
                typeof(List<IfDstD>),
                _ => new List<IfDstD>
                {
                    new()
                    {
                        X = 42
                    }
                });

            var mapped = (List<IfDstD>)DwarfMapperRegistry.Map(new List<IfSrcD>(), typeof(List<IfDstD>));

            Assert.Equal(42, Assert.Single(mapped).X);
        }

        [Fact]
        public void Two_matching_interfaces_throw_rather_than_picking_one()
        {
            // A type implements many interfaces and there is no defined order among them. Choosing arbitrarily
            // would mean the same program mapping through a different map on a different run — the exact class of
            // silent divergence this library exists to refuse.
            DwarfMapperRegistry.Register(typeof(IEnumerable<IfSrcE>),
                typeof(List<IfDstE>),
                _ => new List<IfDstE>
                {
                    new()
                    {
                        X = 1
                    }
                });
            DwarfMapperRegistry.Register(typeof(IReadOnlyCollection<IfSrcE>),
                typeof(List<IfDstE>),
                _ => new List<IfDstE>
                {
                    new()
                    {
                        X = 2
                    }
                });

            var ex = Assert.Throws<DwarfMapMissingException>(() => DwarfMapperRegistry.Map(new List<IfSrcE>(), typeof(List<IfDstE>)));

            Assert.Equal(2, ex.AmbiguousInterfaces.Count);
            Assert.Contains("Ambiguous", ex.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_duplicate_interface_registration_is_marked_but_does_not_poison_lookup()
        {
            // Pins the stated invariant in Register: the InterfaceMaps mirror is appended ONLY on a successful
            // TryAdd. If a duplicate fell through to the mirror as well, the same interface would appear twice in
            // the assignability list, single-candidate resolution would count two candidates for ONE registered
            // pair, and lookup would throw "ambiguous" for a map that is not ambiguous at all — the duplicate is
            // first-wins and merely MARKED, so resolution must keep working with the first delegate.
            DwarfMapperRegistry.Register(typeof(IEnumerable<IfSrcH>),
                typeof(List<IfDstH>),
                o => ((IEnumerable<IfSrcH>)o).Select(s => new IfDstH
                {
                    X = s.X
                }).ToList());
            DwarfMapperRegistry.Register(typeof(IEnumerable<IfSrcH>),
                typeof(List<IfDstH>),
                _ => new List<IfDstH>
                {
                    new()
                    {
                        X = -1
                    }
                });

            Assert.True(DwarfMapperRegistry.IsAmbiguous(typeof(IEnumerable<IfSrcH>), typeof(List<IfDstH>)),
                "sanity: the duplicate itself must still be marked on the create table.");

            var mapped = (List<IfDstH>)DwarfMapperRegistry.Map(
                new List<IfSrcH>
                {
                    new()
                    {
                        X = 5
                    }
                },
                typeof(List<IfDstH>));

            Assert.Equal(5, Assert.Single(mapped).X);
        }

        [Fact]
        public void The_missing_map_message_says_what_lookup_actually_tried()
        {
            var ex = Assert.Throws<DwarfMapMissingException>(() => DwarfMapperRegistry.Map(new IfSrcF(), typeof(IfDstF)));

            Assert.Contains("runtime type, its base types, and its interfaces", ex.Message, StringComparison.Ordinal);
            Assert.Empty(ex.AmbiguousInterfaces);
        }

        [Fact]
        public void A_lazy_iterator_with_no_map_at_all_is_told_to_materialize()
        {
            // Generic "declare the pair" advice is unactionable for a compiler-generated iterator — no attribute
            // can name it. The message has to offer the two things that actually work.
            var lazy = new List<IfSrcG>
            {
                new()
            }.Where(_ => true);

            var ex = Assert.Throws<DwarfMapMissingException>(() => DwarfMapperRegistry.Map(lazy, typeof(IfDstG)));

            Assert.Contains(".ToList()", ex.Message, StringComparison.Ordinal);
            Assert.Contains("IEnumerable<T>", ex.Message, StringComparison.Ordinal);
            // The remedy's DIAGNOSIS half: the message must say WHY the generic declare advice does not apply
            // here — the reader is holding a type no attribute can name, and the sentence saying so is the only
            // thing that stops them trying. (E3-E1 hole 11 remainder: this fragment could be blanked unnoticed.)
            Assert.Contains("is a compiler-generated LINQ iterator — no [GenerateMap] attribute can name it.",
                ex.Message,
                StringComparison.Ordinal);
            // And the interface-declaration alternative must be a COMPLETE, pasteable attribute — through the
            // closing ">]." — not a fragment that trails off mid-generic-argument.
            Assert.Contains("[GenerateMap<IEnumerable<T>, IfDstG>].", ex.Message, StringComparison.Ordinal);
        }
    }
}
