//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------
namespace Microsoft.Azure.Documents.Rntbd
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.Azure.Cosmos.Core.Trace;
    using Microsoft.Azure.Documents.FaultInjection;

    internal sealed class LoadBalancingPartition : IDisposable
    {
        private readonly Uri serverUri;
        private readonly ChannelProperties channelProperties;
        private readonly bool localRegionRequest;
        private readonly int maxCapacity;  // maxChannels * maxRequestsPerChannel

        private int requestsPending = 0;  // Atomic.
        // Clock hand.
        private readonly SequenceGenerator sequenceGenerator =
            new SequenceGenerator();

        private readonly ReaderWriterLockSlim capacityLock =
            new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        // capacity == openChannels.Count * maxRequestsPerChannel.
        private int capacity = 0;  // Guarded by capacityLock.
        private readonly List<LbChannelState> openChannels =
            new List<LbChannelState>();  // Guarded by capacityLock.

        private readonly SemaphoreSlim concurrentOpeningChannelSlim;

        private readonly IChaosInterceptor chaosInterceptor;

        // This channel factory delegate is meant for unit testing only and a default implementation is provided.
        // However it can be extended to support the main line code path if needed.
        private readonly Func<Guid, Uri, ChannelProperties, bool, SemaphoreSlim, IChaosInterceptor, Action<Guid, Guid, Uri, Channel>, IChannel> channelFactory;

        public LoadBalancingPartition(
            Uri serverUri,
            ChannelProperties channelProperties,
            bool localRegionRequest,
            Func<Guid, Uri, ChannelProperties, bool, SemaphoreSlim, IChaosInterceptor, Action<Guid, Guid, Uri, Channel>, IChannel> channelFactory = null,
            IChaosInterceptor chaosInterceptor = null)
        {
            Debug.Assert(serverUri != null);
            this.serverUri = serverUri;
            Debug.Assert(channelProperties != null);
            this.channelProperties = channelProperties;
            this.localRegionRequest = localRegionRequest;

            this.maxCapacity = checked(channelProperties.MaxChannels *
                channelProperties.MaxRequestsPerChannel);

            this.concurrentOpeningChannelSlim =
                new SemaphoreSlim(channelProperties.MaxConcurrentOpeningConnectionCount, channelProperties.MaxConcurrentOpeningConnectionCount);

            this.channelFactory = channelFactory != null
                ? channelFactory
                : CreateAndInitializeChannel;

            this.chaosInterceptor = chaosInterceptor;
        }

        public async Task<StoreResponse> RequestAsync(
            DocumentServiceRequest request,
            TransportAddressUri physicalAddress,
            ResourceOperation resourceOperation,
            Guid activityId,
            TransportRequestStats transportRequestStats)
        {
            int currentPending = Interlocked.Increment(
                ref this.requestsPending);
            transportRequestStats.NumberOfInflightRequestsToEndpoint = currentPending;
            try
            {
                if (currentPending > this.maxCapacity)
                {
                    throw new RequestRateTooLargeException(
                        string.Format(
                            "All connections to {0} are fully utilized. Increase " +
                            "the maximum number of connections or the maximum number " +
                            "of requests per connection", this.serverUri),
                        SubStatusCodes.ClientTcpChannelFull);
                }

                transportRequestStats.RecordState(TransportRequestStats.RequestStage.ChannelAcquisitionStarted);

                while (true)
                {
                    LbChannelState channelState = null;
                    bool addCapacity = false;

                    uint sequenceNumber = this.sequenceGenerator.Next();

                    this.capacityLock.EnterReadLock();
                    try
                    {
                        transportRequestStats.NumberOfOpenConnectionsToEndpoint = this.openChannels.Count; // Lock has already been acquired for openChannels
                        if (currentPending <= this.capacity)
                        {
                            // Enough capacity is available, pick a channel.
                            int channelIndex = (int) (sequenceNumber % this.openChannels.Count);
                            LbChannelState candidateChannel = this.openChannels[channelIndex];
                            if (candidateChannel.Enter())
                            {
                                // Do not check the health status yet. Do it
                                // without holding the capacity lock.
                                channelState = candidateChannel;
                            }
                        }
                        else
                        {
                            addCapacity = true;
                        }
                    }
                    finally
                    {
                        this.capacityLock.ExitReadLock();
                    }

                    if (channelState != null)
                    {
                        bool healthy = false;
                        try
                        {
                            healthy = channelState.DeepHealthy;
                            if (healthy)
                            {
                                return await channelState.Channel.RequestAsync(
                                    request,
                                    physicalAddress,
                                    resourceOperation,
                                    activityId,
                                    transportRequestStats);
                            }

                            // Unhealthy channel
                            this.capacityLock.EnterWriteLock();
                            try
                            {
                                // Other callers might have noticed the channel
                                // fail at the same time. Do not assume that
                                // this caller is the only one trying to remove
                                // it.
                                if (this.openChannels.Remove(channelState))
                                {
                                    this.capacity -= this.channelProperties.MaxRequestsPerChannel;
                                }
                                Debug.Assert(
                                    this.capacity ==
                                    this.openChannels.Count *
                                    this.channelProperties.MaxRequestsPerChannel);
                            }
                            finally
                            {
                                this.capacityLock.ExitWriteLock();
                            }
                        }
                        finally
                        {
                            bool lastCaller = channelState.Exit();
                            if (lastCaller && !channelState.ShallowHealthy)
                            {
                                channelState.Dispose();
                                DefaultTrace.TraceInformation(
                                    "Closed unhealthy channel {0}",
                                    channelState.Channel);
                            }
                        }
                    }
                    else if (addCapacity)
                    {
                        int targetCapacity = MathUtils.CeilingMultiple(
                            currentPending,
                            this.channelProperties.MaxRequestsPerChannel);
                        Debug.Assert(targetCapacity % this.channelProperties.MaxRequestsPerChannel == 0);
                        int targetChannels = targetCapacity / this.channelProperties.MaxRequestsPerChannel;
                        int channelsCreated = 0;

                        this.capacityLock.EnterWriteLock();
                        try
                        {
                            if (this.openChannels.Count < targetChannels)
                            {
                                channelsCreated = targetChannels - this.openChannels.Count;
                            }

                           Action<Guid, Guid, Uri, Channel> onChannelOpen = 
                                (Guid createId, Guid connectionCorrilationId, Uri serverUri, Channel createdChannel) => this.chaosInterceptor?.OnChannelOpen(createId, connectionCorrilationId, serverUri, request, createdChannel);

                            while (this.openChannels.Count < targetChannels)
                            {                              
                                this.OpenChannelAndIncrementCapacity(
                                    activityId: activityId,
                                    onChannelOpen: onChannelOpen);
                            }
                            Debug.Assert(
                                this.capacity ==
                                this.openChannels.Count *
                                this.channelProperties.MaxRequestsPerChannel);
                        }
                        finally
                        {
                            this.capacityLock.ExitWriteLock();
                        }
                        if (channelsCreated > 0)
                        {
                            DefaultTrace.TraceInformation(
                                "Opened {0} channels to server {1}",
                                channelsCreated, this.serverUri);
                        }
                    }
                }
            }
            finally
            {
                currentPending = Interlocked.Decrement(
                    ref this.requestsPending);
                Debug.Assert(currentPending >= 0);
            }
        }

        /// <summary>
        /// Open and initializes the <see cref="Channel"/>.
        /// </summary>
        /// <param name="activityId">An unique identifier indicating the current activity id.</param>
        internal Task OpenChannelAsync(Guid activityId)
        {
            IChannel channel = null;
            this.capacityLock.EnterUpgradeableReadLock();
            try
            {
                if (this.capacity < this.maxCapacity)
                {
                    foreach (LbChannelState channelState in this.openChannels)
                    {
                        if (channelState.DeepHealthy)
                        {
                            return Task.FromResult(0);
                        }
                    }

                    this.capacityLock.EnterWriteLock();
                    try
                    {
                        channel = this.OpenChannelAndIncrementCapacity(
                            activityId: activityId);
                    }
                    finally
                    {
                        this.capacityLock.ExitWriteLock();
                    }
                }
                else
                {
                    string errorMessage = $"Failed to open channels to server {this.serverUri} because the current channel capacity {this.capacity} has exceeded the maaximum channel capacity limit: {this.maxCapacity}";

                    // Converting the error into invalid operation exception. Note that the OpenChannelAsync() method is used today, by the open connection flow
                    // in RntbdOpenConnectionHandler that is primarily used for the replica validation. Because the replica validation is done deterministically
                    // to open the Rntbd connections with best effort, throwing an exception from this place won't impact the replica validation flow because it
                    // will be caught and swallowed by the RntbdOpenConnectionHandler and the replica validation flow will continue.
                    throw new InvalidOperationException(
                        message: errorMessage);
                }
            }
            finally
            {
                this.capacityLock.ExitUpgradeableReadLock();
            }

            if (channel == null)
            {
                throw new InvalidOperationException(
                    message: $"Could not open a channel to server {this.serverUri} because a channel instance didn't get created.");
            }

            return channel.OpenChannelAsync(activityId);
        }

        public void Dispose()
        {
            this.capacityLock.EnterWriteLock();
            try
            {
                foreach (LbChannelState channelState in this.openChannels)
                {
                    channelState.Dispose();
                }
            }
            finally
            {
                this.capacityLock.ExitWriteLock();
            }

            try
            {
                this.capacityLock.Dispose();
            }
            catch(SynchronizationLockException)
            {
                // SynchronizationLockException is thrown if there are inflight requests during the disposal of capacityLock
                // suspend this exception to avoid crashing disposing other partitions/channels in hierarchical calls
                return;
            }
        }

        /// <summary>
        /// Open and initializes the <see cref="Channel"/> and adds
        /// the corresponding channel state to the openChannels pool
        /// and increment the currrent channel capacity.
        /// </summary>
        /// <param name="activityId">An unique identifier indicating the current activity id.</param>
        /// <param name="onChannelOpen">An action to be invoked when the channel is opened.</param>
        /// <returns>An instance of <see cref="IChannel"/>.</returns>
        private IChannel OpenChannelAndIncrementCapacity(
            Guid activityId,
            Action<Guid, Guid, Uri, Channel> onChannelOpen = null)
        {
            Debug.Assert(this.capacityLock.IsWriteLockHeld);

            IChannel newChannel = this.channelFactory(
                activityId,
                this.serverUri,
                this.channelProperties,
                this.localRegionRequest,
                this.concurrentOpeningChannelSlim,
                this.chaosInterceptor,
                onChannelOpen);

            if (newChannel == null)
            {
                throw new ArgumentNullException(
                    paramName: nameof(newChannel),
                    message: "Channel can't be null.");
            }

            this.openChannels.Add(
                new LbChannelState(
                    newChannel,
                    this.channelProperties.MaxRequestsPerChannel));
            this.capacity += this.channelProperties.MaxRequestsPerChannel;

            return newChannel;
        }

        /// <summary>
        /// Creates and initializes a new instance of rntbd <see cref="Channel"/>.
        /// </summary>
        /// <param name="activityId">A guid containing the activity id for the operation.</param>
        /// <param name="serverUri">An instance of <see cref="Uri"/> containing the physical server uri.</param>
        /// <param name="channelProperties">An instance of <see cref="ChannelProperties"/>.</param>
        /// <param name="localRegionRequest">A boolean flag indicating if the request is intendent for local region.</param>
        /// <param name="concurrentOpeningChannelSlim">An instance of <see cref="SemaphoreSlim"/>.</param>
        /// <param name="chaosInterceptor">The Interceptor for fault injection. Null if not using fault injection</param>
        /// <param name="onChannelOpen">An action to be invoked when the channel is opened.</param>
        /// <returns>An instance of <see cref="IChannel"/>.</returns>
        private static IChannel CreateAndInitializeChannel(
            Guid activityId,
            Uri serverUri,
            ChannelProperties channelProperties,
            bool localRegionRequest,
            SemaphoreSlim concurrentOpeningChannelSlim,
            IChaosInterceptor chaosInterceptor = null,
            Action<Guid, Guid, Uri, Channel> onChannelOpen = null)
        {
            return new Channel(
                activityId,
                serverUri,
                channelProperties,
                localRegionRequest,
                concurrentOpeningChannelSlim,
                chaosInterceptor,
                onChannelOpen);
        }

        private sealed class SequenceGenerator
        {
            private int current = 0;

            // Returns the next positive integer in the sequence. Wraps around
            // when it reaches UInt32.MaxValue.
            public uint Next()
            {
                // Interlocked.Increment only works with signed integer.
                // Map the returned value to the unsigned int32 range.
                return (uint) (
                    ((uint) int.MaxValue) + 1 +
                    Interlocked.Increment(ref this.current));
            }
        }
    }
}