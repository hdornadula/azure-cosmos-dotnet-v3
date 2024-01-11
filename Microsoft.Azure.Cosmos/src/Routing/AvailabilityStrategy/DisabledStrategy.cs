﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos
{
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Handlers;

    /// <summary>
    /// A Disabled availability strategy that does not do anything. Used for overriding the default global availability strategy.
    /// </summary>
#if PREVIEW
    public 
#else
    internal
#endif
    class DisabledStrategy : AvailabilityStrategy
    {
        internal override bool Enabled()
        {
            return false;
        }

        internal override Task<ResponseMessage> ExecuteAvailablityStrategyAsync(RequestInvokerHandler requestInvokerHandler, CosmosClient client, RequestMessage requestMessage, CancellationToken cancellationToken)
        {
            // This should never be called.
            throw new System.NotImplementedException();
        }
    }
}