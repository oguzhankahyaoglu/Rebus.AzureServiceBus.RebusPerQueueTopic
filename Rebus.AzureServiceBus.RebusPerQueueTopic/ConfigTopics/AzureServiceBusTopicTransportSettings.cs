﻿using System;

// ReSharper disable UnusedMember.Global

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics
{
    /// <summary>
    /// Allows for configuring additional options for the Azure Service Bus transport (when running in full-duplex mode)
    /// </summary>
    public class AzureServiceBusTopicTransportSettings
    {
        internal bool PrefetchingEnabled { get; set; }
        internal int MaxDeliveryCount { get; set; }
        internal int NumberOfMessagesToPrefetch { get; set; }
        internal bool PartitioningEnabled { get; set; }
        // internal bool AutomaticPeekLockRenewalEnabled { get; set; }
        internal TimeSpan? DefaultMessageTimeToLive { get; set; }
        internal TimeSpan? LockDuration { get; set; }
        internal TimeSpan? AutoDeleteOnIdle { get; set; }
        internal TimeSpan? DuplicateDetectionHistoryTimeWindow { get; set; }
        internal TimeSpan ReceiveOperationTimeout { get; set; } = TimeSpan.FromSeconds(5);

        internal int MaximumMessagePayloadBytes { get; set; } = 256 * 1024;

        /// <summary>
        /// Enables partitioning whereby Azure Service Bus will be able to distribute messages between message stores and this way increase throughput.
        /// Partitioning cannot be enabled after a queue is created, so it must be enabled before Rebus creates the input queue.
        /// </summary>
        public AzureServiceBusTopicTransportSettings EnablePartitioning()
        {
            PartitioningEnabled = true;
            return this;
        }

        /// <summary>
        /// Configures the maxiumum payload request limit. Relevant, when Rebus auto-batches sent messages, keeping the size of each individual batch below 256 kB.
        /// If the SKU allows more than the default 256 kB, it can be increased by calling this method.
        /// </summary>
        public AzureServiceBusTopicTransportSettings SetMessagePayloadSizeLimit(int maximumMessagePayloadBytes)
        {
            if (maximumMessagePayloadBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumMessagePayloadBytes), maximumMessagePayloadBytes, "Please provide a value greater than 0");
            }

            MaximumMessagePayloadBytes = maximumMessagePayloadBytes;

            return this;
        }

        /// <summary>
        /// Configures the duplicate detection history window on the input queue. Please note that this setting cannot be changed after the queue is created,
        /// so it must be configured before Rebus creates the input queue the first time. The value must be at least 20 seconds and at most 1 day.
        /// </summary>
        public AzureServiceBusTopicTransportSettings SetDuplicateDetectionHistoryTimeWindow(TimeSpan duplicateDetectionHistoryTimeWindow)
        {
            if (duplicateDetectionHistoryTimeWindow < TimeSpan.FromSeconds(20) || duplicateDetectionHistoryTimeWindow > TimeSpan.FromDays(1))
            {
                throw new ArgumentException($"The duplicate detection history time window {duplicateDetectionHistoryTimeWindow} cannot be used - it must be at least 20 seconds and at most 1 day");
            }
            DuplicateDetectionHistoryTimeWindow = duplicateDetectionHistoryTimeWindow;
            return this;
        }

        /// <summary>
        /// Configures the auto-delete-on-idle duration of the input queue. This will make Azure Service Bus automatically delete
        /// the queue when the time has elapsed without any activity.
        /// </summary>
        public AzureServiceBusTopicTransportSettings SetAutoDeleteOnIdle(TimeSpan autoDeleteOnIdleDuration)
        {
            if (autoDeleteOnIdleDuration < TimeSpan.FromMinutes(5))
            {
                throw new ArgumentException($"Auto-delete-on-idle duration {autoDeleteOnIdleDuration} cannot be used - it must be at least five minutes");
            }
            AutoDeleteOnIdle = autoDeleteOnIdleDuration;
            return this;
        }

        /// <summary>
        /// Configures the default TTL on the input queue. This is the longest messages get to stay in the input queue.
        /// If a shorter TTL is set on the message when sending it, that TTL is used instead.
        /// </summary>
        public AzureServiceBusTopicTransportSettings SetDefaultMessageTimeToLive(TimeSpan messageTimeToLive)
        {
            if (messageTimeToLive < TimeSpan.FromSeconds(1))
            {
                throw new ArgumentException($"Default message TTL {messageTimeToLive} cannot be used - it must be at least one second");
            }
            DefaultMessageTimeToLive = messageTimeToLive;
            return this;
        }

        /// <summary>
        /// Configures the message peek lock duration for received messages. 
        /// </summary>
        public AzureServiceBusTopicTransportSettings SetMessagePeekLockDuration(TimeSpan messagePeekLockDuration)
        {
            if (messagePeekLockDuration < TimeSpan.FromSeconds(5) || messagePeekLockDuration > TimeSpan.FromMinutes(5))
            {
                throw new ArgumentException($"Message peek lock duration {messagePeekLockDuration} cannot be used - it must be at least 5 seconds and at most 5 minutes");
            }
            LockDuration = messagePeekLockDuration;
            return this;
        }

        /// <summary>
        /// Enables prefetching whereby a batch of messages will be prefetched instead of only one at a time.
        /// By enabling prefetching, the automatic peek lock renewal will be disabled, because it is assumed
        /// that prefetching will be enabled only in cases where messages can be processed fairly quickly.
        /// </summary>
        public AzureServiceBusTopicTransportSettings EnablePrefetching(int numberOfMessagesToPrefetch)
        {
            if (numberOfMessagesToPrefetch < 1)
            {
                throw new ArgumentOutOfRangeException($"Cannot set prefetching to {numberOfMessagesToPrefetch} messages - must be at least 1");
            }

            PrefetchingEnabled = true;
            NumberOfMessagesToPrefetch = numberOfMessagesToPrefetch;
            return this;
        }

        // <summary>
        // Enables automatic peek lock renewal. Only enable this if you intend on handling messages for a long long time, and
        // DON'T intend on handling messages quickly - it will have an impact on message receive, so only enable it if you
        // need it. You should usually strive after keeping message processing times low, much lower than the 5 minute lease
        // you get with Azure Service Bus.
        // </summary>
        // public AzureServiceBusTopicTransportSettings AutomaticallyRenewPeekLock()
        // {
        //     AutomaticPeekLockRenewalEnabled = true;
        //     return this;
        // }


        /// <summary>
        /// Sets the receive operation timeout. This is basically the time the client waits for a message to appear in the queue.
        /// This includes the time taken to establish a connection (either during the first receive or when connection needs to be re-established).
        /// Defaults to 5 seconds.
        /// </summary>
        public AzureServiceBusTopicTransportSettings SetReceiveOperationTimeout(TimeSpan receiveOperationTimeout)
        {
            ReceiveOperationTimeout = receiveOperationTimeout;
            return this;
        }
    }
}