using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces;
using Rebus.ServiceProvider.Named;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Providers
{
    public class RebusProvider : IMessageBrokerProvider
    {
        private readonly IServiceProvider _serviceProvider;

        public RebusProvider(
            IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task Send<TMessage>(TMessage message)
            where TMessage : class
        {
            var bus = _serviceProvider.GetRequiredService<ITypedBus<TMessage>>();
            await bus.Send(message);
        }
    }
}