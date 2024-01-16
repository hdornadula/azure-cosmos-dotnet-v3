﻿//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Cosmos
{
    using System;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Handlers;

    /// <summary>
    /// Types of availability strategies supported
    /// </summary>
#if PREVIEW
    public 
#else
    internal
#endif
    abstract class AvailabilityStrategy
    {
        /// <summary>
        /// Execute the availability strategy
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="client"></param>
        /// <param name="requestMessage"></param>
        /// <param name="cancellationToken"></param>
        internal virtual Task<ResponseMessage> ExecuteAvailablityStrategyAsync(
            Func<RequestMessage, CancellationToken, Task<ResponseMessage>> sender,
            CosmosClient client,
            RequestMessage requestMessage,
            CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        internal virtual bool Enabled()
        {
            throw new NotImplementedException();
        }
    }
}