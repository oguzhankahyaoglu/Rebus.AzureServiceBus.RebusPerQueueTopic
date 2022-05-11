using System;

// ReSharper disable once CheckNamespace
namespace Fernas.Business.ServiceBus
{
    public class MasstransitQueueTestMessage
    {
        public const string QUEUE_NAME = "fernas-masstransit-queue-test";
        
        public DateTime Date { get; set; }
    }
}