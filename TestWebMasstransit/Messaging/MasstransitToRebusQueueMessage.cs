using System;
using ServiceBusConfigurator.Attributes;

namespace ServiceBusConfigurator.TestWebMasstransit.Messaging
{
    [QueueMessageName(QUEUE_NAME)]
    public class MasstransitToRebusQueueMessage
    {
        public const string QUEUE_NAME = "fernas-masstransit-to-rebus-test";
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
    }
}