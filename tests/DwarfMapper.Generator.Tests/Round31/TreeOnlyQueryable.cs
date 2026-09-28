// SPDX-License-Identifier: GPL-2.0-only
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;

namespace DwarfMapper.Generator.Tests.Round31
{
    /// <summary>
    ///     An in-memory IQueryable that is NOT an EnumerableQuery, so a generated projection always takes the
    ///     expression-tree path for it. The oracle for tree-vs-routed parity (round 31 T13).
    /// </summary>
    public sealed class TreeOnlyQueryable<T> : IQueryable<T>, IQueryProvider
    {
        public TreeOnlyQueryable(IEnumerable<T> source)
        {
            Expression = source.AsQueryable().Expression;
        }

        public Type ElementType => typeof(T);

        public Expression Expression { get; }

        public IQueryProvider Provider => this;

        public IEnumerator<T> GetEnumerator()
        {
            return new EnumerableQuery<T>(Expression).AsEnumerable().GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public IQueryable CreateQuery(Expression expression)
        {
            throw new NotSupportedException();
        }

        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            return new EnumerableQuery<TElement>(expression);
        }

        public object Execute(Expression expression)
        {
            throw new NotSupportedException();
        }

        public TResult Execute<TResult>(Expression expression)
        {
            return ((IQueryProvider)new EnumerableQuery<T>(Expression)).Execute<TResult>(expression);
        }
    }
}
