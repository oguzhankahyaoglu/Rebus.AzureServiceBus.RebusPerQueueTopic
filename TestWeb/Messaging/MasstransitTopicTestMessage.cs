using System;

// ReSharper disable once CheckNamespace
namespace Fernas.Business.ServiceBus
{
    public class MasstransitTopicTestMessage
    {
        public const string QUEUE_NAME = "fernas-masstransit-topic-test";
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
    }
}