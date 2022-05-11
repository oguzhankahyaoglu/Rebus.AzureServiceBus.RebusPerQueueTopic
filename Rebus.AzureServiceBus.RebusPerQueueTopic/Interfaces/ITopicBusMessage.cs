namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces
{
    public interface ITopicBusMessage<out T> : IBusMessage<T> where T : class
    {
    }
}