﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

namespace Microsoft.Azure.Cosmos.Query.Core.Pipeline.Aggregate.Aggregators
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Text;
    using Microsoft.Azure.Cosmos.CosmosElements;
    using Microsoft.Azure.Cosmos.CosmosElements.Numbers;
    using Microsoft.Azure.Cosmos.Query.Core.Exceptions;
    using Microsoft.Azure.Cosmos.Query.Core.Monads;

    internal sealed class MakeSetAggregator : IAggregator
    {
        private readonly HashSet<CosmosElement> globalSet;

        private MakeSetAggregator(CosmosArray initialSet)
        {
            this.globalSet = new HashSet<CosmosElement>();
            foreach (CosmosElement setItem in initialSet)
            {
                this.globalSet.Add(setItem);
            }
        }

        public void Aggregate(CosmosElement localSet)
        {
            if (!(localSet is CosmosArray cosmosArray))
            {
                throw new ArgumentException($"{nameof(localSet)} must be an array.");
            }

            foreach (CosmosElement setItem in cosmosArray)
            {
                this.globalSet.Add(setItem);
            }
        }

        public CosmosElement GetResult()
        {
            CosmosElement[] cosmosElementArray = new CosmosElement[this.globalSet.Count];
            this.globalSet.CopyTo(cosmosElementArray);
            return CosmosArray.Create(cosmosElementArray);
        }

        public string GetContinuationToken()
        {
            return this.globalSet.ToString();
        }

        public static TryCatch<IAggregator> TryCreate(CosmosElement continuationToken)
        {
            CosmosArray partialSet;
            if (continuationToken != null)
            {
                if (!(continuationToken is CosmosArray cosmosPartialSet))
                {
                    return TryCatch<IAggregator>.FromException(
                        new MalformedContinuationTokenException($@"Invalid MakeSet continuation token: ""{continuationToken}""."));
                }

                partialSet = cosmosPartialSet;
            }
            else
            {
                partialSet = CosmosArray.Empty;
            }

            return TryCatch<IAggregator>.FromResult(
                new MakeSetAggregator(initialSet: partialSet));
        }

        public CosmosElement GetCosmosElementContinuationToken()
        {
            CosmosElement[] cosmosElementArray = new CosmosElement[this.globalSet.Count];
            this.globalSet.CopyTo(cosmosElementArray);
            return CosmosArray.Create(cosmosElementArray);
        }
    }
}
