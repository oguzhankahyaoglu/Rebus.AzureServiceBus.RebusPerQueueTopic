using Azure.Messaging.ServiceBus;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus
{
    class ReceivedMessage
    {
        public ServiceBusReceivedMessage Message { get; }
        public ServiceBusReceiver MessageReceiver { get; }

        public ReceivedMessage(ServiceBusReceivedMessage message, ServiceBusReceiver messageReceiver)
        {
            Message = message;
            MessageReceiver = messageReceiver;
        }    
    }
}