using System.Threading.Tasks;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces
{
    public interface IMessageBrokerProvider
    {
        Task Send<TMessage>(TMessage message) where TMessage : class;
    }
}