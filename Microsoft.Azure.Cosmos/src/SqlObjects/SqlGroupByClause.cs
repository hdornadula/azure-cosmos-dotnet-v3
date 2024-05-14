﻿// ------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// ------------------------------------------------------------
namespace Microsoft.Azure.Cosmos.SqlObjects
{
    using System;
    using System.Collections.Immutable;
    using Microsoft.Azure.Cosmos.SqlObjects.Visitors;

#if INTERNAL
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600 // Elements should be documented
    public
#else
    internal
#endif
    sealed class SqlGroupByClause : SqlObject
    {
        private SqlGroupByClause(ImmutableArray<SqlSelectItem> expressions)
        {
            foreach (SqlSelectItem expression in expressions)
            {
                if (expression == null)
                {
                    throw new ArgumentException($"{nameof(expressions)} must not have null items.");
                }
            }

            this.Expressions = expressions;

        }

        // Each key is a SqlSelectItem to capture the possible aliasing of keys
        public ImmutableArray<SqlSelectItem> Expressions { get; }

        public static SqlGroupByClause Create(params SqlSelectItem[] expressions) => new SqlGroupByClause(expressions.ToImmutableArray());

        public static SqlGroupByClause Create(ImmutableArray<SqlSelectItem> expressions) => new SqlGroupByClause(expressions);

        public override void Accept(SqlObjectVisitor visitor) => visitor.Visit(this);

        public override TResult Accept<TResult>(SqlObjectVisitor<TResult> visitor) => visitor.Visit(this);

        public override TResult Accept<T, TResult>(SqlObjectVisitor<T, TResult> visitor, T input) => visitor.Visit(this, input);
    }
}
