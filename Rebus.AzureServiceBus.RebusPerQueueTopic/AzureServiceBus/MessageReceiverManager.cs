using System;
using System.Collections.Concurrent;
using Azure.Messaging.ServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.Logging;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus
{
    internal static class MessageReceiverManager
    {
        public static ServiceBusReceiver RecreateQueueReceiver(int prefetchCount,
            string address,
            ServiceBusClient client,
            ILog log,
            ConcurrentStack<IDisposable> disposables)
        {
            var receiverOptions = new ServiceBusReceiverOptions
            {
                PrefetchCount = prefetchCount,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            };

            var messageReceiver = client.CreateReceiver(address, receiverOptions);
            disposables.Push(messageReceiver.AsDisposable(m => AsyncHelpers.RunSync(async () =>
            {
                try
                {
                    await m.CloseAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    log.Error(ex, "Cancelled, why?");
                }
            })));
            return messageReceiver;
        }  
        
        public static ServiceBusReceiver RecreateTopicReceiver(int prefetchCount,
            string address,
            string subscriptionName,
            ServiceBusClient client,
            ILog log,
            ConcurrentStack<IDisposable> disposables)
        {
            var receiverOptions = new ServiceBusReceiverOptions
            {
                PrefetchCount = prefetchCount,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            };
            var messageReceiver = client.CreateReceiver(address, subscriptionName, receiverOptions);

            disposables.Push(messageReceiver.AsDisposable(m => AsyncHelpers.RunSync(async () =>
            {
                try
                {
                    await m.CloseAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException ex)
                {
                    log.Error(ex, "Cancelled, why?");
                }
            })));
            return messageReceiver;
        }
    }
}