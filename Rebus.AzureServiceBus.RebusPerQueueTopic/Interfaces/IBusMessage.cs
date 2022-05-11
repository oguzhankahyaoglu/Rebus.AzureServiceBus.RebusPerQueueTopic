using System;
using System.Collections.Generic;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces
{
    public interface IBusMessage<out T> where T : class
    {
        T Body { get; }
        Guid Id { get; }
        string Name { get; }
        Dictionary<string, string> Headers { get; }
    }
}