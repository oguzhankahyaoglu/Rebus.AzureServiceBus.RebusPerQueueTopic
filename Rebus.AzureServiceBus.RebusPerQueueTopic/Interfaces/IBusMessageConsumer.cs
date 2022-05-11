using System.Threading.Tasks;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces
{
    public interface IBusMessageConsumer<in TMessage> where TMessage : class
    {
        Task Consume(TMessage message);
    }
}
