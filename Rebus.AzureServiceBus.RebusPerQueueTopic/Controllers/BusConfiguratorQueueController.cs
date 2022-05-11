using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BusConfiguratorQueueController : Controller
    {
        private readonly IConfiguration _config;

        public BusConfiguratorQueueController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet(nameof(PurgeQueue))]
        public async Task<string> PurgeQueue(string queueName)
        {
            var serviceBusConnectionString = _config.GetValue<string>("ServiceBus:ConnectionString");
            await ManagementExtensions.PurgeQueue(serviceBusConnectionString, queueName);
            return "Purged";
        }
        
        [HttpGet(nameof(GetQueueMessages))]
        public async Task<IReadOnlyList<ServiceBusReceivedMessage>> GetQueueMessages(string queueName)
        {
            var serviceBusConnectionString = _config.GetValue<string>("ServiceBus:ConnectionString");
            var clientOptions = new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets
            };

            await using (var client = new ServiceBusClient(serviceBusConnectionString, clientOptions))
            await using (var messageReceiver = client.CreateReceiver(queueName, new ServiceBusReceiverOptions
            {
                PrefetchCount = 0,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            }))
            {
                var messages = await messageReceiver.PeekMessagesAsync(100000).ConfigureAwait(false);
                messages = messages.OrderBy(x => x.EnqueuedTime)
                    .Where(x => x.ScheduledEnqueueTime == default)
                    .Where(x => x.LockedUntil == default)
                    .ToList();
                return messages;
            }
        }

    }
}