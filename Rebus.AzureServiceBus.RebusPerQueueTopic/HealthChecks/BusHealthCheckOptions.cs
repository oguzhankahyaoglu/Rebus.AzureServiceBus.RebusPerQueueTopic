using System;
using Azure.Messaging.ServiceBus;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.HealthChecks
{
    public class BusHealthCheckOptions
    {
        /// <summary>
        /// Dont check queue/topic message times for health checks in app startup phase for this duration.
        /// By this way, in case too many messages in queue, the checks would be delayed.
        /// Default: 1 hour
        /// </summary>
        public TimeSpan WarmupIgnoreDelayForHealthChecking { get; set; } = TimeSpan.FromHours(1);

        /// <summary>
        /// The returned Timespan would be compared to message's EnqueueTime and in case this delay is exceeded,
        /// the queue would be considered is not being consumed and would result in Unhealthy healthcheck
        /// Default: 1 hour
        /// </summary>
        public Func<ServiceBusReceivedMessage, Type, TimeSpan> ExpireConditionDelay = (message,
            messageType) => TimeSpan.FromHours(1);
    }
}