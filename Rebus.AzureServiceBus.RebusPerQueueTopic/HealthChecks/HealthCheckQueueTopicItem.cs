using System;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.HealthChecks
{
    internal class HealthCheckQueueTopicItem
    {
        public string QueueName { get; set; }
        public string TopicName { get; set; }
        public string SubscriptionName { get; set; }
        public Type MessageType { get; set; }

        public override string ToString()
        {
            if (!string.IsNullOrEmpty(QueueName))
                return $"Type: {MessageType.Name} Queue: {QueueName}";

            return $"Type: {MessageType.Name} Topic: {TopicName} Subscription: {SubscriptionName}";
        }
    }
}