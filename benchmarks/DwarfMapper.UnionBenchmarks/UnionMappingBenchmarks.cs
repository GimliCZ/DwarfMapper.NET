// SPDX-License-Identifier: GPL-2.0-only

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Mapster;

namespace DwarfMapper.UnionBenchmarks
{
    /// <summary>
    ///     Payments, in both shapes. The questions, one per comparison the categories make possible:
    ///     <list type="number">
    ///         <item>What does DwarfMapper's union map cost over the switch a developer writes by hand? (Union_DwarfMapper
    ///         against the baseline, Union_Hand; Union_HandCaseMethods, the control, is the same switch hand-written in
    ///         the generated code's structure - a public method per case, each checking its argument - so the part of
    ///         the difference that is that structure can be told from the rest.)</item>
    ///         <item>Is a union slower or faster to map than the class hierarchy that holds the same data? (Hierarchy_Hand
    ///         against Union_Hand: the same data, the same leaf types, only the container differs.)</item>
    ///         <item>On the hierarchy, where the other libraries can take part, how do they compare? (The Hierarchy_ arms
    ///         against Hierarchy_Hand.)</item>
    ///     </list>
    ///     <para>
    ///         Mapperly and Mapster appear on the hierarchy only: neither maps a C# 15 union (what each does with one is
    ///         recorded in the results document). AutoMapper is not referenced by this project at all - see the project
    ///         file.
    ///     </para>
    ///     <para>
    ///         Single payments cycle through a ring of distinct draws (the main suite's rule since round 26: no benchmark
    ///         maps one static object). A batch is one list of <see cref="BatchSize" /> payments, mapped whole.
    ///     </para>
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class PaymentBenchmarks
    {
        public const int BatchSize = 1000;

        private const string One = "One payment";
        private const string Batch = "Batch of 1000";

        private readonly DwarfPaymentMapper _dwarf = new();
        private readonly HandWrittenCaseMethods _handCaseMethods = new();
        private readonly DwarfPaymentNodeMapper _dwarfNodes = new();
        private readonly MapperlyPaymentNodeMapper _mapperly = new();
        private TypeAdapterConfig _mapster = null!;

        private Payment[] _unionRing = null!;
        private PaymentNode[] _nodeRing = null!;
        private PaymentBatch _unionBatch = null!;
        private PaymentNodeBatch _nodeBatch = null!;
        private int _ring;

        [GlobalSetup]
        public void Setup()
        {
            (_unionRing, _nodeRing) = Payloads.Payments(Payloads.RingSize, 1);
            var (unions, nodes) = Payloads.Payments(BatchSize, 2);
            _unionBatch = new PaymentBatch(new List<Payment>(unions));
            _nodeBatch = new PaymentNodeBatch(new List<PaymentNode>(nodes));
            Payloads.AssertRealistic(_unionRing, "the single-payment ring");
            Payloads.AssertRealistic(unions, "the batch");
            _mapster = MapsterPaymentNodeConfig.Create();
        }

        /// <summary>For the dry run: every arm starts from the same ring slot, so their results line up.</summary>
        public void ResetRing()
        {
            _ring = 0;
        }

        private int Next()
        {
            return _ring++ & (Payloads.RingSize - 1);
        }

        // ── One payment ─────────────────────────────────────────────────────────────────────────────────────────────

        [Benchmark(Baseline = true)]
        [BenchmarkCategory(One)]
        public PaymentDto One_Union_Hand()
        {
            return HandWritten.ToDto(_unionRing[Next()]);
        }

        [Benchmark]
        [BenchmarkCategory(One)]
        public PaymentDto One_Union_HandCaseMethods()
        {
            return _handCaseMethods.ToDto(_unionRing[Next()]);
        }

        [Benchmark]
        [BenchmarkCategory(One)]
        public PaymentDto One_Union_DwarfMapper()
        {
            return _dwarf.ToDto(_unionRing[Next()]);
        }

        [Benchmark]
        [BenchmarkCategory(One)]
        public PaymentNodeDto One_Hierarchy_Hand()
        {
            return HandWritten.ToDto(_nodeRing[Next()]);
        }

        [Benchmark]
        [BenchmarkCategory(One)]
        public PaymentNodeDto One_Hierarchy_DwarfMapper()
        {
            return _dwarfNodes.ToDto(_nodeRing[Next()]);
        }

        [Benchmark]
        [BenchmarkCategory(One)]
        public PaymentNodeDto One_Hierarchy_Mapperly()
        {
            return _mapperly.ToDto(_nodeRing[Next()]);
        }

        [Benchmark]
        [BenchmarkCategory(One)]
        public PaymentNodeDto One_Hierarchy_Mapster()
        {
            return _nodeRing[Next()].Adapt<PaymentNode, PaymentNodeDto>(_mapster);
        }

        // ── A batch ─────────────────────────────────────────────────────────────────────────────────────────────────

        [Benchmark(Baseline = true)]
        [BenchmarkCategory(Batch)]
        public PaymentBatchDto Batch_Union_Hand()
        {
            return HandWritten.ToDto(_unionBatch);
        }

        [Benchmark]
        [BenchmarkCategory(Batch)]
        public PaymentBatchDto Batch_Union_HandCaseMethods()
        {
            return _handCaseMethods.ToDto(_unionBatch);
        }

        [Benchmark]
        [BenchmarkCategory(Batch)]
        public PaymentBatchDto Batch_Union_DwarfMapper()
        {
            return _dwarf.ToDto(_unionBatch);
        }

        [Benchmark]
        [BenchmarkCategory(Batch)]
        public PaymentNodeBatchDto Batch_Hierarchy_Hand()
        {
            return HandWritten.ToDto(_nodeBatch);
        }

        [Benchmark]
        [BenchmarkCategory(Batch)]
        public PaymentNodeBatchDto Batch_Hierarchy_DwarfMapper()
        {
            return _dwarfNodes.ToDto(_nodeBatch);
        }

        [Benchmark]
        [BenchmarkCategory(Batch)]
        public PaymentNodeBatchDto Batch_Hierarchy_Mapperly()
        {
            return _mapperly.ToDto(_nodeBatch);
        }

        [Benchmark]
        [BenchmarkCategory(Batch)]
        public PaymentNodeBatchDto Batch_Hierarchy_Mapster()
        {
            return _nodeBatch.Adapt<PaymentNodeBatch, PaymentNodeBatchDto>(_mapster);
        }
    }

    /// <summary>
    ///     Value cases: an <c>int</c> case widened into a <c>long</c> case, <c>double</c> and <c>string</c> passed through.
    ///     A value case lives boxed in the union's <c>Value</c>, so mapping one unboxes it and boxes the result - the cost
    ///     a hand-written switch pays too, which is the baseline. No other library maps a union, and a class hierarchy
    ///     cannot hold an <c>int</c>, so there is no third arm.
    /// </summary>
    [MemoryDiagnoser]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    [CategoriesColumn]
    public class ReadingBenchmarks
    {
        public const int BatchSize = 1000;

        private const string One = "One reading";
        private const string Batch = "Batch of 1000";

        private readonly DwarfReadingMapper _dwarf = new();
        private Reading[] _ring = null!;
        private ReadingBatch _batch = null!;
        private int _next;

        [GlobalSetup]
        public void Setup()
        {
            _ring = Payloads.Readings(Payloads.RingSize, 3);
            _batch = new ReadingBatch(new List<Reading>(Payloads.Readings(BatchSize, 4)));
        }

        /// <summary>For the dry run: every arm starts from the same ring slot, so their results line up.</summary>
        public void ResetRing()
        {
            _next = 0;
        }

        private int Next()
        {
            return _next++ & (Payloads.RingSize - 1);
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory(One)]
        public ReadingDto One_Hand()
        {
            return HandWritten.ToDto(_ring[Next()]);
        }

        [Benchmark]
        [BenchmarkCategory(One)]
        public ReadingDto One_DwarfMapper()
        {
            return _dwarf.ToDto(_ring[Next()]);
        }

        [Benchmark(Baseline = true)]
        [BenchmarkCategory(Batch)]
        public ReadingBatchDto Batch_Hand()
        {
            return HandWritten.ToDto(_batch);
        }

        [Benchmark]
        [BenchmarkCategory(Batch)]
        public ReadingBatchDto Batch_DwarfMapper()
        {
            return _dwarf.ToDto(_batch);
        }
    }
}
