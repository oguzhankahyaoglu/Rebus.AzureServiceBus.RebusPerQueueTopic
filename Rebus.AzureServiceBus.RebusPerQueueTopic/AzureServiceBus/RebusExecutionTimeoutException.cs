using System;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus
{
    public class RebusExecutionTimeoutException : TimeoutException
    {
        public RebusExecutionTimeoutException()
        {
        }

        public RebusExecutionTimeoutException(string message) : base(message)
        {
        }
    }
}