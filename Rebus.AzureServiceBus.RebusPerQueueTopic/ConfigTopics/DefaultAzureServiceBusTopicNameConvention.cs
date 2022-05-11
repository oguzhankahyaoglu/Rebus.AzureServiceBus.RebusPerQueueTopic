using System;
using Rebus.Topic;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics
{
    /// <summary>
    /// Helper responsible for implementing how various names turn out
    /// </summary>
    public class DefaultAzureServiceBusTopicNameConvention : ITopicNameConvention
    {
        private readonly string _topicName;

        public DefaultAzureServiceBusTopicNameConvention(string topicName)
        {
            _topicName = topicName;
        }

        /// <summary>
        /// Gets a topic name from the given <paramref name="eventType"/>
        /// </summary>
        public string GetTopic(Type eventType)
        {
            return _topicName;
        }
    }
}